# 枪的包围盒对比(相对坐标):M4=左下浅色,AK47 握把=右下橙色
from PIL import Image

def gun_bbox(path, label):
    im = Image.open(path).convert("RGB")
    w, h = im.size
    px = im.load()
    # M4:左下 1/4 区域,浅色(近似白/银,低饱和高亮)
    pts = [(x, y) for y in range(int(h*0.4), h) for x in range(0, int(w*0.30))
           if abs(px[x,y][0]-px[x,y][1]) < 30 and abs(px[x,y][1]-px[x,y][2]) < 30
           and 120 < px[x,y][0] < 240]
    if pts:
        xs=[p[0] for p in pts]; ys=[p[1] for p in pts]
        print(f"{label} M4: x {min(xs)/w:.3f}..{max(xs)/w:.3f} y {min(ys)/h:.3f}..{max(ys)/h:.3f}")
    # AK47 握把橙:右下,r>120, 40<g<110, b<70
    pts = [(x, y) for y in range(int(h*0.4), h) for x in range(int(w*0.6), w)
           if px[x,y][0] > 110 and 40 < px[x,y][1] < 120 and px[x,y][2] < 80]
    if pts:
        xs=[p[0] for p in pts]; ys=[p[1] for p in pts]
        print(f"{label} AK握把: x {min(xs)/w:.3f}..{max(xs)/w:.3f} y {min(ys)/h:.3f}..{max(ys)/h:.3f}")

gun_bbox(r"G:\FPSGame\tools\screenshots\unity_device.png", "unity")
gun_bbox(r"G:\FPSGame\tools\screenshots\gd_device.png", "godot")
