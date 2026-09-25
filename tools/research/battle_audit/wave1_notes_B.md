# Wave1 Agent B 修复笔记(PlayerState / Player)

日期:2026(以最新会话为准)。工单:hud_fire.md #4/#7/#13(子弹分支)/#15/#16/#19/#23/#32 + #3 头像 HP 条节。
独占文件:`game/src/Player/PlayerState.cs`、`game/src/Player/Player.cs`(其余文件未动;BoxMonster 调用入口保持不变)。

## 修复清单(逐项)

### 1. HP 绿条隐藏(hud_fire #4/#3,l1_visual #2)— 已修
- 真值:l1u_gun_25s / l1u_battle_45s 满血/残血左右均无可见 HP 条(以截图为真值,未按 prefab 坐标另摆)。
- 改法:`PlayerState.MakeHeadHud` 的 TextureProgressBar 加 `Visible = false`;节点/贴图/尺寸保留,`RefreshBattleHud` 仍每帧刷新 `_hpRight/_hpLeft.Value`(HP 值内部追踪与 API 不变)。
- 截图验证:wave1_b/hud_25s.png 左右头像环下无绿条 ✓。

### 2. Debug 90 子弹分支(hud_fire #7/#13)— 已修
- 真值:原作 `Player.Born()`(Player.cs:50-57):`GlobalObject.IsDebug → Bullet=90`,否则 `DataMgr.Ins._UserData.MaxBullet(120)`。
- 改法:`Player.Born` 改 `_bullet = Game.Instance.IsDebug ? 90 : SaveService.Instance.MaxBullet;`(Game.IsDebug 已存在,默认 true 照原作)。
- 截图验证:wave1_b/hud_25s.png 子弹 ×90 ✓。

### 3. 左子弹位置(hud_fire #15)— 已修
- 真值:PlayerSystem.prefab BulletLeft anchor(0.5,0) **pivot(0.5,0.5)** pos (-258.72,31.4) → 图标中心在屏底上方 31.4px;BulletRight pivot(0.5,0) pos (185.12,0) → 图标底贴屏底(原 Godot 对左误用 pivot(0.5,0) 语义,中心低 32px)。
- 改法:`MakeBulletHud` 增加 `pivotY` 参数(右 0.0 / 左 0.5),底锚偏移公式 `OffsetTop/Bottom = pivotY*64 - offset.Y - 64 / pivotY*64 - offset.Y`。右弹偏移逐值不变,左弹 OffsetTop=-63.4/OffsetBottom=0.6 → 中心恰在屏底上方 31.4px。
- 说明:单人模式左弹隐藏,截图不可见,几何按 prefab 数值核对。

### 4. 子弹"×"位置 + 数字垂直(hud_fire #16/#32)— 已修
- 真值:prefab 内 X(Text)anchor/pivot (0.5,0.5) pos **(58.43,-23.58)** size 60×60,28 号(右暗绿 (0.0088,0.3113,0.1265)/左暗黄绿 (0.3973,0.4057,0),原已一致);BulletRightText/BulletLeftText 左中锚 (0,0.5) pivot (0,0.5) pos **(83.31,-3.1)** size 120×60(Unity +y 向上)。
- 改法:X 偏移改为中心在图标中心 (+58.43,+23.58 Godot 坐标)(图标右下);数字 Label `Position` 由 `(83.31, -3.1-30)` 改 `(83.31, 3.1-30)` → 数字中心在图标中心**下方** 3.1px(原误在上方)。
- 截图验证:× 在图标右下、暗绿色,后跟白字 90 ✓(对照 l1u_gun_25s 同构)。

### 5. 受击红屏(hud_fire #19)— 已修
- 真值(PlayerSystem.cs:262-300 UpdateHurtEffect):运行时物理=黄 **(1,1,0)**、冰=青 **(0,1,1)**、毒=绿 **(0,1,0)**;**起始 alpha=1**,每 0.1s 减 0.05(约 2s 淡完),结束 **alpha 复位 1** 后 SetActive(false);效果激活中再受击 → 颜色保持(`c = img.color` 覆盖新色),仅 alpha 复位 1 继续淡。
- 改法:`OnHurtFlash` 全换:原近似色 (1,0.85,0.2)/(0.3,0.9,1)/(0.4,1,0.3) + 起始 0.392 → 原色 + 起始 a=1;0.2s 保持+1.6s 渐隐 → 线性 2.0s a→0(= 0.05/0.1s 速率);结束回调 a 复位 1 + `Visible=false`。进行中再受击:杀旧 tween、保色、a=1 重起淡出(与原作协程行为等价)。
- `MakeHurtOverlay` 初始 `Visible=false`、Modulate 恢复 prefab 值 (1,0,0,0.392)(原 a=0 常驻可见)。
- 运行时验证:level1 自检 `HitPlayer(999,Phy,Right)` 路径通过,无异常。

### 6. 加子弹放大动画(hud_fire #23)— 已补实现
- 真值(PlayerSystem.cs:475-516 AddBulletAni):图标根 `localScale ×1.5`,while 循环每帧 `AddBullet(1)` 并刷新文本(yield return null = 每帧 +1 逐发增长),结束恢复 1.0;宝箱触发(BoxMonster.cs:119);Born/Relife 直接赋值无动画。
- 改法(BoxMonster.cs 未动,`p.Bullet += 60` 调用入口保持):
  - `Player.Bullet` 字段 → 属性(backing `_bullet`)。setter 检测"外部增加且 Active"→ 回调 `PlayerState.OnBulletAdded(this, old, new)`;`Born`/`Relife` 直写 `_bullet` 绕过(原作无动画)。
  - `PlayerState.OnBulletAdded/AddBulletAni`:图标根 `Scale=1.5`(绕原作 pivot:右底中/左中心,由 MakeBulletHud 写入 `PivotOffset`),文本从当前显示值每帧 +1 涨到终值(`await ProcessFrame`),结束恢复 `Vector2.One`。seq 代际防重入(连开两箱时新动画从当前显示值接管);`RefreshBattleHud` 在动画侧不覆盖文本;`CancelBulletAni` 在 `UpdateUiMode` 入口中断并复位(≈原作 SetActive(false) 杀协程)。
  - 差异说明:原作协程逐帧加"实际子弹数",Godot 版实际子弹即刻到位、仅显示逐帧涨(视觉一致;动画中途开枪的弹药可用性为边缘差异,Level1 无影响)。
- 运行时验证:level1 自检 DebugAutoKill 全程(宝箱 159 次概率roll,含掉落路径)无异常,全 PASS。

### 7. 左 HUD 显隐 — 未改行为(待确认项记录)
- 现状保持与 L1 真值一致:"左环常显(即使左手未激活)、左图标仅左手激活时显示"(`UpdateUiMode` 战斗分支 `_headLeft.Visible=true`、`_headLeftIcon.Visible=PlayerLeft.Active`)。
- **待确认项**(l234 L3-6):原作 UpdateUI 在左手未激活时 `HeadLeftIcon.SetActive(false)` 但不隐藏 HeadLeft 根,与现 Godot 行为一致;但原作 `UpdateUIMode` 战斗分支对所有场景(含开场)即 SetActive(true) 子弹/头像,开场隐藏子弹的实际机制(疑为 Born 前 Active=false 由 UpdateUI 门控)未逐帧验证——Godot 以 `ShowBattleBullets` 开战显形达到同一画面,行为等价性待后续双人对照确认。

### 8. UpdateUiMode 显隐表复核(PlayerSystem.cs:322-364)— 已核 + 小修
- 对照结果:Menu/LevelChoose=只金币、DeviceConnection/LoadingScene=全隐藏、其余=战斗,三分支原已一致;子弹开战显形为审计确认的一致项(见上)。
- 偏差修复:原作**任意分支入口**均 `HurtEffectRight/Left.SetActive(false)`,Godot 原仅靠 alpha=0(节点常驻)。现 `UpdateUiMode` 入口统一调 `HideHurtOverlays()`(杀 tween+隐藏)+ `CancelBulletAni()`。
- 加/回血数字动画其余细节:AddHpAni(原作 PlayerSystem.cs:403-449,×1.5+每帧+10)在 Level1 无调用点(原作死代码),按工单未移植,记此备注。

## 验证结果

| 项 | 命令 | 结果 |
|---|---|---|
| 构建 | `cd game && dotnet build`(带 .build_lock 文件锁) | 0 错误 |
| level1 自检 | `godot --headless --path . scenes/levels/level1_battle.tscn -- --level1-selftest` | 全 PASS,exit 0(含 player_died→open_continue、Relife、boss、victory、HUD coin) |
| menu 自检 | `godot --headless --path . scenes/ui/menu.tscn -- --menu-selftest` | `MENU SELFTEST PASS (fails=0)`,exit 0,共享 HUD 无误伤(末尾 MessageBox.grab_focus/LevelChoose SubViewport 报错为切场景拆除期既有问题,文件均非本 agent 所改) |
| 截图 | `--level1-shot:...wave1_b/hud_25s.png:25 --shot-res:1472x668` | `tools/screenshots/audit/wave1_b/hud_25s.png`:无 HP 绿条 ✓、子弹 ×90(debug)✓、× 在图标右下 ✓、金币/头像环不变 ✓(对照真值 l1u_gun_25s.png) |

## 移交/边界说明

- 未做(他 agent 范围):暂停键蓝块/面板文字(InGamePanel,agent C)、命中橙团/激光(LaserSight/FireSystem,agent D)、怪物( agent A)。
- 未提交 git;README 未动;`game/` 下仅改 `src/Player/PlayerState.cs`、`src/Player/Player.cs`。
- 新增产物:`tools/screenshots/audit/wave1_b/hud_25s.png`、本笔记。
- 已知出界偏差(不在本工单,记录备查):hud_fire #31 HUD 全局缩放策略(Unity ConstantPixelSize 1:1 vs Godot canvas_items+expand 1280×720)使 HUD 元素相对屏宽位置与真值有系统性差异(如右子弹真值 rel.x≈0.75 vs Godot ≈0.62),属 project.godot stretch 设置层,非本工单修项。
