import datetime
import json
import subprocess
import sys
from pathlib import Path

folder = Path(sys.argv[1]).resolve()
target = Path(sys.argv[2]).resolve()
record = json.loads((folder / "recording.json").read_text(encoding="utf-8-sig"))
frames = record["samples"]
utc = lambda value: datetime.datetime.fromisoformat(value.replace("Z", "+00:00")).timestamp()
times = [utc(frame["utc"]) for frame in frames]
if not times or any(later <= earlier for earlier, later in zip(times, times[1:])):
    raise ValueError("Recording frame timestamps must strictly increase")
lines = ["ffconcat version 1.0"]
for index, frame in enumerate(frames):
    path = (folder / frame["file"]).resolve()
    if path.parent != folder or not path.is_file():
        raise ValueError("Frame path escaped its recording")
    duration = times[index + 1] - times[index] if index + 1 < len(times) else 1 / 30
    lines += [f"file '{path.as_posix()}'", f"duration {duration:.9f}"]
lines += [f"file '{(folder / frames[-1]['file']).as_posix()}'"]
concat = folder / "frames.ffconcat"
concat.write_text("\n".join(lines) + "\n", encoding="utf-8")
offset = utc(record["sound"]["firstUtc"]) - times[0]
start = .4
audio_trim = max(0, start - offset)
audio_delay = max(0, offset - start) * 1000
target.parent.mkdir(parents=True, exist_ok=True)
encoder = str(Path(sys.argv[3]).resolve())
filters = f"[0:v]trim=start={start}:duration=10,setpts=PTS-STARTPTS,fps=30[v];[1:a]atrim=start={audio_trim:.9f},asetpts=PTS-STARTPTS,adelay={audio_delay:.3f}:all=1,apad,atrim=duration=10,afade=t=in:d=0.04,afade=t=out:st=9.85:d=0.15[a]"
subprocess.run([encoder,"-hide_banner","-loglevel","error","-y","-f","concat","-safe","0","-i",str(concat),"-i",str(folder / record["sound"]["file"]),"-filter_complex",filters,"-map","[v]","-map","[a]","-c:v","libx264","-preset","medium","-crf","21","-pix_fmt","yuv420p","-c:a","aac","-b:a","192k","-t","10","-movflags","+faststart",str(target)],check=True)
summary = {"output":str(target),"audioOffsetSeconds":offset,"sourceFrames":len(frames),"sourceDuration":times[-1]-times[0],"videoStartTrim":start}
(target.with_suffix(".json")).write_text(json.dumps(summary,indent=2),encoding="utf-8")
print(json.dumps(summary,indent=2))
