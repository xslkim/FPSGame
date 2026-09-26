#!/bin/bash
# 采集当前 Godot 版本 Level1 全节拍截图,与 tools/screenshots/l1u_* Unity 真值对比
G=/g/FPSGame/godot/bin/godot.windows.editor.x86_64.mono.exe
GAME=/g/FPSGame/game
OUT=G:/FPSGame/tools/screenshots/check1
mkdir -p "$OUT"
cd "$GAME" || exit 1

# school_day 含 19s 开场(对应 l1u_intro_*,时刻自场景启动)
for t in 3 8 15 21 26 35; do
  echo "=== intro_${t}s ==="
  "$G" --path . scenes/levels/level1_battle.tscn -- "--level1-shot:$OUT/gd_intro_${t}s.png:${t}" "--shot-res:1472x668" > "$OUT/intro_${t}s.log" 2>&1
done
# debug 直开战(对应 l1u_battle_*)
for t in 6 12 25 45; do
  echo "=== battle_${t}s ==="
  "$G" --path . scenes/levels/level1_battle.tscn -- "--level1-shot-battle:$OUT/gd_battle_${t}s.png:${t}" "--shot-res:1472x668" > "$OUT/battle_${t}s.log" 2>&1
done
# 机位 G0~G3(对应 l1u_cam_G*)
for g in 0 1 2 3; do
  echo "=== cam_G${g} ==="
  "$G" --path . scenes/levels/level1_battle.tscn -- "--level1-shot-group:$OUT/gd_cam_G${g}.png:${g}" "--shot-res:1472x668" > "$OUT/cam_G${g}.log" 2>&1
done
# Boss 波
echo "=== boss ==="
"$G" --path . scenes/levels/level1_battle.tscn -- "--level1-shot-boss:$OUT/gd_boss.png" "--shot-res:1472x668" > "$OUT/boss.log" 2>&1
# 剧情场景(Level1.unity 等价),真值 2454x668 超宽
for t in 3 11 26 40 52; do
  echo "=== story_${t}s ==="
  "$G" --path . scenes/levels/level1_story.tscn -- "--story-shot:$OUT/gd_story_${t}s.png:${t}" "--shot-res:2454x668" > "$OUT/story_${t}s.log" 2>&1
done
echo "ALL DONE"
ls "$OUT"/*.png | wc -l
