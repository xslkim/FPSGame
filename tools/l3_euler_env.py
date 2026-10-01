#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import sys, math
sys.path.insert(0, r'G:\FPSGame\tools')
from l3_cam_extract import sc, fmt, qmul, qnorm

def quat_to_unity_euler(q):
    # Unity ZXY order (x=pitch, y=yaw, z=roll), degrees
    x, y, z, w = q
    # rotation matrix
    m00 = 1-2*(y*y+z*z); m01 = 2*(x*y-z*w);   m02 = 2*(x*z+y*w)
    m10 = 2*(x*y+z*w);   m11 = 1-2*(x*x+z*z); m12 = 2*(y*z-x*w)
    m20 = 2*(x*z-y*w);   m21 = 2*(y*z+x*w);   m22 = 1-2*(x*x+y*y)
    # unity: pitch = asin(-m12)? use standard ZXY extraction
    pitch = math.asin(max(-1, min(1, m12)))
    if abs(math.cos(pitch)) > 1e-6:
        yaw = math.atan2(-m02, m22)
        roll = math.atan2(-m10, m11)
    else:
        yaw = math.atan2(m20, m00); roll = 0
    return tuple(math.degrees(a) for a in (pitch, yaw, roll))

u0 = (-0.037342947, 0.90012144, 0.078750459, 0.42683166)
g0_target = (0.0177, 0.9001, 0.0373, 0.4268)
g0_mirror = (u0[0], -u0[1], -u0[2], u0[3])
print('cam0 unity quat          :', fmt(u0,8), ' unity-euler(x,y,z)=', fmt(quat_to_unity_euler(u0),5))
print('cam0 mirror-X godot quat :', fmt(g0_mirror,8))
print('cam0 godot target quat   :', fmt(g0_target,8), ' euler-like        =', fmt(quat_to_unity_euler(qnorm(g0_target)),5))

# what unity quat would produce the godot target under mirror-X? u=(gx,-gy,-gz,gw)
u_needed = (g0_target[0], -g0_target[1], -g0_target[2], g0_target[3])
print('unity quat needed for target:', fmt(qnorm(u_needed),8), ' euler=', fmt(quat_to_unity_euler(qnorm(u_needed)),5))

# yaw-only comparison for all cams
CAMQ = {0:u0,
 1:(-0.0085806187, 0.99135503, 0.086732367, 0.098076867),
 2:(-0.014609861, 0.98209864, 0.08592254, 0.1669914),
 3:(0.048167909, 0.83023242, 0.072635954, -0.55056154),
 4:(0.020420068, 0.96846642, 0.084729867, -0.23340235),
 5:(-0.027727064, 0.94443876, 0.08262773, 0.3169216),
 6:(0.03683362, 0.90285902, 0.078989969, -0.42101005)}
print()
for n in sorted(CAMQ):
    e = quat_to_unity_euler(CAMQ[n])
    print('cam%d unity euler (pitch,yaw,roll) = %s   fwd=(%.4f, %.4f, %.4f)' % (n, fmt(e,5),
        math.sin(math.radians(e[1]))*math.cos(math.radians(e[0])),
        math.sin(math.radians(e[0])),
        math.cos(math.radians(e[1]))*math.cos(math.radians(e[0]))))

print()
print('=== Env children (1 level) ===')
env_go = next(f for f,g in sc.gos.items() if g['name']=='Env')
env_tr = sc.go_tr[env_go]
kids = [f for f,t in sc.trs.items() if not t['stripped'] and t['father']==env_tr]
print('Env tr=%d, %d direct children' % (env_tr, len(kids)))
rows = []
for f in kids:
    go = sc.gos.get(sc.trs[f]['go'])
    if not go: continue
    wp, wq, ws = sc.world_of(f)
    rows.append((go['name'] or '?', wp, wq, ws, f))
for nm, wp, wq, ws, f in sorted(rows):
    print('  %-40s pos=%s scl=%s tr=%d' % (nm, fmt(wp,7), fmt(ws,4), f))
