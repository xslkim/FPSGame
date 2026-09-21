# FPSGame 项目结构与运行说明

Unity 2019.4 原作(`G:\test\FPSGame`)→ Godot 4.8 的逐界面 1:1 重制。**代码全部 C#**(`game/src/`)。

## 目录

```
G:\FPSGame\
├── godot/          # Godot 4.8 dev 引擎源码(bin/ 下为自编译 mono 版编辑器与导出模板)
├── game/           # 游戏工程(C#/.NET 8,主工程)
│   ├── src/Core/       # Game(场景状态/流转)、AudioService(音乐音效)、SaveService(存档/金币)、ConfigService(远程配置)
│   ├── src/Input/      # InputRouter(UDP 体感枪/键盘回落/鼠标光枪路由)、GunMath(四元数镜像)、UdpDeviceServer、MouseGunSource、AimState
│   ├── src/Effects/    # MuzzleFlash(枪口火光,1:1 移植 Unity FPS Pack MuzzleFlash1.prefab:火焰翻页+烟雾+灯光曲线)
│   ├── src/Player/     # PlayerState(双玩家/受击/金币 HUD)、Player(单玩家数据)
│   ├── src/UI/         # MenuScreen、StartupScreen、LevelChooseScreen、DeviceConnectionScreen、LoadingScreen、
│   │                   # MessageBox、UiKit(坐标构建辅助)、UiSwapButton(SpriteSwap 按钮)、UiTheme、JustRotate、GunUiController、UiButton3D
│   ├── scenes/ui/      # startup → menu → level_choose / device_connection → loading
│   ├── assets/ data/   # 贴图/模型/音频/字体、数值 JSON
│   └── FPSGame.csproj / FPSGame.sln / NuGet.config
├── legacy_gd/      # 上一版 GDScript 重构的归档(战斗/关卡/工具,已被否定的实现,仅作参考,不参与构建)
├── dist/           # 导出的 Windows 可执行文件 FPSGame.exe
└── tools/          # 截图对照产物、Unity 侧工具脚本等
```

Autoload 顺序:Game → SaveService → AudioService → PlayerState → InputRouter。

## 运行

- **直接玩**:`G:\FPSGame\dist\FPSGame.exe`(单文件,无边框全屏)。
- **编辑器开发**:`G:\FPSGame\godot\bin\godot.windows.editor.x86_64.mono.exe --path G:\FPSGame\game`
- **编译 C#**:`cd game && dotnet build`(改代码后必须;NuGet 走本地自编译包,见 NuGet.config)。

## 操作(主菜单)

- **鼠标模拟光枪**(无实体枪时自动生效):移动 = 瞄准(枪口跟随,按钮悬停高亮),左键 = 扳机,右键 = 换枪;
  单人游戏 → "选择控制方式"弹框可选 **鼠标** 模式(原作只有 手机/遥控器 两键,鼠标为新增第三键)。
- **键盘**:方向键焦点导航(默认选中单人游戏),回车 = 确认,Esc = 返回(选关页)。
- **键盘调试战斗模式**:主菜单 → 单人游戏 → 选"遥控器";方向键瞄准(±45°)、回车射击、Menu 键或 LeftAlt 换枪。

## 分辨率自适应

逻辑分辨率恒为 1280×720(与 Unity 参考一致):`canvas_items` 拉伸 + `expand` 宽高比,
任意物理分辨率/宽高比下布局不变;背景等比覆盖无黑边;枪口命中判定经 canvas 逆变换,与分辨率无关。
默认无边框全屏;窗口模式截图会自动切换。

## 体感设备(UDP)

- 手机 App 协议与原作一致:22/23 字节小包到 8281/udp,心跳/广播("Fortune"→255.255.255.255:8282)自动配对。
- 若枪的方向镜像不对:project.godot 添加 `[fpsgame] input/quat_mirror = 0|1|2|3`(默认 3)。
- 实体枪连上后自动接管瞄准(鼠标模拟让位)。

## 自检回归

```bash
G=G:/FPSGame/godot/bin/godot.windows.editor.x86_64.mono.exe
cd /g/FPSGame/game
dotnet build                                                 # 先编译 C#
$G --headless --path . scenes/ui/menu.tscn -- --menu-selftest   # 29 项(主菜单 1:1)
$G --headless --path . scenes/ui/level_choose.tscn -- --levelchoose-selftest   # 34 项(选关 1:1)
$G --headless --path . scenes/ui/device_connection.tscn -- --deviceconnection-selftest   # 20 项(连接手机 1:1)
```

截图对照(窗口模式,可指定分辨率;选关/连接页加 `--shot-nobeam` 隐藏鼠标激光以对照无设备真值):

```bash
$G --path . scenes/ui/menu.tscn -- --shot:<out.png>  --shot-res:1920x1080   # 主菜单
$G --path . scenes/ui/menu.tscn -- --shot-box:<out.png>                     # 带单人弹框(三键)
$G --path . scenes/ui/menu.tscn -- --shot-flash:<out.png>                   # 枪口火光峰值帧
$G --path . scenes/ui/level_choose.tscn -- --shot:<out.png>                 # 选关(第 1 页)
$G --path . scenes/ui/level_choose.tscn -- --shot-p2:<out.png>              # 选关第 2 页
$G --path . scenes/ui/level_choose.tscn -- --shot-diff:<out.png>            # 难度面板
$G --path . scenes/ui/device_connection.tscn -- --shot:<out.png>            # 连接手机
```

Unity 侧真值:`G:\test\FPSGame\Assets\Editor\MenuScreenshot.cs`(GUI 模式 `-executeMethod MenuScreenshot.Capture` / `.CaptureStill`),几何测量 `MenuMeasure.cs -executeMethod MenuMeasure.Dump`,枪口火光 `FlashScreenshot.cs -executeMethod FlashScreenshot.Capture`(FLASH_ISO=nosmoke/noflame 可隔离子效果),选关/连接页 `LevelShot.cs -executeMethod LevelShot.Capture`(LEVEL_SHOT_SCENE=Assets/UI/LevelChoose.unity 或 DeviceConnection.unity,LEVEL_SHOT_ACTION=page2/difficult),双枪包围盒 `GunMeasure.cs -executeMethod GunMeasure.Dump`。

## 导出

```bash
cd /g/FPSGame/game
G:/FPSGame/godot/bin/godot.windows.editor.x86_64.mono.exe --headless --path . --export-release "Windows" ../dist/FPSGame.exe
```

导出模板走 `export_presets.cfg` 的 `custom_template/release`(指向自编译 mono 模板);
dotnet publish 的运行时包来自 nuget.org(NuGet.config 已配)。

## 已知遗留(非阻塞)

1. 全部 4 个战斗关 + Level1 剧情过场已完成 C# 移植并提交;Level2(34 项)/Level3(15 项)/Level4(13 项)自检全过。
2. 发布前把 `game/assets/fonts/cjk_fallback.ttf`(本机 SimHei 副本)换成可分发字体(UiTheme 引用)。
3. `assets/models/` 部分环境贴图目录大小写与引用不一致(Windows 无碍,跨平台需修)。
4. 体感枪真机方向校准(quat_mirror)、枪口火光/激光真机效果未经实机验证(无设备)。

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
- `Monster.cs` 基类:生命周期(出生等待→追击→攻击→死亡回收)/动画事件 0.3s 攻击结算(按屏幕 x 分侧)/25s 超时自毁/对象池复用;数值全来自 `data/monster_meta.json`
- `MonsterPool.cs` 类型池 / `MonsterInfo.cs` meta 读取
- `LevelBase.cs` 波次框架(难度倍率:数量×rate、间隔÷rate、同屏×rate;机位切换冻结刷怪 2s;补给箱概率;胜利 2s 延迟单次触发)/`Level1.cs` 19s 开场+5 波+Baotou Boss
- `FireSystem.cs` 双枪(挂相机):每帧枪口旋转=输入瞄准→射线→命中点光标火光→扳机(CD+耗弹)→Button 触发/怪物 hit/环境弹着特效池;换枪下沉动画;弹尽→续币面板;暂停期只放行 Button 命中(枪打面板)
- `InGamePanel.cs` 三面板(World-Space 挂相机,1:1 prefab 贴图/布局):暂停/续币(复活+兑换子弹)/胜利(照原作无星数结算)
- `IntroBadGroup.cs` school_day 19s 开场:3 NPC 负重行进(根运动 z 分段线性)+尖叫/求救音频+相机注视
- `StoryStart.cs` 剧情过场:9 机位切镜表 blend(GroupComposer 跟踪机位 LookAt 近似)+字幕+dance.mp3+RockWarrior 50.5s 冲出+跳过按钮

自检(全 headless):
```bash
$G --headless --path . scenes/levels/level1_battle.tscn -- --level1-selftest   # 13 项
$G --headless --path . scenes/levels/level1_story.tscn -- --story-selftest     # 7 项
$G --path . scenes/levels/level1_battle.tscn -- "--level1-shot-battle:out.png[:sec]"  # 战斗截图
$G --path . scenes/levels/level1_battle.tscn -- "--level1-shot:out.png"                # 开场截图
```

数值来源:`data/level_meta.json`(原 LevelBase.GetLevelMeta 硬编码)、`data/fire_meta.json`(原 firemetajson.json)、`data/monster_meta.json`(原 MonsterBase.GetMeta)。

### 战斗/剧情移植保真注记(有意的偏差)

- 被抱女生(开场 3 人组背负的学生)动画:原作是 UMotion 导出的 **humanoid 肌肉曲线** .anim(RootQ/RightFootQ 等,390 条 muscle 通道),无法映射 Godot 骨骼;以绑定姿态+挂件调位近似。
- K-POP 舞蹈:原作 200.83s 完整版(K-POP Dance 1.anim)资产不可得;演员 `_anims.tres` 内置 4s dance 循环(legacy 占位),剧情 54.8s 用循环替代,3 学生错开 0.07s 相位照原作。
- blade_girl:原作场景中 inactive 且 Animator 被清空(不参与演出),剧情不创建该角色。
- 怪物攻击动画事件:原作各 FBX 的 event 帧不可得,统一 0.3s(legacy 定值)。
- 相机切换:Cinemachine blend 曲线无精确值,统一 1.5s Sine ease;vcam3 系 GroupComposer 阻尼跟踪以每帧 LookAt 近似。
- 难度数量截断:Unity float 数学改 double 精确(10×1.8 恒 18,不再掉 17)。
- 胜利结算:原作只弹 VectoryPanel 无星数;本地版另做 HP+用时星级落盘(原作服务器下发不可得)。
- Level2/3/4 原工程 `Invoke("FinishLevel")` bug(方法不存在永不触发)在 LevelBase 统一修复。
- Level2:烘焙 lightmap 不可得,实时等效光照观感偏白日;Boss 模型未按 Unity ×3 缩放(照 legacy 结构)。
- Level3:烘焙导出的坐标约定为 mirror-X(SceneExporter.cs 注释),机位照此换算并经落位验证;部分机位视野内城市观感偏空;线性管线下画面比 Unity gamma 工程偏暗。
- Level4:env 同 mirror-X 约定;雾用 Godot 指数雾近似 Unity ExpSquared;fire_breath 特效 emit 默认值 bug-for-bug 保留。
- 各关环境烘焙坐标约定可能不同(走廊=数值不变 / 城市与村庄=mirror-X),机位均按各自 env 已验证约定换算,场景内自洽。

其余关卡自检:
```bash
$G --headless --path . scenes/levels/level2.tscn -- --level2-selftest   # 34 项
$G --headless --path . scenes/levels/level3.tscn -- --level3-selftest   # 15 项
$G --headless --path . scenes/levels/level4.tscn -- --level4-selftest   # 13 项
$G --path . scenes/levels/level2.tscn -- "--level2-shot:<out.png>[:sec]"  # L2 截图(同理 L3/L4)
```
