import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(r'C:/Users/wook1/GitBub-Unity/Science-Project-2026')
FOLDER = ROOT / 'Logs/ResumeDemoReplay'
audit = json.loads((FOLDER / 'ReplayAudit.json').read_text(encoding='utf-8-sig'))
assert audit['status'] == 'passed', audit['status']
frames = sorted((FOLDER / 'Frames').glob('*.png'))
times = audit['frameTimes']
assert len(frames) == len(times) == audit['capturedFrames']
timeline = ['ffconcat version 1.0']
for index, frame in enumerate(frames):
    duration = times[index + 1] - times[index] if index + 1 < len(times) else 0.1
    assert duration > 0, (index, duration)
    timeline.extend([f"file 'Frames/{frame.name}'", f'duration {duration:.6f}'])
timeline.append(f"file 'Frames/{frames[-1].name}'")
concat = FOLDER / 'actual-frame-timing.ffconcat'
concat.write_text('\n'.join(timeline) + '\n', encoding='utf-8')
sys.path.insert(0, str(ROOT / 'tmp/video-encoding'))
import imageio_ffmpeg

output = FOLDER / 'ScienceProject-Menu-Player-FixedElectrode.mp4'
subprocess.run([
    imageio_ffmpeg.get_ffmpeg_exe(), '-hide_banner', '-loglevel', 'warning',
    '-y', '-safe', '0', '-f', 'concat', '-i', str(concat),
    '-an', '-fps_mode', 'vfr', '-c:v', 'libx264', '-preset', 'fast',
    '-crf', '19', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(output)
], check=True)
print(json.dumps({'video': str(output), 'frames': len(frames),
                  'seconds': times[-1] - times[0] + 0.1,
                  'bytes': output.stat().st_size}, ensure_ascii=False))
