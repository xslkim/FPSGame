#!/bin/bash
# 战斗+UI 全回归:编译 C# 后顺序跑全部 headless 自检(顺序跑避免 UDP 8281 端口冲突)
# 用法: bash tools/research/battle_audit/regress.sh
cd /g/FPSGame/game || exit 1
echo "=== BUILD ==="
dotnet build 2>&1 | grep -E "错误|error" | head -5
echo "build done"
G=/g/FPSGame/godot/bin/godot.windows.editor.x86_64.mono.exe
run() { echo "=== $1 ==="; $G --headless --path . "$2" -- "$3" 2>&1 | grep -E "TEST\] (PASS|FAIL)|^(PASS|FAIL) |SELFTEST (PASS|FAIL)"; }
run L1-BATTLE  scenes/levels/level1_battle.tscn  --level1-selftest
run L1-STORY   scenes/levels/level1_story.tscn   --story-selftest
run L2         scenes/levels/level2.tscn         --level2-selftest
run L3         scenes/levels/level3.tscn         --level3-selftest
run L4         scenes/levels/level4.tscn         --level4-selftest
run MENU       scenes/ui/menu.tscn               --menu-selftest
run LEVELCHOOSE scenes/ui/level_choose.tscn      --levelchoose-selftest
run DEVICE     scenes/ui/device_connection.tscn  --deviceconnection-selftest
echo "=== REGRESS DONE ==="
