import hashlib
import os
import json
import math
from pathlib import Path
import struct
import subprocess
import sys
import uuid
import wave
import zipfile

ROOT = Path(__file__).resolve().parents[2]
CLI = ROOT / "tests/reference/audio-python/boombox_audio.py"
FFMPEG = str(Path(os.environ["VALHEIM_FFMPEG_ROOT"]) / "bin/ffmpeg.exe")
FFPROBE = str(Path(os.environ["VALHEIM_FFMPEG_ROOT"]) / "bin/ffprobe.exe")
RUN = Path(os.environ["VALHEIM_QA_ROOT"]) / "current/offline" / ("audio-" + uuid.uuid4().hex)
MUSIC = RUN / "Музыка с пробелами"
MUSIC.mkdir(parents=True)
results = []


def call(arguments):
    result = subprocess.run(arguments, capture_output=True, text=True, encoding="utf-8", timeout=90)
    if result.returncode:
        raise RuntimeError(result.stderr or result.stdout)
    return result.stdout


def checksum(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


source = MUSIC / "source.wav"
with wave.open(str(source), "wb") as sound:
    sound.setparams((1, 2, 8000, 0, "NONE", "not compressed"))
    sound.writeframes(b"".join(struct.pack("<h", int(20000 * math.sin(2 * math.pi * 100 * i / 8000)) if any(start <= i / 8000 < start + .12 for start in (.3, 1.3, 2.3, 3.3)) else 0) for i in range(32000)))
for name, codec in (("album.flac", "flac"), ("song.mp3", "libmp3lame"), ("song.ogg", "libvorbis"), ("song.wv", "wavpack")):
    call([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-i", str(source), "-c:a", codec, str(MUSIC / name)])
cue = 'PERFORMER "Исполнитель"\nFILE "album.flac" WAVE\n TRACK 01 AUDIO\n TITLE "Первая"\n INDEX 01 00:00:00\n TRACK 02 AUDIO\n TITLE "Вторая"\n INDEX 00 00:01:70\n INDEX 01 00:02:00\n'
(MUSIC / "album.cue").write_bytes(cue.encode("cp1251"))
call([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-i", str(source), "-metadata", "CUESHEET=" + cue, "-c:a", "flac", str(MUSIC / "embedded.flac")])
with zipfile.ZipFile(MUSIC / "music.zip", "w", zipfile.ZIP_DEFLATED) as archive:
    archive.write(MUSIC / "album.flac", "inside/album.flac")
    archive.writestr("inside/album.cue", cue.encode("cp1251"))
    archive.writestr("../escape.wav", b"forbidden")
before = {path.name: checksum(path) for path in MUSIC.iterdir()}
folders = RUN / "library.json"
folders.write_text(json.dumps({"folders": [str(MUSIC), str(MUSIC)]}), encoding="utf-8")
catalog = RUN / "catalog.json"
summary = json.loads(call([sys.executable, "-B", str(CLI), "scan", "--folders-file", str(folders), "--output", str(catalog), "--ffprobe", FFPROBE]))
data = json.loads(catalog.read_text(encoding="utf-8"))
tracks = data["playlists"][0]["tracks"]
assert len(tracks) == len({track["id"] for track in tracks})
assert "foobarDirectory" not in data and len(data["folders"]) == 1
assert len(data["errors"]) == 1 and "escape.wav" in data["errors"][0]["error"]
results.append({"case": "folder-scan", "passed": True, "tracks": len(tracks), "unsafeMemberRejected": True})
for name, predicate, duration in (
    ("wav", lambda t: t["source"].endswith("source.wav"), 4),
    ("mp3", lambda t: t["source"].endswith("song.mp3"), 4),
    ("ogg", lambda t: t["source"].endswith("song.ogg"), 4),
    ("wv", lambda t: t["source"].endswith("song.wv"), 4),
    ("external-cue", lambda t: t["source"].startswith("file://") and t["source"].endswith("album.cue") and t["subsong"] == 2, 2),
    ("embedded-cue", lambda t: t["source"].endswith("embedded.flac") and t["subsong"] == 2, 2),
    ("zip-cue", lambda t: t["source"].startswith("unpack://") and t["subsong"] == 2, 2),
):
    track = next(track for track in tracks if predicate(track))
    result = json.loads(call([sys.executable, "-B", str(CLI), "prepare", "--catalog", str(catalog), "--track", track["id"], "--output-dir", str(RUN / name), "--ffmpeg", FFMPEG, "--ffprobe", FFPROBE]))
    assert result["status"] == "ready", result
    assert abs(result["durationSeconds"] - duration) < .06, result
    assert result["sampleRate"] == 48000 and result["channels"] == 2 and result["bitsPerSample"] == 16
    assert all(0 <= beat < result["durationSeconds"] for beat in result["bassBeatsSeconds"])
    if "cue" in name:
        assert result["title"] == "Вторая" and result["artist"] == "Исполнитель", result
    results.append({"case": name, "passed": True, "seconds": result["durationSeconds"], "beats": len(result["bassBeatsSeconds"])})
assert before == {path.name: checksum(path) for path in MUSIC.iterdir()}
results.append({"case": "sources-unchanged", "passed": True})
(RUN / "results.json").write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({"status": "passed", "groups": len(results), "report": str(RUN / "results.json")}, ensure_ascii=False))
