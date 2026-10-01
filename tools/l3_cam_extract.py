#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Extract world-space transforms from Level3.unity (read-only analysis)."""
import re, os, math

SCENE = r"G:\test\FPSGame\Assets\Scenes\Level3.unity"
ASSETS = r"G:\test\FPSGame\Assets"
HDR = re.compile(r'^--- !u!(\d+) &(-?\d+)( stripped)?\s*$', re.M)
NUM = r'([-+0-9.eEnaif]+)'

def parse_blocks(path):
    text = open(path, encoding='utf-8').read()
    ms = list(HDR.finditer(text))
    out = []
    for i, m in enumerate(ms):
        end = ms[i+1].start() if i+1 < len(ms) else len(text)
        out.append({'cls': int(m.group(1)), 'fid': int(m.group(2)),
                    'stripped': bool(m.group(3)), 'body': text[m.end():end]})
    return out

def get_vec(body, key):
    m = re.search(re.escape(key) + r':\s*\{x:\s*' + NUM + r',\s*y:\s*' + NUM + r',\s*z:\s*' + NUM + r'(?:,\s*w:\s*' + NUM + r')?\}', body)
    return tuple(float(g) for g in m.groups() if g is not None) if m else None

def get_ref(body, key):
    m = re.search(re.escape(key) + r':\s*\{fileID:\s*(-?\d+)(?:,\s*guid:\s*([0-9a-fA-F]+))?', body)
    return (int(m.group(1)), m.group(2)) if m else (0, None)

def get_name(body):
    m = re.search(r'^\s*m_Name:\s*(.*)$', body, re.M)
    return m.group(1).strip() if m else None

def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw*bx + ax*bw + ay*bz - az*by, aw*by - ax*bz + ay*bw + az*bx,
            aw*bz + ax*by - ay*bx + az*bw, aw*bw - ax*bx - ay*by - az*bz)

def qrot(q, v):
    x, y, z, w = q; ux, uy, uz = v
    tx = 2*(y*uz - z*uy); ty = 2*(z*ux - x*uz); tz = 2*(x*uy - y*ux)
    return (ux + w*tx + (y*tz - z*ty), uy + w*ty + (z*tx - x*tz), uz + w*tz + (x*ty - y*tx))

def qnorm(q):
    n = math.sqrt(sum(c*c for c in q)) or 1.0
    return tuple(c/n for c in q)

def compose(parent, local):
    pp, pq, ps = parent; lp, lq, ls = local
    wp = tuple(pp[i] + v for i, v in enumerate(qrot(pq, (ps[0]*lp[0], ps[1]*lp[1], ps[2]*lp[2]))))
    return (wp, qnorm(qmul(pq, lq)), (ps[0]*ls[0], ps[1]*ls[1], ps[2]*ls[2]))

IDENT = ((0.,0.,0.), (0.,0.,0.,1.), (1.,1.,1.))

class Scene:
    def __init__(self, path):
        self.path = path
        self.blocks = {b['fid']: b for b in parse_blocks(path)}
        self.gos, self.trs, self.pis, self.go_tr = {}, {}, {}, {}
        for b in self.blocks.values():
            if b['cls'] == 1 and not b['stripped']:
                comps = [int(x) for x in re.findall(r'component:\s*\{fileID:\s*(-?\d+)\}', b['body'])]
                self.gos[b['fid']] = {'name': get_name(b['body']), 'comps': comps}
            elif b['cls'] in (4, 224):
                go, _ = get_ref(b['body'], 'm_GameObject')
                fa, _ = get_ref(b['body'], 'm_Father')
                cf, cg = get_ref(b['body'], 'm_CorrespondingSourceObject')
                pi, _ = get_ref(b['body'], 'm_PrefabInstance')
                self.trs[b['fid']] = {'go': go,
                    'pos': get_vec(b['body'], 'm_LocalPosition'),
                    'rot': get_vec(b['body'], 'm_LocalRotation'),
                    'scl': get_vec(b['body'], 'm_LocalScale'),
                    'father': fa, 'stripped': b['stripped'], 'cso': (cf, cg), 'pi': pi}
                if go and not b['stripped']:
                    self.go_tr[go] = b['fid']
            elif b['cls'] == 1001:
                tp, _ = get_ref(b['body'], 'm_TransformParent')
                src = re.search(r'm_SourcePrefab:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-fA-F]+)', b['body'])
                mods = {}
                for mm in re.finditer(r'-\s*target:\s*\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-fA-F]+)[^}]*\}\s*\n\s*propertyPath:\s*(\S+)\s*\n\s*value:\s*([^\n]*)', b['body']):
                    mods.setdefault(int(mm.group(1)), {})[mm.group(3)] = mm.group(4).strip()
                self.pis[b['fid']] = {'parent': tp, 'guid': src.group(1) if src else None, 'mods': mods}
        self.wcache, self.gmap, self.pcache = {}, None, {}

    def guid_path(self, guid):
        if self.gmap is None:
            self.gmap = {}
            for root, dirs, files in os.walk(ASSETS):
                for f in files:
                    if f.endswith('.meta'):
                        p = os.path.join(root, f)
                        try: t = open(p, encoding='utf-8', errors='ignore').read(4000)
                        except Exception: continue
                        m = re.search(r'^guid:\s*([0-9a-fA-F]+)', t, re.M)
                        if m: self.gmap[m.group(1)] = p[:-5]
        return self.gmap.get(guid)

    def prefab(self, guid):
        if guid not in self.pcache:
            p = self.guid_path(guid)
            self.pcache[guid] = Scene(p) if p and os.path.exists(p) and p.lower().endswith(('.prefab', '.unity')) else None
        return self.pcache[guid]

    @staticmethod
    def _over(base, mods, prefix, comps):
        v = list(base)
        for i, c in enumerate(comps):
            k = '%s.%s' % (prefix, c)
            if k in mods:
                try: v[i] = float(mods[k])
                except ValueError: pass
        return tuple(v)

    def _world_in_prefab(self, psc, fid, pi, d=0):
        if d > 64 or fid not in psc.trs: return IDENT
        st = psc.trs[fid]
        mods = pi['mods'].get(fid, {})
        local = (self._over(st['pos'] or (0,0,0), mods, 'm_LocalPosition', 'xyz'),
                 qnorm(self._over(st['rot'] or (0,0,0,1), mods, 'm_LocalRotation', 'xyzw')),
                 self._over(st['scl'] or (1,1,1), mods, 'm_LocalScale', 'xyz'))
        fa = st['father']
        if fa and fa in psc.trs:
            return compose(self._world_in_prefab(psc, fa, pi, d+1), local)
        pw = self.world_of(pi['parent'], d+1) if pi['parent'] else IDENT
        return compose(pw, local)

    def world_of(self, fid, d=0):
        if not fid: return IDENT
        if fid in self.wcache: return self.wcache[fid]
        if d > 64 or fid not in self.trs: return IDENT
        tr = self.trs[fid]
        if not tr['stripped']:
            local = (tr['pos'] or (0,0,0), qnorm(tr['rot'] or (0,0,0,1)), tr['scl'] or (1,1,1))
            w = compose(self.world_of(tr['father'], d+1), local)
        else:
            cf, cg = tr['cso']
            pi = self.pis.get(tr['pi'])
            if pi is None: return IDENT
            psc = self.prefab(cg) if cg else None
            mods = pi['mods'].get(cf, {})
            base = ((0.,0.,0.), (0.,0.,0.,1.), (1.,1.,1.))
            src_fa = 0
            if psc and cf in psc.trs:
                st = psc.trs[cf]
                base = (st['pos'] or base[0], st['rot'] or base[1], st['scl'] or base[2])
                src_fa = st['father']
            local = (self._over(base[0], mods, 'm_LocalPosition', 'xyz'),
                     qnorm(self._over(base[1], mods, 'm_LocalRotation', 'xyzw')),
                     self._over(base[2], mods, 'm_LocalScale', 'xyz'))
            if src_fa and psc and src_fa in psc.trs:
                w = compose(self._world_in_prefab(psc, src_fa, pi, d+1), local)
            else:
                w = compose(self.world_of(pi['parent'], d+1), local)
        self.wcache[fid] = w
        return w

    def chain(self, fid):
        out, cur, seen = [], fid, set()
        while cur and cur not in seen and cur in self.trs and len(out) < 64:
            seen.add(cur)
            tr = self.trs[cur]
            if tr['stripped']:
                nm = 'STRIPPED(pi=%d cso=%d)' % (tr['pi'], tr['cso'][0])
            else:
                nm = self.gos.get(tr['go'], {}).get('name', '?%d' % tr['go'])
            out.append((cur, nm, tr))
            cur = tr['father']
        return out

sc = Scene(SCENE)

def fmt(v, nd=7):
    return '(' + ', '.join(('%.*g' % (nd, c)) for c in v) + ')'

CAM_FIDS = {0:1184428282, 1:681749417, 2:1851237386, 3:343091416,
            4:310712955, 5:271760697, 6:1662174246}
GODOT = {
 0: {'pos': (61.1671, 1, -67.7054), 'quat': (0.0177, 0.9001, 0.0373, 0.4268)},
 1: {'pos': (4.37, 1, -62.54)}, 2: {'pos': (-71.27, 1, -67.7054)},
 3: {'pos': (-304, 1, -67)},     4: {'pos': (-148.62, 1, -67.7054)},
 5: {'pos': (-211.2, 1, -62.9)}, 6: {'pos': (-112.22, 1, -67.7054)},
}

print('=== _CameraBattle fileID resolution ===')
for n, fid in CAM_FIDS.items():
    kind = 'GameObject' if fid in sc.gos else ('Transform' if fid in sc.trs else
           ('cls%d' % sc.blocks[fid]['cls'] if fid in sc.blocks else 'MISSING'))
    print('  cam%d fid=%d -> %s' % (n, fid, kind))

def go_of(fid):
    if fid in sc.gos: return fid
    if fid in sc.trs: return sc.trs[fid]['go']
    if fid in sc.blocks and sc.blocks[fid]['cls'] == 114:
        return get_ref(sc.blocks[fid]['body'], 'm_GameObject')[0]
    return None

results = {}
print()
print('=== camera chains ===')
for n, fid in CAM_FIDS.items():
    go = go_of(fid)
    tfid = sc.go_tr.get(go)
    nm = sc.gos.get(go, {}).get('name', '?')
    print('--- cam%d: GameObject "%s" go=%s tr=%s ---' % (n, nm, go, tfid))
    if tfid is None:
        print('    NO TRANSFORM'); continue
    for cfid, cnm, ctr in sc.chain(tfid):
        if ctr['stripped']:
            print('    fid=%d %s' % (cfid, cnm))
        else:
            print('    fid=%-12d %-26s pos=%s rot=%s scl=%s' % (cfid, cnm, fmt(ctr['pos']), fmt(ctr['rot']), fmt(ctr['scl'], 4)))
    wp, wq, ws = sc.world_of(tfid)
    results[n] = (nm, wp, wq, ws)
    print('    WORLD pos=%s quat=%s scale=%s' % (fmt(wp, 8), fmt(wq, 8), fmt(ws, 4)))

print()
print('=== mirror-X comparison: godot=(-ux,uy,uz), quat=(uqx,-uqy,-uqz,uqw) ===')
for n in sorted(results):
    nm, wp, wq, ws = results[n]
    gp = (-wp[0], wp[1], wp[2])
    tp = GODOT[n]['pos']
    d = tuple(gp[i]-tp[i] for i in range(3))
    print('cam%d "%s"' % (n, nm))
    print('  unity_pos =%s' % fmt(wp, 8))
    print('  ->godot   =%s  target=%s  diff=%s' % (fmt(gp, 8), fmt(tp, 8), fmt(d, 5)))
    gq = (wq[0], -wq[1], -wq[2], wq[3])
    print('  unity_quat=%s' % fmt(wq, 8))
    line = '  ->godot   =%s' % fmt(gq, 8)
    if 'quat' in GODOT[n]:
        tq = GODOT[n]['quat']
        d1 = tuple(gq[i]-tq[i] for i in range(4))
        d2 = tuple(-gq[i]-tq[i] for i in range(4))
        line += '  target=%s diff=%s negdiff=%s' % (fmt(tq, 8), fmt(d1, 5), fmt(d2, 5))
    print(line)

print()
print('=== RockWarrior / Level3Boss ===')
for fid, go in sorted(sc.gos.items()):
    if go['name'] in ('RockWarrior', 'Level3Boss'):
        tfid = sc.go_tr.get(fid)
        wp, wq, ws = sc.world_of(tfid)
        print('%s go=%d tr=%d WORLD pos=%s quat=%s scale=%s' % (go['name'], fid, tfid, fmt(wp, 8), fmt(wq, 8), fmt(ws, 4)))
        for cfid, cnm, ctr in sc.chain(tfid):
            if ctr['stripped']:
                print('    fid=%d %s' % (cfid, cnm))
            else:
                print('    fid=%-12d %-24s pos=%s rot=%s scl=%s' % (cfid, cnm, fmt(ctr['pos']), fmt(ctr['rot']), fmt(ctr['scl'], 4)))

print()
print('=== RenderSettings fog/ambient ===')
for fid, b in sc.blocks.items():
    if b['cls'] == 104:
        for line in b['body'].splitlines():
            if any(k in line for k in ('m_Fog', 'FogColor', 'FogMode', 'FogDensity', 'LinearFog', 'AmbientSkyColor', 'AmbientMode', 'AmbientIntensity', 'm_SkyboxMaterial')):
                print(' ', line.strip())

print()
print('=== root objects (father==0) ===')
roots = []
for fid, tr in sc.trs.items():
    if tr['stripped'] or tr['father'] != 0: continue
    go = sc.gos.get(tr['go'])
    if not go: continue
    wp, _, _ = sc.world_of(fid)
    roots.append((go['name'] or '?', wp))
for nm, wp in sorted(roots):
    print('  %-44s %s' % (nm, fmt(wp, 7)))

print()
print('=== landmark candidates ===')
pat = re.compile(r'lamp|light|tower|gate|build|house|sign|street|statue|bridge|wall|door|school|roof|灯|塔|楼|门|桥|墙|房|校', re.I)
seen = set()
for fid, go in sc.gos.items():
    nm = go['name'] or ''
    if not pat.search(nm): continue
    tfid = sc.go_tr.get(fid)
    if tfid is None: continue
    wp, wq, _ = sc.world_of(tfid)
    key = (nm, round(wp[0], 3), round(wp[1], 3), round(wp[2], 3))
    if key in seen: continue
    seen.add(key)
    print('  %-40s go=%-12d pos=%s rot=%s' % (nm, fid, fmt(wp, 7), fmt(wq, 6)))
