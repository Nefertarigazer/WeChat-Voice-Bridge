import json
import os
import sqlite3
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from uuid import uuid4

WORKER = Path(__file__).resolve().parents[2] / "worker"
sys.path.insert(0, str(WORKER))
from db_adapter import ACCOUNT, CONVERSATION, SHARDS, FixtureStore, IdentityChanged


class StoreTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.store = FixtureStore(Path(self.temp.name))
        self.addCleanup(self.store.close)

    def test_baseline_and_readonly_query_handles(self):
        self.assertEqual([], self.store.query()[0])
        reader = self.store.readers[SHARDS[0]]
        with self.assertRaises(sqlite3.OperationalError):
            reader.execute("DELETE FROM message")

    def test_shard_and_sequence_collision_with_ack(self):
        self.store.append(SHARDS[0], 10, "哈哈哈")
        self.store.append(SHARDS[1], 10, "哈哈哈")
        first, _ = self.store.query()
        keys = [row["key"] for row in first]
        self.assertEqual(["哈哈哈", "哈哈哈"], [row["text"] for row in first])
        self.assertEqual(["message_0.sqlite:2", "message_1.sqlite:2"], keys)
        with self.assertRaises(ValueError):
            self.store.ack(keys[:1])
        with self.assertRaises(ValueError):
            self.store.query()
        self.store.ack(keys)
        self.store.append(SHARDS[0], 10, "同序号后来提交")
        self.assertEqual(["同序号后来提交"], [row["text"] for row in self.store.query()[0]])

    def test_uncommitted_wal_writes_are_invisible_then_recovered(self):
        writer = self.store.writers[SHARDS[0]]
        writer.execute("INSERT INTO message(conversation,direction,kind,sort_seq,body) VALUES(?,?,?,?,?)",
                       (CONVERSATION, "incoming", "text", 1, "未提交"))
        self.assertEqual([], self.store.query()[0])
        writer.commit()
        self.assertEqual(["未提交"], [row["text"] for row in self.store.query()[0]])

    def test_filtering_at_database_boundary(self):
        self.store.append(SHARDS[0], 1, "other", conversation="demo-other")
        self.store.append(SHARDS[0], 2, "self", direction="outgoing")
        self.store.append(SHARDS[0], 3, "system", direction="system")
        self.store.append(SHARDS[0], 4, "image", kind="image")
        self.store.append(SHARDS[0], 5, "voice", kind="voice")
        self.store.append(SHARDS[1], 6, "唯一入站")
        self.assertEqual(["唯一入站"], [row["text"] for row in self.store.query()[0]])

    def test_pages_do_not_skip_shards(self):
        for i in range(80):
            self.store.append(SHARDS[0 if i < 70 else 1], i, str(i))
        actual = []
        while (rows := self.store.query()[0]):
            actual.extend(row["sequence"] for row in rows)
            self.store.ack([row["key"] for row in rows])
        self.assertEqual(list(range(80)), actual)

    def test_database_query_error_is_not_an_empty_batch(self):
        writer = self.store.writers[SHARDS[0]]
        writer.execute("DROP TABLE message")
        writer.commit()
        with self.assertRaises(sqlite3.OperationalError):
            self.store.query()

    def test_binding_change_fails_closed(self):
        writer = self.store.writers[SHARDS[0]]
        writer.execute("UPDATE binding SET conversation = 'unknown'")
        writer.commit()
        with self.assertRaises(IdentityChanged):
            self.store.query()


class ProtocolTests(unittest.TestCase):
    def test_no_access_before_start_and_eof_stops(self):
        with tempfile.TemporaryDirectory() as path:
            with self.spawn(path) as process:
                self.assertEqual(0, process.communicate(timeout=5)[1].count(b"Traceback"))
                self.assertEqual(0, process.returncode)
                self.assertEqual([], os.listdir(path))

    def test_protocol_is_scoped_and_stop_is_acknowledged(self):
        with tempfile.TemporaryDirectory() as path:
            session = str(uuid4())
            packets = [
                dict(type="start", session=session, generation=4, account=ACCOUNT, conversation=CONVERSATION),
                dict(type="demo", session=str(uuid4()), generation=4),
                dict(type="query", session=session, generation=4),
                dict(type="stop", session=session, generation=4),
            ]
            with self.spawn(path) as process:
                data = b"".join(json.dumps(packet).encode() + b"\n" for packet in packets)
                output, error = process.communicate(data, timeout=5)
                self.assertEqual(b"", error)
                self.assertEqual(0, process.returncode)
                frames = [json.loads(line) for line in output.splitlines()]
                self.assertEqual(["capabilities", "ready", "heartbeat", "stopped"], [frame["type"] for frame in frames])
                self.assertTrue(all(frame["session"] == session for frame in frames))

    @staticmethod
    def spawn(path):
        return subprocess.Popen([sys.executable, "-I", "-B", "-u", str(WORKER / "main.py"), "--fixture", path],
                                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
