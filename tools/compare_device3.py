# 测三处文字的笔画 bbox,算 Unity/Godot 有效字号比
from PIL import Image

OFF = 77.5  # 真值屏幕比画布宽出的半边(单位)

def bbox(path, pred, region):
    im = Image.open(path).convert("RGB")
    w, h = im.size
    px = im.load()
    x0, x1, y0, y1 = region
    pts = [(x, y) for y in range(y0, y1) for x in range(x0, x1) if pred(px[x, y])]
    if not pts:
        return None
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    return min(xs), max(xs), min(ys), max(ys)

white = lambda c: c[0] > 180 and c[1] > 180 and c[2] > 180
yellow = lambda c: c[0] > 180 and c[1] > 180 and c[2] < 120

s = 322 / 717.7
def u_conv(b):
    return tuple(round(v / s - (OFF if i < 2 else -(-1.15)) , 1) for i, v in enumerate(b)) if b else None

# unity 644x322
u_title = bbox(r"G:\FPSGame\tools\screenshots\unity_device.png", yellow, (400, 644, 0, 60))
u_back  = bbox(r"G:\FPSGame\tools\screenshots\unity_device.png", white, (240, 410, 250, 320))
u_instr = bbox(r"G:\FPSGame\tools\screenshots\unity_device.png", white, (0, 360, 0, 220))
# godot 1280x720
g_title = bbox(r"G:\FPSGame\tools\screenshots\gd_device.png", yellow, (800, 1280, 0, 120))
g_back  = bbox(r"G:\FPSGame\tools\screenshots\gd_device.png", white, (470, 830, 560, 700))
g_instr = bbox(r"G:\FPSGame\tools\screenshots\gd_device.png", white, (0, 720, 0, 440))

def show(name, u, g):
    if u and g:
        uh = (u[3]-u[2]) / s; uw = (u[1]-u[0]) / s
        gh = g[3]-g[2]; gw = g[1]-g[0]
        print(f"{name}: unity w={uw:.0f} h={uh:.0f} top={(u[2]/s-1.15):.0f} left={(u[0]/s-OFF):.0f} | godot w={gw:.0f} h={gh:.0f} top={g[2]} left={g[3] and g[0]} | 高比={gh/uh:.3f} 宽比={gw/uw:.3f}")

show("标题68", u_title, g_title)
show("返回60", u_back, g_back)
show("说明40", u_instr, g_instr)
