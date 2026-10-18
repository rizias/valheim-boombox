import os
import array
import hashlib
import json
import math
from pathlib import Path
import struct
import subprocess
import sys
import uuid
import wave

project = Path(__file__).resolve().parents[2]
fixture = Path(os.environ["VALHEIM_QA_ROOT"]) / "current/offline" / ("historical-audio-" + uuid.uuid4().hex)
fixture.mkdir(parents=True)
checks = []


def execute(name, script, arguments, expected):
    result = subprocess.run([sys.executable, str(script), *map(str, arguments)], capture_output=True, text=True, timeout=15)
    if (result.returncode == 0) != expected:
        raise AssertionError(f"{name}: unexpected exit {result.returncode}: {result.stdout} {result.stderr}")
    checks.append({"name": name, "exitCode": result.returncode, "stdout": result.stdout[-1500:], "stderr": result.stderr[-1500:]})


current = project / "tests/offline"
baseline = project / "tests/reference/historical-audio"
partial = fixture / "partial-completion"
partial.mkdir()
metadata = {"phases": [{"name": "mode-Normal", "startDsp": 0, "endDsp": 2}], "firstDspTime": 0,
            "rawCallbacks": True, "callbackCount": 10, "fault": None, "maximumCallbackGap": .02,
            "seconds": 2, "capturePoint": "offline synthetic contract fixture"}
(partial / "output.json").write_text(json.dumps(metadata), encoding="utf-8")
with wave.open(str(partial / "output.wav"), "wb") as audio:
    audio.setparams((1, 2, 4000, 0, "NONE", "not compressed"))
    audio.writeframes(b"".join(struct.pack("<h", int(32767 * sum(.08 * math.sin(2 * math.pi * frequency * sample / 4000) for frequency in (233, 521, 997)))) for sample in range(8000)))
execute("Old analyzer incorrectly accepts one Normal phase", baseline / "analyze-completion-audio.py", [partial], True)
execute("Current analyzer rejects incomplete sequence", current / "analyze-completion-audio.py", [partial], False)

rate = 4000
tone = array.array("f", (.25 * math.sin(2 * math.pi * 233 * sample / rate) for sample in range(rate * 8)))
reference = fixture / "reference.f32"
reference.write_bytes(tone.tobytes())


def compare(name, samples, expected, old_expected=None, reference_path=reference):
    recorded = fixture / ("recorded-" + name + ".f32")
    recorded.write_bytes(samples.tobytes())
    if old_expected is not None:
        execute("Old compare " + name, baseline / "compare-showcase-audio.py", [recorded, reference_path, fixture / (name + "-old.json")], old_expected)
    execute("Current compare " + name, current / "compare-showcase-audio.py", [recorded, reference_path, fixture / (name + ".json")], expected)


compare("equal-eight-seconds", tone, True, False)
compare("nan-recording", array.array("f", [float("nan")] * (rate * 9)), False, True)
compare("inf-recording", array.array("f", [float("inf")] * (rate * 9)), False)
compare("mismatched-tone", array.array("f", (.25 * math.sin(2 * math.pi * 659 * sample / rate) for sample in range(rate * 8))), False)
compare("short-recording", tone[:rate * 7], False)
short_reference = fixture / "short-reference.f32"
short_reference.write_bytes(tone[:rate * 7].tobytes())
compare("short-reference", tone, False, reference_path=short_reference)
nonfinite_reference = fixture / "nonfinite-reference.f32"
invalid = array.array("f", tone)
invalid[123] = float("nan")
nonfinite_reference.write_bytes(invalid.tobytes())
compare("nan-reference", tone, False, reference_path=nonfinite_reference)

sources = {}
for source in ("analyze-completion-audio.py", "compare-showcase-audio.py"):
    sources[source] = {"baselineSha256": hashlib.sha256((baseline / source).read_bytes()).hexdigest(),
                       "currentSha256": hashlib.sha256((current / source).read_bytes()).hexdigest()}
report = {"passed": True, "scope": "Historical standalone audio utility contracts; no current gameplay or decoder verification", "checks": checks, "sources": sources}
(fixture / "result.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps({"passed": True, "checks": len(checks), "report": str(fixture / "result.json")}, indent=2))
