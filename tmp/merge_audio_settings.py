from pathlib import Path
import json
import re
import shutil

root = Path(r'C:/Users/wook1/GitBub-Unity/Science-Project-2026')
clone = Path(r'C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild')
relative = Path('Assets/@Project/@sung_wook/@Game/00_Scenes/MainMenu.unity')
target = root / relative

def documents(text):
    parts = re.split(r'(?m)(?=^--- !u!)', text)
    return parts[0], {re.match(r'--- !u!\d+ &(\d+)', part)[1]: part for part in parts[1:]}

before = target.read_text(encoding='utf-8')
assert before == (root / 'tmp/before-audio-settings/MainMenu.unity').read_text(encoding='utf-8')
header, old = documents(before)
_, authored = documents((clone / relative).read_text(encoding='utf-8'))
changed = {'55362811', '55362812'}
added = {key: value for key, value in authored.items() if key not in old}
assert len(added) == 43
assert authored['9223372036854775807'] == old['9223372036854775807']
for value in added.values():
    for parent in re.findall(r'^  m_Father: \{fileID: (\d+)\}', value, re.M):
        assert parent == '55362812' or parent in added
merged = {key: authored[key] if key in changed else value for key, value in old.items()}
roots = '9223372036854775807'
result = header + ''.join(value for key, value in merged.items() if key != roots)
result += ''.join(added.values()) + merged[roots]
_, verified = documents(result)
assert all(value == verified[key] for key, value in old.items() if key not in changed)
target.write_text(result, encoding='utf-8', newline='\n')
shutil.copy2(target, clone / relative)
shutil.copy2(clone / 'Logs/AudioSettingsAuthoringAudit.json', root / 'Logs/AudioSettingsAuthoringAudit.json')
report = {'scene': relative.as_posix(), 'changedContentDocuments': sorted(changed), 'addedSettingsDocuments': len(added), 'existingDocumentsPreserved': len(old)-len(changed), 'existingLayoutColorsAndRootObjectsPreserved': True}
(root / 'Logs/AudioSettingsMergeAudit.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
