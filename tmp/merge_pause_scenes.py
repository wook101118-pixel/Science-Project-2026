from pathlib import Path
import hashlib
import json
import re
import shutil

root = Path(r'C:/Users/wook1/GitBub-Unity/Science-Project-2026')
clone = Path(r'C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild')
base = Path('Assets/@Project/@sung_wook/@Game')
prefab = base / '02_Prefabs/UI/PauseMenu.prefab'
guid = re.search(r'^guid: (\w+)$', (clone / (str(prefab) + '.meta')).read_text(), re.M)[1]

def documents(text):
    parts = re.split(r'(?m)(?=^--- !u!)', text)
    return parts[0], {re.match(r'--- !u!\d+ &(\d+)', doc)[1]: doc for doc in parts[1:]}

report = {'prefabGuid': guid, 'stages': []}
for number in range(1, 6):
    relative = base / f'00_Scenes/Stage/Stage_{number:02}.unity'
    target = root / relative
    before = target.read_text(encoding='utf-8')
    authored = (clone / relative).read_text(encoding='utf-8')
    header, old = documents(before)
    _, new = documents(authored)
    additions = {key: value for key, value in new.items() if key not in old and guid in value}
    assert len(additions) == 1, (relative, len(additions))
    assert all('PrefabInstance:' in value or 'stripped\n' in value for value in additions.values())
    roots_id = '9223372036854775807'
    old_roots = re.findall(r'^  - \{fileID: (\d+)\}$', old[roots_id], re.M)
    authored_roots = re.findall(r'^  - \{fileID: (\d+)\}$', new[roots_id], re.M)
    added_roots = [key for key in authored_roots if key not in old_roots]
    assert len(added_roots) == 1 and added_roots[0] in additions
    final_roots = old[roots_id].rstrip() + '\n' + ''.join(f'  - {{fileID: {key}}}\n' for key in added_roots)
    result = header + ''.join(value for key, value in old.items() if key != roots_id)
    result += ''.join(additions.values()) + final_roots
    _, merged = documents(result)
    assert all(value == merged[key] for key, value in old.items() if key != roots_id)
    assert set(merged) == set(old) | set(additions)
    target.write_text(result, encoding='utf-8', newline='\n')
    shutil.copy2(target, clone / relative)
    report['stages'].append({'scene': str(relative), 'addedDocuments': list(additions), 'addedRoot': added_roots[0], 'existingDocumentsPreserved': len(old) - 1, 'sha256': hashlib.sha256(target.read_bytes()).hexdigest()})

for relative in (prefab, Path(str(prefab) + '.meta')):
    shutil.copy2(clone / relative, root / relative)

settings = root / 'ProjectSettings/EditorBuildSettings.asset'
build_before = settings.read_text(encoding='utf-8')
stage1 = str(base / '00_Scenes/Stage/Stage_01.unity').replace('\\', '/')
if f'    path: {stage1}\n' not in build_before:
    shutil.copy2(settings, root / 'tmp/before-pause-menu/EditorBuildSettings.asset')
    stage_guid = re.search(r'^guid: (\w+)$', (root / (stage1 + '.meta')).read_text(), re.M)[1]
    entry = f'  - enabled: 1\n    path: {stage1}\n    guid: {stage_guid}\n'
    settings.write_text(build_before.replace('  m_configObjects:', entry + '  m_configObjects:'), encoding='utf-8', newline='\n')
shutil.copy2(settings, clone / 'ProjectSettings/EditorBuildSettings.asset')
report['buildSettings'] = 'Stage_01 appended for current-scene restart; existing first scene and scene indices preserved.'
(root / 'Logs/PauseMenuAuthoringAudit.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(report, ensure_ascii=False, indent=2))
