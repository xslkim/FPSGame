#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import sys, math
sys.path.insert(0, r'G:\FPSGame\tools')
import io, contextlib
buf = io.StringIO()
with contextlib.redirect_stdout(buf):
    from l3_cam_extract import sc, fmt

def yaw_of(q):
    x,y,z,w = q
    fx = 2*(x*z + w*y); fz = 1-2*(x*x+y*y)
    return math.degrees(math.atan2(fx, fz))

def kids(name, depth=1):
    go = next((f for f,g in sc.gos.items() if g['name']==name), None)
    if go is None: print('  <%s not found>' % name); return
    tr = sc.go_tr.get(go)
    def rec(tfid, d, prefix):
        if d > depth: return
        cs = [f for f,t in sc.trs.items() if not t['stripped'] and t['father']==tfid]
        for f in cs:
            g2 = sc.gos.get(sc.trs[f]['go'])
            if not g2: continue
            wp, wq, ws = sc.world_of(f)
            print('  %s%-36s pos=%s yaw=%.2f scl=%s' % (prefix, g2['name'] or '?', fmt(wp,7), yaw_of(wq), fmt(ws,3)))
            rec(f, d+1, prefix+'  ')
    wp, wq, ws = sc.world_of(tr)
    print('%s: world pos=%s yaw=%.3f' % (name, fmt(wp,8), yaw_of(wq)))
    rec(tr, 1, '')

for nm in ('Level3', 'Group0', 'Camera'):
    kids(nm, 2)
    print()

# stripped transforms directly under prefab instances that are roots? count
n_stripped = sum(1 for t in sc.trs.values() if t['stripped'])
print('stripped transforms in scene:', n_stripped)
# any stripped roots (father==0)
sr = [f for f,t in sc.trs.items() if t['stripped'] and t['father']==0]
print('stripped with father==0:', len(sr), sr[:5])
for f in sr[:5]:
    pi = sc.pis.get(sc.trs[f]['pi'])
    wp, wq, ws = sc.world_of(f)
    print('  stripped-root fid=%d pi=%d srcguid=%s world=%s' % (f, sc.trs[f]['pi'], pi['guid'] if pi else None, fmt(wp,7)))
