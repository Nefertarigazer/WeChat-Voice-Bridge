"""Start database access only after the parent opens an authorized session."""
import argparse
import errno
import sqlite3
import sys
import traceback
from pathlib import Path
from uuid import UUID

# -I ignores cwd, user site and PYTHONPATH. Add only the bundled worker directory.
sys.path.insert(0, str(Path(__file__).resolve().parent))
from db_adapter import ACCOUNT, CONVERSATION, SHARDS, TEXT_LIMIT, FixtureStore, IdentityChanged
from audited_wechat_keys import UnsupportedVersionError
from ipc_protocol import receive, send


def main():
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    args = argparse.ArgumentParser()
    args.add_argument("--fixture", type=Path, required=True)
    options = args.parse_args()
    store = None
    retryable = (sqlite3.OperationalError,)
    session = ""
    generation = 0
    try:
        packet = receive()
        if packet is None:
            return 0
        session = str(UUID(packet["session"]))
        generation = packet["generation"]
        if packet["type"] != "start" or type(generation) is not int or generation <= 0:
            raise ValueError("invalid startup")
        if sys.version_info[:2] != (3, 12) or sys.maxsize <= 2**32:
            send("error", session, generation, code="runtime")
            return 2
        account, conversation = packet["account"], packet["conversation"]
        directory = options.fixture.resolve(strict=True)
        mode = packet.get("mode", "fixture")
        conversations = packet.get("conversations", [conversation])
        if mode != "browse" and (
                not isinstance(conversations, list) or not 1 <= len(conversations) <= 16
                or any(not isinstance(peer, str) or not peer or len(peer) > 256
                       or peer.endswith("@chatroom") or peer == "filehelper" for peer in conversations)
                or len(set(conversations)) != len(conversations) or conversations[0] != conversation):
            raise IdentityChanged()
        if mode in ("wechat", "browse"):
            sys.path.insert(0, str(Path(__file__).resolve().parent / "deps"))
            from wechat_adapter import SnapshotBusy, WeChatStore, configure, render_speech, sql
            retryable += (SnapshotBusy, sql.OperationalError)
            configure(packet.get("rules"), packet.get("lexicon"))
            store = WeChatStore(directory, packet["root"], account, packet["pids"], None,
                                include_speech=packet.get("includeSpeech") is True,
                                version=packet.get("version"))
            if mode == "wechat":
                store.open_conversations(conversations)
            shards = store.shards
        elif mode == "fixture" and account == ACCOUNT:
            if conversations == [CONVERSATION]:
                store = FixtureStore(directory)
            else:
                from multi_fixture_adapter import MultiFixtureStore
                store = MultiFixtureStore(directory, conversations)
            shards = SHARDS
        else:
            raise IdentityChanged()
        portraits = []
        if mode == "wechat":
            try:
                portraits = store.selected_contact_info(conversations)
            except retryable:
                pass  # Optional display metadata cannot block an otherwise ready message reader.
        send("capabilities", session, generation, mode=mode, shards=shards)
        send("ready", session, generation, account=account, conversation=conversation,
             conversations=conversations if mode != "browse" else [], contacts=portraits)
        while (packet := receive()) is not None:
            if packet.get("session") != session or packet.get("generation") != generation:
                continue
            kind = packet.get("type")
            if kind == "stop":
                store.close()
                send("stopped", session, generation)
                return 0
            if kind == "demo" and mode == "fixture":
                store.demo()
                send("injected", session, generation)
            elif kind == "query" and mode in ("fixture", "wechat"):
                try:
                    messages, elapsed = store.query()
                except retryable as error:
                    code = getattr(error, "sqlite_errorcode", None)
                    if code is not None and code & 255 not in (sqlite3.SQLITE_BUSY, sqlite3.SQLITE_LOCKED):
                        raise
                    send("warning", session, generation, code="busy")
                    continue
                if messages:
                    send("message", session, generation, messages=messages, queryMs=elapsed)
                else:
                    send("heartbeat", session, generation)
            elif kind == "ack":
                store.ack(packet["keys"])
                send("ack", session, generation)
            elif kind == "readings" and mode == "wechat":
                configure(packet["rules"])
                send("readings", session, generation)
            elif kind == "speak" and mode == "wechat":
                parts = packet["parts"]
                if not isinstance(parts, list) or not 1 <= len(parts) <= 2 or not all(
                        isinstance(part, str) for part in parts):
                    raise ValueError("invalid speech parts")
                text = render_speech(parts, packet.get("announcement") is True)
                send("spoken", session, generation, text=text[:TEXT_LIMIT])
            elif kind == "contacts":
                contacts = store.search_contacts(packet.get("query", ""))
                send("contacts", session, generation, contacts=contacts)
            elif kind == "contactindex" and mode == "browse":
                index = getattr(store, '_contact_index', None)
                offset = packet.get("offset", 0)
                if index is None or type(offset) is not int or offset < 0:
                    raise ValueError("invalid contact index request")
                # Metadata only, scoped to the account already verified by this browse Worker.
                rows = index.entries[offset:offset + 50]
                import json
                while len(json.dumps(rows, ensure_ascii=False)) > 200_000 and len(rows) > 1:
                    rows = rows[:len(rows) // 2]
                send("contactindex", session, generation, rows=rows,
                     nextOffset=offset + len(rows), total=len(index.entries))
            elif kind == "lastmessage":
                text = store.last_incoming_text(packet["conversation"])
                send("lastmessage", session, generation, text=text)
            elif kind == "switch" and mode == "browse":
                store.open_conversation(packet["conversation"])
                send("switched", session, generation, conversation=packet["conversation"])
            else:
                raise ValueError("invalid command")
        return 0  # The parent pipe closed.
    except IdentityChanged:
        send("error", session, generation, code="identity")
        return 2
    except ModuleNotFoundError:
        send("error", session, generation, code="dependency")
        return 2
    except OSError as error:
        # The database copies need room on the cache volume.
        if error.errno in (errno.ENOSPC, errno.EDQUOT):
            send("error", session, generation, code="disk")
            return 3
        traceback.print_exc(file=sys.stderr)
        send("error", session, generation, code="worker")
        return 2
    except UnsupportedVersionError as error:
        # Distinguish "outside the supported range / unparsable" from a generic
        # worker failure, and keep the Chinese "不支持此版本" reason on the wire.
        send("error", session, generation, code="unsupported_version", message=str(error))
        return 2
    except Exception:
        # Stderr is discarded by the parent; keep the traceback for local diagnosis.
        traceback.print_exc(file=sys.stderr)
        send("error", session, generation, code="worker")
        return 2
    finally:
        if store is not None:
            store.close()


if __name__ == "__main__":
    raise SystemExit(main())
