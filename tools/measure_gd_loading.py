# Godot 版 Loading 元素 bbox(转 Loading 单位:中心 (640,360),1 单位=2/3 px),与真值同口径对比
from PIL import Image

im = Image.open(r"G:\FPSGame\tools\screenshots\gd_loading.png").convert("RGB")
px = im.load()
CX, CY = 640.0, 360.0

def to_units(b):
    x0, x1, y0, y1 = b
    return dict(x0=round((x0-CX)*1.5, 1), x1=round((x1-CX)*1.5, 1),
                y_up0=round((CY-y1)*1.5, 1), y_up1=round((CY-y0)*1.5, 1),
                w=round((x1-x0)*1.5, 1), h=round((y1-y0)*1.5, 1))

def find(pred, region):
    x0, x1, y0, y1 = region
    pts = [(x, y) for y in range(y0, y1) for x in range(x0, x1) if pred(px[x, y])]
    if not pts:
        return None
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    return min(xs), max(xs), min(ys), max(ys)

white = lambda c: c[0] > 200 and c[1] > 200 and c[2] > 200
barline = lambda c: c[2] > 120 and c[1] > 60 and c[0] < 60
sparkle = lambda c: c[0] > 120 and c[1] > 150 and c[2] > 200
tips = lambda c: c[0] > 200 and c[1] > 210 and 140 < c[2] < 230
pct = lambda c: c[0] > 200 and c[1] > 210 and c[2] > 220

print("标题字:", to_units(find(white, (300, 1000, 100, 300))))
print("进度条线:", to_units(find(barline, (0, 1280, 580, 640))))
print("星芒:", to_units(find(sparkle, (0, 300, 560, 660))))
print("Tips:", to_units(find(tips, (300, 1000, 640, 700))))
print("60%:", to_units(find(pct, (1000, 1280, 630, 710))))
print("真值对照: 标题 x-252..252 y153..205.5 | 线 x-933..1138.5 y-421.5..-348")
print("         星芒 x-903..-876 y-390..-378 | Tips x-90..96 y-465..-427.5 | 60% x825..907.5 y-469.5..-414")
