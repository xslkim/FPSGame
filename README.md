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
│   │                   # MessageBox、UiKit(坐标构建辅助)、UiTheme、JustRotate、GunUiController、UiButton3D
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
$G --headless --path . scenes/ui/menu.tscn -- --menu-selftest   # 28 项(主菜单 1:1)
```

截图对照(窗口模式,可指定分辨率):

```bash
$G --path . scenes/ui/menu.tscn -- --shot:<out.png>  --shot-res:1920x1080   # 主菜单
$G --path . scenes/ui/menu.tscn -- --shot-box:<out.png>                     # 带单人弹框(三键)
$G --path . scenes/ui/menu.tscn -- --shot-flash:<out.png>                   # 枪口火光峰值帧
```

Unity 侧真值:`G:\test\FPSGame\Assets\Editor\MenuScreenshot.cs`(GUI 模式 `-executeMethod MenuScreenshot.Capture` / `.CaptureStill`),几何测量 `MenuMeasure.cs -executeMethod MenuMeasure.Dump`,枪口火光 `FlashScreenshot.cs -executeMethod FlashScreenshot.Capture`(FLASH_ISO=nosmoke/noflame 可隔离子效果)。

## 导出

```bash
cd /g/FPSGame/game
G:/FPSGame/godot/bin/godot.windows.editor.x86_64.mono.exe --headless --path . --export-release "Windows" ../dist/FPSGame.exe
```

导出模板走 `export_presets.cfg` 的 `custom_template/release`(指向自编译 mono 模板);
dotnet publish 的运行时包来自 nuget.org(NuGet.config 已配)。

## 已知遗留(非阻塞)

1. 战斗关卡(怪物/关卡/特效)尚未按新标准重做:`legacy_gd/` 里是否定版 GDScript 实现,仅作参考;后续逐场景从 Unity 原作重新移植(C#)。
2. 发布前把 `game/assets/fonts/cjk_fallback.ttf`(本机 SimHei 副本)换成可分发字体(UiTheme 引用)。
3. `assets/models/` 部分环境贴图目录大小写与引用不一致(Windows 无碍,跨平台需修)。
4. 体感枪真机方向校准(quat_mirror)、枪口火光/激光真机效果未经实机验证(无设备)。
