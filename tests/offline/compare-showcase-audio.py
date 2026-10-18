import array
import json
import math
import operator
import sys
from pathlib import Path

recorded = array.array("f", Path(sys.argv[1]).read_bytes())
reference = array.array("f", Path(sys.argv[2]).read_bytes())
rate = 4000
if len(reference) < rate * 8:
    raise SystemExit("Audio identity check requires at least 8 seconds of reference PCM")
if not all(math.isfinite(value) for value in recorded) or not all(math.isfinite(value) for value in reference):
    raise SystemExit("Audio identity check rejects nonfinite PCM samples")
reference = reference[:rate * 8]
if len(recorded) < len(reference):
    raise SystemExit("Audio identity check requires a complete 8-second recorded window")
energy = sum(map(operator.mul, reference, reference))

def correlation(offset):
    segment = recorded[offset:offset + len(reference)]
    if len(segment) != len(reference):
        return -1
    norm = math.sqrt(energy * sum(map(operator.mul, segment, segment)))
    return sum(map(operator.mul, reference, segment)) / norm if norm else 0

last_offset = min(len(recorded) - len(reference), rate * 3)
coarse = max(range(0, last_offset + 1, 8), key=correlation)
best = max(range(max(0, coarse - 8), min(last_offset, coarse + 8) + 1), key=correlation)
result = {"sampleRate":rate,"referenceSeconds":len(reference)/rate,"matchingOffsetSeconds":best/rate,"correlation":correlation(best),"method":"normalized cross-correlation of recorded game listener and decoded Paranoid beginning at source second 5"}
print(json.dumps(result, indent=2))
Path(sys.argv[3]).write_text(json.dumps(result, indent=2), encoding="utf-8")
if not math.isfinite(result["correlation"]) or result["correlation"] < .7:
    raise SystemExit("Audio identity check requires review")
