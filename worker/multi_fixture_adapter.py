"""Synthetic multi-contact messages, sharing one local WAL fixture and cursor set."""
import heapq
import time

from db_adapter import ACCOUNT, CONVERSATION, SHARDS, TEXT_LIMIT, BATCH_PER_SHARD, FixtureStore, IdentityChanged


class MultiFixtureStore(FixtureStore):
    def __init__(self, directory, peers):
        if (not isinstance(peers, list) or not 1 <= len(peers) <= 16
                or len(set(peers)) != len(peers)
                or any(not isinstance(peer, str) or not peer.startswith("demo-") or len(peer) > 256
                       for peer in peers)):
            raise IdentityChanged()
        self.peers = tuple(peers)
        super().__init__(directory)
        self.baseline = {(peer, shard): value for peer in peers for shard, value in self.baseline.items()}

    def append(self, shard, seq, text, conversation=CONVERSATION, direction="incoming", kind="text"):
        super().append(shard, seq, text, conversation, direction, kind)
        if (conversation in self.peers and conversation != CONVERSATION
                and direction == "incoming" and kind == "text"):
            local_id = self.writers[shard].execute("SELECT last_insert_rowid()").fetchone()[0]
            self.arrivals[(shard, local_id)] = time.perf_counter_ns()

    def demo(self):
        self.next_sequence += 1
        index = self.next_sequence - 1
        peer = self.peers[index % len(self.peers)]
        self.append(SHARDS[index % len(SHARDS)], self.next_sequence,
                    ("你在干嘛呀？", "哈哈哈", "哈哈哈", "我听得到。", "等一下，我回来啦。")[index % 5],
                    conversation=peer)

    def query(self):
        if self.outstanding is not None:
            raise ValueError("unacknowledged batch")
        started = time.perf_counter_ns()
        streams = []
        for shard, reader in self.readers.items():
            if self.file_identity(self.paths[shard]) != self.identities[shard]:
                raise IdentityChanged()
            if reader.execute("SELECT account, conversation FROM binding LIMIT 2").fetchall() != [(ACCOUNT, CONVERSATION)]:
                raise IdentityChanged()
            for peer in self.peers:
                records = reader.execute("""
                    SELECT local_id, sort_seq, substr(body, 1, ?) FROM message
                    WHERE conversation = ? AND direction = 'incoming' AND kind = 'text' AND local_id > ?
                    ORDER BY local_id LIMIT ?
                """, (TEXT_LIMIT, peer, self.baseline[(peer, shard)], BATCH_PER_SHARD)).fetchall()
                if records:
                    streams.append((peer, shard, records))
        heads = []
        for index, (peer, shard, records) in enumerate(streams):
            local_id, sequence, _ = records[0]
            heapq.heappush(heads, (sequence, peer, shard, local_id, index, 0))
        cursor = self.baseline.copy()
        messages = []
        finished = time.perf_counter_ns()
        while heads and len(messages) < BATCH_PER_SHARD:
            sequence, peer, shard, local_id, index, position = heapq.heappop(heads)
            records = streams[index][2]
            text = records[position][2]
            arrival = self.arrivals.get((shard, local_id))
            messages.append(dict(key=f"{peer}:{shard}:{local_id}", account=ACCOUNT, conversation=peer,
                                 direction="incoming", isText=True, sequence=sequence, text=text,
                                 fixtureDetectionMs=(finished-arrival)/1e6 if arrival is not None else None))
            cursor[(peer, shard)] = local_id
            position += 1
            if position < len(records):
                next_id, next_sequence, _ = records[position]
                heapq.heappush(heads, (next_sequence, peer, shard, next_id, index, position))
        if messages:
            self.outstanding = (cursor, [message["key"] for message in messages])
        return messages, (finished-started)/1e6

    def ack(self, keys):
        if self.outstanding is None or keys != self.outstanding[1]:
            raise ValueError("invalid acknowledgment")
        self.baseline, self.outstanding = self.outstanding[0], None
        for key in keys:
            _, shard, local_id = key.rsplit(":", 2)
            self.arrivals.pop((shard, int(local_id)), None)
