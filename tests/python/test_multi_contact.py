"""Encrypted synthetic fixtures for an explicit multi-contact whitelist."""
from contextlib import closing
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
from uuid import uuid4

import test_live_adapter as fixtures

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "worker"))
from db_adapter import SHARDS, IdentityChanged
from multi_fixture_adapter import MultiFixtureStore


@unittest.skipIf(fixtures.adapter is None, "Use the bundled Python and SQLCipher")
class MultiEncryptedAdapterTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.account, self.peers = "test-account", ("peer-one", "peer-two")
        self.adapter = fixtures.adapter
        self.writers, self.store, _ = fixtures.EncryptedAdapterTests().build(
            self.root, self.account, self.peers[0], ("message_0.db", "message_1.db"))
        self.tables = {peer: "Msg_" + hashlib.md5(peer.encode(), usedforsecurity=False).hexdigest()
                       for peer in self.peers}
        for writer in self.writers:
            writer.execute("INSERT INTO Name2Id VALUES (?)", (self.peers[1],))
            writer.execute(f'CREATE TABLE "{self.tables[self.peers[1]]}" (local_id INTEGER PRIMARY KEY, '
                           "sort_seq INTEGER, local_type INTEGER, real_sender_id INTEGER, message_content BLOB)")
            writer.commit()
        self.store.open_conversations(list(self.peers))

    def tearDown(self):
        self.store.close()
        for writer in self.writers:
            writer.close()
        self.temporary.cleanup()

    def append(self, peer, local_id, sequence, text, shard=0):
        sender = 1 if peer == self.peers[0] else 3
        writer = self.writers[shard]
        writer.execute(f'INSERT INTO "{self.tables[peer]}" VALUES (?, ?, 1, ?, ?)',
                       (local_id, sequence, sender, text.encode()))
        writer.commit()

    def drain(self):
        output = []
        for _ in range(20):
            messages, _ = self.store.query()
            if not messages:
                return output
            self.assertLessEqual(len(messages), 32)
            output.extend(messages)
            self.store.ack([message["key"] for message in messages])
        self.fail("The bounded batches did not drain")

    def test_same_local_id_and_sort_sequence_remain_distinct_across_people_and_shards(self):
        for index, peer in enumerate(self.peers):
            for shard in range(2):
                self.append(peer, 1, 7, f"same-{index}-{shard}", shard)
        messages = self.drain()
        self.assertEqual(4, len(messages))
        self.assertEqual(4, len({message["key"] for message in messages}))
        self.assertEqual(set(self.peers), {message["conversation"] for message in messages})

    def test_global_batches_merge_monotonic_streams_without_losing_a_page(self):
        for number in range(1, 51):
            for index, peer in enumerate(self.peers):
                self.append(peer, number, number * 2 + index, f"message-{index}-{number}")
        messages = self.drain()
        self.assertEqual(100, len(messages))
        self.assertEqual(100, len({message["key"] for message in messages}))
        self.assertEqual(sorted(message["sequence"] for message in messages),
                         [message["sequence"] for message in messages])

    def test_sequence_regression_does_not_skip_a_lower_local_id(self):
        self.append(self.peers[0], 1, 1000, "first local id")
        self.append(self.peers[0], 2, 1, "second local id")
        for number in range(1, 41):
            self.append(self.peers[1], number, number, f"other-{number}")
        messages = self.drain()
        self.assertEqual(42, len(messages))
        self.assertEqual(["first local id", "second local id"],
                         [row["text"] for row in messages if row["conversation"] == self.peers[0]])

    def test_baseline_excludes_both_contacts_history_and_ack_is_required(self):
        for peer in self.peers:
            self.append(peer, 1, 1, "old")
        self.store.open_conversations(list(self.peers))
        self.assertEqual([], self.store.query()[0])
        for peer in self.peers:
            self.append(peer, 2, 2, "new")
        baseline = self.store.baseline.copy()
        messages, _ = self.store.query()
        self.assertEqual(2, len(messages))
        self.assertEqual(baseline, self.store.baseline)
        with self.assertRaises(ValueError):
            self.store.query()
        with self.assertRaises(ValueError):
            self.store.ack([messages[0]["key"]])
        self.store.ack([message["key"] for message in messages])
        self.assertEqual([], self.store.query()[0])

    def test_one_snapshot_connection_per_shard_and_no_unapproved_body_query(self):
        self.append(self.peers[0], 1, 1, "approved")
        statements = []
        calls = []
        for snapshot in self.store.snapshots.values():
            original = snapshot.connect
            def traced(original=original):
                conn = original()
                calls.append(conn)
                conn.set_trace_callback(statements.append)
                return conn
            snapshot.connect = traced
        self.store.query()
        self.assertEqual(2, len(calls))
        message_queries = [query for query in statements if "message_content" in query]
        self.assertEqual(4, len(message_queries))
        self.assertTrue(all(any(table in query for table in self.tables.values())
                            for query in message_queries))

    def test_busy_shard_and_removed_target_fail_without_advancing_watermarks(self):
        baseline = self.store.baseline.copy()
        snapshot = next(iter(self.store.snapshots.values()))
        with patch.object(snapshot, "connect", side_effect=self.adapter.SnapshotBusy):
            with self.assertRaises(self.adapter.SnapshotBusy):
                self.store.query()
        self.assertEqual(baseline, self.store.baseline)
        self.writers[0].execute(f'DROP TABLE "{self.tables[self.peers[1]]}"')
        self.writers[0].commit()
        with self.assertRaises(IdentityChanged):
            self.store.query()
        self.assertEqual(baseline, self.store.baseline)

    def test_invalid_duplicate_unknown_and_group_contacts_are_rejected(self):
        for peers in ([], [self.peers[0], self.peers[0]], ["x@chatroom"], ["filehelper"], ["unknown"],
                      [f"peer-{index}" for index in range(17)]):
            with self.subTest(peers=peers), self.assertRaises(IdentityChanged):
                self.store.open_conversations(peers)

    def test_avatar_is_optional_and_only_projected_when_column_really_exists(self):
        entries = self.store.search_contacts("test-alias")
        self.assertNotIn("avatarUrl", entries[0])
        with closing(self.adapter.sql.connect(self.store.contact_path)) as contact:
            contact.execute("PRAGMA key = \"x'%s'\"" % bytes(range(32)).hex())
            contact.execute("ALTER TABLE contact ADD COLUMN small_head_url TEXT")
            contact.execute("UPDATE contact SET small_head_url=? WHERE username=?",
                            ("https://example.org/selected-avatar", self.peers[0]))
            contact.commit()
        entries = self.store.search_contacts("test-alias")
        self.assertEqual("https://example.org/selected-avatar", entries[0]["avatarUrl"])

    def test_startup_portraits_only_return_the_explicit_whitelist(self):
        entries = self.store.selected_contact_info([self.peers[0]])
        self.assertEqual([self.peers[0]], [item['id'] for item in entries])
        self.assertEqual([], self.store.selected_contact_info(['absent-peer']))


class MultiFixtureTests(unittest.TestCase):
    def test_demo_rotates_contacts_and_shared_store_has_unique_delivery_keys(self):
        with tempfile.TemporaryDirectory() as directory:
            store = MultiFixtureStore(Path(directory), ["demo-peer", "demo-peer-2"])
            try:
                for _ in range(5):
                    store.demo()
                messages, _ = store.query()
                self.assertEqual(["demo-peer", "demo-peer-2", "demo-peer", "demo-peer-2", "demo-peer"],
                                 [message["conversation"] for message in messages])
                self.assertEqual(5, len({message["key"] for message in messages}))
                store.ack([message["key"] for message in messages])
                self.assertEqual([], store.query()[0])
            finally:
                store.close()

    def test_protocol_confirms_entire_whitelist_and_stops(self):
        if sys.version_info[:2] != (3, 12):
            self.skipTest("Worker requires Python 3.12")
        worker = Path(__file__).resolve().parents[2] / "worker" / "main.py"
        with tempfile.TemporaryDirectory() as directory:
            child = subprocess.Popen([sys.executable, "-I", "-B", "-u", str(worker), "--fixture", directory],
                                     stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                     text=True, encoding="utf-8")
            try:
                stamp = dict(session=str(uuid4()), generation=1)
                def send(kind, **fields):
                    child.stdin.write(json.dumps(dict(type=kind, **stamp, **fields)) + "\n")
                    child.stdin.flush()
                def read():
                    return json.loads(child.stdout.readline())
                send("start", account="demo-local", conversation="demo-peer", mode="fixture",
                     conversations=["demo-peer", "demo-peer-2"])
                self.assertEqual("capabilities", read()["type"])
                self.assertEqual(["demo-peer", "demo-peer-2"], read()["conversations"])
                for _ in range(2):
                    send("demo")
                    self.assertEqual("injected", read()["type"])
                send("query")
                messages = read()["messages"]
                self.assertEqual(2, len(messages))
                send("ack", keys=[row["key"] for row in messages])
                self.assertEqual("ack", read()["type"])
                send("stop")
                self.assertEqual("stopped", read()["type"])
                child.communicate(timeout=5)
                self.assertEqual(0, child.returncode)
            finally:
                if child.poll() is None:
                    child.kill()
                child.communicate(timeout=5)


if __name__ == "__main__":
    unittest.main()
