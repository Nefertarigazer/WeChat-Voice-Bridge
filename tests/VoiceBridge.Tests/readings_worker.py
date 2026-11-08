"""Protocol double for queuing messages before changing the active readings."""
import json
import sys

start = json.loads(sys.stdin.readline())
stamp = {key: start[key] for key in ("session", "generation")}
target = {key: start[key] for key in ("account", "conversation")}
pending = False
reading = start["rules"][0]["say"]


def send(kind, **fields):
    print(json.dumps(dict(type=kind, **stamp, **fields)), flush=True)


send("capabilities", mode="fixture", shards=["message_0.sqlite", "message_1.sqlite"])
send("ready", **target)
for line in sys.stdin:
    packet = json.loads(line)
    kind = packet["type"]
    if kind == "stop":
        send("stopped")
        break
    if kind == "demo":
        pending = True
        send("injected")
    elif kind == "query":
        if pending:
            pending = False
            send("message", queryMs=0, messages=[
                dict(key=str(i), direction="incoming", isText=True, sequence=i, text=reading,
                     fixtureDetectionMs=None, speech={"parts": ["demo"], "announcement": False}, **target)
                for i in range(3)
            ])
        else:
            send("heartbeat")
    elif kind == "ack":
        send("ack")
    elif kind == "readings":
        reading = packet["rules"][0]["say"]
        send("readings")
    elif kind == "speak":
        send("spoken", text=reading)
