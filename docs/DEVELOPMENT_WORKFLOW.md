# 后续开发与调试工作流

## 这次梳理的结论

项目已有按职责分开的目录和场景自检，但修改成本主要来自三处：

1. `LevelChooseScreen`、`Level2`、`MenuScreen`、`StoryStart` 等单个脚本超过 600 行，UI 构建、状态、动画、截图钩子及测试混在一起。当前最大的四个脚本分别约 790、741、678、616 行。
2. 场景、相机和效果大量依赖字符串 NodePath。路径失效时往往在很久之后才出现空引用或渲染异常；之前没有统一的场景/对象 ID。
3. 每个界面和关卡已有自检、截图参数，但入口分散。截图也缺少相同帧、相同视口的差异工具。仅靠人工描述视觉问题，很难重现相机与动画相位。

目前约有 186 个 C# 源文件。代码关系图没有发现导入循环，主要风险是**大场景控制器承担过多职责，以及跨场景的隐式 NodePath 契约**。因此按场景逐步拆分，避免一次性改写所有关卡。

## 开发结构约定

- `Core` Autoload 只保存跨场景状态、存档、音频与场景跳转；战斗、镜头、动画时间留在各自场景内。
- 场景控制器负责协调节点，不直接承担模型材质、粒子生成、UI 纹理绘制等全部细节。可复用表现优先保存为 `.tscn`/`.tres`，关卡参数放在资源或数据文件。
- 子场景通过信号向父场景报告事件；不要反向查找另一场景的固定绝对路径。必须存在的节点用 `SceneContract.Require<T>`，缺失时报告场景与对象 ID。
- 新增可排查的游戏对象实现 `IDebugInspectable`，只暴露只读状态。动态池对象保持确定性名称，例如 `MonsterPool/axe_zombie_2`。需要跨重排保持 ID 时，可设置节点 `debug_id` 元数据。
- Godot 可直接导入的动画使用 `AnimationPlayer`；需要混合或状态切换时使用 `AnimationTree`。Unity Humanoid 烘焙的 K-POP 舞蹈暂由 `KDancePlayer` 适配，人物根节点与动画骨架分离。所有剧情镜头按固定时间、机位、FOV 保存参考帧。
- 材质、灯光、粒子按目标渲染器制作。项目当前是 Compatibility；切换到 Forward+ 前，先在相同设备与相同画面比较性能和参考截图，逐个校正材质与环境。不要把切换渲染器当成无成本的画质修复。

这些约定遵循 Godot 对[场景组织](https://docs.godotengine.org/en/stable/tutorials/best_practices/scene_organization.html)、[AnimationTree](https://docs.godotengine.org/en/stable/tutorials/animation/animation_tree.html)和[渲染器差异](https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html)的官方说明。

## 玩家发现问题时

1. 按 **F4** 打开调试面板：查看场景 ID、对象 ID、焦点对象、世界坐标、相机、FPS、CPU 帧时和 draw calls。F3 仍只显示 FPS。
2. 鼠标或光枪指向有问题的对象。按 **F6** 固定当前对象，再移动视角；再次按 F6 取消固定。按 **F7** 复制对象 ID。
3. 按 **F5** 保存同名 PNG 与 JSON。路径自动复制到剪贴板，文件在 `%APPDATA%\Godot\app_userdata\FPSGame\debug_reports`。JSON 含场景、对象、相机、输入、关卡波次、怪物 HP/动画状态等，并记录编译时间、Build ID、运行的 EXE 路径和帧编号。图形模式在同一个完成渲染的帧读取截图与状态。关掉 F4 后按 F5 可保存无遮挡截图。无图形设备的 headless 模式只生成 JSON。
4. 反馈时附上对象 ID 和这一对文件即可。例：`L2/Battle/MonsterPool/axe_zombie_2`。如果未指向对象，面板显示 UI 焦点 ID；对象不存在时仍记录场景 ID 和相机状态。

对象 ID 是 `场景 ID/场景内节点路径`。更改节点层级会更改默认 ID；需要长期稳定的对象应显式提供 `debug_id`。

## 修改后如何验证

一条命令编译并运行三个 UI 场景、剧情、四个关卡及诊断报告的自检：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File G:\FPSGame\tools\verify.ps1
```

修改界面时可先用 `-Suite ui`，修改战斗或剧情时用 `-Suite levels`。已自行编译可加 `-SkipBuild`。每次运行的完整日志保存在 `G:\FPSGame\temp\verify\<时间戳>`；脚本任何测试失败会返回非零退出码。自动测试只证明场景流程和关键数据没有回归，画质仍需逐帧对照。

每个测试使用独立存档目录和 UDP 端口，不改玩家存档。单项默认超时为 300 秒；日志中的 C# 异常即使没有使进程退出，也会令测试失败。`Runtime/InputAndSession` 使用真实 UDP 数据包覆盖双枪按下/释放、设备断连、无效姿态、鼠标归属和失焦释放，并检查加载恢复、双人续命排队、弹药兑换、冰冻结束、复活后旧中毒失效、胜利保存以及重复进入关卡。关卡波次流程另由四关的自检覆盖；这些检查不替代实机画面和光枪硬件验收。

发布入口保持为 **`G:\FPSGame\dist\FPSGame.exe`**。日期目录用于归档，不能只更新日期目录而把常用 EXE 留在旧版本。导出后执行 `& G:\FPSGame\tools\verify_export.ps1 -Executable G:\FPSGame\dist\FPSGame.exe -Graphics`，直接检验交付 EXE 的实际射击、第二关表现、动画事件、输入/会话和 F5 截图。原生测试日志在 `temp/native-verify/<时间戳>`；测试报告也写入各案例的隔离目录，不混入玩家的 `debug_reports`。正式运行仍使用原来的玩家报告目录。归档时同时保存 EXE 和对应的 `data_FPSGame_windows_x86_64`，不要混用不同编译版本。

可用 `-CaseIds @('Runtime/InputAndSession','Combat/PauseShot')` 只重跑有关案例。`Combat/PauseShot` 使用实际射线点击暂停面板；运行时检查还覆盖一次扳机只操作一个面板、箱子旧回收任务不能影响重新出生，以及损坏存档恢复。怪物的延迟操作统一用 `ScheduleLifeAction`：绑定节点、随游戏暂停、在死亡/回收/重新出生/离开场景时取消，避免跨生命和跨场景回调。

存档保存先写临时文件并落盘，再替换正式文件；上一版本保存在 `save.json.bak`。损坏文件保留为 `save.json.corrupt-<时间>` 并尝试恢复备份。加载校验关卡索引、金币、弹药和计时数值；离线回币按周期直接结算，避免逐秒或逐周期循环阻塞画面。

### 战斗枪口与激光约定

`GunBase.CalibrateMuzzle()` 从实际枪管网格测量枪口，并跟随模型动画；瞄准、碰撞射线、枪口火光和激光使用同一个枪口及枪管轴。不要再给单个关卡写枪口偏移。`FireSystem` 在绘制前同步最终镜头和模型姿态，`LaserSight` 使用面向镜头的光带，避免圆柱表面的亮纹偏离轴线。UI 的独立模型由 `UiWeaponPresentation`/`UiAimGuide` 管理。

`Combat/WeaponsAndHealth` 回归覆盖 AK、M4、手枪的真实枪口、45°/52° 视野下的右下角布局、多个机位/瞄准点、后坐力与镜头同时运动、连续换枪，以及血量数值显示。战斗枪位置集中在 `fire_meta.json` 的 `pos`/`pos60`，按当前相机 FOV 选择，换枪复位也使用同一配置。修改激光后，还必须查看渲染截图中的亮线是否从枪管口发出：节点坐标重合不等于贴图亮纹正确。

`L3/Ground` 使用报告中的街道外星兵位置，覆盖无等待出生、受伤/装填过程中落地，以及分配窗口的敌人保持平台高度。`ToonMonster` 只有存在有效 `FireWindow` 时才锁定高度，无窗口的街道敌人持续受重力影响。

`L2/Presentation` 检查民兵完整待机动作的上半身轨道、民兵/外星兵头部上缘射线命中，以及三个第二关落点的箱子落地。头部碰撞随骨骼移动，回收/死亡时关闭；箱子使用底面位于模型原点的盒形碰撞，避免胶囊最小高度造成悬空。

该检查还走完第二机位 5 个入口的民兵/外星兵路线，验证位置、高度及从实际机位发出的头部射线。原始 `ToonSolder.prefab` / `Alien1.prefab` 没有 CharacterController，`ToonMonster.UpdateMoveTo` 使用直接位移分支；Godot 中有效 `FireWindow` 的演员同样使用直接水平位移及原始高度。命中碰撞仍保留。不要把街道落地规则套到这些平台演员，或让门墙挡住它们的原始出场路线。

`Combat/ActualShots` 从实际鼠标输入、枪口射线到命中结算，覆盖三种武器朝空处开枪/释放、全部战斗敌人种类、普通敌人头部、第二关 Boss 骨骼护甲和胸口弱点、补给掉落、飞斧拦截及弹尽朝空处补弹。骨骼命中区域用 `MonsterHitRegion` 转发固定部位类型，并在死亡/回收时立即退出射线层。第二关 Boss 的头部是护甲，胸口 `HurtSphere` 才扣血；这与普通民兵/外星兵的头部可受伤规则不同。

测试续命币可写入 `%APPDATA%\Godot\app_userdata\FPSGame\test_credits.json`，内容为 `{"coin":50}`。下一次启动时加币、保存存档并删除该文件，只补一次。已有游戏进程运行时，先关闭旧进程再启动新版，避免旧进程写回旧存档。

### 战斗动画与攻击事件

2026-10-08 的报告回归新增 `Visual/ReportedIssues`（导出检查为 `ReportedVisualIssues`）：覆盖金币 0/9/60/999 在 720p、1080p、4:3 视口内的排版，开场 5.72/8/10/15 秒的被抱姿态，三把枪的墙面/续命按钮及后坐力落点，补弹箱模型复用，以及 Boss 闪现图集、蓝色闪光和释放。女孩使用弯膝、伏卧的被抱姿态并跟随士兵手臂骨；补弹箱使用独立的原生模型，AK/M4 箱保留各自表现。

激光现在在实际命中面截断；光斑只向镜头微移 1mm，保持命中点的屏幕投影，不再沿枪管方向回退 0.1–0.5m。F5 报告新增 `beam_end_world`、`impact_screen`、`impact_alignment_error_pixels`，并在绘制前同步最终镜头/后坐力后的射线。金币与弹药数字统一保留屏底间距，避免字体最小高度挤出旧的 60px 文本框。

实图验收可从导出包运行 `--quick:res://scenes/debug/reported_visual_qa.tscn`，同时传 `--qa-save-dir:<隔离目录>`、`--qa-udp-port:<独立端口>` 和 `--visual-shot-dir:<输出目录>`。不加 `--headless`，会输出菜单、四个开场时间点、三把枪的墙面/续命、补弹箱远景/近景、Boss 闪现前中后的 PNG。必须实际打开这些图片检查；自动坐标断言不能替代视觉验收。

`AnimTrackUtil` 生成不可变的共享动画库副本，将 `data/combat_animation_events.json` 的原始事件写入 Godot 方法轨道。资源缓存按原始资源路径及怪物类型保存，重复进关不会按对象实例累计整套动画。`Monster.OnCombatAnimationEvent` 核对生命状态、暂停状态和当前剪辑，再交给具体怪物处理。伤害不能再用固定 0.3 秒 Tween 替代动画键；动画打断、停止、倍速与暂停都应影响对应事件。

事件秒数来自 Unity `AnimationUtility.GetAnimationEvents`，保存在 `tools/combat_bake/unity_animation_events.json`。FBX `.meta` 中的事件时间可能是归一化值，不能直接当秒使用。民兵/外星兵按原始 `InfantryGun.controller` 的退出时间从 `reload` 切到 `shoot`，仅 `shoot` 的 `ToonShoot` 键发射子弹。飞斧用 `EventSkill` 的字符串参数区分拿斧和抛斧；飞龙保留 `StartFire` 和 `EventAttack` 两个独立事件。

第二关 Boss 的完整 Humanoid 动作先由 Unity PlayableGraph 烘焙，再由 `BinToTres` 转成 Godot 全骨骼轨道。`tools/prepare_combat_truth.py` 仅复制参考资源到隔离的 `temp/unity_combat_truth`，不会修改原始 Unity 项目；`tools/BakeCombatTruth.cs` 输出烘焙和事件，`tools/export_combat_events.py` 映射剪辑名称。首帧先求值再记录，避免把 T pose 写入第一帧。根场景节点与动画骨架保持分开。

Boss 的技能旋转需要按坐标系转换（Unity 局部旋转的 X/Y 符号转换后再应用）；回到行走/待机时用完整四元数复位，不能只更新 yaw。胸口球与护甲会在不同姿态下重叠，`Combat/ActualShots` 在原始 Skill1 的后续动作中验证弱点真实射线命中；不要为通过测试移动弱点或去掉护甲。

`Combat/AnimationEvents` 通过实际 `AnimationPlayer.Advance` 验证原始事件时间、多段攻击、受伤打断、暂停/恢复、装弹→开枪、倍速、真实飞斧生成、三色龙伤害，以及 Boss 的慢速起手。怪物调试报告新增动画时间、播放速度、事件数量和最近一次事件。

### 正常关卡自动试玩

`tools/verify_playthrough.ps1` 是较长的独立验收入口。它使用原始波次、场景碰撞、100 血和 120 弹，通过实际鼠标瞄准、扳机、换枪以及续命按钮完成关卡；不调用 `Monster.Hit`、不强制胜利、不改攻击冷却或时间倍率。测试存档初始 50 币，每次续命仍按正常规则扣币。默认试玩四个已实现关卡，也可指定 `-Levels level2 -Difficulty hard -Seed 144007`；`-Executable <EXE路径>` 验证导出包，`-Graphics` 打开真实渲染。

每个关卡写出 `levelN-playthrough.json`，包含 Build ID、波次、开枪/续命次数，以及每次出生的对象类型、伤害、死亡/超时、可瞄准时间和最后位置。完全不可瞄准的出生即使因超时而推进波次，也会使验收失败；不能仅靠“最后胜利”判断关卡正确。启动参数为 `--playthrough:level2,level1,level3,level4:easy:144006`，必须同时提供隔离存档。

加 `-Graphics -CaptureScreenshots` 会保存真实试玩中的每个机位、敌人类型、攻击动作、特效、续命和胜利截图到该次日志的 `screenshots` 目录。必须查看这些图片再报告视觉验收通过。瞄准采样同时覆盖身体宽度和胶囊高度，射线仍包含 UI 与环境；中心被暂停按钮遮住不能直接判定整个怪物不可瞄准。持续射击用过的扳机不能同时触发暂停按钮，需释放后再按下。

`Visual/OtherLevels` / 原生 `OtherLevelsVisual` 覆盖第二至第四关全部 16 个机位、三种枪对续命按钮的激光对齐、连续射击与暂停隔离、雷电图集、岩石 Boss 实际动画生成的大火球，以及三色龙吐息与受伤打断。需要固定渲染图时运行 `res://scenes/debug/other_levels_visual_qa.tscn` 并传入 `--visual-shot-dir:<绝对目录>`；导出 EXE 使用 `--quick:res://scenes/debug/other_levels_visual_qa.tscn`，同时提供隔离存档和 UDP 端口。这个固定镜头检查与正常试玩互补，不能代替正常波次通关。

试玩固定 1280×720 视口。Godot headless 默认是 64×64 方窗，未设尺寸会裁掉原场景两侧窗口，产生错误的“敌人不可见”判断。`Port/Geometry` 同时对照第二关所有相机的三轴与 Unity 原始四元数；Godot `.tscn` 的 `Transform3D` 文本按行保存，不能直接把运行时轴向量顺序写入文件。

固定第二关截图可用 `--combat-presentation-shot:<绝对PNG路径>|<机位索引>|<枪类型>|<血量>`，机位 0/1、枪类型 0=AK/1=M4/2=手枪。例如导出版本：

```powershell
G:\FPSGame\dist\FPSGame.exe -- --quick:res://scenes/levels/level2.tscn '--combat-presentation-shot:G:/FPSGame/temp/level2-check.png|1|0|37'
```

截图入口会固定场景并退出，供开发验证使用；正常游戏不需要这些参数。第五字段 `|controller` 可截取正前方的控制器瞄准姿态，未提供时使用固定鼠标瞄准点。玩家状态的调试报告包含左右 HP 和血条显隐状态。

对照 Unity 截图时，先让两个画面具有**相同的视口尺寸、剧情时间/动画帧和机位**，再运行：

```powershell
python G:\FPSGame\tools\compare_frames.py unity.png godot.png --out G:\FPSGame\temp\visual_review
```

Unity 截图带编辑器边框时，可加 `--reference-crop x,y,width,height`。输出 `overlay.png`（半透明叠图）、`heatmap.png`（像素差异）及 `summary.json`。数值用于定位构图、位置、亮度问题，不直接代表主观画质。

## 后续按场景迁移的顺序

1. **UI**：将 `MenuScreen`、`LevelChooseScreen` 的程序化布局拆成可复用的按钮/弹框场景与输入控制器；先保持现有 ID 和自检结果。
2. **战斗**：从 `LevelBase` 和 `Level2` 中拆出波次推进、机位导演、出生点规则与独立表现节点。每拆一个职责，先在原场景跑完整自检和对应参考帧。
3. **剧情**：将 `StoryStart` 的时间轴、镜头切换、演员动画与落地效果分开；允许逐帧回放到指定时间，不再依赖等待或目测。
4. **渲染/粒子**：为菜单、舞蹈、Level2、枪口火光及激光分别建立固定机位的 Unity/Godot 对照帧。选定目标渲染器后再统一材质、灯光、透明混合和粒子资源。

每个迁移单元以“场景自检通过、关键参考帧未退化、导出包能运行”为完成条件。避免同时修改多个无关关卡，便于定位新问题。
