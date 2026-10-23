"""Encode passed QA frames at their observed real times; preserve original pixels/aspect."""
import json
import subprocess
from pathlib import Path

root = Path(__file__).resolve().parent.parent
logs = root / 'Logs' / 'ScienceCampaign'
report_path = logs / 'Campaign-Visible.json'
report = json.loads(report_path.read_text(encoding='utf-8-sig'))
assert report['status'] == 'passed', 'Only encode the successful final campaign.'
frames = Path(report['recordFolder'])
times = report['recordFrameTimes']
assert len(times) == report['recordedFrames'] and len(times) > 10
assert frames.resolve().is_relative_to((logs / 'DemoFrames').resolve())
concat = logs / 'ScienceCampaign-Demo.ffconcat'
lines = ['ffconcat version 1.0']
for index, moment in enumerate(times):
    frame = frames / f'{index:05}.png'
    assert frame.is_file(), frame
    duration = times[index + 1] - moment if index + 1 < len(times) else 1 / 12
    assert duration > 0
    lines += [f"file '{frame.as_posix()}'", f'duration {duration:.9f}']
lines.append(f"file '{(frames / f'{len(times)-1:05}.png').as_posix()}'")
concat.write_text('\n'.join(lines) + '\n', encoding='utf-8')
ffmpeg = root / 'tmp' / 'video-encoding' / 'imageio_ffmpeg' / 'binaries' / 'ffmpeg-win-x86_64-v7.1.exe'
output = logs / 'ScienceCampaign-Playthrough.mp4'
subprocess.run([str(ffmpeg), '-hide_banner', '-loglevel', 'warning', '-y', '-safe', '0', '-f', 'concat', '-i', str(concat),
                '-vf', 'scale=1600:900:force_original_aspect_ratio=decrease,pad=1600:900:(ow-iw)/2:(oh-ih)/2:color=0xf5f7f2,setsar=1',
                '-r', '30', '-c:v', 'libx264', '-preset', 'medium', '-crf', '20', '-pix_fmt', 'yuv420p', '-an',
                '-movflags', '+faststart', str(output)], check=True)
manifest = {
    'status': 'passed', 'report': str(report_path), 'frames': len(times), 'sourceWidth': report['recordWidth'],
    'sourceHeight': report['recordHeight'], 'sourceFolder': str(frames), 'output': str(output),
    'durationSeconds': times[-1] - times[0] + 1 / 12, 'bytes': output.stat().st_size,
    'method': 'Actual ScreenCapture frames, held for observed realtime intervals. Aspect-preserving scale/pad to 1600x900, 30 fps. No synthesized gameplay frames or audio.'
}
(logs / 'DemoEncoding.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(manifest, ensure_ascii=False, indent=2))
