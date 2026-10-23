"""Export only the validated campaign assets; leave unrelated user changes intact."""
import hashlib
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path

root = Path(__file__).resolve().parent.parent
clone = Path('C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild')
logs = root / 'Logs/ScienceCampaign'
game = Path('Assets/@Project/@sung_wook/@Game')

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()

checks = {}
for name in ('Campaign.json', 'Campaign-Visible.json', 'Stage03-Alternate.json', 'PresentationCapture.json', 'Interaction.json'):
    report = read(logs / name)
    assert report['status'] == 'passed', (name, report['status'])
    checks[name] = report['status']
for mode in ('Write', 'Read'):
    name = f'BgmRegression/{mode}.json'
    report = read(logs / name)
    assert report['status'] == 'passed' and all(item['passed'] for item in report['checks']), name
    checks[name] = report['status']
assert read(clone / 'tmp/BgmSettingsPlayerPrefsBackup.json')['restored']
release = read(clone / 'Logs/ScienceCampaign/ReleaseBuild.json')
assert release['status'] == 'passed' and release['totalErrors'] == 0 and release['qa'] is False
assert not release['extraDefines'], release['extraDefines']

preserved = []
for item in read(root / 'Logs/BgmIntegration/SourceExport.json'):
    relative = Path(item['path'])
    assert digest(root / relative) == digest(clone / relative) == item['sha256'], relative
    preserved.append({'path': relative.as_posix(), 'unchanged': True})
music = game / '03_Sounds/AudioClips/tatamusic-game-game-music-599001.mp3'
assert digest(root / music) == digest(clone / music) == digest(clone / 'tmp/BeforeScienceCampaign20261022' / music)
preserved.append({'path': music.as_posix(), 'unchanged': True})

assets = []
for stage in range(1, 6):
    name = f'Stage_{stage:02}.unity'
    relative = game / '00_Scenes/@InGame/Stage' / name
    before = root / 'tmp/before-science-campaign-20261022/Stage' / name
    assert digest(root / relative) in (digest(before), digest(clone / relative)), relative
    assert digest(root / (str(relative) + '.meta')) == digest(clone / (str(relative) + '.meta'))
    assets.append(relative)

art = game / '08_Presentation/ScienceLab'
assets.append(Path(str(art) + '.meta'))
assets += [p.relative_to(clone) for p in (clone / art).rglob('*') if p.is_file()]
font = game / '08_Presentation/Fonts/SciencePresentation SDF.asset'
assets += [font, Path(str(font) + '.meta')]
for name in ('SciencePresentationView', 'ScienceApparatusView', 'TutorialController2D', 'ElectricStageHudView', 'ElectricGameCompleteView'):
    for extension in ('.cs', '.cs.meta'):
        relative = game / '01_Scripts/ElectricBounce' / (name + extension)
        assert digest(root / relative) == digest(clone / relative), f'Unsynced runtime source: {relative}'
        assets.append(relative)

exported = []
for relative in assets:
    source, target = clone / relative, root / relative
    assert source.is_file(), source
    target.parent.mkdir(parents=True, exist_ok=True)
    if not target.is_file() or digest(target) != digest(source):
        shutil.copy2(source, target)
    assert digest(target) == digest(source), relative
    exported.append({'path': relative.as_posix(), 'sha256': digest(target), 'matchesValidationClone': True})

destination = root / 'Builds/ScienceProject-Final-2026-10-22'
buildsource = clone / 'Builds/ScienceCampaignRelease'
assert not destination.exists(), f'Preserve existing release: {destination}'
shutil.copytree(buildsource, destination)
build_files = []
for source in buildsource.rglob('*'):
    if not source.is_file():
        continue
    relative = source.relative_to(buildsource)
    target = destination / relative
    assert digest(source) == digest(target), relative
    build_files.append({'path': relative.as_posix(), 'sha256': digest(target)})
shutil.copy2(clone / 'Logs/ScienceCampaign/ReleaseBuild.json', logs / 'ReleaseBuild.json')
report = {'status': 'passed', 'exportedUtc': datetime.now(timezone.utc).isoformat(), 'checks': checks,
          'assets': exported, 'preservedBgmAndMenu': preserved, 'release': str(destination / 'ScienceProject.exe'),
          'releaseQaDefineAbsent': True, 'buildFiles': build_files, 'buildFileCount': len(build_files)}
(logs / 'SourceExport.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({'status': report['status'], 'assets': len(exported), 'buildFiles': len(build_files), 'release': report['release']}, ensure_ascii=False))
