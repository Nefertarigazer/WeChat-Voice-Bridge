"""Return a transient database error, then a fatal error after demo injection."""
import json
import sys

start = json.loads(sys.stdin.readline())
stamp = {key: start[key] for key in ("session", "generation")}


def send(kind, **fields):
    print(json.dumps(dict(type=kind, **stamp, **fields)), flush=True)


send("capabilities", mode="fixture", shards=["message_0.sqlite", "message_1.sqlite"])
send("ready", account=start["account"], conversation=start["conversation"])
busy = True
failed = False
for line in sys.stdin:
    kind = json.loads(line)["type"]
    if kind == "stop":
        send("stopped")
        break
    if kind == "demo":
        failed = True
        send("injected")
    elif kind == "query":
        if busy:
            send("warning", code="busy")
            busy = False
        elif failed:
            send("error", code="worker")
            break
        else:
            send("heartbeat")
