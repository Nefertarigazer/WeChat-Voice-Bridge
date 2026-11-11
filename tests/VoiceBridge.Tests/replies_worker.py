"""Protocol double: the user's replies arrive with heartbeats and with messages."""
import json
import sys

start = json.loads(sys.stdin.readline())
stamp = {key: start[key] for key in ("session", "generation")}
target = {key: start[key] for key in ("account", "conversation")}
queries = 0


def send(kind, **fields):
    print(json.dumps(dict(type=kind, **stamp, **fields)), flush=True)


def reply(sequence):
    return dict(account=target["account"], conversation=target["conversation"], sequence=sequence)


send("capabilities", mode="fixture", shards=["message_0.sqlite", "message_1.sqlite"])
send("ready", **target)
for line in sys.stdin:
    packet = json.loads(line)
    kind = packet["type"]
    if kind == "stop":
        send("stopped")
        break
    if kind == "query":
        queries += 1
        if queries == 1:
            send("heartbeat", replies=[reply(2000)])
        elif queries == 2:
            send("message", queryMs=0, replies=[reply(2500)], messages=[
                dict(key="k1", direction="incoming", isText=True, sequence=3000, text="hello",
                     fixtureDetectionMs=None, **target)])
        else:
            send("heartbeat")
    elif kind == "ack":
        # A reply must never be acknowledged like an incoming message.
        if packet["keys"] != ["k1"]:
            send("error", code="worker")
            break
        send("ack")
