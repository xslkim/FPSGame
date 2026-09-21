import re, sys
import numpy as np

text = open(r'G:/test/FPSGame/Assets/Scenes/Level2.unity', encoding='utf-8', errors='replace').read()
docs = text.split('--- !u!')

def parse(doc):
    m = re.match(r'(\d+) &(\d+)( stripped)?', doc)
    return int(m.group(1)), int(m.group(2)), doc

go_name = {}
comp_owner = {}
go_components = {}
transforms = {}
prefab_instances = {}
stripped_tf = {}  # transform fid -> prefabinstance fid

for d in docs[1:]:
    cid, fid, body = parse(d)
    m = re.search(r'm_GameObject: \{fileID: (\d+)\}', body)
    if cid == 1:
        nm = re.search(r'm_Name: (.*)', body)
        go_name[fid] = nm.group(1).strip() if nm else '?'
    if cid in (4, 224):
        transforms[fid] = body
        if ' stripped' in d.split('\n',1)[0]:
            mpi = re.search(r'm_PrefabInstance: \{fileID: (\d+)\}', body)
            if mpi:
                stripped_tf[fid] = int(mpi.group(1))
    if cid == 1001:
        prefab_instances[fid] = body
    if m:
        go = int(m.group(1))
        comp_owner[fid] = go
        go_components.setdefault(go, []).append((cid, fid))

def quat_mat(q):
    x, y, z, w = q
    s = 2.0/(x*x+y*y+z*z+w*w)
    xx, yy, zz = x*x*s, y*y*s, z*z*s
    xy, xz, yz = x*y*s, x*z*s, y*z*s
    wx, wy, wz = w*x*s, w*y*s, w*z*s
    return np.array([
        [1-(yy+zz), xy-wz, xz+wy],
        [xy+wz, 1-(xx+zz), yz-wx],
        [xz-wy, yz+wx, 1-(xx+yy)]])

def trs(pos, rot, scl):
    M = np.eye(4)
    M[:3,:3] = quat_mat(rot) * np.array(scl)
    M[:3,3] = pos
    return M

def local_trs(body):
    p = re.search(r'm_LocalPosition: \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)\}', body)
    r = re.search(r'm_LocalRotation: \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+), w: ([-\d.e]+)\}', body)
    s = re.search(r'm_LocalScale: \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)\}', body)
    pos = [float(p.group(i)) for i in (1,2,3)] if p else [0,0,0]
    rot = [float(r.group(i)) for i in (1,2,3,4)] if r else [0,0,0,1]
    scl = [float(s.group(i)) for i in (1,2,3)] if s else [1,1,1]
    return trs(pos, rot, scl)

def prefab_mods(pifid):
    body = prefab_instances[pifid]
    mods = {}
    for m in re.finditer(r'- target: \{fileID: (-?\d+), guid: \w+, type: 3\}\n\s+propertyPath: (m_Local\w+\.[xyzw])\n\s+value: ([-\d.e]+)', body):
        mods.setdefault(int(m.group(1)), {})[m.group(2)] = float(m.group(3))
    return mods

def prefab_parent(pifid):
    body = prefab_instances[pifid]
    m = re.search(r'm_TransformParent: \{fileID: (\d+)\}', body)
    return int(m.group(1)) if m else 0

def prefab_root_local(pifid):
    """Local TRS of prefab root: from modifications on the target that has m_LocalPosition.x."""
    mods = prefab_mods(pifid)
    pos = [0,0,0]; rot = [0,0,0,1]; scl = [1,1,1]
    best = None
    for tid, md in mods.items():
        if 'm_LocalPosition.x' in md:
            best = md
            break
    if best:
        pos = [best.get('m_LocalPosition.'+a, 0.0) for a in 'xyz']
        rot = [best.get('m_LocalRotation.'+a, d) for a, d in zip('xyzw',[0,0,0,1])]
        scl = [best.get('m_LocalScale.'+a, 1.0) for a in 'xyz']
    return trs(pos, rot, scl)

def world_matrix_of_transform(tfid):
    M = np.eye(4)
    chain = []
    cur = tfid
    while cur and cur != 0:
        if cur in stripped_tf:
            pi = stripped_tf[cur]
            chain.append(prefab_root_local(pi))
            cur = prefab_parent(pi)
        else:
            body = transforms.get(cur)
            if body is None:
                break
            chain.append(local_trs(body))
            m = re.search(r'm_Father: \{fileID: (\d+)\}', body)
            cur = int(m.group(1)) if m else 0
    for m in reversed(chain):
        M = M @ m
    return M

def transform_of_go(go_fid):
    for cid, fid in go_components.get(go_fid, []):
        if cid in (4, 224):
            return fid
    return None

def world_pos_go(go):
    tfid = transform_of_go(go)
    return world_matrix_of_transform(tfid)[:3,3] if tfid else np.zeros(3)

def fullname(go_fid):
    parts = []
    cur = go_fid
    depth = 0
    while cur and depth < 30:
        depth += 1
        parts.append(go_name.get(cur, '?'))
        tfid = transform_of_go(cur)
        if tfid is None: break
        if tfid in stripped_tf:
            cur = comp_owner.get(prefab_parent(stripped_tf[tfid]))
            continue
        body = transforms.get(tfid)
        if body is None: break
        m = re.search(r'm_Father: \{fileID: (\d+)\}', body)
        if not m or int(m.group(1)) == 0: break
        cur = comp_owner.get(int(m.group(1)))
        if cur is None and int(m.group(1)) in stripped_tf:
            cur = comp_owner.get(prefab_parent(stripped_tf[int(m.group(1))]))
    return '/'.join(reversed(parts))

mode = sys.argv[1]
if mode == 'fw':
    fw_guid = '5e6dc95b7dca7b144b8629aec98b4cf4'
    for d in docs[1:]:
        cid, fid, body = parse(d)
        if cid == 114 and fw_guid in body:
            go = comp_owner[fid]
            srcm = re.search(r'SrcPosition: \{fileID: (\d+)\}', body)
            srcfid = int(srcm.group(1)) if srcm else 0
            w = world_pos_go(go)
            line = f'{fullname(go):55s} world=({w[0]:8.2f},{w[1]:8.2f},{w[2]:8.2f})'
            if srcfid:
                sgo = comp_owner.get(srcfid)
                if sgo is not None:
                    sp = world_pos_go(sgo)
                    line += f'  src=({sp[0]:8.2f},{sp[1]:8.2f},{sp[2]:8.2f})'
                elif srcfid in transforms:
                    sp = world_matrix_of_transform(srcfid)[:3,3]
                    line += f'  src=({sp[0]:8.2f},{sp[1]:8.2f},{sp[2]:8.2f}) [stripped]'
            print(line)
elif mode == 'obj':
    pat = sys.argv[2]
    for go, nm in sorted(go_name.items(), key=lambda x: fullname(x[0])):
        if pat.lower() in fullname(go).lower():
            w = world_pos_go(go)
            comps = sorted(set(c[0] for c in go_components.get(go, [])))
            print(f'{fullname(go):70s} world=({w[0]:8.2f},{w[1]:8.2f},{w[2]:8.2f}) comps={comps}')
elif mode == 'cam':
    for d in docs[1:]:
        cid, fid, body = parse(d)
        if cid == 20:
            go = comp_owner[fid]
            w = world_pos_go(go)
            r = re.search(r'm_LocalRotation: \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+), w: ([-\d.e]+)\}', transforms[transform_of_go(go)])
            fov = re.search(r'field of view: ([-\d.e]+)', body)
            print(f'{fullname(go):40s} world=({w[0]:8.3f},{w[1]:8.3f},{w[2]:8.3f}) rot=({r.group(1)},{r.group(2)},{r.group(3)},{r.group(4)}) fov={fov.group(1)}')
