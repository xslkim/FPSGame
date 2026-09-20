# 真值元素精确 bbox(转 Loading 单位:Loading 中心 (640,308),1 单位=2/3 px)
from PIL import Image

im = Image.open(r"G:\FPSGame\tools\screenshots\unity_loading.png").convert("RGB")
w, h = im.size
px = im.load()
CX, CY = 640.0, 308.0

def to_units(b):
    x0, x1, y0, y1 = b
    return (round((x0-CX)*1.5, 1), round((x1-CX)*1.5, 1),
            round((CY-y0)*1.5, 1), round((CY-y1)*1.5, 1))

def find(pred, region):
    x0, x1, y0, y1 = region
    pts = [(x, y) for y in range(y0, y1) for x in range(x0, x1) if pred(px[x, y])]
    if not pts:
        return None
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    return min(xs), max(xs), min(ys), max(ys)

white = lambda c: c[0] > 200 and c[1] > 200 and c[2] > 200
yellowish = lambda c: c[0] > 200 and c[1] > 210 and 140 < c[2] < 230
nearwhite = lambda c: c[0] > 200 and c[1] > 210 and c[2] > 220
barblue = lambda c: c[2] > 160 and c[2] > c[0] + 50 and c[1] > 80

b = find(white, (300, 1000, 100, 300)); print("标题字 px:", b, "→ units:", to_units(b))
b = find(barblue, (0, 1300, 330, 430)); print("进度条 px:", b, "→ units:", to_units(b))
b = find(yellowish, (300, 900, 380, 470)); print("Tips px:", b, "→ units:", to_units(b))
b = find(nearwhite, (1150, 1350, 380, 470)); print("60% px:", b, "→ units:", to_units(b))
# 星星 handle:左端高亮白点
b = find(lambda c: c[0] > 220 and c[1] > 230 and c[2] > 240, (0, 400, 330, 430))
print("星芒 px:", b, "→ units:", to_units(b))
# BG 左右边界(用相机底色对比)
cam = px[1450, 50]
for x in range(1460, 0, -1):
    c = px[x, 650]
    if abs(c[0]-cam[0]) > 12 or abs(c[2]-cam[2]) > 12:
        print("BG 右边界:", x); break
