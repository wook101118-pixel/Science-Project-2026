from pathlib import Path
import re
import json
import hashlib

BASE = Path('Assets/@Project/@sung_wook/@Game/02_Prefabs/Modules')
OUT = Path('tmp/existing-stage-art')
OUT.mkdir(parents=True, exist_ok=True)
SPRITES = {
    'Normal_Block': (-4811868259617723127, '722c3c5c68bb3ff4a86ac7c11b654af9'),
    'Falling_Block': (149281472327576391, 'a13a5052c514b284ca45ea7a86974ec5'),
    'Star': (1829968164609653595, '63965c769966e3c408605d28f6d68153'),
    'Door': (2997777510098346555, '8686993a80ed8f94cbbfb25d14975ca2'),
    'Plus': (7235916251380086844, '65098d0c25ad8a24bbb75c46d2e5ccb9'),
    'Minus': (-776779474669583278, 'd37ba1e0c26de5e4285a10cdbe811e74'),
}
WHITE = '{r: 1, g: 1, b: 1, a: 1}'
IDENTITY = '{x: 0, y: 0, z: 0, w: 1}'
reports = []

def get(body, field):
    found = re.search(r'^  ' + re.escape(field) + r': ([^\n]*)', body, re.M)
    return found[1] if found else None

def put(body, field, value):
    expression = r'(^  ' + re.escape(field) + r': )[^\n]*'
    if not re.search(expression, body, re.M):
        raise RuntimeError('Missing field ' + field)
    return re.sub(expression, lambda match: match[1] + value, body, count=1, flags=re.M)

def ref(name):
    ident, guid = SPRITES[name]
    return '{fileID: ' + str(ident) + ', guid: ' + guid + ', type: 3}'

class Prefab:
    def __init__(self, name):
        self.path = BASE / (name + '.prefab')
        self.original_bytes = self.path.read_bytes()
        self.original = self.original_bytes.decode('utf-8-sig').replace('\r\n', '\n')
        self.newline = '\r\n' if b'\r\n' in self.original_bytes else '\n'
        self.prefix = self.original[:self.original.index('--- !u!')]
        self.docs = []
        for found in re.finditer(r'^--- !u!(\d+) &(\d+)([^\n]*)\n([\s\S]*?)(?=^--- !u!|\Z)', self.original, re.M):
            self.docs.append({'kind': int(found[1]), 'id': found[2], 'suffix': found[3], 'body': found[4]})
        self.before = {doc['id']: dict(doc) for doc in self.docs}
        self.by_id = {doc['id']: doc for doc in self.docs}
        self.go = {doc['id']: get(doc['body'], 'm_Name') for doc in self.docs if doc['kind'] == 1}

    def owner(self, doc):
        value = get(doc['body'], 'm_GameObject')
        return re.search(r'fileID: (\d+)', value)[1] if value else None

    def named(self, name, kind):
        result = [doc for doc in self.docs if doc['kind'] == kind and self.go.get(self.owner(doc)) == name]
        if len(result) != 1:
            raise RuntimeError(str(self.path) + ': Expected one ' + name + ', found ' + str(len(result)))
        return result[0]

    def all_off(self):
        for doc in self.docs:
            if doc['kind'] == 212:
                doc['body'] = put(doc['body'], 'm_Enabled', '0')

    def art(self, name, sprite, enabled=True, color=WHITE, order=None):
        doc = self.named(name, 212)
        doc['body'] = put(doc['body'], 'm_Sprite', ref(sprite))
        doc['body'] = put(doc['body'], 'm_Enabled', '1' if enabled else '0')
        doc['body'] = put(doc['body'], 'm_Color', color)
        if order is not None:
            doc['body'] = put(doc['body'], 'm_SortingOrder', str(order))
        return doc

    def art_transform(self, name, position, scale):
        doc = self.named(name, 4)
        doc['body'] = put(doc['body'], 'm_LocalPosition', position)
        doc['body'] = put(doc['body'], 'm_LocalScale', scale)
        doc['body'] = put(doc['body'], 'm_LocalRotation', IDENTITY)
        doc['body'] = put(doc['body'], 'm_LocalEulerAnglesHint', '{x: 0, y: 0, z: 0}')

    def save(self):
        added = [doc['id'] for doc in self.docs if doc['id'] not in self.before]
        changed = [doc for doc in self.docs if doc['id'] in self.before and doc['body'] != self.before[doc['id']]['body']]
        # Gameplay components and all collider/rigidbody documents remain byte-for-byte
        # equivalent after line-ending normalization. Only station overlay binding changes.
        protected = []
        for doc in changed:
            if doc['kind'] not in (4, 212, 1839735485):
                if self.path.name == 'ContactChargingStation.prefab' and doc['id'] == '2093923957761826071':
                    expected = self.before[doc['id']]['body'].rstrip('\n') + '\n  chargeRenderer: {fileID: 710210001000000003}\n'
                    assert doc['body'] == expected
                else:
                    protected.append(doc['id'])
        assert not protected, ('Gameplay documents changed', protected)
        output = self.prefix + ''.join('--- !u!' + str(doc['kind']) + ' &' + doc['id'] + doc['suffix'] + '\n' + doc['body'] for doc in self.docs)
        assert len({doc['id'] for doc in self.docs}) == len(self.docs)
        if output == self.original:
            return
        (OUT / self.path.name).write_bytes(self.original_bytes)
        self.path.write_bytes(output.replace('\n', self.newline).encode('utf-8'))
        active = []
        for doc in self.docs:
            if doc['kind'] == 212 and get(doc['body'], 'm_Enabled') == '1':
                sprite = get(doc['body'], 'm_Sprite')
                assert any(sprite == ref(name) for name in SPRITES), ('Unowned active sprite', self.path.name, sprite)
                active.append({'object': self.go.get(self.owner(doc), 'Charge Texture'), 'sprite': sprite})
        reports.append({'prefab': str(self.path), 'changedDocuments': [{'id': doc['id'], 'kind': doc['kind']} for doc in changed], 'addedDocuments': added, 'activeSprites': active, 'gameplayComponentsAndCollidersPreserved': True})

# Moving bodies keep all of their collider, rigidbody and motion transforms.
# Reuse a purely decorative child to compensate for Falling_Block's 100 PPU bottom pivot.
for index in range(1, 4):
    prefab = Prefab('Movement/MovingPlatform_' + str(index).zfill(2))
    prefab.all_off()
    prefab.art('Obstacle Blue Falling Stone ' + str(index).zfill(2), 'Falling_Block', enabled=False)
    prefab.art('Lower Edge - Editable', 'Falling_Block', order=14)
    prefab.art_transform('Lower Edge - Editable', '{x: 0, y: -0.5, z: 0}', '{x: 6.25, y: 6.25, z: 1}')
    prefab.save()

for stage in (2, 3, 5):
    prefab = Prefab('Progress/Stage' + str(stage).zfill(2) + '_Star')
    prefab.all_off()
    prefab.art('Stage' + str(stage).zfill(2) + '_Star', 'Star', enabled=False)
    prefab.art('Star Center', 'Star', order=20)
    prefab.art_transform('Star Center', '{x: 0, y: 0, z: 0}', '{x: 0.7, y: 0.7, z: 1}')
    prefab.save()

prefab = Prefab('Progress/Stage04_Star')
prefab.all_off()
prefab.art('Visual', 'Star')
prefab.save()

prefab = Prefab('Progress/Stage02_Exit')
prefab.all_off()
prefab.art('Visual', 'Door')
prefab.save()

for stage in (3, 4, 5):
    prefab = Prefab('Progress/Stage' + str(stage).zfill(2) + '_Exit')
    prefab.all_off()
    prefab.art('Stage' + str(stage).zfill(2) + '_Exit', 'Door', enabled=False)
    prefab.art('Frame Base', 'Door', order=18)
    prefab.art_transform('Frame Base', '{x: 0, y: 0, z: 0}', '{x: 1.3, y: 1.4, z: 1}')
    prefab.save()

prefab = Prefab('Electric/DistanceForceLauncher')
prefab.all_off()
prefab.art('Moving Charge Source', 'Plus')
prefab.save()

prefab = Prefab('Electric/UniformElectricField')
prefab.all_off()
prefab.art('Lower Plate', 'Normal_Block')
prefab.art('Upper Plate', 'Normal_Block')
prefab.save()

prefab = Prefab('Electric/VanDeGraaffLauncher')
prefab.all_off()
prefab.art('Positive Plate', 'Normal_Block')
prefab.art('Negative Plate', 'Normal_Block')
prefab.art('Charge Dome', 'Plus')
prefab.art('Belt Housing', 'Normal_Block')
prefab.save()

prefab = Prefab('Electric/ContactChargingStation')
prefab.all_off()
prefab.art('Contact Electrode', 'Normal_Block', color='{r: 0.46, g: 0.49, b: 0.5, a: 1}')
body_renderer = prefab.named('Contact Electrode', 212)
body_transform = prefab.named('Contact Electrode', 4)
go_id, transform_id, renderer_id = '710210001000000001', '710210001000000002', '710210001000000003'
assert not any(ident in prefab.by_id for ident in (go_id, transform_id, renderer_id))
template_go = next(doc for doc in prefab.docs if doc['kind'] == 1 and doc['id'] == prefab.owner(body_renderer))
overlay_go = dict(template_go)
overlay_go['id'] = go_id
overlay_go['body'] = put(overlay_go['body'], 'm_Name', 'Charge Texture')
overlay_go['body'] = re.sub(r'  m_Component:\n(?:  - component: [^\n]*\n)+', '  m_Component:\n  - component: {fileID: ' + transform_id + '}\n  - component: {fileID: ' + renderer_id + '}\n', overlay_go['body'], count=1)
overlay_transform = dict(body_transform)
overlay_transform['id'] = transform_id
overlay_transform['body'] = put(overlay_transform['body'], 'm_GameObject', '{fileID: ' + go_id + '}')
overlay_transform['body'] = put(overlay_transform['body'], 'm_LocalPosition', '{x: 0, y: 0, z: 0}')
overlay_transform['body'] = put(overlay_transform['body'], 'm_LocalScale', '{x: 1, y: 1, z: 1}')
overlay_transform['body'] = put(overlay_transform['body'], 'm_LocalRotation', IDENTITY)
overlay_transform['body'] = put(overlay_transform['body'], 'm_Father', '{fileID: ' + body_transform['id'] + '}')
overlay_transform['body'] = re.sub(r'  m_Children:\n(?:  - [^\n]*\n)*', '  m_Children: []\n', overlay_transform['body'], count=1)
overlay_renderer = dict(body_renderer)
overlay_renderer['id'] = renderer_id
overlay_renderer['body'] = put(overlay_renderer['body'], 'm_GameObject', '{fileID: ' + go_id + '}')
overlay_renderer['body'] = put(overlay_renderer['body'], 'm_Sprite', ref('Plus'))
overlay_renderer['body'] = put(overlay_renderer['body'], 'm_Color', '{r: 1, g: 1, b: 1, a: 0}')
overlay_renderer['body'] = put(overlay_renderer['body'], 'm_Enabled', '0')
overlay_renderer['body'] = put(overlay_renderer['body'], 'm_SortingOrder', '13')
body_transform['body'] = body_transform['body'].replace('  m_Children:\n', '  m_Children:\n  - {fileID: ' + transform_id + '}\n', 1)
visual = prefab.by_id['2093923957761826071']
visual['body'] = visual['body'].rstrip('\n') + '\n  chargeRenderer: {fileID: ' + renderer_id + '}\n'
prefab.docs.extend((overlay_go, overlay_transform, overlay_renderer))
prefab.save()

prefab = Prefab('Terrain/HexBackground')
for doc in prefab.docs:
    if doc['kind'] == 1839735485:
        doc['body'] = put(doc['body'], 'm_Color', '{r: 0.06666667, g: 0.10196079, b: 0.2, a: 1}')
prefab.save()

(OUT / 'report.json').write_text(json.dumps({'status': 'passed', 'newSourceAssetFiles': [], 'spriteAllowlist': SPRITES, 'prefabs': reports}, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({'status': 'passed', 'changedPrefabs': len(reports), 'report': str(OUT / 'report.json')}, ensure_ascii=False))
