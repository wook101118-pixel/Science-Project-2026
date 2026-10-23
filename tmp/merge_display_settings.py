from pathlib import Path
import json
import re
import shutil

root = Path(r'C:/Users/wook1/GitBub-Unity/Science-Project-2026')
clone = Path(r'C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild')
base = Path('Assets/@Project/@sung_wook/@Game')
targets = {
    base / '00_Scenes/MainMenu.unity': {'55362811', '55362812', '341725223', '585935975', '913510340', '1143943664'},
    base / '02_Prefabs/UI/PauseMenu.prefab': {'4206805838851088942', '1315310713038760952', '3325529820297298492', '8516896113265049023', '8806058240766315632', '3756643935908766237'}
}

def documents(text):
    parts = re.split(r'(?m)(?=^--- !u!)', text)
    return parts[0], {re.match(r'--- !u!\d+ &(\d+)', part)[1]: part for part in parts[1:]}

report = []
for relative, changed in targets.items():
    target = root / relative
    before = target.read_text(encoding='utf-8')
    assert before == (root / 'tmp/before-display-settings' / relative.name).read_text(encoding='utf-8')
    header, old = documents(before)
    _, authored = documents((clone / relative).read_text(encoding='utf-8'))
    assert old.keys() <= authored.keys()
    additions = {key: value for key, value in authored.items() if key not in old}
    assert len(additions) == 111
    for key, original in old.items():
        for prop in ['m_Color', 'm_Sprite', 'm_Font', 'm_FontSize', 'm_Transition']:
            assert re.findall(r'^\s+'+prop+r':.*$', original, re.M) == re.findall(r'^\s+'+prop+r':.*$', authored[key], re.M), (key, prop)
    merged = {key: authored[key] if key in changed else value for key, value in old.items()}
    roots = '9223372036854775807'
    result = header + ''.join(value for key, value in merged.items() if key != roots)
    result += ''.join(additions.values())
    if roots in merged: result += merged[roots]
    _, verified = documents(result)
    assert all(value == verified[key] for key, value in old.items() if key not in changed)
    target.write_text(result, encoding='utf-8', newline='\n')
    shutil.copy2(target, clone / relative)
    report.append({'asset': relative.as_posix(), 'changedContentDocuments': sorted(changed), 'addedDisplayDocuments': len(additions), 'existingDocumentsPreserved': len(old)-len(changed), 'existingColorsSpritesFontsPreserved': True})
(root/'Logs/DisplaySettingsSourceAudit.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
