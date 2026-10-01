# FPSGame 复刻工程交接文档(新 agent 必读)

> 写于 2026-09-28,L3/L2 两轮实机反馈修复完成后。本文档 + `tools/research/battle_audit/l234.md`(第五/六轮详情)+ `README.md`(挂接清单)+ `AGENTS.md` 构成续作最小上下文。

## 1. 现状一句话

Unity 2019 街机光枪 FPS → Godot 4.8(自编译 mono 引擎)+ C# 复刻。**Level1/2/3 已 1:1 复刻并通过实机反馈验证;Level4 主体已做、剩若干遗留项**。当前有**一整轮未提交改动**(L3 第五轮 + L2 第六轮,含 dist 成品,regress 186 项全绿),用户未说"提交"前不要 commit。

## 2. 双仓库与纪律

- **主仓 `G:\FPSGame`**(Godot 引擎源码 `godot/` + 游戏 `game/` + 工具/文档 `tools/`)。dist/ 在 gitignore;temp/(用户截图)不提交;`graphify-out/`、`tools/__pycache__` 不提交。证据截图目录 `tools/screenshots/fix3|fix4/` 按惯例提交。
- **Unity 仓 `G:\test\FPSGame`**:**只读真值参考**(其 AGENTS.md 约束;不主动提交/不改动)。可 grep 场景 YAML/prefab/脚本取真值。2019.4 batchmode 许可证已挂(无法补拍截图);GUI 编辑器用户可开;**2022.3.62f3 可用**(牛魔王/K-POP 烘焙曾在临时工程 `G:\tmp\kpopbake` 完成)。
- **提交纪律:用户明说"提交"才 commit,从不 push。**

## 3. 工具链(全部验证过)

```bash
cd /g/FPSGame/game && dotnet build                                    # 编译(看"个错误"行)
G=G:/FPSGame/godot/bin/godot.windows.editor.x86_64.mono.exe
bash tools/research/battle_audit/regress.sh                           # 全回归 8 场景 186 项,无 FAIL 即绿
cd game && $G --headless --path . --export-release "Windows" ../dist/FPSGame.exe   # 导出
# 成品冒烟(导出包吃同样参数):
dist/FPSGame.exe -- "--quick:res://scenes/levels/level3.tscn" --no-mouse "--level3-shot:x.png:8"
```

窗口截图**必带 `--no-mouse`**(否则物理光标会瞄准/点击污染画面,误中暂停按钮会把游戏锁死在暂停-关闭循环——历轮多组验证截图曾被毁)。
截图模式各关 `SwitchCamera` 直接到位不 blend(ShotModeSnap),所以截图里看不到开场运镜;运镜要实跑观察。
多 shot 注意:`--levelX-shot` 的 delay < 序列初始 2s 等待时,全部坍缩到同一帧截(不是 bug)。

## 4. 已完成工作(按轮次,详情见各偏差文档)

- **Level1**(早前):战斗/剧情 19s 开场/K-POP 舞蹈(2022 烘焙管线)/背负动画/激光红点共线/枪口朝向/humanoid 牛魔王/粒子透明度/菜单卡死。真值 `tools/screenshots/l1u_*`。
- **L2 光照**(早前 wave3):黄昏毒气镇按真值重做(暗 ambient+远景压暗+9 窗口点光),文档 `battle_audit/wave3_l2_lighting.md`。
- **L2/L3/L4 审计**:`battle_audit/l234.md` 偏差表(10/9/11 项)。
- **第五轮 L3 实机反馈**(本轮):
  1. 枪口火光偏后 ~9cm → `MuzzleFlash.cs` Flame/Smoke 前移到枪口尖(真值 `m7b_muzzle_fire_f1.png`;检视 `game/tools/flash_view.gd`);
  2. dist 丢外星人头盔/步枪、toon AK47(tscn 挂 FBX 子树节点导出丢失)→ `ToonMonster.EnsureAttachments()` 代码补挂;
  3. L3 开场运镜缺失 → `level3.tscn` 相机初始=cam_pos_6(Unity 主相机=vcam6 位姿),`LevelBase.CamBlendTime` 1→2s(L1-L4 brain m_DefaultBlend 全为 2s,旧注释"Level1.asset:1s"系误记);
  4. 出生/掉落查证排除:7 机位 8m/12m 出生锥+网格+Boss 路径探针全覆盖有地面;"空降"是原作语义。
- **第六轮 L2 复刻**(本轮):
  1. 偏差表 8/10 早前已修(Hard G0=30 bug-for-bug/toon·箱等待/Boss×3/Skill1 旋转/死亡下沉/cam_far/光照);本轮补 L2-5 `boss_pos`=(11.12,-8,0.74)(meta+SpawnBossDelayed+定格断言);
  2. L2 开场运镜 → 相机初始=cam_pos_1(=Unity 主相机位姿,逐元素一致);
  3. **"怪物掉下去"(用户指认 L2)根因**:Unity `ToonMonster.UpdateMoveTo` `dir.y=0` 纯水平、全状态无重力源;Godot 各分支误加 ApplyGravity → toon 沉穿廊桥接缝。修:ToonMonster 全分支去重力 + Level2 出生嵌入上推(w4 檐板特例)+ `game/tools/env_collision_bake.gd` 补 6 个缺碰撞网格(Unity prefab 全带 MeshCollider);
  4. 左 HUD 红环悬案(L3-6)**终裁维持常显**:代码仅藏脸图标,自采 L1/L2 真值(l1u_*/level2_unity)左上有环无脸;level3/l4 存量截图来源不同不作准。
- 回归:186 PASS / 0 FAIL;dist 最新包含全部修复(外星人戴盔/枪口火光/开场运镜/窗口 toon 站立射击)。

## 5. 未提交改动清单(git status)

- 改:`README.md`、`game/data/level_meta.json`(L2 boss_pos)、`game/scenes/levels/level2.tscn`(相机初始=cam_pos_1)、`level3.tscn`(相机初始=cam_pos_6)、`env_level2.tscn`(6 个网格补 trimesh 碰撞)、`src/Battle/{Level2,Level3,LevelBase,ToonMonster,FireSystem}.cs`、`src/Effects/MuzzleFlash.cs`、`src/Input/InputRouter.cs`、`src/Player/PlayerState.cs`(注释更正,行为不变)、`tools/research/battle_audit/l234.md`(第五/六轮)。
- 新:`game/tools/{env_collision_bake,flash_view,l2_probe}.gd(+uid)`、`tools/l3_*.py`(相机提取脚本)、`tools/screenshots/fix4/`(证据)、`tools/logs/regress_*.log`。
- 注意:老 `game/tools/l3_probe.gd`、`l3_watch.gd` 已删(一次性);`game/tools/flash_view.gd`/`l2_probe.gd`/`env_collision_bake.gd` 保留可复用。

## 6. 下一步候选(按优先级)

1. **Level4 遗留**(`l234.md` 偏差表):L4-3 Magma 四色变体(Blue/Green/Orange/Purple 材质+pool 拆键)、L4-6 方向光被停用(env_level4 visible=false,确认是否有意)、L4-7 ambient 来源(sky0.6 vs flat 0.801)、**L4 开场运镜未做**(Unity 主相机预摆 (36.376953,16.273102,-1.6030273),参照 L2/L3 做法改 level4.tscn 相机初始位姿)、L4-4/5/8/9/10/11 小项。龙四点/出生瞬移已修(自检有断言)。
2. **L3 小项**:L3-5 天空观感(无云,全景图朝向)、L3-9 move_speed_level_rate 0.5→0.3。
3. **X-3 枪占屏比**:真值 L3/L4 枪更贴右下小一些;`FireSystem.GunModelScale=0.725` 是按 L1 FOV45 真值定的,L3 FOV52 下观感略大——若用户再提再调。
4. L2-10 箱满 tick 语义(影响极小,已声明不修)。

## 7. 关键认知(勿推翻)

- **2026-09-29 复核镜像**:L2 的 `Environments` 根在 Unity 为 X=-84.9、Godot 为 X=+84.9;`WoodenTown (1)` 的局部 X 也从 +100 变为 -100,所以 L2 环境同样沿 X 镜像。原文“L2 无镜像、机位数值直用”不成立。现已镜像 L2 三个战斗机位、窗口/点光及 Boss 出生点;流程自检通过,但场景截图构图仍未对齐 Unity。
- **tscn Transform3D 序列化是天坑**:9 个 basis 分量按行存,`basis.z`(Godot 前向参考)= 第 3/6/9 个分量组成的列。错读会导致"机位看反方向"的假结论(本轮已踩,核实脚本 `tools/l3_cam_extract.py`)。
- **导出包≠编辑器的三类坑**(全部踩过):① FBX 实例子树内挂的节点(BoneAttachment 等)导出后整支丢失 → 改代码补挂;② FBX 实例子节点 surface_material_override 导出失效 → Monster 基类 `[Export] Material? BodyMaterial` 代码接线;③ include_filter 不含未导入文件(.kdance.bin 已加)。**改视觉效果必须在 dist 成品复验**。
- **怪物行为语义**:出生锥=相机原点±33°/8m(FOV>50 用 40°),`pos.y-=胶囊中心y+0.1`,不贴地,等待期 3m/s 落地("空降"是原作语义);近战怪半径内站桩不贴脸;**toon(L2 窗口兵)全状态无重力**;Boss 缩放:L2 ×3、L3 rock_warrior ×5(tscn 根 scale,勿动)。
- **BossGroundPatch**(level3.tscn):Boss 街区缺碰撞的 20×30 补丁,Boss 全程只走 ~8m 不出补丁;`env_collision_bake.gd` 可给 env 补 trimesh 碰撞(跳 _LOD1-3/cliff/Terrain)。
- **测试基建**:`--no-mouse` 窗口截图必带;ShotModeSnap=截图模式相机直到位;`--autofire` 0.4s 强制开火;`--cam-debug` blend 打点;`--mat-audit` 材质审计;`--fire-debug` 射线命中打印;怪物/枪/特效检视 `--monster-view/--gun-view/--fx-view`。
- **真值来源优先级**:自采截图(l1u_*/level2_unity)> Unity 场景 YAML/prefab/脚本数值 > 存量截图(level3_battle/l4_*,来源不同,HUD 类判断不作准)。
- **窗口验证截图的纪律**:`--levelX-shot` delay 要 >2.5s 且间隔 >0.3s(初始 2s 等待+同帧坍缩)。

## 8. 每关标准工作流(照旧)

1. 读 `l234.md` 该关偏差表 + 现状(有些可能已修)→ 2. 真值核对(Unity YAML/自采截图)→ 3. 修复+自检断言 → 4. 截图对比(`--no-mouse`,编辑器)→ 5. `regress.sh` 全绿 → 6. 导出 dist + 成品复验 → 7. 文档(l234 新轮次+README 若动挂接)→ 8. **报告并等用户说"提交"**。

## 9. 文档地图

- `tools/research/battle_audit/l234.md`:L2/L3/L4 偏差总表 + 第五/六轮修复记录(最新)。
- `tools/research/battle_audit/DEVIATIONS.md`:战斗场景种子偏差终态表。
- `tools/research/level1_deviation_log.md`:L1 全轮次记录(#25-#33 为最近)。
- `README.md`:运行/操作/挂接清单/导出/保真注记。
- `battle_audit/battle0_mirror_verdict.md`:L1 镜像裁决依据。
- 证据截图:`tools/screenshots/fix3/`(上一轮)、`fix4/`(本轮);真值:`l1u_*`、`level2_unity.png`、`level3_battle.png`、`l4_battle_*`。
