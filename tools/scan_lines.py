# 真值逐行扫描:每行取若干采样行,输出白字像素 x 范围(转画布单位)
from PIL import Image

im = Image.open(r"G:\FPSGame\tools\screenshots\unity_device.png").convert("RGB")
w, h = im.size
px = im.load()
s = 322 / 717.7
OFF = 77.5

def row_extent(y):
    xs = [x for x in range(0, 360)
          if sum(1 for dy in (-1, 0, 1)
                 if 0 <= y + dy < h and all(c > 170 for c in px[x, y + dy])) > 0]
    xs = [x for x in range(0, 360) if any(
        all(c > 170 for c in px[x, y + dy]) for dy in (-1, 0, 1) if 0 <= y + dy < h)]
    if not xs:
        return None
    return min(xs) / s - OFF, max(xs) / s - OFF

# 找文字行:对每 4px 行带扫白字密度
rows = []
for y in range(10, 220, 2):
    cnt = sum(1 for x in range(80, 360, 2) if all(c > 170 for c in px[x, y]))
    rows.append((y, cnt))
# 打印密度剖面以定位行
prev = 0
for y, cnt in rows:
    bar = "#" * cnt
    if cnt or prev:
        print(f"y={y:3d} ({y/s - 1.15:5.0f}u) {bar}")
    prev = cnt
