# Level1 偏差清单与修复记录(2025-09-22)

调研真值:`tools/research/level1_unity_truth.md`(子代理)+ 本机 Unity 2019.4.40f1 实测截图
(`tools/screenshots/l1u_*.png`,采集脚本 `Assets/Editor/Level1Shot.cs`)。

## 已修复(本轮)

| # | 偏差 | 原作真值 | 修复 |
|---|---|---|---|
| 1 | 菜单/UI 激光起点用 MuzzleFlash 投影(距相机 0.3m 近轴)→ 光束起点落在屏幕中心怪兽胸口,与枪管脱节 | 原作 Lazer = LineRenderer 挂枪原点,(0,0,0)→(0,0,200),宽 0.023→0.03 | UiAimGuide 重写:枪原点→前方 200m 锥形投影(K 段 TextureRect,近粗远细);光点=3D 射线命中点回退 10m(原作 Flash);怪兽加 AABB 碰撞体 |
| 2 | Polygon2D/Line2D 在本引擎构建不可用(加色材质不生效、CW 顶点剔除) | — | 光束全走旋转 TextureRect(已验证路径) |
| 3 | G1~G4 出生散开角误用 born_length_override=15(距离) | 原作 GroupMaxBornFov={1:15,2:15,3:15,4:15}(FOV 角,长度恒 8m) | level_meta.json 改 born_fov_override |
| 4 | G2/G3 怪池混入牛魔王 | 原作 G2=3×飞斧(6槽3空)、G3=斧头2+飞斧4 | level_meta.json 波次池改正 |
| 5 | G4 骷髅全 0 级 | 原作 G4 有 2 只 MonsterLevel=1(HP35/攻20/速1.3/CD4.5) | 池条目支持 `type@lv`,槽位绑定实例 |
| 6 | 刷怪轮询取怪(PoolIdx) | 原作 Random.Range 起点+前向扫描第一个未激活 | SpawnTick 随机槽位扫描 |
| 7 | 所有怪出生等待按难度 | 原作只有 _Name==0 的怪(牛魔王/斧头/骷髅,序列化同值)吃难度等待;飞斧/宝箱/其他恒 0 | Born 传 0,Level1.OnMonsterBorn 钩子按类型设置 |
| 8 | 出生怪不面向相机、无位置微调 | 原作 LookAt(相机)+三怪 y+0.2、x<0→x+0.3 | OnMonsterBorn:FaceCamera+偏移 |
| 9 | Boss 走随机出生点 | 原作 InitBoss 固定点 (0,0.885,66) | SpawnBoss 固定 boss_pos |
| 10 | 机位混合 1.5s Sine | 原作 Blend 资产 Level1.asset:1s(Style 6) | CamBlendTime=1.0,Cubic EaseInOut |
| 11 | Battle0 机位 yaw180(沿走廊直视) | 原作 vcam Battle0 quat (-0.0402,0.4903,0.0227,0.8703)(≈yaw 58° 看楼梯间) | cam_pos_0 换精确姿态;**环境模型 X 镜像**→用镜像四元数转换(qy,qz 取反) |
| 12 | 主相机/平行光姿态与原作有出入 | quat 同上转换(镜像) | level1_battle.tscn 更新 |
| 13 | 雾:密度 0.16 深色 | 原作线性 5→12m,色 (0.581,0.585,0.430) | 本引擎 fog_depth_begin/end 无效,用指数密度 0.08 近似校准 |
| 14 | 胜利写星级结算(移植版发明) | 原作 Victory 只弹面板回菜单,全工程无星级写入 | 删 SettleStars |
| 15 | 怪血条黄血 | 原作 Hp_Yellow × 红色 tint (1,0,0) | MonsterHpBar fill 染红 |
| 16 | Boss 本体可直接打 | 原作本体 Untagged 打不到,弱点=脊柱上悬浮爱心(BoxHead 转发) | baotou.tscn 身体改 layer5;新增 BossHeart(脊柱骨挂红心,脉动+浮动) |
| 17 | debug 跳开场时 FOV 停留 30 | 战斗机位 FOV45 | debug 分支设 45 |

## 待办/验证中
- 枪与激光在战斗中的可见性真值(Unity 诊断运行中)
- 命中红点 Flash 尺寸曲线校验
- 胜利面板/暂停面板像素级对比

## 第三轮修复(2026-09-26,终验轮)

| # | 偏差/缺陷 | 根因 | 修复 |
|---|---|---|---|
| 18 | **战斗中鼠标左键完全无法开火**(原作按住连发) | 移植版鼠标左键只有 `Triggered` 边沿事件(UI 用),从未写入扳机电平 `_key1RightLevel`;原作 `GetCurKeyRing = _LastKey1_Ring==1 \|\| _MouseFireRing`(InputManager.cs:567) | MouseGunSource 增加 `LeftHeld` 电平;InputRouter._Process 在 Battle 态注入右路扳机电平(OnlyLeft 走左路);UI 态不注入避免与 GunUiController 边沿双发 |
| 19 | 战斗中鼠标右键无法换枪 | MouseGun.SwitchGun 事件在战斗场景无订阅者(FireSystem 只听 InputRouter.SwitchGunRight 信号) | InputRouter._Ready 转发:MouseGun.SwitchGun → SwitchGunRight/Left 信号(OnlyLeft 走左) |
| 20 | **所有开枪全部 NullReferenceException**(GunBase.Fire 每帧抛、弹药不扣、怪不掉血) | `GunBase.Player` 字段从未赋值(BindSide 漏接) | FireSystem.BindSide 补 `gun.Player = player`;L1 自检新增开火契约断言(AllGunsBound + HasMethod("Hit")) |
| 21 | 命中分发永远落空(即使开枪成功也不结算伤害/按钮) | FireSystem 用 `HasMethod("hit")`/`Call("hit")`/`"on_shot"` 小写名;Godot C# 方法按原名注册大小写敏感(`Hit`/`OnShot`) | 改为 `"Hit"`/`"OnShot"` |
| 22 | 开场 3s 画面右上巨大棕色"帆状"异物 | 剑女孩 FBX 网格是厘米单位(绑定 AABB z≈154m),挂骨后按 1.4286 放大 → 百米巨物横在镜头前 | IntroBadGroup 剑女孩挂骨 scale 改 1.4286×0.01;真值 local TRS 不变 |
| 23 | 命中光标近距成实心大红球 | flash_point19.png 转换丢 alpha,R 亮度当 alpha 留大面积实心核;原作 Blend_CenterGlow 是软光斑 | LoadFlashCursor 叠乘径向平方衰减(亮点+光晕观感) |
| 24 | 走廊窗口光晕片成深色半透明板 | env 烘焙时 Add_effect 材质带成 alpha 混合;原作是 Particles/Additive | env_school_hallway.tscn 两处 SHW_Add_effect_01 材质改 blend_mode=Add + unshaded |

验证:`--level1-fire` 挂接实测(按住左键跟踪瞄准):子弹 120→98、换枪 2→0、怪 HP 30→0 死亡回收全通;`--level1-probe` 落位探针;`--level1-shot-victory` 胜利面板。全回归 8/8 绿 + loading 两分支 + e2e-mouse-flow 绿。
新挂接:`--level1-probe:<sec>`(怪物落位+视线网格扫描)、`--level1-probe-intro:<sec>`、`--level1-fire:<png>:<sec>`(含换枪验证)、`--level1-shot-victory:<png>`。

## 第四轮修复(2026-09-26,玩家实机反馈)

| # | 偏差/缺陷 | 根因 | 修复 |
|---|---|---|---|
| 25 | 红点悬浮错位且巨大(开火/瞄准时红点浮在半空,~200px) | ① 鼠标模式命中射线从相机出、激光从枪口出,两者平行错位不重合;② 机位 x 未随环境镜像(项目既定"位置沿用原值"约定),距楼梯间墙仅 1.27m(真值 2.74m)→ 命中点在 1.5m 近墙,flashScale 公式压不下视尺寸 | `FireSystem.UpdateSide` 改回原作语义:射线=枪原点+枪 forward(与激光共线,终点即红点);鼠标模式先取相机光标目标点、枪指向它再出射线;机位见 #27 |
| 26 | 手枪/M4 渲染反向(枪口朝后朝镜头) | FBX 实测枪口/准星朝 +Z(AK 沿 -X);装配注释误称"m4/handgun 已沿 -Z" | `FireSystem.BindSide`:type1/2 补 rotY180°;`--gun-view` 前后两视角截图取证 |
| 27 | 怪出生贴脸/钻相机、玩家看不到怪却被打死 | Battle0 机位 x 未镜像:G0 出生锥 1.27m 打近墙 → 出生 ~0.77m,相机嵌进怪体内(背面剔除=怪隐形),怪贴身攻击;真值同锥 2.74m→2.24m+ | `level1_battle.tscn` cam_pos_0~4 原点 x 取负(+0.735/+0.085,镜像约定);G0 出生恢复 3.2m 楼梯间中景。裁决文档 battle0_mirror_verdict.md §六.3 的"全镜像"选项经用户实机反馈后落地(仅战斗机位) |
| 28 | 牛魔王行走/待机整体前俯 ~90°(低头冲锋姿态,真值为直立行走) | bull 是全场唯一 humanoid(animationType=2)怪;旧 bull_anims.tres 为原始曲线直转,缺少 Unity humanoid 肌肉归一化 | K-POP 同款烘焙管线:Unity 2022.3 临时工程(G:\tmp\kpopbake)PlayableGraph 逐帧求值 → .kdance.bin(idle/walk/attack×3/damage/die 七剪辑全长度)→ `BinToTres` 离线转写 bull_anims.tres(骨骼局部=镜像 Unity 局部,30fps);meta 新增 bull idle_anim="idle"(原作 Locomotion 混合树 Speed=0=idle 语义)。axe 双刃 BoneAttachment3D 挂头/左手骨为 FBX 原生挂点,非缺陷 |

验证:`--monster-view`(idle/walk 直立姿态对照 Unity 烘焙参考图 bull_*_unity_t1s.png 一致)、`--level1-shot` 21/24/28s(牛魔王直立正面逼近,对位 l1u_intro_21s/26s 构图)、`--level1-fire`(耗弹 120→89、击杀回收、右键换 AK 链路全通)、regress.sh 8 项全绿、dist 导出冒烟(menu 正常)。
新工具:`src/Tools/MonsterView.cs`(怪物/枪检视 + --dump-tree/--fbx-view/--bone-diff)、`src/Tools/MonsterSizeAudit.cs`、`src/Tools/BinToTres.cs`;烘焙产物存 `tools/bull_bake/`(不入包)。

### 第四轮补丁(导出包实机复测追加)

| # | 偏差/缺陷 | 根因 | 修复 |
|---|---|---|---|
| 29 | **导出包里全部 humanoid/generic 怪白模**(编辑器正常) | 怪物 tscn 用 FBX 实例子节点的 `surface_material_override` 接线——导出后实例化子场景覆盖不生效,回退到 FBX 自带无贴图 wire 材质 | 材质接线改代码:`Monster` 基类新增 `[Export] Material BodyMaterial`,_Ready 时对 BodyNode 子树 `SetSurfaceOverrideMaterial(0)`(与 L3 Dragon/Magma/Wolf/RockWarrior 既有模式统一,派生类重复实现已删);5 个 L1 怪 tscn 根节点注入 `BodyMaterial = ExtResource("5")`。成品内 `--mat-audit` 验证 override=y/tex 正确 |
| 30 | 菜单激光不从枪口出(悬浮在枪身上方) | UiAimGuide 光束起点用枪节点原点(握把),枪口在前方 0.2m | MenuScreen/LevelChooseScreen/DeviceConnectionScreen 三处 SetAim 起点改 MuzzleFlash 标记节点(枪口尖);战斗 3D LaserSight 本就起自 Muzzle,不变 |
| 31 | 战斗红色命中光斑与激光视觉错位("歪") | 同 #25:枪口/相机双射线平行错位 | 已于 #25 修复(共线),本轮复核 dist_fire 命中/击杀/换枪链路在导出包正常 |

验证:dist 成品 `--quick:` 直跳 + `--mat-audit`/`--level1-shot-battle`/`--level1-fire`/`--story-shot` 全链路;牛魔王贴脸攻击与 l1u_intro_26s 构图一致(有贴图、直立、面向相机)。

### 第四轮补丁 2(粒子透明度 + 菜单卡死)

| # | 偏差/缺陷 | 根因 | 修复 |
|---|---|---|---|
| 32 | 粒子特效成片不透明(metal 火花/拾取爱心/Boss 火球等) | 这些贴图**源头就是黑底无 alpha**(FPS Pack/KriptoFX 原作),原作靠 Particles/Additive 黑=透明;移植材质误用 alpha 混合 → 全彩方块 | 材质改 Add 混合(黑=透明):impact_metal mat_sparks、pickup_drop mat_heart、Fireball core;核查其余:lightning_pillar/teleport_flash/BossHeart/MuzzleFlash/glow(mat_flash)本就用 Add,impact 碎石/木屑是实体碎片(原作同样不透明,正确),hole_*/blood/dust 贴图自带真 alpha 不变 |
| 33 | **暂停/续币面板打开后卡死,点不了任何按钮** | InputRouter(autoload)默认 ProcessMode 可暂停:timeScale=0 后 _Process/_Input 停走,扳机电平(MouseGun.LeftHeld 注入)与瞄准停在旧值;FireSystem 虽 Always 运行但 GetCurKeyRing 恒假 → 3D 按钮永不触发 | InputRouter._Ready 设 `ProcessModeEnum.Always`(照原作 Unity Update 不受 timeScale 影响);新增 `--panel-click-test` 自测:开暂停→模拟鼠标点"返回游戏"→断言关闭(编辑器/成品均 PASS) |

