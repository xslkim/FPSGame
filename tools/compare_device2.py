# 测说明文字白字 bbox(左上区域亮白像素)与底衬(比背景暗的矩形,用行间采样)
from PIL import Image

def text_bbox(path, label, scale, x_max_frac, y_max_frac):
    im = Image.open(path).convert("RGB")
    w, h = im.size
    px = im.load()
    pts = [(x, y) for y in range(0, int(h * y_max_frac)) for x in range(0, int(w * x_max_frac))
           if px[x, y][0] > 180 and px[x, y][1] > 180 and px[x, y][2] > 180]
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    print(f"{label}: text x {min(xs)/scale:.0f}..{max(xs)/scale:.0f} y {min(ys)/scale:.0f}..{max(ys)/scale:.0f}"
          f" (w={(max(xs)-min(xs))/scale:.0f} h={(max(ys)-min(ys))/scale:.0f})")

def backing(path, label, scale):
    im = Image.open(path).convert("RGB")
    w, h = im.size
    px = im.load()
    # 底衬左边约在 6%~30% 宽、垂直中段。取 x=8% 处一列,与 x=50%(底衬外)同列亮度对比
    col_in = int(w * 0.08); col_out = int(w * 0.55)
    ys = []
    for y in range(0, int(h * 0.85)):
        din = sum(px[col_in, y]); dout = sum(px[col_out, y])
        if dout - din > 25:  # 底衬处显著更暗
            ys.append(y)
    if ys:
        print(f"{label}: backing y {ys[0]/scale:.0f}..{ys[-1]/scale:.0f} (h={(ys[-1]-ys[0])/scale:.0f})")
    # 横向:取 y=45% 高一行,与 y=92% 行对比
    row_in = int(h * 0.45); row_out = int(h * 0.92)
    xs = []
    for x in range(0, int(w * 0.6)):
        din = sum(px[x, row_in]); dout = sum(px[x, row_out])
        if dout - din > 25:
            xs.append(x)
    if xs:
        print(f"{label}: backing x {xs[0]/scale:.0f}..{xs[-1]/scale:.0f} (w={(xs[-1]-xs[0])/scale:.0f})")

s = 322 / 717.7
text_bbox(r"G:\FPSGame\tools\screenshots\unity_device.png", "unity", s, 0.58, 0.75)
text_bbox(r"G:\FPSGame\tools\screenshots\gd_device.png", "godot", 1.0, 0.58, 0.75)
backing(r"G:\FPSGame\tools\screenshots\unity_device.png", "unity", s)
backing(r"G:\FPSGame\tools\screenshots\gd_device.png", "godot", 1.0)
