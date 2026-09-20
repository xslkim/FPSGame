# 测 godot 截图文字行带位置(画布单位)
from PIL import Image
import sys

im = Image.open(r"G:\FPSGame\tools\screenshots\gd_device.png").convert("RGB")
w, h = im.size
px = im.load()
prev = 0
for y in range(20, 540, 2):
    cnt = sum(1 for x in range(200, 720, 2) if all(c > 170 for c in px[x, y]))
    if cnt or prev:
        print(f"y={y:3d} {'#' * cnt}")
    prev = cnt
