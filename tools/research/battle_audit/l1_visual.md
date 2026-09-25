# Level1(school_day + 19s 开场)视觉对照审计报告

审计日期:2026-06-09 · 审计角色:测试/审计(未改任何游戏代码)
Godot 截图:`tools/screenshots/audit/l1_visual/*.png`(1472×668,与真值同宽高比)
Unity 真值:`tools/screenshots/l1u_*.png` + `tools/research/level1_unity_truth.md`

## 总结

机位数值层面移植质量高:G0–G3 位置与真值逐位一致,朝向四元数与"X 镜像 + Unity/Godot 前向翻转"换算自洽,FOV(开场 30 / 战斗 45)与真值文档吻合。但**视觉层面偏差显著**,集中在五类:① HUD 三件套(暂停按钮蓝色方块底、头像下多出绿色 HP 条、子弹计数样式/位置)与种子 S1/S2/S3 全部坐实;② 瞄准系统(命中红点过大偏橙、激光偏粗偏粉)坐实 S4/S5;③ 整体色调偏亮偏白、黄绿雾感不足,坐实 S7;④ **新发现高严重度问题:G0 牛魔王近战收不住,贴到相机正下方几乎全部出画,战斗画面"无怪可打"而玩家却在掉血**;⑤ 开场 19s 运镜路径与真值大幅不同(3s/10s/15s 构图对不上,10s 相机钻进石头人躯干),且被抱女生双腿纯白无贴图。另有 debug 直开模式下原作 BadGroup 演员滞留画面、Godot 隐藏——按任务约定为已知差异,仅记录。

## 偏差表

| # | 区域 | 偏差 | Unity 真值(出处) | Godot 现状(证据) | 严重度 | 建议修复位置 |
|---|---|---|---|---|---|---|
| 1 | HUD/暂停按钮 (S1) | 暂停键带不透明深蓝方块底(~70×70px),双竖条缩在方块内;真值为裸青色→绿渐变双竖条(高约 85px),无底色无方块 | l1u_gun_25s.png 顶中(放大:仅两条竖杠);l1u_intro_21s.png 同 | gun_25s.png、intro_21s.png 顶中(放大:蓝紫方块+内嵌双条) | 高 | `game/src/Battle/InGamePanel.cs` `BuildPauseBtn()`(137–152 行,"深蓝横条"为代码自建底色);核对贴图 `assets/textures/ui/Btn_pause_n.png` 是否本身含底 |
| 2 | HUD/头像 HP 条 (S2) | 左右头像圆环下各多一条绿色 HP 条(长约 260px,绿填充+暗底);真值全部截图(开场+战斗)头像下均无 HP 条 | l1u_gun_25s.png 左上/右上放大:仅粉/蓝圆环;l1u_battle_25s.png 同 | intro_3s.png、gun_25s.png 左上/右上放大:绿色横条 | 高 | `game/src/Player/PlayerState.cs` 战斗容器(~172 行起,"头像+血条×2");注:truth §6.1 载 prefab 有 HP Slider 字段,但真值截图均不可见,以截图为准 |
| 3 | HUD/子弹计数 (S3) | Godot:单个绿色子弹组合图标+白字"120",位于底部约 65% 宽度处,无"×"前缀;真值:三个独立绿色子弹图标+绿色"×"+白字,位于右下约 80% 宽度处 | l1u_gun_25s.png 右下放大("🥢×3 × 90") | gun_25s.png 底中偏右放大(图标+"120") | 中 | `game/src/Player/PlayerState.cs` 子弹 HUD(位置/×前缀/图标排版)。注:90 vs 120 是 Debug/正常配置差(truth §5),数值本身不算偏差 |
| 4 | 瞄准命中红点 (S4) | Godot:橙黄色大光球,核心约 45px+光晕约 70px,色偏橙;真值:深红色小点,核心约 10px+红晕约 25px | l1u_gun_25s.png 楼梯墙面红点(放大) | gun_25s.png 楼梯墙面光球(放大) | 高 | `game/src/UI/UiAimGuide.cs`:`dotSize` 默认 28(39 行)过大,光晕=3×dot(68 行)且 tint 偏橙;按真值缩到核心 ~10px、纯红 (0.99,0,0),并核对 `HitBackOffset`/缩放公式(truth §4.1:scale=1-clamp01(3/len)×0.8) |
| 5 | 瞄准激光 (S5) | Godot:粉红~4–6px 柔边线;真值:纯红~2–3px 细线(0.023→0.03m) | l1u_gun_25s.png 激光段(放大) | gun_25s.png 激光段(放大) | 中 | `game/src/Effects/LaserSight.cs`(线宽/颜色/辉光;真值材质红色 (0.99,0,0,0.82)) |
| 6 | 光线/雾/色调 (S7) | Godot 整体偏亮偏白、中景(5–12m)黄绿雾氛围不足、远景(20m+)能见度高于真值;真值黄绿雾线性 5→12m,12m 外近乎纯色雾墙,暖色阳光光斑明显 | l1u_intro_21s.png、l1u_intro_15s.png(远景全被黄绿雾吞没);truth §1.1 | intro_21s.png(同机位远景清晰)、cam_G1/G2/G3.png(走廊尽头可读,仅薄霾) | 高 | `game/scenes/levels/level1_battle.tscn` WorldEnvironment(16–18 行:fog 指数 0.08 曲线与线性 5→12m 的中近景等效性/ambient)/DirectionalLight 强度与色温 |
| 7 | 演员/怪物近战站位(新) | G0 牛魔王贴身收不住,最终停在相机正下方,95% 出画(仅底缘见牛角);intro-run 35s 画面无怪且玩家满血,battle-run 45s 玩家 HP 已掉约 75%——怪在不可见位置持续攻击。真值:牛魔王停在攻击距离画面内,25s/45s 稳定可见贴脸攻击 | l1u_intro_26s/35s.png、l1u_battle_25s/45s.png(牛魔王画面左侧清晰攻击位);truth §3(攻击半径 2) | extra_40s.png(底缘牛角)、battle_45s.png(无怪但右 HP 条剩 ~25%)、intro_35s.png(无怪) | 高 | `game/src/Battle/Monster.cs` `MoveToPlayerAndAttack()`(140–160 行:flatDist/AttackRadius 判定与停止逻辑,攻击未触发时应原地等待而非继续走进相机) |
| 8 | 演员/G0 到位时机(新) | Godot 21s/23s 楼梯间无怪,~26s 才首次贴脸入画;真值 21s 牛魔王已在中景(距相机约 5–6m)走近 | l1u_intro_21s.png(牛魔王中景+品红血条) | intro_21s.png、extra_23s.png(空楼梯间)、intro_26s.png(怪贴脸) | 中 | `game/src/Battle/MonsterPool.cs`/`Monster.cs`(出生时刻=2s 混合结束后?WaittingTime Easy 3–8s 随机)。**待确认**:出生 ±33° 与等待时间均有随机性,需多次采样排除 RNG |
| 9 | 开场运镜(新) | 开场相机路径与真值大面积对不上:① 3s 真值对楼梯间空墙,Godot 直视走廊见士兵坏人+石头人;② 10s 真值俯拍包头僵尸抱女生,Godot 相机钻进石头人躯干(满屏绿色岩石贴图);③ 15s 真值远景雾中追逃(鞋箱左+走る标语右),Godot 中景双人(右墙为公告板) | l1u_intro_3s/8s/15s.png、l1u_gun_8s/10s.png | intro_3s.png、gun_10s.png、intro_15s.png | 高 | `game/src/Battle/IntroBadGroup.cs` `Play(Camera)`(开场运镜仅跟随演员,未还原原作 SchoolNightTimeline 的多 vcam 切换,truth §2.2/§9) |
| 10 | 开场演员材质(新) | 被抱女生双腿纯白无贴图(两条白色裤腿悬于画面中央);包头僵尸通体蓝衣,与真值绿皮肤+绷带的观感和配色不同 | l1u_gun_10s.png(绿皮肤绷带僵尸抱水手服女生,女生白袜有肤色/织物质感) | intro_15s.png 中央放大(纯白双腿+蓝衣绷带演员) | 高 | `game/src/Battle/IntroBadGroup.cs` 引用的演员 FBX 材质(`game/assets/` 下 f05_schoolwear/Chr_Zcharacter 导入贴图丢失,参照 MenuScreen.cs:76 已有的"FBX 导入丢贴图代码覆盖"先例) |
| 11 | HUD/开场暂停按钮时机(新) | Godot 开场 3s/8s/15s 顶部即有暂停按钮(蓝块);真值开场阶段无暂停按钮,19s 开战后(l1u_intro_21s)才出现 | l1u_intro_3s/8s/15s.png(顶中无暂停)、l1u_gun_8s/10s.png 同 | intro_3s.png、intro_8s.png、intro_15s.png(顶中蓝块) | 中 | `game/src/Battle/InGamePanel.cs` PauseBtn 显隐时机(应随开战 PlayerSystem.Show()/FireSystem 激活再放行,truth §2.2) |
| 12 | 枪模型(新) | Godot 手枪在画面中约 440×250px,明显大于真值约 320×180px(同 1472×668);枪管上扬角度也更大 | l1u_gun_25s.png 右下枪 | gun_25s.png、intro_21s.png 右下枪 | 中 | `game/src/Battle/FireSystem.cs` 枪挂点/缩放(真值 firemetajson pos (0.10,-0.04,0.16),FOV≤50 档) |
| 13 | battle(debug)模式 BadGroup(已知) | 原作 debug 直开时 BadGroup 演员滞留画面(6s 走廊见石头人/士兵,12s 贴脸);Godot 版直开即隐藏 | l1u_battle_6s/12s.png | battle_6s.png、battle_12s.png(无滞留演员) | 低(已知差异,只记录) | —(任务约定不深挖;`Level1.cs:44` `_badGroup.Visible=false`) |
| 14 | 环境细节/布景(新) | 同机位楼梯间布景不一致:Godot 楼梯上方墙有海报公告板、左侧为白墙+单门;真值楼梯上方为素墙、左侧为玻璃双开门。窗/门/标语相对走廊段的排布有出入 | l1u_intro_21s.png、l1u_gun_20s.png | intro_21s.png、gun_20s.png | 低 | 待确认:可能为环境 X 镜像(README 保真注记)后布景未逐段对位;需与 env_school_hallway 场景逐段核对 |
| 15 | 怪物血条(待确认) | 真值:怪头顶细品红/粉紫色血条(约 110×8px)。Godot 现有全部截图无法验证:怪入画时已贴脸、血条出画或未渲染 | l1u_intro_21s.png 牛魔王头顶品红条(放大) | intro_26s.png(怪贴脸,头顶出画,无血条可见)、extra_40s.png 同 | 待确认 | `game/src/Battle/MonsterHpBar.cs`;验证方法:修 #7 后重截 21–26s,或单独挂接一只远处静止怪截图 |

## 补充说明(非偏差的运行条件差异)

- **枪/激光在部分真值截图中不可见**:l1u_intro_21/26/35、l1u_battle_25/45 整组无枪无激光(该真值采集 run 无输入设备接入,玩家未 Show 枪);l1u_gun_25s 有枪有激光。Godot 挂接自动把瞄准点置窗口中心,故枪/激光/红点常驻。两组对按时已分别选用对应条件的真值图,不计偏差。
- **子弹数 90 vs 120**:真值 gun-run 为 Debug 配置 90 发(truth §5),Godot 挂接 run 为 120 发,配置差异非样式偏差(样式差异见 #3)。
- **机位像素对照**:l1u_cam_G0..G3 为 blend 中途,按任务约定不做像素对照,仅做数值对照(下表)。

## 机位数值对照表

Godot 侧取自 `--level1-shot-group` 日志(2026-06-09 采集);真值取自 truth §1.2 vcam 表及 `level1_deviation_log.md` #11(原作 Battle0 四元数)。按 README 保真注记,环境整体 X 镜像,机位朝向按"镜像四元数(qy,qz 取反)+ Unity(+Z 前)→Godot(−Z 前)偏航 +180°"换算,验证自洽性。

| 机位 | 项 | Unity 真值 | Godot 实测 | 判定 |
|---|---|---|---|---|
| 开场/待机 (vcam1) | pos | (1.35, 1.86, 22.44) | (1.325, 1.850, 22.05) | Δ(−0.025, −0.01, −0.39),z 差 0.39m,中低影响,建议复核 |
| | quat/yaw | (−0.0009, 0.9986, −0.0189, −0.0485) ≈yaw 185° | (−0.0185, −0.0354, −0.0015, 0.9992) ≈yaw −4.1° | 换算期望 185°→镜像 175°→翻转 −5°,实测 −4.1°,**自洽 ✓** |
| | FOV | 30 | 30(开场流程,`Level1.cs:121`);shot-group 日志打 45 系 debug 直开路径(`Level1.cs:46`) | **一致 ✓** |
| G0 (Battle0) | pos | (−0.735, 1.0, −10.27) | (−0.7353, 1.0, −10.2706) | **逐位一致 ✓** |
| | quat/yaw | (−0.0402, 0.4903, 0.0227, 0.8703) ≈yaw 58.8° | (0.0227, 0.8703, −0.0402, 0.4903) ≈yaw 121.2° | 换算期望 180−58.8=121.2°,**自洽 ✓**(含 LookAt 微俯仰分量) |
| | FOV | 45 | 45 | **一致 ✓** |
| G1 (Battle1) | pos | (−0.085, 1.051, −3) | (−0.0851, 1.0515, −3) | **逐位一致 ✓** |
| | quat | truth 未记录 | (0, 1, 0, 0)(纯 yaw 180°,水平直视 +Z 走廊) | **待确认**:建议从 school_day.unity 补录 vcam Battle1 原始 rotation 复核(截图 cam_G1 为走廊直视,观感合理) |
| | FOV | 45 | 45 | **一致 ✓** |
| G2 (Battle2) | pos | (−0.085, 1.051, 3.01) | (−0.0851, 1.0515, 3.01) | **逐位一致 ✓** |
| | quat | truth 未记录 | (0, 1, 0, 0) | **待确认**(同 G1) |
| | FOV | 45 | 45 | **一致 ✓** |
| G3 (Battle3) | pos | (−0.085, 1.051, 21.02) | (−0.0851, 1.0515, 21.02) | **逐位一致 ✓** |
| | quat | truth 未记录 | (0, 1, 0, 0) | **待确认**(同 G1) |
| | FOV | 45 | 45 | **一致 ✓** |

注:位置 x 分量未做镜像取负(与 G0 真值原始值逐位相等),与项目"环境几何已镜像、机位位置沿用原值、仅朝向换算"的既有约定一致(deviation log #11/12)。

## 截图产物清单(tools/screenshots/audit/l1_visual/)

- 对位组:intro_3s/8s/15s/21s/26s/35s.png、gun_8s/10s/20s/25s.png、battle_6s/12s/25s/45s.png、cam_G0/G1/G2/G3.png
- 补充验证:extra_23s.png(G0 时机)、extra_40s.png(牛魔王贴相机底缘铁证)、extra_battle_30s.png(battle 模式无怪)
