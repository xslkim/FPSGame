# FPSGame 项目结构与运行说明

Unity 2019.4 原作(`G:\test\FPSGame`)→ Godot 4.8 的逐界面 1:1 重制。**代码全部 C#**(`game/src/`)。

## 目录

```
G:\FPSGame\
├── godot/          # Godot 4.8 dev 引擎源码(bin/ 下为自编译 mono 版编辑器与导出模板)
├── game/           # 游戏工程(C#/.NET 8,主工程)
│   ├── src/Core/       # Game(场景状态/流转)、AudioService(音乐音效)、SaveService(存档/金币)、ConfigService(远程配置)
│   ├── src/Input/      # InputRouter(UDP 体感枪/键盘回落/鼠标光枪路由)、GunMath(四元数镜像)、UdpDeviceServer、MouseGunSource、AimState
│   ├── src/Effects/    # MuzzleFlash(枪口火光,1:1 移植 Unity FPS Pack MuzzleFlash1.prefab:火焰翻页+烟雾+灯光曲线)、
│   │                   # LaserSight(激光瞄准器:右红/左绿,枪口恒伸 200m 锥形光束(近 0.0089 远 0.03 照原作 widthCurve),照 Lazer.mat 加色滚动贴图)
│   ├── src/Player/     # PlayerState(双玩家/受击/金币 HUD)、Player(单玩家数据;IsDebug→子弹 90,否则 120)
│   ├── src/UI/         # MenuScreen、StartupScreen、LevelChooseScreen、DeviceConnectionScreen、LoadingScreen、
│   │                   # MessageBox、UiKit(坐标构建辅助)、UiSwapButton(SpriteSwap 按钮)、UiTheme、JustRotate、
│   │                   # GunUiController、UiButton3D(显隐与碰撞联动,隐藏按钮不吃射线)、UiAimGuide(2D 激光指引:画在 UI 最上层的锥形光束(枪原点→200m)+命中光点,悬停放光)
│   ├── scenes/ui/      # startup → menu → level_choose / device_connection → loading
│   ├── assets/ data/   # 贴图/模型/音频/字体、数值 JSON
│   └── FPSGame.csproj / FPSGame.sln / NuGet.config
├── legacy_gd/      # 上一版 GDScript 重构的归档(战斗/关卡/工具,已被否定的实现,仅作参考,不参与构建)
├── dist/           # 导出的 Windows 可执行文件 FPSGame.exe
└── tools/          # 截图对照产物、Unity 侧工具脚本、battle_audit(战斗 1:1 审计/修复全记录)
```

Autoload 顺序:Game → SaveService → AudioService → PlayerState → InputRouter。

## 运行

- **直接玩**:`G:\FPSGame\dist\FPSGame.exe`(单文件,无边框全屏)。
- **编辑器开发**:`G:\FPSGame\godot\bin\godot.windows.editor.x86_64.mono.exe --path G:\FPSGame\game`
- **编译 C#**:`cd game && dotnet build`(改代码后必须;NuGet 走本地自编译包,见 NuGet.config)。

## 操作(主菜单)

- **鼠标模拟光枪**(无实体枪时自动生效):移动 = 瞄准(枪口跟随;红色激光束+红点画在 UI 最上层指引命中,悬停按钮红点放大并放光),左键 = 扳机,右键 = 换枪;
  单人游戏 → "选择控制方式"弹框可选 **鼠标** 模式(原作只有 手机/遥控器 两键,鼠标为新增第三键)。
- **键盘**:方向键焦点导航(默认选中单人游戏),回车 = 确认,Esc = 返回(选关页)。
- **键盘调试战斗模式**:主菜单 → 单人游戏 → 选"遥控器";方向键瞄准(±45°)、回车射击、Menu 键或 LeftAlt 换枪。

## 分辨率自适应

逻辑分辨率恒为 1280×720(与 Unity 参考一致):`canvas_items` 拉伸 + `expand` 宽高比,
任意物理分辨率/宽高比下布局不变;背景等比覆盖无黑边;鼠标/枪口命中判定直接用画布逻辑坐标(Godot 投递输入事件前已完成 stretch 逆变换),与分辨率无关。
默认无边框全屏;窗口模式截图会自动切换。

## 体感设备(UDP)

- 手机 App 协议与原作一致:22/23 字节小包到 8281/udp,心跳/广播("Fortune"→255.255.255.255:8282)自动配对。
- 若枪的方向镜像不对:project.godot 添加 `[fpsgame] input/quat_mirror = 0|1|2|3`(默认 3)。
- 实体枪连上后自动接管瞄准(鼠标模拟让位)。

## 自检回归

一键全回归(编译+全部 8 项 headless 自检,顺序跑避免 UDP 端口冲突):

```bash
bash tools/research/battle_audit/regress.sh
```

单项:

```bash
G=G:/FPSGame/godot/bin/godot.windows.editor.x86_64.mono.exe
cd /g/FPSGame/game
dotnet build                                                 # 先编译 C#
$G --headless --path . scenes/ui/menu.tscn -- --menu-selftest   # 29 项(主菜单 1:1)
$G --headless --path . scenes/ui/level_choose.tscn -- --levelchoose-selftest   # 34 项(选关 1:1)
$G --headless --path . scenes/ui/device_connection.tscn -- --deviceconnection-selftest   # 20 项(连接手机 1:1)
$G --path . scenes/ui/menu.tscn -- --e2e-mouse-flow   # 端到端:单人→鼠标模式→选关瞄准点击全链路
$G --headless --path . scenes/levels/level1_battle.tscn -- --level1-selftest   # 18 项
$G --headless --path . scenes/levels/level1_story.tscn -- --story-selftest     # 7 项
$G --headless --path . scenes/levels/level2.tscn -- --level2-selftest   # 36 项
$G --headless --path . scenes/levels/level3.tscn -- --level3-selftest   # 16 项(含 Boss 出生点断言)
$G --headless --path . scenes/levels/level4.tscn -- --level4-selftest   # 20 项(含龙四点/瞬移断言)
```

截图对照(窗口模式,`--shot-res:WxH` 指定分辨率,**全部关卡通用**;选关/连接页加 `--shot-nobeam` 隐藏鼠标激光以对照无设备真值):

```bash
$G --path . scenes/ui/menu.tscn -- --shot:<out.png>  --shot-res:1920x1080   # 主菜单
$G --path . scenes/ui/menu.tscn -- --shot-box:<out.png>                     # 带单人弹框(三键)
$G --path . scenes/ui/menu.tscn -- --shot-flash:<out.png>                   # 枪口火光峰值帧
$G --path . scenes/ui/menu.tscn -- --shot-aim:<out.png>                     # 激光瞄准单人游戏(光束+红点)
$G --path . scenes/ui/level_choose.tscn -- --shot-aim:<out.png>             # 激光瞄准第 1 关
$G --path . scenes/ui/device_connection.tscn -- --shot-aim:<out.png>        # 激光瞄准返回键
$G --path . scenes/ui/level_choose.tscn -- --shot:<out.png>                 # 选关(第 1 页)
$G --path . scenes/ui/level_choose.tscn -- --shot-p2:<out.png>              # 选关第 2 页
$G --path . scenes/ui/level_choose.tscn -- --shot-diff:<out.png>            # 难度面板
$G --path . scenes/ui/device_connection.tscn -- --shot:<out.png>            # 连接手机
# Level1 战斗/开场(截图挂接与 IsDebug 解耦,下列参数恒走对应分支):
$G --path . scenes/levels/level1_battle.tscn -- "--level1-shot:<out.png>:<sec>"          # 含 19s 开场,sec 自场景启动
$G --path . scenes/levels/level1_battle.tscn -- "--level1-shot-battle:<out.png>:<sec>"   # debug 直开战,sec 自战斗开始
$G --path . scenes/levels/level1_battle.tscn -- "--level1-shot-group:<out.png>:<g>"      # 直跳第 g 波,2.5s 后截,日志打印机位 pos/quat/fov
$G --path . scenes/levels/level1_battle.tscn -- "--level1-shot-boss:<out.png>"           # 直跳 Boss 波
$G --path . scenes/levels/level1_battle.tscn -- "--level1-fire:<out.png>:<sec>"         # 按住左键实战开火验证(耗弹/伤害/击杀/右键换枪)
$G --path . scenes/levels/level1_battle.tscn -- "--level1-probe:<sec>"                  # 怪物落位+视线网格扫描探针
$G --path . scenes/levels/level1_battle.tscn -- "--level1-shot-victory:<out.png>"       # 胜利面板
# L2/L3/L4 支持一次多 shot(逗号分隔)+防死亡守护;跳波/Boss 挂接:
$G --path . scenes/levels/level2.tscn -- "--level2-shot:<out.png>:<sec>"
$G --path . scenes/levels/level2.tscn -- "--level2-shot-group:<out.png>:<g>"  # 直跳第 g 波 2.5s 后截
$G --path . scenes/levels/level2.tscn -- "--level2-shot-boss:<out.png>"       # 直跳 G2 Boss 立即出场 5s 后截
```

Unity 侧真值:`G:\test\FPSGame\Assets\Editor\MenuScreenshot.cs`(GUI 模式 `-executeMethod MenuScreenshot.Capture` / `.CaptureStill`),几何测量 `MenuMeasure.cs -executeMethod MenuMeasure.Dump`,枪口火光 `FlashScreenshot.cs -executeMethod FlashScreenshot.Capture`(FLASH_ISO=nosmoke/noflame 可隔离子效果),选关/连接页 `LevelShot.cs -executeMethod LevelShot.Capture`(LEVEL_SHOT_SCENE=Assets/UI/LevelChoose.unity 或 DeviceConnection.unity,LEVEL_SHOT_ACTION=page2/difficult),双枪包围盒 `GunMeasure.cs -executeMethod GunMeasure.Dump`,Level1 战斗 `Level1Shot.cs -executeMethod Level1Shot.Capture`(L1_SHOT_TIMES/L1_SHOT_DEBUG/L1_SHOT_GROUPS 环境变量)。

## 导出

```bash
cd /g/FPSGame/game
G:/FPSGame/godot/bin/godot.windows.editor.x86_64.mono.exe --headless --path . --export-release "Windows" ../dist/FPSGame.exe
```

导出模板走 `export_presets.cfg` 的 `custom_template/release`(指向自编译 mono 模板);
dotnet publish 的运行时包来自 nuget.org(NuGet.config 已配)。

## 已知遗留(非阻塞)

1. 发布前把 `game/assets/fonts/cjk_fallback.ttf`(本机 SimHei 副本)换成可分发字体(UiTheme 引用)。
2. `assets/models/` 部分环境贴图目录大小写与引用不一致(Windows 无碍,跨平台需修)。
3. 体感枪真机方向校准(quat_mirror)、枪口火光/激光真机效果未经实机验证(无设备)。
4. L2 观感已按真值黄昏毒气镇重做实时光照(详见保真注记);Unity 侧 L2 战斗仅 1 张存量真值截图(2019 batchmode 许可证失效后无法补拍,可恢复后补拍更多节拍)。

## 移植保真注记(有意的偏差)

- 选关:原作 AK47 侧枪口火光父节点 `Sphere (1)` 默认 inactive,火光永不显示;移植版修正为与 M4/菜单一致正常播放。
- 选关:枪上 `Movie`(RawImage+VideoPlayer 播 startmov.mp4,默认不播、渲染透明)未移植,属连接手机流程。
- 选关:难度按钮热区用全尺寸 500×110(原作 Hard/Hell 的 BoxCollider 只有 400×100)。
- 选关存档默认值照原作 UserMeta.cs:13 关全 3 星/得分 3/排名 1。
- 连接手机:原作三个 VideoPlayer(MovieImage/双枪 Movie)全部 playOnAwake=0 且无脚本调用 Play,视频实际从不播放,未移植;mp4 已转 `assets/video/startmov.ogv` 备用(Godot 不支持 mp4)。
- 连接手机:`GameObject` 英语教学小游戏组(AppleController:Apple/Candy/IceCream/Bread + 面板)原作整组 active=0 遗留,未移植。
- 连接手机:`二维码Android` 组原作 active=0 且无任何代码切换,移植版建好但保持隐藏(其 Image 组件原作禁用→无底图)。
- 连接手机:说明文字第 1 行在 "App" 后折行是真值实测的临界折行(621 vs 620 宽),移植版用显式 `\n` 固定同款折行点;行距按真值逐行扫描实测 51 单位标定(Godot `line_spacing=+10`,原作 lineSpacing=1.1 的等效)。
- 连接手机:原作 BackMenuBtn onClick 第二绑定指向未实例化的 Utils.prefab(实际不响),移植版按框架惯例播 UI 音效;枪口闪光父链原作默认 inactive,修正为命中时播放(同菜单/选关)。

## 战斗系统(Level1 战斗/剧情,C#)

架构(`game/src/Battle/`):
- `Monster.cs` 基类:生命周期(出生等待→追击→攻击→死亡回收)/动画事件 0.3s 攻击结算(按屏幕 x 分侧)/25s 超时自毁/对象池复用;近战进攻击半径站桩(CD 期原地等待不收脚);数值全来自 `data/monster_meta.json`
- `MonsterPool.cs` 类型池 / `MonsterInfo.cs` meta 读取
- `LevelBase.cs` 波次框架(难度倍率:数量×rate、间隔÷rate、同屏×rate;机位切换冻结刷怪 2s;补给箱概率;首怪即刷(原作 lastBornTime 语义);胜利 2s 延迟单次触发)/`Level1.cs` 19s 开场+5 波+Baotou Boss
- `FireSystem.cs` 双枪(挂相机):每帧枪口旋转=输入瞄准→射线→命中点光标火光(右红/左绿,距离衰减公式照原作)→扳机(CD+耗弹)→Button 触发/怪物 hit/环境弹着特效池(Concrete×4 其余×3,死亡爆血×3,绿血=GreenImpact);换枪收枪 0.5s 后新枪瞬现(照原作字面);暂停期只放行 Button 命中(枪打面板);激光恒伸 200m 穿透怪(墙体深度剔除)
- `InGamePanel.cs` 三面板(World-Space 挂相机,根缩放 0.00115,1:1 prefab 贴图/布局/文字):暂停/续币(复活+兑换子弹分支、币不足文案、倒计时 m:s 不补零)/胜利(照原作无星数结算);暂停键开战才显示、任一面板打开即隐藏;金币仅在续币面板打开时计时回复(原作 Update 门控)
- `IntroBadGroup.cs` school_day 19s 开场:vcam1 固定机位盯被抱女生 Neck、BadGroup 2.7286s 激活、三人负重行进(根运动 z 分段线性 pre-Hold 修正)/背负挂骨真值(f05→僵尸右臂、剑女孩→士兵左臂、士兵持沙漠之鹰)/尖叫求救音频
- `StoryStart.cs` 剧情过场:9 机位切镜表 blend(GroupComposer 跟踪机位 LookAt 近似)+字幕+dance.mp3+RockWarrior 50.5s 冲出+跳过按钮
- 飞斧投射物 `ProjectileAxe.cs`:TakeHandAxe@0.224s/ThrowAxe@0.600s 节奏、命中率 0.15(难度倍率)、未中偏移 3~6m、2m/s 自旋 10s、相机空间盒判定分侧扣血
- 宝箱 `BoxMonster.cs`:原地舔舐 20s(AK15/M4 20)自灭无掉落;被打(HP1)才掉,仅受击侧 +60 弹/解锁枪

数值来源:`data/level_meta.json`(原 LevelBase.GetLevelMeta 硬编码)、`data/fire_meta.json`(原 firemetajson.json)、`data/monster_meta.json`(原 MonsterBase.GetMeta)。

### 战斗/剧情移植保真注记(有意的偏差)

> **2026-09-26 终验轮修复**(详见 tools/research/level1_deviation_log.md 第三轮):修复了战斗中鼠标左键无法开火/右键无法换枪(输入电平未接入)、开枪 NullReferenceException(GunBase.Player 未赋值)、命中分发大小写不匹配("hit"→"Hit"/"OnShot")、开场剑女孩 FBX 厘米单位巨人化(百米网格挡镜头)、命中光斑实心红球(径向衰减软化)、走廊窗口光晕片改加色混合。此后 `--level1-fire` 实测:耗弹/伤害/飘字/击杀回收/右键换枪全通。

- 被抱女生/报人/换人抱(开场背负):原作是 UMotion 导出的 **humanoid 肌肉曲线** .anim——已用 K-POP 同款烘焙管线还原(BakeKpopDance.cs 任务模式,`KPOP_JOBS`,rootMotion=0):`dance/baotou_carry.kdance.bin`(1s 循环)/`soldier_carry.kdance.bin`(1.167s)/`f05_carried.kdance.bin`(7s),IntroBadGroup 以 KDancePlayer 循环回放(挂骨 local TRS 保持原作序列化值,f05 Animator applyRootMotion=0 语义)。
- K-POP 舞蹈:**已完整还原**——原作 `K-POP Dance 1.anim` 是 humanoid 肌肉曲线(无 FBX 源),无法直接转骨骼;改为在 Unity(2022.3 临时工程,2019.4 许可证失效)用 PlayableGraph 逐帧烘焙两舞者全骨骼局部 TRS 为 `.kdance.bin`(`assets/models/actors/dance/`,f05 36MB/casual 9MB,200.8s@30fps),运行时 `KDancePlayer` 按"局部链→Unity 全局→镜像 X→父全局⁻¹→Godot 局部"回放(f05 t=5s 六骨骼世界坐标与 Unity 逐位一致,见 tools/dance_bake/);剧情时钟驱动(2.9667s 起、相位错落照原作 0/0.0667s)。烘焙器在 `G:\test\FPSGame\Assets\Editor\AITools\BakeKpopDance.cs`。
- blade_girl:原作场景中 inactive 且 Animator 被清空(不参与演出),剧情不创建该角色。
- 相机切换:Level1 用原作自定义 Blend 资产 Level1.asset 的 1s(Cubic EaseInOut 近似);vcam1 注视 Neck 以每帧 LookAt 近似。
- 难度数量截断:Unity float 数学改 double 精确(10×1.8 恒 18,不再掉 17);**例外:L2 G0 Hard 按原作 `(int)DiffRateHard` 强转 bug bug-for-bug 保留为 30**(level_meta num_override,L234 审计 L2-1)。
- 胜利结算:原作只弹 VectoryPanel 无星数(全工程无星级写入点,选关星数恒默认值)——1:1 照此,无本地结算。
- Level2/3/4 原工程 `Invoke("FinishLevel")` bug(方法不存在永不触发)在 LevelBase 统一修复。
- Level2:光照按真值(level2_unity.png 黄昏毒气镇)重做实时光照——env 全部材质点亮化(去 unshaded)、远景 Terrain_d_gas 整组压暗(暗剪影,VC 材质不可乘色只能盖材质)、环境改暗冷 ambient(0.30/0.34/0.44×0.22)、方向光 0.55 带阴影、9 窗口补 Unity 同款暖点光(intensity 2/range 10/(1,0.893,0.707),Level2.unity type2 灯)、相机逐波 far 50/80/100(meta cam_far,基类 SwitchCamera 应用);Boss 已按 Unity 真值 ×3 缩放(根节点,命中体/血条随动)。
- Level3:烘焙导出的坐标约定为 mirror-X(SceneExporter.cs 注释),机位照此换算并经落位验证;雾=深度雾 20→90 真值色 (0.356,0.476,0.575);Boss 出生点照真值 (-83.83,-0.02,-101.3) 并已 ×5 缩放;部分机位视野内城市观感偏空。
- Level4:env 同 mirror-X 约定;雾按真值 ExpSquared 近似值;fire_breath 特效 emit 默认值 bug-for-bug 保留;方向光按真值恢复常开 0.57;龙四点巡回(FarWay75/80→InCamera150/15±10/20±20→Attack 贴脸 20m→CamOffset±20)+出生瞬移 FarWay 已按 Unity 重写;Magma 四色变体(蓝/绿/橙/紫)已补齐。
- Level1 战斗场景环境(env_school_hallway)整体为原作 X 镜像(FBX 导入差异)——机位/平行光全部按"镜像四元数 (z,w,x,y) 分量置换"换算并经运行时逐位验证(裁决记录 tools/research/battle_audit/battle0_mirror_verdict.md);雾按真值线性 5→12m 用深度雾原值落地(本引擎深度雾实测生效,旧"无效"注记作废);出生后修正 x 微调随镜像翻转(x>0→−0.3)。
- Level1 Boss(包头僵尸):本体不可被打(layer5 只起弹着特效),弱点为脊柱上悬浮爱心(BossHeart,BoxHead 转发语义),被打 0.5s 瞬移(满幅 ±1.8/±2m)并重置攻击计时;爱心贴图黑底加色染红;火球=爆炸音+0.1s 后双手各 15。
- Level1 出生:±33°(FOV>50→40°)/8m 射线落点,G1~G4 覆盖散开角 15°(原作 GroupMaxBornFov);宝箱/枪箱占刷怪配额(5%/2%);只有牛魔王/斧头/骷髅(原作 _Name 序列化同 0)吃难度等待 Easy3~8s。
- 各关环境烘焙坐标约定可能不同(走廊=数值不变 / 城市与村庄=mirror-X),机位均按各自 env 已验证约定换算,场景内自洽。
- 玩家 HUD:HP Slider 原作 prefab 存在但真值截图恒不可见(满血/残血均无),移植版隐藏(值内部追踪);子弹真值 Debug 构建 90 发/发布 120 发(Game.IsDebug 分支已移植,默认 false=120);换枪"新枪瞬现"按原作字面行为(伸出动画作用于隐藏旧枪)。
- L3 Boss 血条宽度:原作 HpReduceNumber RectTransform x 被 override 0.03(×5 后 9m 宽细条,疑似原作调参遗留),移植版血条随根 ×5(3m),未逐 bug 复刻;L3 火球起点原作锚点随 ×5 到 +15m(同为缩放遗留),移植版保持代码常量 1.5m。

其余关卡自检见上文命令清单。战斗 1:1 审计与修复全记录:`tools/research/battle_audit/`(4 份审计报告 + 偏差总表 DEVIATIONS.md + 各波修复笔记 wave*_notes_*.md)。
