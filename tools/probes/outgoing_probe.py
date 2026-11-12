"""Read-only probe: how do the user's own messages look in the PC message shards?

Task 2 (reminder mode) needs to notice when the user replies, including replies
sent from the phone. This probe answers, for the selected contacts only:
  * which sender ids appear in each private chat (peer / someone else / none),
  * which local_type values the non-peer rows have,
  * whether sort_seq follows local_id order (so outgoing and incoming rows compare),
  * a few metadata columns (status, origin_source) for the non-peer rows.

It prints counts and types only: never message bodies, user names or ids.
Usage (WeChat must be running):
  runtime\\python\\python.exe -I -B tools\\probes\\outgoing_probe.py <Weixin pid> [...]
Follows AGENTS.md: TemporaryDirectory on the local scratch drive outside OneDrive,
one snapshot per shard for the whole batch, connections closed before cleanup.
"""
from collections import Counter
from contextlib import closing
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile

REPO = Path(__file__).resolve().parents[2]
# Third-party worker packages (sqlcipher3, zstandard) live next to a built worker.
DEPS = next((path for path in (
    Path(os.environ.get("WVB_WORKER_DEPS", "")),
    REPO / "worker" / "deps",
    REPO / "src" / "VoiceBridge.App" / "bin" / "Release" / "net8.0-windows" / "worker" / "deps",
) if str(path) not in ("", ".") and (path / "sqlcipher3").is_dir()), None)
if DEPS is None:
    raise SystemExit("worker deps not found; build the app or set WVB_WORKER_DEPS")
sys.path.insert(0, str(REPO / "worker"))
sys.path.insert(0, str(DEPS))

from wechat_adapter import WeChatStore  # noqa: E402

# The local scratch drive outside OneDrive: WVB_SCRATCH if set, otherwise the user's temp folder.
SCRATCH = Path(os.environ.get("WVB_SCRATCH") or tempfile.gettempdir())
ROWS_PER_SHARD = 400
METADATA = ("status", "origin_source", "upload_status", "server_seq")


def selected_peers(settings, account):
    peers = [profile.get("ConversationId") for profile in settings.get("ContactProfiles", [])
             if profile.get("Selected") and profile.get("AccountId") == account]
    return [peer for peer in peers if isinstance(peer, str) and peer][:8]


def probe(store, peers):
    report = []
    for number, peer in enumerate(peers, 1):
        table = "Msg_" + hashlib.md5(peer.encode("utf-8"), usedforsecurity=False).hexdigest()
        senders, other_types, none_types, peer_types = Counter(), Counter(), Counter(), Counter()
        metadata = {column: Counter() for column in METADATA}
        inversions = rows_seen = shards_with_table = 0
        distinct_ids = set()
        latest = {"peer": None, "other": None}
        for name, snapshot in store.snapshots.items():
            with closing(snapshot.connect()) as conn:
                if not store.has_table(conn, table):
                    continue
                shards_with_table += 1
                columns = {row[1] for row in conn.execute(f'PRAGMA table_info("{table}")')}
                extra = [column for column in METADATA if column in columns]
                peer_ids = {row[0] for row in conn.execute(
                    "SELECT rowid FROM Name2Id WHERE user_name = ?", (peer,))}
                projection = ", ".join(["local_id", "sort_seq", "local_type", "real_sender_id"] + extra)
                rows = conn.execute(f'SELECT {projection} FROM "{table}" '
                                    f'ORDER BY local_id DESC LIMIT {ROWS_PER_SHARD}').fetchall()
            rows.reverse()
            previous = None
            for row in rows:
                local_id, sequence, local_type, sender = row[:4]
                rows_seen += 1
                distinct_ids.add((name, sender))
                if previous is not None and (sequence or 0) < previous:
                    inversions += 1
                previous = sequence or 0
                if sender in peer_ids:
                    kind = "peer"
                    peer_types[local_type] += 1
                elif sender:
                    kind = "other"
                    other_types[local_type] += 1
                    for column, value in zip(extra, row[4:]):
                        metadata[column][value] += 1
                else:
                    kind = "none"
                    none_types[local_type] += 1
                senders[kind] += 1
                if kind in latest and (latest[kind] is None or (sequence or 0) > latest[kind]):
                    latest[kind] = sequence or 0
        report.append({
            "contact": f"联系人{number}",
            "shards": shards_with_table,
            "rows": rows_seen,
            "senders": dict(senders),
            "distinctSenderIds": len(distinct_ids),
            "peerTypes": dict(peer_types.most_common(8)),
            "otherTypes": dict(other_types.most_common(12)),
            "noSenderTypes": dict(none_types.most_common(8)),
            "otherMetadata": {column: dict(counter.most_common(6))
                              for column, counter in metadata.items() if counter},
            "sortSeqInversions": inversions,
            "latestOtherNewerThanLatestPeer": (latest["other"] or 0) > (latest["peer"] or 0),
            "sortSeqDigits": len(str(latest["peer"] or latest["other"] or 0)),
        })
    return report


def main():
    pids = [int(value) for value in sys.argv[1:]]
    if not pids:
        raise SystemExit("usage: outgoing_probe.py <Weixin pid> [...]")
    settings_path = Path(os.environ["LOCALAPPDATA"]) / "WeChatVoiceBridge" / "settings.json"
    settings = json.loads(settings_path.read_text("utf-8-sig"))
    root, account = settings.get("WeChatRoot"), settings.get("WeChatAccount")
    peers = selected_peers(settings, account)
    if not root or not account or not peers:
        raise SystemExit("no WeChat root, account or selected contacts in settings")
    SCRATCH.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=SCRATCH, prefix="wvb-outgoing-probe-") as cache:
        store = WeChatStore(Path(cache), root, account, pids, version="4.2.0.13")
        try:
            report = probe(store, peers)
        finally:
            store.close()
    print(json.dumps(report, ensure_ascii=False, indent=1))


if __name__ == "__main__":
    main()
