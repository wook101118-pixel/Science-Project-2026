from pathlib import Path
import re, json, hashlib, datetime
root=Path.cwd()
folder=root/'Assets/@Project/@sung_wook/@Game/00_Scenes/@InGame/Stage'
pattern=re.compile(r'^--- !u!(\d+) &(\d+)[^\n]*\n.*?(?=^--- !u!|\Z)', re.M|re.S)
def blocks(text): return {m[2]:(m[1],m[0]) for m in pattern.finditer(text)}
source=(folder/'Stage_01.unity').read_text(encoding='utf-8-sig')
template=blocks(source)
ids=['1494224064','1494224065','1494224066','1494224067','1494224068','1494224069','821002790','821002791','821002792','821002793','821002794','1360592280','1360592281','1360592282']
assert all(i in template for i in ids)
report=[]
prior=root/'Logs/Stage5Guidance/CameraTransfer.json'
if prior.exists(): report=json.loads(prior.read_text(encoding='utf-8'))['scenes']
for stage in range(2,6):
    path=folder/f'Stage_{stage:02}.unity'
    if any(item['stage']==stage for item in report): continue
    original=path.read_bytes()
    text=original.decode('utf-8-sig')
    docs=blocks(text)
    ball=next(b for t,b in docs.values() if 'Assembly-CSharp::sung_wook.ElectricBall2D\n' in b)
    go=re.search(r'm_GameObject: \{fileID: (\d+)\}',ball)[1]
    player=next(i for i,(t,b) in docs.items() if t=='4' and f'm_GameObject: {{fileID: {go}}}' in b)
    main=next(i for i,(t,b) in docs.items() if t=='1' and '  m_Name: Main Camera\n' in b)
    mapping={i:str(910000000+n) for n,i in enumerate(ids)}
    mapping['1494224064']=main
    for old,kind in [('1494224065','114'),('1494224066','81'),('1494224067','20'),('1494224068','4')]:
        mapping[old]=next(i for i,(t,b) in docs.items() if t==kind and f'm_GameObject: {{fileID: {main}}}' in b)
    for old,newid in mapping.items():
        if old not in ids[:5]: assert newid not in docs, f'Unexpected camera ID collision {stage}/{newid}'
    replacement={mapping[i]:re.sub(r'(?<=fileID: )\d+|(?<=&)\d+',lambda m:mapping.get(m[0],m[0]),template[i][1]).replace('TrackingTarget: {fileID: 89239553}',f'TrackingTarget: {{fileID: {player}}}') for i in ids}
    changed=pattern.sub(lambda m: replacement.get(m[2],m[0]),text)
    new=''.join(b for i,b in replacement.items() if i not in docs)
    marker=re.search(r'^--- !u!1660057539 ',changed,re.M)
    assert marker
    changed=changed[:marker.start()]+new+changed[marker.start():]
    changed=changed.rstrip('\n')+f'\n  - {{fileID: {mapping["821002793"]}}}\n  - {{fileID: {mapping["1360592282"]}}}\n'
    final=blocks(changed)
    for i,(t,b) in docs.items():
        if i not in replacement and t!='1660057539': assert final[i][1]==b, f'Non-camera object changed {i}'
    backup=root/'tmp/before-fall-camera-20261022'/path.relative_to(root)
    backup.parent.mkdir(parents=True,exist_ok=True)
    assert not backup.exists()
    backup.write_bytes(original)
    assert path.read_bytes()==original, 'Concurrent scene edit'
    path.write_text(changed,encoding='utf-8',newline='\n')
    report.append(dict(stage=stage,path=str(path.relative_to(root)),before=hashlib.sha256(original).hexdigest(),after=hashlib.sha256(path.read_bytes()).hexdigest(),trackingTarget=player,nonCameraObjectsUnchanged=True))
    prior.write_text(json.dumps(dict(status='in-progress',scenes=report),indent=2),encoding='utf-8')
output=root/'Logs/Stage5Guidance/CameraTransfer.json'
output.write_text(json.dumps(dict(status='passed',source='Stage_01',utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),scenes=report),indent=2),encoding='utf-8')
print(json.dumps(report,indent=2))
