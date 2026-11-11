"""Speak short texts with Microsoft Edge's read-aloud voices through edge-tts.

The host keeps one copy of this process for the whole session: loading edge-tts
and its TLS contexts takes several seconds on Windows, a request afterwards only
costs the network round trip.

Protocol, one UTF-8 JSON object per line in each direction:
  in:  {"text": "...", "voice": "zh-CN-XiaoxiaoNeural", "speed": 1.0}
  out: {"ok": true, "audio": "<base64 MP3>"}  or  {"ok": false, "code": "..."}
Codes: request, no-audio, too-large, network. The first line written is
{"ready": true} once edge-tts has loaded, or {"ok": false, "code": "dependency"}
when this build carries no edge-tts. stdout carries nothing else, and no text
of a message is ever written to stderr.
"""
import asyncio
import base64
import json
import re
import sys
from pathlib import Path

# -I ignores cwd, user site and PYTHONPATH. Add only the bundled speech packages.
sys.path.insert(0, str(Path(__file__).resolve().parent / "tts-deps"))

VOICE = re.compile(r"\A[A-Za-z0-9-]{3,96}\Z")
MAX_TEXT = 4096
MAX_AUDIO = 16 * 1024 * 1024
MAX_LINE = 64 * 1024


def reply(payload):
    sys.stdout.buffer.write(json.dumps(payload).encode("ascii") + b"\n")
    sys.stdout.buffer.flush()


def parse(line):
    request = json.loads(line.decode("utf-8"))
    text, voice, speed = request.get("text"), request.get("voice"), request.get("speed", 1)
    if not isinstance(text, str) or not text.strip() or len(text) > MAX_TEXT:
        raise ValueError("text")
    if not isinstance(voice, str) or not VOICE.match(voice):
        raise ValueError("voice")
    if isinstance(speed, bool) or not isinstance(speed, (int, float)) or not 0.25 <= speed <= 4:
        raise ValueError("speed")
    # edge-tts takes a signed percentage; the service accepts -50% .. +100%.
    percent = max(-50, min(100, round((float(speed) - 1) * 100)))
    return text, voice, f"{percent:+d}%"


async def speak(edge_tts, text, voice, rate):
    audio = bytearray()
    async for chunk in edge_tts.Communicate(text, voice, rate=rate).stream():
        if chunk.get("type") == "audio":
            audio.extend(chunk["data"])
            if len(audio) > MAX_AUDIO:
                raise OverflowError()
    return bytes(audio)


def main():
    sys.stdout.reconfigure(newline="\n")
    try:
        import edge_tts
        from edge_tts import exceptions
    except ImportError:
        reply({"ok": False, "code": "dependency"})
        return 5
    loop = asyncio.new_event_loop()
    reply({"ready": True})
    while True:
        line = sys.stdin.buffer.readline(MAX_LINE)
        if not line:
            return 0  # The host closed the pipe.
        try:
            text, voice, rate = parse(line)
        except (ValueError, UnicodeDecodeError, AttributeError):
            reply({"ok": False, "code": "request"})
            continue
        try:
            audio = loop.run_until_complete(speak(edge_tts, text, voice, rate))
        except exceptions.NoAudioReceived:
            reply({"ok": False, "code": "no-audio"})
            continue
        except OverflowError:
            reply({"ok": False, "code": "too-large"})
            continue
        except Exception as error:  # aiohttp, WebSocket and TLS failures all land here
            print("network:" + type(error).__name__, file=sys.stderr)
            reply({"ok": False, "code": "network"})
            continue
        if not audio:
            reply({"ok": False, "code": "no-audio"})
            continue
        reply({"ok": True, "audio": base64.b64encode(audio).decode("ascii")})


if __name__ == "__main__":
    raise SystemExit(main())
