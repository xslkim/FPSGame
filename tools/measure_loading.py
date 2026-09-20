# 真值 Loading 各元素 bbox(1472x668),对照 YAML(Loading 局部 1920x1080,中心原点)
from PIL import Image

im = Image.open(r"G:\FPSGame\tools\screenshots\unity_loading.png").convert("RGB")
w, h = im.size
px = im.load()
print(f"size {w}x{h}")

# 标题白字(上半屏高亮白)
pts = [(x, y) for y in range(40, 200) for x in range(200, 900)
       if px[x, y][0] > 200 and px[x, y][1] > 200 and px[x, y][2] > 200]
if pts:
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    print(f"标题字 bbox: x {min(xs)}..{max(xs)} y {min(ys)}..{max(ys)} center=({(min(xs)+max(xs))/2:.0f},{(min(ys)+max(ys))/2:.0f})")

# 进度条蓝线(中下,亮蓝)
pts = [(x, y) for y in range(int(h*0.45), int(h*0.75)) for x in range(0, w)
       if px[x, y][2] > 150 and px[x, y][2] > px[x, y][0] + 60]
if pts:
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    print(f"进度条 bbox: x {min(xs)}..{max(xs)} y {min(ys)}..{max(ys)} center=({(min(xs)+max(xs))/2:.0f},{(min(ys)+max(ys))/2:.0f})")

# Tips 黄绿字(进度条下方偏左中)
pts = [(x, y) for y in range(int(h*0.5), int(h*0.75)) for x in range(0, int(w*0.7))
       if px[x, y][0] > 150 and px[x, y][1] > 160 and px[x, y][2] < 160]
if pts:
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    print(f"Tips bbox: x {min(xs)}..{max(xs)} y {min(ys)}..{max(ys)} center=({(min(xs)+max(xs))/2:.0f},{(min(ys)+max(ys))/2:.0f})")

# 60% 淡蓝白字(右下)
pts = [(x, y) for y in range(int(h*0.5), int(h*0.75)) for x in range(int(w*0.7), w)
       if px[x, y][0] > 180 and px[x, y][1] > 190 and px[x, y][2] > 200]
if pts:
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    print(f"60% bbox: x {min(xs)}..{max(xs)} y {min(ys)}..{max(ys)} center=({(min(xs)+max(xs))/2:.0f},{(min(ys)+max(ys))/2:.0f})")

# BG 图右边界(找最右非纯蓝灰列):相机底色约 (75,110,160)?
cam = px[w-5, 50]
print("相机底色:", cam)
for x in range(w-1, 0, -1):
    c = px[x, 50]
    if abs(c[0]-cam[0]) > 12 or abs(c[1]-cam[1]) > 12 or abs(c[2]-cam[2]) > 12:
        print(f"BG 右边界 x={x}")
        break
