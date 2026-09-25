# Wave1 Agent C 修复清单 + 验证结果

负责文件:`game/src/Battle/InGamePanel.cs`、`game/src/UI/UiButton3D.cs`、`game/src/Core/SaveService.cs`
工单:hud_fire.md #1/#5/#6/#11(面板部分)/#12/#13/#14/#22/#26/#27/#28/#29/#30;l1_visual.md #1/#11(暂停按钮视觉/时机)
日期:2026-09-24。未 git commit;未改 README;未越界改他人文件。

## 修复明细

| 工单 | 修复 | 位置 |
|---|---|---|
| #1 面板文字全灭(高) | Label3D `PixelSize` 0.001 → **1.0**(面板局部 1 单位 = 1 canvas px,字号数字即 canvas 像素,由面板根缩放换算到米;不再双重缩小) | InGamePanel.cs MakeText / MakeButton / BuildPauseBtn |
| #22 面板根缩放 | `Scale` 0.001 → **(0.00115, 0.00115, 0.01)**(照 InGamePanel.prefab 根 m_LocalScale) | InGamePanel.cs CanvasScale/CanvasScaleZ |
| #5 暂停按钮样式 | 底框 alpha 生效为半透明 (0,0,0.4528,**0.2353**);**删掉"主菜单"40 号文字**(原作子 Text 空串);暂停图标 80×80 → **120×120 全幅** | InGamePanel.cs BuildPauseBtn;UiButton3D.cs |
| #5 UiButton3D Transparency | 修条件:无贴图也开 `Transparency=Alpha`(原来仅贴图非空才开,无贴图按钮 alpha 失效成实心块) | UiButton3D.cs _Ready |
| #6 暂停键可见时机 | 默认隐藏;**开战(LevelBase.StartBattle,以公开字段 BattleStartTime>0 判定)才显示**。L1 开场 19s 内隐藏;L2/L3/L4 无开场进场即战立即显示。SceneState 在 _Ready 即为 Battle,不能作判据,故用 BattleStartTime | InGamePanel.cs UpdatePauseBtn |
| #26 暂停键面板联动 | 任一面板(Pause/Continue/Victory)打开 → 暂停键隐藏,关闭恢复。由 UpdatePauseBtn 每帧统一判定;`UiButton3D.SetActiveVisible` 同步切 Visible + CollisionLayer(隐藏时枪射线不可命中,等价原作 SetActive) | InGamePanel.cs;UiButton3D.cs |
| #12 兑换子弹分支 | OpenContinue(bullet=true):标题"兑换子弹"、Info"兑换子弹需要消耗一枚游戏币,每3分钟增加一枚游戏币,最高10枚。"、按钮"兑换子弹";bullet=false 对应"继续游戏"文案(原文照抄原作 InGamePanel.cs:74-104) | InGamePanel.cs OpenContinue |
| #12③ 币不足 | 币 <1 时按钮文字"币不足"(不置灰,照原作;ContinueGame 由 Coin<1 拦截) | InGamePanel.cs OpenContinue/ContinueGame |
| #13 Info 动态文本 | `InfoTextFor(bullet)` 由 SaveService.AddCoinTime/60 与 MaxCoin 动态生成(180/10 → "每3分钟…最高10枚"),替换硬编码"每15分钟…最高5枚" | InGamePanel.cs InfoTextFor |
| #14 金币回复时机 | 删除 SaveService._Process 后台持续回币;改为 **`TickCoinRegen()` 仅由 InGamePanel 在 Continue 面板打开期间逐帧调用**(原作 InGamePanel.cs:178-198 Update 门控)。满币时也推进计时(原作 LastAddCoinTime 无条件重置,倒计时循环)。选关/菜单金币显示只读,不受影响 | SaveService.cs;InGamePanel.cs _Process |
| #27 倒计时/币满 | 格式 `min + ":" + second` 不补零(实测截图 "0:46");删除多出的"金币已满"文本(原作无,满币照显倒计时) | InGamePanel.cs _Process;SaveService.TimeToNextCoin 去掉满币短路 |
| #28 Continue 标题 | AddTitle 参数化字号与左右装饰条偏移:Continue 标题 **50 号** @(5.2,202),装饰条 -190/+199(prefab 实测,与标题 x 偏移无关);Pause 60 号 ±173.2/173.1;Victory 60 号 -235.75/+239.7 | InGamePanel.cs AddTitle |
| #29 Continue-MenuBtn | "主菜单" **40 号白**(原 52 号浅绿) | InGamePanel.cs BuildContinuePanel |
| #30 BackToMenu 防误触 | `Time.GetTicksMsec()/1000 - _openContinueTime < 1` 直接 return(等价原作 Time.unscaledTime - startOpenContinueTime;计时点仅在 OpenContinue 时更新,照原作) | InGamePanel.cs BackToMenu |

## 修复中发现的隐藏 bug(一并修复)

- **UiButton3D 文字偶发被底图盖住**:Continue 面板 ConfirmBtn 的 Label3D 在部分配置下(同字重排/图集差异)透明排序输给同按钮的半透明底图 quad(z 差仅 0.1mm 世界),文字整枚消失。实测 _dbg_relif2 复现稳定、SortingOffset=0.1 实验修复。根治:`UiButton3D._Ready` 给 `_label.SortingOffset = 0.1f`;InGamePanel 的 MakeText/暂停图标同法兜底(VisualInstance3D.sorting_offset 直接参与透明队列深度排序,引擎源码 render_forward_mobile.cpp:2245 `depth = distance_to(center) - sorting_offset`)。
- **InfoText 不换行横穿全屏**:Label3D 默认无宽度限制;InfoText 加 `Width=400 + AutowrapMode.Arbitrary`(照原作 400×150 rect 内按字换行)。

## 验证结果

### 构建 + 自检(全绿)

- `dotnet build`:0 error(期间两次撞到并行 agent 改 Level2.cs/FlyAxeMonster.cs 的半成品,等其稳定后复建通过;错误均非本工单文件)。
- L1 自检:`--level1-selftest` → exit 0,18×PASS,`done, failed=False`(日志 tools/logs/wave1_c_selftest.log)。
- L2 自检:`--level2-selftest` → exit 0,36×PASS,`done, failed=False`(日志 tools/logs/wave1_c_selftest_l2.log)。

### 截图(tools/screenshots/audit/wave1_c/,1472×668)

| 文件 | 验证点 | 结果 |
|---|---|---|
| battle_8s.png | 战斗中暂停键:半透明底(透出背景墙)、无文字、双竖条全幅、无方块 | ✓(#1/#5/#22) |
| intro_10s.png | 开场 10s 顶中无暂停键(像素扫描 0 命中) | ✓(#6) |
| pause_panel.png | 暂停面板"暂停"60 号+装饰条+"返回游戏"52 号+"主菜单"全部可见;面板打开时暂停键隐藏 | ✓(#1/#26/#28) |
| continue_relif.png | 标题"继续游戏"50 号、金币 X 10、倒计时"0:46"不补零、Info"…每3分钟增加一枚游戏币,最高10枚。"400px 内换行、按钮"继续游戏"/"主菜单"40 号白 | ✓(#12/#13/#27/#28/#29) |
| continue_bullet.png | 标题/Info/按钮全部切"兑换子弹" | ✓(#12) |
| victory_panel.png | "游戏胜利"60 号+VICTORY 横幅+"主菜单" | ✓(#1) |

### 行为说明

- 面板截图时相机停在开场位而非 G0:面板打开即 `SetPaused(true)`,相机 blend Tween 随树暂停冻结 —— 与原作 timeScale=0 行为一致,非偏差。
- 续币面板倒计时取自存档 LastAddCoinTime(满币也循环走动,照原作);选关/菜单金币只读不受影响。
- L2/L3/L4 暂停键时机:L2-L4 的 `LevelBase._Ready → EnterLevel → StartBattle` 首帧即置 BattleStartTime,与 L1 debug 直开同路径(battle_8s 已实证该路径暂停键正常显示)。
- 新增调试挂接:`--panel-shot:<path>:<pause|continue|bullet|victory>:<delaySec>`(InGamePanel 内,从右往左拆兼容盘符),供面板布局/文字回归截图。

## 未做/越界声明

- PlayerState(agent B)、FireSystem(agent D)、怪物(agent A)、Level*.cs/LevelBase.cs 均未改动。
- hud_fire #31(HUD 全局缩放策略 canvas_items vs ConstantPixelSize)不在本工单;当前 canvas 高 720×0.00115=0.828m 与 FOV45 下屏高精确相等,1280 宽方向 0.928×,与审计记录一致。
