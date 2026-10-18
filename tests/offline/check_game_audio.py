import os
import sys
import json
from pathlib import Path
import re
import subprocess

RECORDING = Path(sys.argv[1]).resolve(strict=True)
ENCODER = str(Path(os.environ["VALHEIM_FFMPEG_ROOT"]) / "bin/ffmpeg.exe")
result = {"recording": str(RECORDING), "method": "100 Hz band matching the generated source; AudioListener capture before and after the recorded pause", "segments": []}
for label, start in (("playing", 120), ("paused", 180)):
    process = subprocess.run([ENCODER, "-hide_banner", "-nostats", "-ss", str(start), "-t", "10", "-i", result["recording"], "-af", "bandpass=f=100:t=h:w=8,volumedetect", "-f", "null", "-"], capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=30)
    if process.returncode:
        raise RuntimeError(process.stderr)
    values = {name: float(re.search(name + r"_volume:\s*(-?[\d.]+) dB", process.stderr).group(1)) for name in ("mean", "max")}
    result["segments"].append({"state": label, "startSeconds": start, "durationSeconds": 10, "exitCode": process.returncode, **values})
playing, paused = result["segments"]
result["peakDropDb"] = playing["max"] - paused["max"]
result["meanDropDb"] = playing["mean"] - paused["mean"]
result["passed"] = result["peakDropDb"] > 20 and result["meanDropDb"] > 15
result["limit"] = "Objective output signal check; not a claim of human listening or musical quality. The accompanying old GDI hwnd video was rejected separately."
destination = Path(sys.argv[2]).resolve() if len(sys.argv) > 2 else Path(os.environ["VALHEIM_QA_ROOT"]) / "evidence/offline/game-audio-signal.json"
destination.parent.mkdir(parents=True, exist_ok=True)
destination.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(result, ensure_ascii=False))
if not result["passed"]:
    raise SystemExit(1)
