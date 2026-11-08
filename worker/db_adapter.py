"""Disposable synthetic WAL databases. No WeChat schema or data is used."""
import sqlite3
import time
from contextlib import closing

ACCOUNT = "demo-local"
CONVERSATION = "demo-peer"
SHARDS = ("message_0.sqlite", "message_1.sqlite")
BATCH_PER_SHARD = 32
TEXT_LIMIT = 1001


class IdentityChanged(Exception):
    pass


class FixtureStore:
    def __init__(self, directory):
        self.writers = {}
        self.readers = {}
        self.paths = {}
        self.baseline = {}
        self.identities = {}
        self.arrivals = {}
        self.next_sequence = 0
        self.outstanding = None
        try:
            for name in SHARDS:
                path = directory / name
                with path.open("xb"):
                    pass
                self.paths[name] = path
                self.identities[name] = self.file_identity(path)
                writer = self.writers[name] = sqlite3.connect(path, timeout=0.05)
                writer.execute("PRAGMA journal_mode=WAL")
                writer.executescript("""
                    CREATE TABLE binding (account TEXT NOT NULL, conversation TEXT NOT NULL);
                    CREATE TABLE message (local_id INTEGER PRIMARY KEY AUTOINCREMENT,
                        conversation TEXT NOT NULL, direction TEXT NOT NULL, kind TEXT NOT NULL,
                        sort_seq INTEGER NOT NULL, body TEXT NOT NULL);
                    CREATE INDEX target_cursor ON message(conversation, direction, kind, local_id);
                """)
                writer.execute("INSERT INTO binding VALUES (?, ?)", (ACCOUNT, CONVERSATION))
                writer.commit()
                self.append(name, 0, "启动前历史消息")
                reader = self.readers[name] = sqlite3.connect(path.as_uri() + "?mode=ro", uri=True, timeout=0.05)
                reader.execute("PRAGMA query_only=ON")
                self.baseline[name] = reader.execute("SELECT COALESCE(MAX(local_id), 0) FROM message").fetchone()[0]
            self.arrivals.clear()
        except BaseException:
            self.close()
            raise

    @staticmethod
    def file_identity(path):
        try:
            stat = path.stat()
        except FileNotFoundError as error:
            raise IdentityChanged() from error
        return stat.st_dev, stat.st_ino

    def append(self, shard, seq, text, conversation=CONVERSATION, direction="incoming", kind="text"):
        writer = self.writers[shard]
        cursor = writer.execute("INSERT INTO message(conversation,direction,kind,sort_seq,body) VALUES(?,?,?,?,?)",
                                (conversation, direction, kind, seq, text))
        writer.commit()
        if direction == "incoming" and kind == "text" and conversation == CONVERSATION:
            self.arrivals[(shard, cursor.lastrowid)] = time.perf_counter_ns()

    def demo(self):
        self.next_sequence += 1
        examples = ("你在干嘛呀？", "哈哈哈", "哈哈哈", "我听得到。", "等一下，我回来啦。")
        index = (self.next_sequence - 1) % len(examples)
        self.append(SHARDS[self.next_sequence % len(SHARDS)], self.next_sequence, examples[index])

    def query(self):
        if self.outstanding is not None:
            raise ValueError("unacknowledged batch")
        started = time.perf_counter_ns()
        rows = []
        fences = []
        cursor = self.baseline.copy()
        for shard, reader in self.readers.items():
            if self.file_identity(self.paths[shard]) != self.identities[shard]:
                raise IdentityChanged()
            with closing(reader.execute("SELECT account, conversation FROM binding LIMIT 2")) as binding:
                if binding.fetchall() != [(ACCOUNT, CONVERSATION)]:
                    raise IdentityChanged()
            records = reader.execute("""
                SELECT local_id, sort_seq, substr(body, 1, ?) FROM message
                WHERE conversation = ? AND direction = 'incoming' AND kind = 'text' AND local_id > ?
                ORDER BY local_id LIMIT ?
            """, (TEXT_LIMIT, CONVERSATION, cursor[shard], BATCH_PER_SHARD)).fetchall()
            for local_id, seq, text in records:
                rows.append((seq, shard, local_id, text))
            if len(records) == BATCH_PER_SHARD:
                local_id, seq, _ = records[-1]
                fences.append((seq, shard, local_id))

        finished = time.perf_counter_ns()
        messages = []
        # The fixture writer assigns nondecreasing sequences within each shard. If a
        # page is full, its unseen rows may precede rows fetched from other shards.
        fence = min(fences) if fences else None
        for seq, shard, local_id, text in sorted(rows):
            if fence is not None and (seq, shard, local_id) > fence:
                break
            cursor[shard] = max(cursor[shard], local_id)
            arrival = self.arrivals.get((shard, local_id))
            messages.append(dict(key=f"{shard}:{local_id}", account=ACCOUNT, conversation=CONVERSATION,
                                 direction="incoming", isText=True, sequence=seq, text=text,
                                 fixtureDetectionMs=(finished - arrival) / 1e6 if arrival is not None else None))
        if messages:
            self.outstanding = (cursor, [item["key"] for item in messages])
        return messages, (finished - started) / 1e6

    def ack(self, keys):
        if self.outstanding is None or keys != self.outstanding[1]:
            raise ValueError("invalid acknowledgment")
        self.baseline = self.outstanding[0]
        self.outstanding = None
        for key in keys:
            shard, local_id = key.split(":")
            self.arrivals.pop((shard, int(local_id)), None)

    def close(self):
        for connections in (self.readers, self.writers):
            for conn in connections.values():
                conn.close()
            connections.clear()
