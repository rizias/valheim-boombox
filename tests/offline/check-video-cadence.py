import os
import collections
import json
import subprocess
import sys
from pathlib import Path

video=Path(sys.argv[1]).resolve()
encoder=str(Path(os.environ['VALHEIM_FFMPEG_ROOT']) / 'bin/ffmpeg.exe')
probe=str(Path(os.environ['VALHEIM_FFMPEG_ROOT']) / 'bin/ffprobe.exe')
metadata=json.loads(subprocess.check_output([probe,'-v','error','-show_entries','stream=codec_type,r_frame_rate,duration,nb_frames,width,height:format=duration,size','-of','json',str(video)]))
def frames(filters):
    command=[encoder,'-v','error','-i',str(video),'-map','0:v:0','-an']
    if filters:command+=['-vf',filters,'-fps_mode','vfr']
    text=subprocess.check_output(command+['-f','framemd5','-'],text=True)
    return [line.split(',') for line in text.splitlines() if line and not line.startswith('#')]
decoded=frames(None)
distinct=frames('mpdecimate=hi=768:lo=256:frac=0.1')
duration=float(metadata['format']['duration'])
result={'file':str(video),'metadata':metadata,'decodedFrames':len(decoded),'differentDecodedHashes':len(set(row[-1].strip() for row in decoded)),'framesAfterNearDuplicateRemoval':len(distinct),'effectiveFramesPerSecond':len(distinct)/duration,'method':'Decoded frame hashes and mpdecimate SAD thresholds hi=768 lo=256 frac=0.1; calibrated against the rejected 14-fps recording to reject codec noise; no interpolation added'}
video.with_suffix('.cadence.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps(result,indent=2))
if result['effectiveFramesPerSecond']<50:
    raise SystemExit('Capture does not reach 50 visibly changing frames per second')
