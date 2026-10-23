from pathlib import Path
import re,json

ROOT=Path(__file__).resolve().parents[1]
BASE=ROOT/'Assets/@Project/@sung_wook/@Game/00_Scenes'
PATHS=[BASE/'Stage'/f'Stage_{n:02}.unity' for n in range(1,6)]+[BASE/'sungwook.unity',BASE/'sungwook_ElectricBounce.unity']

def docs(path):
    text=path.read_text(encoding='utf-8-sig')
    matches=list(re.finditer(r'^--- !u!(\d+) &(-?\d+)(?: stripped)?\n',text,re.M))
    return text[:matches[0].start()], [dict(typ=int(m[1]),id=int(m[2]),text=text[m.start():matches[i+1].start() if i+1<len(matches) else len(text)]) for i,m in enumerate(matches)]

def field(text,name):
    m=re.search(r'^  '+re.escape(name)+r': (.*)$',text,re.M)
    return m[1] if m else None

def ref(text,name):
    f=field(text,name)
    m=re.search(r'fileID: (-?\d+)',f or '')
    return int(m[1]) if m else 0

def inventory(path):
    head,ds=docs(path); byid={d['id']:d for d in ds}
    gos={d['id']:d for d in ds if d['typ']==1 and field(d['text'],'m_Name') is not None}
    transforms={ref(d['text'],'m_GameObject'):d for d in ds if d['typ'] in (4,224) and ref(d['text'],'m_GameObject')}
    components={gid:[] for gid in gos}
    for d in ds:
        gid=ref(d['text'],'m_GameObject')
        if gid in components: components[gid].append(d)
    def pathname(gid):
        go=gos.get(gid)
        if not go:return '<stripped>'
        name=field(go['text'],'m_Name'); tr=transforms.get(gid)
        parent=byid.get(ref(tr['text'],'m_Father')) if tr else None
        return (pathname(ref(parent['text'],'m_GameObject'))+'/' if parent else '')+name
    records=[]
    for d in ds:
        if d['typ']!=212:continue
        gid=ref(d['text'],'m_GameObject'); tr=transforms.get(gid)
        if not tr:continue
        records.append(dict(id=d['id'],go=gid,path=pathname(gid),active=field(gos[gid]['text'],'m_IsActive'),enabled=field(d['text'],'m_Enabled'),sprite=field(d['text'],'m_Sprite'),colliders=[c['typ'] for c in components[gid] if c['typ'] in (58,60,61,64,66,68,70,19719996,19719997)],other=[c['typ'] for c in components[gid] if c['typ'] not in (4,224,1,212)],pos=field(tr['text'],'m_LocalPosition'),scale=field(tr['text'],'m_LocalScale')))
    return records

if __name__=='__main__':
    allrec={p.name:inventory(p) for p in PATHS}
    out=ROOT/'tmp/assigned_scene_inventory.json'
    out.write_text(json.dumps(allrec,ensure_ascii=False,indent=2),encoding='utf-8')
    for name,recs in allrec.items():
        print('\n'+name)
        for r in recs:
            print(f"{r['id']} a{r['active']} e{r['enabled']} c{r['colliders']} {r['path']} {r['pos']} {r['scale']} {r['sprite']}")
