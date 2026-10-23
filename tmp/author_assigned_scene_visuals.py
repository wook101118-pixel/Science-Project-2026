from pathlib import Path
import json,re,shutil,hashlib
from inspect_assigned_scenes import ROOT,BASE,PATHS,docs,field,ref

BACKUP=ROOT/'tmp/assigned-scenes-before-visuals'
BACKUP.mkdir(exist_ok=True)
PRIMITIVE='311925a002f4447b3a28927169b83ea6'
CIRCLE='a86470a33a6bf42c4b3595704624658b'
NORMAL='{fileID: -4811868259617723127, guid: 722c3c5c68bb3ff4a86ac7c11b654af9, type: 3}'
CHANGE='fe86a7abd1bc9004ebf323b1dd5307c2'
VECTOR='eb2e6a53ee4242d2a7c85504a190252a'
COLLIDERS={58,60,61,64,66,68,70,19719996,19719997}

def setfield(d,name,value):
    pattern=r'^  '+re.escape(name)+r': .*?$'
    assert re.search(pattern,d['text'],re.M),(d['id'],name)
    d['text']=re.sub(pattern,'  '+name+': '+str(value),d['text'],count=1,flags=re.M)

def addfield(d,name,value):
    if field(d['text'],name) is not None:setfield(d,name,value)
    else:d['text']=d['text'].rstrip('\n')+'\n  '+name+': '+str(value)+'\n'

def author(path,index):
    saved=BACKUP/path.name
    assert not saved.exists(),f'Refusing to overwrite backup {saved}'
    shutil.copy2(path,saved)
    raw=path.read_bytes(); newline='\r\n' if b'\r\n' in raw else '\n'; bom=raw.startswith(b'\xef\xbb\xbf')
    head,ds=docs(path); original={d['id']:d['text'] for d in ds}; types={d['id']:d['typ'] for d in ds}
    byid={d['id']:d for d in ds}
    gos={d['id']:d for d in ds if d['typ']==1 and field(d['text'],'m_Name') is not None}
    trs={ref(d['text'],'m_GameObject'):d for d in ds if d['typ'] in (4,224) and ref(d['text'],'m_GameObject')}
    comps={gid:[] for gid in gos}
    for d in ds:
        gid=ref(d['text'],'m_GameObject')
        if gid in comps:comps[gid].append(d)
    def pathname(gid):
        go=gos.get(gid)
        if not go:return '<stripped>'
        tr=trs.get(gid);parent=byid.get(ref(tr['text'],'m_Father')) if tr else None
        return (pathname(ref(parent['text'],'m_GameObject'))+'/' if parent else '')+field(go['text'],'m_Name')
    changed=[];disabled=[];vectors=[]
    # Only direct scene SpriteRenderer documents; prefab asset geometry is owned elsewhere.
    if path.parent.name=='Stage':
        for d in ds:
            if d['typ']!=212:continue
            gid=ref(d['text'],'m_GameObject');sprite=field(d['text'],'m_Sprite') or '';name=pathname(gid)
            if name=='Player/Visual':continue
            if PRIMITIVE not in sprite and CIRCLE not in sprite:continue
            owncollider=any(c['typ'] in COLLIDERS for c in comps.get(gid,[]))
            # All source bodies keep their authored collider/Transform and enabled state.
            if owncollider or (name.startswith('GearLever') and name.endswith('/Visual')):
                setfield(d,'m_Sprite',NORMAL)
                setfield(d,'m_Color','{r: 1, g: 1, b: 1, a: 1}')
                changed.append(dict(id=d['id'],path=name,action='existing Normal_Block'))
            elif name.startswith('Stage '):
                setfield(d,'m_Enabled',0)
                disabled.append(dict(id=d['id'],path=name,action='decoration renderer disabled'))
    for d in ds:
        if d['typ']==114 and VECTOR in d['text']:
            setfield(d,'m_Enabled',0);vectors.append(d['id'])
    changes=[d for d in ds if d['typ']==114 and CHANGE in d['text']]
    assert len(changes)==1,(path,len(changes))
    change=changes[0];playerid=ref(change['text'],'m_GameObject')
    playertr=trs[playerid]
    visuals=[gid for gid,go in gos.items() if field(go['text'],'m_Name')=='Visual' and ref(trs[gid]['text'],'m_Father')==playertr['id']]
    assert len(visuals)==1,(path,visuals)
    visualid=visuals[0];visualtr=trs[visualid]
    bodies=[d for d in comps[visualid] if d['typ']==212]
    assert len(bodies)==1,(path,bodies)
    body=bodies[0]
    assert CIRCLE in field(body['text'],'m_Sprite'),(path,'neutral body changed')
    positive=field(change['text'],'positiveSprite')
    assert positive and 'guid: 65098d0c25ad8a24bbb75c46d2e5ccb9' in positive
    goid=821000000000000000+index*10+1;trid=goid+1;srid=goid+2
    assert not any(x in byid for x in (goid,trid,srid))
    layer=field(gos[visualid]['text'],'m_Layer')
    chargego=f'''--- !u!1 &{goid}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {trid}}}
  - component: {{fileID: {srid}}}
  m_Layer: {layer}
  m_Name: Charge Texture
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
'''
    chargetr=f'''--- !u!4 &{trid}
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {goid}}}
  serializedVersion: 2
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 0, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {{fileID: {visualtr['id']}}}
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
'''
    chargesr=dict(typ=212,id=srid,text=re.sub(r'^--- !u!212 &-?\d+',f'--- !u!212 &{srid}',body['text'],count=1))
    setfield(chargesr,'m_GameObject',f'{{fileID: {goid}}}')
    setfield(chargesr,'m_Enabled',0)
    setfield(chargesr,'m_Sprite',positive)
    setfield(chargesr,'m_Color','{r: 1, g: 1, b: 1, a: 0}')
    setfield(chargesr,'m_SortingOrder',int(field(body['text'],'m_SortingOrder'))+1)
    children=re.search(r'^  m_Children:.*?(?=^  m_Father:)',visualtr['text'],re.M|re.S)
    assert children
    text=children[0]
    text='  m_Children:\n'+(text.split('\n',1)[1] if text.split('\n',1)[0]=='  m_Children:' else '')+f'  - {{fileID: {trid}}}\n'
    visualtr['text']=visualtr['text'][:children.start()]+text+visualtr['text'][children.end():]
    addfield(change,'chargeRenderer',f'{{fileID: {srid}}}')
    setfield(change,'existingNeutralVisual',f'{{fileID: {visualid}}}')
    newdocs=[dict(typ=1,id=goid,text=chargego),dict(typ=4,id=trid,text=chargetr),chargesr]
    insertion=next((i for i,d in enumerate(ds) if d['typ']==1660057539),len(ds))
    ds[insertion:insertion]=newdocs
    allowedtypes={212,114}
    for d in ds:
        if d['id'] not in original:continue
        if d['text']!=original[d['id']]:assert d['typ'] in allowedtypes or d['id']==visualtr['id'],(path,d['id'],d['typ'])
    output=head+''.join(d['text'] for d in ds)
    data=output.replace('\n',newline).encode('utf-8')
    if bom:data=b'\xef\xbb\xbf'+data
    path.write_bytes(data)
    result=dict(scene=str(path.relative_to(ROOT)),overlay=dict(go=goid,transform=trid,renderer=srid,parent=visualtr['id'],neutral=visualid),replaced=changed,disabled=disabled,vectors=vectors,newDocuments=3,oldDocuments=len(original),newSha256=hashlib.sha256(data).hexdigest())
    return result

results=[author(path,index) for index,path in enumerate(PATHS,1)]
(ROOT/'tmp/assigned-scene-visual-changes.json').write_text(json.dumps(results,indent=2,ensure_ascii=False),encoding='utf-8')
for r in results:print(Path(r['scene']).name, 'overlays=1', 'body replacements=',len(r['replaced']),'decorations hidden=',len(r['disabled']),'vector displays disabled=',len(r['vectors']))
