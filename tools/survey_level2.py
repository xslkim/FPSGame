import re, collections, sys

text = open(r'G:/test/FPSGame/Assets/Scenes/Level2.unity', encoding='utf-8', errors='replace').read()
docs = text.split('--- !u!')

def parse(doc):
    m = re.match(r'(\d+) &(\d+)', doc)
    return int(m.group(1)), int(m.group(2)), doc

go_name = {}
go_components = collections.defaultdict(list)   # go fileid -> [(cid, fid)]
comp_owner = {}
for d in docs[1:]:
    cid, fid, body = parse(d)
    m = re.search(r'm_GameObject: \{fileID: (\d+)\}', body)
    if cid == 1:
        nm = re.search(r'm_Name: (.*)', body)
        go_name[fid] = nm.group(1).strip() if nm else '?'
    if m:
        go = int(m.group(1))
        go_components[go].append((cid, fid))
        comp_owner[fid] = go

def go_of(comp_fid): return comp_owner.get(comp_fid)

def getf(body, key):
    m = re.search(rf'{key}: \{{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)(?:, w: ([-\d.e]+))?\}}', body)
    if not m: return None
    vals = [float(v) for v in m.groups() if v is not None]
    return vals

def vec(body, key):
    m = re.search(rf'{key}: \{{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)\}}', body)
    return [float(m.group(1)), float(m.group(2)), float(m.group(3))] if m else None

def getv(body, key):
    m = re.search(rf'{key}: ([-\d.e]+)', body)
    return float(m.group(1)) if m else None

transforms = {}  # fid -> body
for d in docs[1:]:
    cid, fid, body = parse(d)
    if cid in (4, 224):
        transforms[fid] = body

def transform_of_go(go_fid):
    for cid, fid in go_components[go_fid]:
        if cid in (4, 224):
            return fid, transforms[fid]
    return None, None

def parents_chain(go_fid):
    chain = []
    tfid, tbody = transform_of_go(go_fid)
    while tfid:
        chain.append(go_fid)
        m = re.search(r'm_Father: \{fileID: (\d+)\}', tbody)
        if not m or int(m.group(1)) == 0:
            break
        ftid = int(m.group(1))
        go_fid = comp_owner.get(ftid)
        if go_fid is None: break
        tfid, tbody = transform_of_go(go_fid)
    return list(reversed(chain))

def fullname(go_fid):
    return '/'.join(go_name.get(g, '?') for g in parents_chain(go_fid))

mode = sys.argv[1] if len(sys.argv) > 1 else 'all'

# --- 1. named object report ---
def report(name_filter):
    for fid, name in go_name.items():
        if name_filter.lower() in name.lower():
            tfid, tb = transform_of_go(fid)
            pos = vec(tb, 'm_LocalPosition') if tb else None
            rot = getf(tb, 'm_LocalRotation') if tb else None
            comps = [c for c in go_components[fid]]
            print(f'{fid} {fullname(fid)}  pos={pos} rot={rot} comps={[c[0] for c in comps]}')

if mode == 'name':
    report(sys.argv[2])
elif mode == 'render':
    # RenderSettings
    for d in docs[1:]:
        cid, fid, body = parse(d)
        if cid == 104:
            for k in ['m_Fog', 'm_FogColor', 'm_FogMode', 'm_FogDensity', 'm_LinearFogStart',
                      'm_LinearFogEnd', 'm_AmbientSkyColor', 'm_AmbientEquatorColor', 'm_AmbientGroundColor',
                      'm_AmbientIntensity', 'm_AmbientMode', 'm_SkyboxMaterial', 'm_Sun']:
                m = re.search(rf'{k}: (.*)', body)
                if m: print(k, '=', m.group(1).strip())
elif mode == 'light':
    for d in docs[1:]:
        cid, fid, body = parse(d)
        if cid == 108:
            go = go_of(fid)
            print('Light on', fullname(go))
            for k in ['m_Type', 'm_Color', 'm_Intensity', 'm_Range', 'm_SpotAngle', 'm_Shadows']:
                m = re.search(rf'{k}: .*', body)
                if m: print('  ', m.group(0).strip()[:120])
elif mode == 'audio':
    for d in docs[1:]:
        cid, fid, body = parse(d)
        if cid == 82:
            go = go_of(fid)
            clip = re.search(r'm_audioClip: \{fileID: (\d+), guid: (\w+)', body)
            loop = re.search(r'Loop: (\d)', body)
            vol = re.search(r'm_Volume: ([-\d.e]+)', body)
            print('AudioSource on', fullname(go), 'clip_guid=', clip.groups() if clip else None,
                  'loop=', loop.group(1) if loop else '?', 'vol=', vol.group(1) if vol else '?')
elif mode == 'firewin':
    fw_guid = '5e6dc95b7dca7b144b8629aec98b4cf4'
    for d in docs[1:]:
        cid, fid, body = parse(d)
        if cid == 114 and fw_guid in body:
            go = go_of(fid)
            tfid, tb = transform_of_go(go)
            pos = vec(tb, 'm_LocalPosition') if tb else None
            rot = getf(tb, 'm_LocalRotation') if tb else None
            fields = re.findall(r'm_EditorClassIdentifier.*', body)
            print('FireWindow on', fullname(go), 'pos=', pos, 'rot=', rot)
            # custom fields
            for m in re.finditer(r'^\s{2}(\w+): (.*)$', body, re.M):
                print('   ', m.group(1), '=', m.group(2))
elif mode == 'cam':
    for d in docs[1:]:
        cid, fid, body = parse(d)
        if cid == 20:
            go = go_of(fid)
            tfid, tb = transform_of_go(go)
            pos = vec(tb, 'm_LocalPosition') if tb else None
            rot = getf(tb, 'm_LocalRotation') if tb else None
            fov = getv(body, 'field of view')
            print('Camera on', fullname(go), 'pos=', pos, 'rot=', rot, 'fov=', fov)
elif mode == 'guids':
    cnt = collections.Counter()
    mono = []
    for d in docs[1:]:
        cid, fid, body = parse(d)
        if cid == 114:
            m = re.search(r'm_Script: \{fileID: \d+, guid: (\w+)', body)
            if m:
                cnt[m.group(1)] += 1
                mono.append((fid, m.group(1), body))
    print(cnt.most_common(40))
    print('---- toon/boss script search ----')
    for fid, g, body in mono:
        if g in (sys.argv[2] if len(sys.argv)>2 else '',):
            go = go_of(fid)
            print(g, fullname(go))
