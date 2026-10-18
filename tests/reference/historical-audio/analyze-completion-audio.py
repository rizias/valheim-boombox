import array
import json
import math
import pathlib
import sys
import wave

folder = pathlib.Path(sys.argv[1])
meta = json.loads((folder / "output.json").read_text(encoding="utf-8-sig"))
with wave.open(str(folder / "output.wav"), "rb") as audio:
    rate, channels = audio.getframerate(), audio.getnchannels()
    samples = array.array("h", audio.readframes(audio.getnframes()))
mono = [sum(samples[i:i + channels]) / (32768 * channels) for i in range(0, len(samples), channels)]

def amplitude(start, end, frequency):
    values = mono[max(0, int(start * rate)):min(len(mono), int(end * rate))]
    if not values:
        return 0
    coefficient = 2 * math.cos(2 * math.pi * frequency / rate)
    older = old = 0.0
    for value in values:
        current = value + coefficient * old - older
        older, old = old, current
    power = old * old + older * older - coefficient * old * older
    return 2 * math.sqrt(max(0, power)) / len(values)

results = []
for index, phase in enumerate(meta["phases"]):
    start = phase["startDsp"] - meta["firstDspTime"]
    end = phase["endDsp"] - meta["firstDspTime"]
    result = {"name": phase["name"], "start": start, "end": end}
    for frequency in (233, 521, 659, 997):
        result[str(frequency)] = amplitude(start + .8, end - .1, frequency)
    if phase["name"] == "inside-silence-track":
        silence_start = start + 2.5 - phase["sourceTime"]
        silence_end = start + 6.5 - phase["sourceTime"]
        result["silentNative"] = amplitude(silence_start, silence_end, 233)
        result["silentBoombox"] = amplitude(silence_start, silence_end, 521)
        result["silentEnvironment"] = amplitude(silence_start, silence_end, 997)
    if phase["name"] == "transition":
        result["firstTone"] = amplitude(start + .5, start + 1.5, 521)
        result["nextTone"] = amplitude(end - 1.5, end - .3, 659)
    results.append(result)

normal = results[0]
checks = []
def check(name, passed, detail):
    checks.append({"name": name, "passed": bool(passed), "detail": detail})

check("raw_dsp_capture", meta["rawCallbacks"] and meta["callbackCount"] > 0 and meta["fault"] is None, {"callbacks": meta["callbackCount"], "maximumCallbackGap": meta["maximumCallbackGap"]})
span = meta["phases"][-1]["endDsp"] - meta["firstDspTime"]
check("dsp_sample_coverage", abs(meta["seconds"] - span) < .1, {"recorded": meta["seconds"], "dspSpan": span})
check("known_signals_present", normal["233"] > .001 and normal["521"] > .001 and normal["997"] > .001, normal)
for index, result in enumerate(results):
    name = result["name"]
    ratio = result["233"] / max(normal["233"], 1e-12)
    if name == "mode-Quiet":
        check("native_quiet_amplitude", .15 < ratio < .25, ratio)
    if name == "mode-Normal":
        check("native_normal_not_accumulated_" + str(index), .85 < ratio < 1.15, ratio)
    if name == "pause-changed-volume":
        check("restored_changed_native_volume", .34 < ratio < .46 and result["521"] < normal["521"] * .08, ratio)
    if name == "mode-Off":
        check("native_off_" + str(index), ratio < .08, ratio)
    if name in ("pause", "inventory", "active-device-removed", "load-failure"):
        check("native_restored_" + name, .85 < ratio < 1.15, ratio)
        check("boombox_stopped_" + name, result["521"] < normal["521"] * .08, result["521"])
    if name in ("mode-Normal", "mode-Quiet", "mode-Off", "resume", "seek", "equipped"):
        own = result["521"] / max(normal["521"], 1e-12)
        check("boombox_retained_" + str(index), .8 < own < 1.2, own)
    environment = result["997"] / max(normal["997"], 1e-12)
    check("environment_unchanged_" + str(index), .85 < environment < 1.15, environment)
    if name == "inside-silence-track":
        check("silence_does_not_restore_native", result["silentNative"] < normal["233"] * .08 and result["silentBoombox"] < normal["521"] * .08 and result["silentEnvironment"] > normal["997"] * .85, result)
    if name == "transition":
        check("both_tracks_in_output", result["firstTone"] > .001 and result["nextTone"] > .001, result)
    if name == "one-device-remains":
        check("second_device_retains_suppression", ratio < .08 and result["659"] > .001, result)

report = {"capturePoint": meta["capturePoint"], "windowsEndpointCapture": False, "frequencies": {"native": 233, "boombox": 521, "secondTrack": 659, "environment": 997}, "checks": checks, "phases": results}
(folder / "analysis.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
failed = [check for check in checks if not check["passed"]]
print(json.dumps({"checks": len(checks), "failed": failed, "path": str(folder / "analysis.json")}, ensure_ascii=False))
sys.exit(1 if failed else 0)
