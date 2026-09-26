#!/bin/bash
# 最终对照集(修复后):与 tools/screenshots/l1u_* Unity 真值逐拍对应
G=/g/FPSGame/godot/bin/godot.windows.editor.x86_64.mono.exe
OUT=G:/FPSGame/tools/screenshots/final_check
mkdir -p "$OUT"
cd /g/FPSGame/game || exit 1
for t in 3 8 15 21; do
  "$G" --path . scenes/levels/level1_battle.tscn -- "--level1-shot:$OUT/gd_intro_${t}s.png:${t}" "--shot-res:1472x668" > "$OUT/intro_${t}s.log" 2>&1
  echo "intro_${t}s done"
done
"$G" --path . scenes/levels/level1_battle.tscn -- "--level1-fire:$OUT/gd_fire.png:6" "--shot-res:1472x668" > "$OUT/fire.log" 2>&1
echo "fire done"
"$G" --path . scenes/levels/level1_battle.tscn -- "--level1-shot-boss:$OUT/gd_boss.png" "--shot-res:1472x668" > "$OUT/boss.log" 2>&1
echo "boss done"
"$G" --path . scenes/levels/level1_battle.tscn -- "--level1-shot-victory:$OUT/gd_victory.png" "--shot-res:1472x668" > "$OUT/victory.log" 2>&1
echo "victory done"
"$G" --path . scenes/levels/level1_story.tscn -- "--story-shot:$OUT/gd_story_11s.png:11" > "$OUT/story.log" 2>&1
echo "story done"
ls "$OUT"/*.png | wc -l
