# Test-only worker that ignores stop and never produces readiness.
import json
import os
import subprocess
import sys
import time
from pathlib import Path

json.loads(sys.stdin.readline())
child = subprocess.Popen([sys.executable, "-I", "-B", "-c", "import time; time.sleep(60)"],
                         stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
(Path(sys.argv[-1]) / "message_0.sqlite").write_text(f"{os.getpid()},{child.pid}", encoding="ascii")
time.sleep(60)
