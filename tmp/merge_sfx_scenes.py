from pathlib import Path
import difflib
import hashlib
import json
import re
import shutil

root = Path(r'C:/Users/wook1/GitBub-Unity/Science-Project-2026')
clone = Path(r'C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild')
base = Path('Assets/@Project/@sung_wook/@Game')

def docs(text):
    parts = re.split(r'(?m)(?=^--- !u!)', text)
    return parts[0], {re.match(r'--- !u!\d+ &(\d+)', part)[1]: part for part in parts[1:]}

audit = {'stages': [], 'artworkChanged': False, 'newAudioClips': 0}
for number in range(1, 6):
    relative = base / f'00_Scenes/Stage/Stage_{number:02}.unity'
    target = root / relative
    before = target.read_text()
    assert before == (root / 'tmp/before-sfx' / relative.name).read_text()
    header, old = docs(before)
    _, authored = docs((clone / relative).read_text())
    ids = ('1328700482', '1328700483', '1328700484') if number <= 2 else ('181826596', '181826597', '181826598')
    replacements = (('  m_IsActive: 1\n', '  m_IsActive: 0\n'), ('  m_PlayOnAwake: 1\n', '  m_PlayOnAwake: 0\n'), ('  m_Enabled: 1\n', '  m_Enabled: 0\n'))
    merged = dict(old)
    for identity, (find, replace) in zip(ids, replacements):
        assert old[identity].count(find) == 1
        merged[identity] = old[identity].replace(find, replace)
        assert authored[identity] == merged[identity]
    result = header + ''.join(merged.values())
    assert all(value == merged[key] for key, value in old.items() if key not in ids)
    target.write_text(result, encoding='utf-8', newline='\n')
    shutil.copy2(target, clone / relative)
    audit['stages'].append({'scene': relative.as_posix(), 'changedDocuments': list(ids), 'existingDocumentsPreserved': len(old) - len(ids)})

relative = base / '00_Scenes/MainMenu.unity'
target = root / relative
before = target.read_text()
assert before == (root / 'tmp/before-sfx/MainMenu.unity').read_text()
header, old = docs(before)
_, authored = docs((clone / relative).read_text())
added = {key: value for key, value in authored.items() if key not in old}
assert len(added) == 1 and 'AudioListener:' in next(iter(added.values()))
camera_id = '459365106'
listener_id = next(iter(added))
camera = old[camera_id].replace('  - component: {fileID: 459365107}\n', f'  - component: {{fileID: 459365107}}\n  - component: {{fileID: {listener_id}}}\n')
assert camera == authored[camera_id]
merged = dict(old)
merged[camera_id] = camera
roots_id = '9223372036854775807'
result = header + ''.join(value for key, value in merged.items() if key != roots_id)
result += ''.join(added.values()) + merged[roots_id]
_, verified = docs(result)
assert all(value == verified[key] for key, value in old.items() if key != camera_id)
target.write_text(result, encoding='utf-8', newline='\n')
shutil.copy2(target, clone / relative)
audit['mainMenu'] = {'changedCamera': camera_id, 'addedListener': listener_id, 'existingDocumentsPreserved': len(old) - 1}

relative = base / '02_Prefabs/UI/SceneTransition.prefab'
_, original = docs((root / relative).read_text())
_, authored = docs((clone / relative).read_text())
changed = [key for key, value in original.items() if authored.get(key) != value]
assert set(changed) == {'8974443812833234699', '3807981918364022801'}
for identity in changed:
    removed = [line[1:] for line in difflib.ndiff(original[identity].splitlines(), authored[identity].splitlines()) if line.startswith('- ')]
    assert not removed
shutil.copy2(clone / relative, root / relative)
audit['transitionPrefab'] = {'changedRootDocuments': changed, 'newDocuments': len(authored) - len(original), 'fadeAndVisualSettingsPreserved': True}
shutil.copy2(clone / 'Logs/SfxAuthoringAudit.json', root / 'Logs/SfxAuthoringAudit.json')
(root / 'Logs/SfxSourceMergeAudit.json').write_text(json.dumps(audit, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(audit, ensure_ascii=False, indent=2))
