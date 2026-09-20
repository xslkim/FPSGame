# 对比 unity_device.png(真值,644x322,scale=0.4486 px/unit,世界画布中心=(322,161))
# 与 gd_device.png(1280x720 逻辑=物理)的关键矩形
from PIL import Image
import sys

def analyze(path, label, scale, cx, cy):
    im = Image.open(path).convert("RGB")
    w, h = im.size
    px = im.load()
    print(f"== {label} {w}x{h}")

    # 1) 底衬:半透明黑盖在背景上 → 同一列内比背景更暗的长条区域。扫 x = 25% 宽处一列
    col = int(w * 0.25)
    darks = [y for y in range(h) if sum(px[col, y]) < 60]
    if darks:
        print(f"  backing col25%: y {darks[0]}..{darks[-1]} -> top={darks[0]/scale:.1f} bottom={darks[-1]/scale:.1f} (units, 顶原点)")
    row = int(h * 0.30)
    darkx = [x for x in range(w) if sum(px[x, row]) < 60]
    if darkx:
        print(f"  backing row30%: x {darkx[0]}..{darkx[-1]} -> left={darkx[0]/scale:.1f} right={darkx[-1]/scale:.1f}")

    # 2) 二维码白块:扫右上区域亮像素
    bright = [(x, y) for y in range(0, int(h*0.75)) for x in range(int(w*0.6), w)
              if px[x, y][0] > 200 and px[x, y][1] > 200 and px[x, y][2] > 200]
    if bright:
        xs = [p[0] for p in bright]; ys = [p[1] for p in bright]
        print(f"  QR white bbox: x {min(xs)}..{max(xs)} y {min(ys)}..{max(ys)}"
              f" -> units x {min(xs)/scale:.1f}..{max(xs)/scale:.1f} y {min(ys)/scale:.1f}..{max(ys)/scale:.1f}"
              f" center=({(min(xs)+max(xs))/2/scale - cx/scale:.1f}, {cy/scale - (min(ys)+max(ys))/2/scale:.1f})")

    # 3) 返回按钮:蓝色按钮边框,扫底部中央。Btn_button03 边框亮蓝 (r<150,g>100,b>150)?
    btn = [(x, y) for y in range(int(h*0.72), h) for x in range(int(w*0.3), int(w*0.7))
           if px[x, y][2] > 130 and px[x, y][2] > px[x, y][0] + 40]
    if btn:
        xs = [p[0] for p in btn]; ys = [p[1] for p in btn]
        print(f"  back btn bbox: x {min(xs)}..{max(xs)} y {min(ys)}..{max(ys)}"
              f" -> units w={(max(xs)-min(xs))/scale:.1f} center_up={cy/scale - (min(ys)+max(ys))/2/scale:.1f}")

# 真值:fov60 在 621.6 处可见高 717.7;scale = 322/717.7
analyze(r"G:\FPSGame\tools\screenshots\unity_device.png", "unity", 322/717.7, 322, 161)
# ours: 1280x720, 画布=屏幕
analyze(r"G:\FPSGame\tools\screenshots\gd_device.png", "godot", 1.0, 640, 360)
