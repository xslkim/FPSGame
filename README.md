# FPSGame 项目结构与运行说明

## 目录

```
G:\FPSGame\
├── godot/          # Godot 4.8 dev 引擎源码(已编译出编辑器与导出模板,bin/)
├── game/           # 游戏工程(GDScript,主工程)
├── dist/           # 导出的 Windows 可执行文件 FPSGame.exe
├── tools/          # 官方 Godot 4.5.1 备用编辑器、资源复制脚本等
└── 迁移方案.md(见会话 plan 文件)
```

## 运行

- **直接玩**:`G:\FPSGame\dist\FPSGame.exe`(189MB 单文件,内嵌全部资源)。
- **编辑器开发**:`G:\FPSGame\godot\bin\godot.windows.editor.x86_64.exe --path G:\FPSGame\game`。
- **键盘调试模式**(无体感设备):主菜单 → 单人游戏 → 选"遥控器";方向键瞄准(±45°)、回车射击、Menu 键或 LeftAlt 换枪、Esc 返回。

## 体感设备(UDP)

- 手机 App 协议与原作一致:23 字节小包到 8281/udp,心跳/广播("Fortune"→255.255.255.255:8282)自动配对。
- 若枪的方向镜像不对:改 project.godot 添加 `[fpsgame] input/quat_mirror = 0|1|2|3`(默认 3)。
- 模拟器测试:`python G:\FPSGame\game\tools\udp_device_sim.py --selftest`。

## 自检回归(8 套件)

```bash
G=/g/FPSGame/godot/bin/godot.windows.editor.x86_64.exe
cd /g/FPSGame/game
$G --headless --path . scenes/test/fire_range.tscn      -- --m1-selftest     # 18 项
$G --headless --path . scenes/test/monster_range.tscn   -- --m3-selftest     # 27 项
$G --headless --path . scenes/levels/level1_battle.tscn -- --m4-selftest     # 12 项
$G --headless --path .                                -- --m5-selftest     # 29 项
$G --headless --path . scenes/test/m6m_range.tscn       -- --m6m-selftest    # 45 项
$G --headless --path . scenes/levels/level2.tscn        -- --m6l2-selftest   # 33 项
$G --headless --path . scenes/levels/level3.tscn        -- --m6l3-selftest   # 14 项
$G --headless --path . scenes/levels/level4.tscn        -- --m6l4-selftest   # 12 项
```

## 环境导出管线(Unity→Godot,可复用于新关卡)

见 `game/tools/import_environment.gd` 与 Unity 侧 `G:\test\FPSGame\Assets\Editor\GodotExporter\SceneExporter.cs`。
命令示例见 game/data/env_export/*_import_report.json 与各 agent 报告。

## 已知遗留(非阻塞)

1. 发布前把 `game/assets/fonts/cjk_fallback.ttf`(本机 SimHei 副本)换成可分发字体(ui_theme.gd 引用)。
2. 光照未烘焙:编辑器打开 env_school_hallway.tscn → 加 LightmapGI → Bake(4.8 dev 命令行烘焙会崩,GUI 可重试)。
3. Level2Boss 的 Skill 动画是程序化补间(原作为 Unity humanoid muscle 曲线,无法自动重定向)。
4. Level1 剧情场景的舞蹈是程序化剪影级简化(原作 K-POP 动画同理)。
5. Level2BossHurt/Death.ogg 有非标准 comment 触发 benign WARNING(播放正常)。
6. `assets/models/school_hallway/textures` 大小写与记录不一致(Windows 无碍,跨平台需修)。
