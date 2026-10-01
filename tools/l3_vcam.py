#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import sys, re, io, contextlib
sys.path.insert(0, r'G:\FPSGame\tools')
buf = io.StringIO()
with contextlib.redirect_stdout(buf):
    from l3_cam_extract import sc, get_ref

CAM_FIDS = {0:1184428282, 1:681749417, 2:1851237386, 3:343091416,
            4:310712955, 5:271760697, 6:1662174246}
for n, fid in CAM_FIDS.items():
    body = sc.blocks[fid]['body']
    follow, _ = get_ref(body, 'm_Follow')
    lookat, _ = get_ref(body, 'm_LookAt')
    fov = re.search(r'Field of View:\s*([-\d.e+]+)', body)
    sg = re.search(r'm_Script:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-f]+)', body)
    print('vcam%d mono=%d script=%s Follow=%d LookAt=%d FOV=%s' % (
        n, fid, sg.group(1) if sg else '?', follow, lookat, fov.group(1) if fov else '?'))

# parent chain of the boss Light
for fid, go in sc.gos.items():
    if go['name'] == 'Light':
        tfid = sc.go_tr.get(fid)
        print()
        print('Light go=%d chain:' % fid)
        for cfid, cnm, ctr in sc.chain(tfid):
            if ctr['stripped']:
                print('  fid=%d %s' % (cfid, cnm))
            else:
                print('  fid=%d %-14s pos=%s rot=%s scl=%s' % (cfid, cnm, ctr['pos'], ctr['rot'], ctr['scl']))
