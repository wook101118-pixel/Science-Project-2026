from pathlib import Path
import re,json,hashlib
root=Path(__file__).resolve().parents[1]
p=root/'Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/Stage_01.unity'
before=p.read_bytes();text=before.decode('utf-8-sig')
def change(doc,old,new):
    global text
    pattern=rf'(?ms)^--- !u!\d+ &{doc}\r?\n.*?(?=^--- !u!|\Z)'
    m=re.search(pattern,text)
    if not m or m[0].count(old)!=1:raise RuntimeError('Decoration field not found: '+str(doc))
    text=text[:m.start()]+m[0].replace(old,new)+text[m.end():]
change(1134893715,'  m_LocalScale: {x: 20, y: 20, z: 20}','  m_LocalScale: {x: 3, y: 3, z: 3}')
change(1134893714,'  m_SortingOrder: 0','  m_SortingOrder: -90')
p.write_bytes(text.encode('utf-8'))
restored=text.replace('  m_LocalScale: {x: 3, y: 3, z: 3}','  m_LocalScale: {x: 20, y: 20, z: 20}',1)
# Restore only the named renderer's sorting field for the audit.
match=re.search(r'(?ms)^--- !u!212 &1134893714\r?\n.*?(?=^--- !u!|\Z)',restored)
restored=restored[:match.start()]+match[0].replace('  m_SortingOrder: -90','  m_SortingOrder: 0')+restored[match.end():]
report={'status':'passed' if restored.encode('utf-8')==before else 'failed','source':p.relative_to(root).as_posix(),
 'beforeSha256':hashlib.sha256(before).hexdigest(),'afterSha256':hashlib.sha256(p.read_bytes()).hexdigest(),
 'changes':[{'object':'Firefly Generated Image','transform':1134893715,'scaleBefore':20,'scaleAfter':3},
 {'renderer':1134893714,'sortingBefore':0,'sortingAfter':-90}],
 'scope':'Independent existing decoration without collider, Rigidbody, or scripts. Its PNG, sprite reference, color, position and all map/player data are preserved. Reversing exactly two decoration fields reproduces the previous tested tutorial scene byte for byte.'}
(root/'Logs/Beginner_TutorialDecoration.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
if report['status']!='passed':raise RuntimeError('Unexpected tutorial changes')
print('Existing decoration scale/sorting adjusted; all other tutorial bytes unchanged.')
