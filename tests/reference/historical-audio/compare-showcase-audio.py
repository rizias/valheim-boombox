import array
import json
import math
import operator
import sys
from pathlib import Path

recorded = array.array("f", Path(sys.argv[1]).read_bytes())
reference = array.array("f", Path(sys.argv[2]).read_bytes())
rate = 4000
reference = reference[:rate * 8]
energy = sum(map(operator.mul, reference, reference))

def correlation(offset):
    segment = recorded[offset:offset + len(reference)]
    if len(segment) != len(reference):
        return -1
    norm = math.sqrt(energy * sum(map(operator.mul, segment, segment)))
    return sum(map(operator.mul, reference, segment)) / norm if norm else 0

coarse = max(range(0, min(len(recorded)-len(reference),rate*3), 8), key=correlation)
best = max(range(max(0,coarse-8),coarse+9), key=correlation)
result = {"sampleRate":rate,"referenceSeconds":len(reference)/rate,"matchingOffsetSeconds":best/rate,"correlation":correlation(best),"method":"normalized cross-correlation of recorded game listener and decoded Paranoid beginning at source second 5"}
print(json.dumps(result, indent=2))
Path(sys.argv[3]).write_text(json.dumps(result, indent=2), encoding="utf-8")
if result["correlation"] < .7:
    raise SystemExit("Audio identity check requires review")
