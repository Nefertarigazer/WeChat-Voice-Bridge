"""UTF-8 JSON Lines. Stdout is reserved for the protocol."""
import json
import sys

MAX_LINE = 65_536


def receive():
    line = sys.stdin.buffer.readline(MAX_LINE + 1)
    if not line:
        return None
    if len(line) > MAX_LINE or not line.endswith(b"\n"):
        raise ValueError("invalid frame")
    packet = json.loads(line)
    if not isinstance(packet, dict):
        raise ValueError("invalid packet")
    return packet


def send(kind, session, generation, **fields):
    packet = dict(type=kind, session=session, generation=generation, **fields)
    print(json.dumps(packet, ensure_ascii=False, separators=(",", ":")), flush=True)
