from pathlib import Path
import hashlib,json,re,datetime

root=Path(__file__).resolve().parents[1]
base=json.loads((root/'tmp/Reevaluation_BeforeHashes.json').read_text(encoding='utf-8-sig'))
before={x['path']:x['sha256'].upper() for x in base}
def digest(p): return hashlib.sha256(p.read_bytes()).hexdigest().upper()
current={p.relative_to(root).as_posix():digest(p) for p in (root/'Assets/@Project/@sung_wook/@Game').rglob('*') if p.is_file()}
missing=sorted(set(before)-set(current)); added=sorted(set(current)-set(before))
changed=[p for p,h in before.items() if current.get(p)!=h]
media_ext={'.png','.jpg','.jpeg','.tga','.psd','.wav','.mp3','.ogg','.ttf','.otf','.mat','.shader','.shadergraph','.asset','.fbx','.mp4'}
media=[p for p in before if Path(p).suffix.lower() in media_ext]
media_changed=[p for p in media if p in changed]
meta_changed=[p for p in changed if p.endswith('.meta')]
unexpected_added=[p for p in added if not (p.endswith('.cs') or p.endswith('.cs.meta'))]
def visual_fields(path):
    lines=path.read_text(encoding='utf-8-sig').splitlines(); doc=''; fields={}; i=0
    while i<len(lines):
        line=lines[i]; m=re.match(r'^--- !u!\d+ &(.+)$',line)
        if m:doc=m[1]
        m=re.match(r'^(\s+)(\w+):',line)
        if m and (re.search(r'sprite|material|color|fontasset',m[2],re.I) or m[2]=='m_Font'):
            indent=len(m[1]); block=[line]; j=i+1
            if line.rstrip().endswith(':'):
                while j<len(lines):
                    child=lines[j]; ci=len(child)-len(child.lstrip())
                    if child.startswith('---') or (child.strip() and (ci<indent or ci==indent and not child.lstrip().startswith('-'))):break
                    block.append(child);j+=1
            fields[(doc,m[2])]='\n'.join(block)
        i+=1
    return fields
visual=[]
backup=root/'tmp/before-reevaluation-20261021'
for old in sorted(backup.rglob('*')):
    if not old.is_file() or old.suffix not in {'.prefab','.unity'}:continue
    relative=old.relative_to(backup).as_posix()
    target=root/'Assets/@Project/@sung_wook/@Game'/ ('02_Prefabs/'+relative if relative.startswith('UI/') else relative)
    if not target.exists():continue
    a,b=visual_fields(old),visual_fields(target)
    differences=[{'id':k[0],'field':k[1],'before':v,'after':b.get(k)} for k,v in a.items() if b.get(k)!=v]
    # New UI objects must use visual values already present in this prefab/scene or its existing UI templates.
    visual.append({'path':target.relative_to(root).as_posix(),'originalVisualBlocks':len(a),'unchanged':not differences,'differences':differences})
scenes=[]
for n in range(2,6):
    p=f'Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/Stage_{n:02}.unity'
    scenes.append({'stage':n,'entireSceneUnchanged':before[p]==current[p]})
passed=not missing and not media_changed and not meta_changed and not unexpected_added and all(x['unchanged'] for x in visual) and all(x['entireSceneUnchanged'] for x in scenes)
report={'status':'passed' if passed else 'failed','createdUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
 'baselineFiles':len(before),'missingFiles':missing,'changedFiles':changed,'addedFunctionalFiles':added,
 'unexpectedAddedAssets':unexpected_added,'existingMediaAndMaterials':{'count':len(media),'changed':media_changed},
 'existingMetadataChanged':meta_changed,'visualResourceAndColorFields':visual,'stage2to5StoredMapsAndPhysics':scenes,
 'scope':'Existing sung_wook game assets. Saved UI clones may add existing-style buttons/text. Original sprites, font resources, colors and material references are compared by original document ID. Stage1 intentionally reuses its map and moves one existing contact station, adds tutorial function, and matches deceleration9 to the campaign.'}
(root/'Logs/Beginner_AssetPreservation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'status':report['status'],'media':len(media),'mediaChanged':media_changed,'changedFiles':len(changed),'newFiles':len(added),'visualFiles':len(visual),'visualMismatches':sum(not v['unchanged'] for v in visual)},ensure_ascii=False))
