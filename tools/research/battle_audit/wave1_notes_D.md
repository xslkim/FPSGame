# Wave1 Agent D 修复笔记(开火/激光/命中特效/换枪/飘字)

日期:2026-06-09 · 负责文件:`game/src/Battle/FireSystem.cs`、`game/src/Battle/GunBase.cs`、`game/src/Effects/LaserSight.cs`、`game/assets/effects/*.tscn`
工单:`hud_fire.md` #3/#6/#8/#9/#10/#11/#17/#18/#21/#24/#25/#34/#35 + `l1_visual.md` #4/#5/#12。

## 修复明细

### #3/#6/#10 命中红点(l1_visual #4)— 高
真值:右手 Flash.prefab(粒子 startColor (1,0,0))/左手 FlashGreen.prefab((0,1,0.0345));
Darkness 粒子 startSize 0.5,材质 Point19bcg.mat = **Blend_CenterGlow(alpha 混合)**,
贴图 Point19.png(= 项目 flash_point19.png,同源同文件,alpha 通道全 255、光存 RGB)。
注意:原作运行时 `Flash.transform.localScale = one×flashScale` **覆盖** prefab 根 0.5。
改动(FireSystem.cs `_Ready`):
- 双手分色:右 (1,0,0) / 左 (0,1,0.0345)(工单写 (0.99,0,0)/(0,0.99,0.13),与 prefab startColor 视觉等价,取 prefab 值)。
- 混合模式 Add → **Mix(alpha 混合)**,贴图运行时加工:取 R 亮度重写为 alpha(RGB 留白),AlbedoColor 染色 —— 复现原作"深红点",不再叠成橙团。
- QuadMesh 0.12m → **0.5m**(= 原作粒子 startSize;flashScale 公式不动)。软边贴图可见亮核约 1/3。
- 删掉 LaserSight 终点红点(`withDot:false`;原作无此指示器,命中指示全靠 Flash)。
验证:运行时打印 `basisXLen=0.278`(=flashScale@3.32m,缩放生效)、quad 投影 36.9px@720p —— 与真值截图同量级(真值 ~28px@8m,两者公式相同)。

### #8 激光宽度曲线(LaserSight.cs)
真值 FireSystem.prefab LazerRight/LazerLeft LineRenderer `widthCurve` 线性两键:
0.00894(右手)/0.00885(左手)→ 0.03,宽乘 1,线长 200m。
实现:CylinderMesh 锥形 TopRadius=0.00894/2(近/枪口端)、BottomRadius=0.03/2(远端);
圆柱 +90° 旋转后 Top(+Y) 朝 +Z = 枪口端(截图验证:枪口侧无宽楔,方向正确)。
LaserSight.Create 增加可选参数 `nearRadius`(不传=圆柱,兼容旧调用)。
复查:levelchoose/deviceconnection 界面只用 `LaserSight.RightRed/LeftGreen` 颜色与贴图静态成员(2D UiAimGuide),不实例化 LaserSight → 无误伤(自检全绿)。

### #9 激光终止行为(FireSystem.cs UpdateSide)
原作 LineRenderer 恒 (0,0,0)→(0,0,200):打怪穿透、墙体遮挡段深度剔除。
改:SetBeam(muzzle, muzzle + dir×200) **恒 200m**,不再止于命中点(Godot 圆柱深度测试天然遮挡)。
副作用:UV 平铺 len×2.5 恒为 500 = 原作 _MainTex tiling 500 ✓。

### #11 飙血绿色(assets/effects/impact_blood.tscn)
真值 GreenImpact.prefab(Impacts/Mobile)三材质 tint:
BrickDust (0.1686,0.6314,0.2431,a0.302) / BrickRocks (0.1412,0.3216,0.1505,a0.510) / BulletDecal (0.1602,0.3396,0.2206,a1)。
改:Spray (0.65,0.04,0.04) → (0.169,0.631,0.243,a0.5);Mist (0.5,0.03,0.03) → (0.141,0.322,0.151,a0.6);
Hole 弹痕加 modulate (0.160,0.340,0.221)。贴图 blood_particle/blood_decal 为灰白无红染,直接染色成立。

### #17 枪口挂点(FireSystem.cs BindSide)
原作枪 prefab 内 Sphere(Unity +Z 前):AK47 z=0.088 / M4 z=0.162 / HandGun z=0.042。
原 Godot 三枪统一 (0,0,-0.088)(只是 AK 的真值)。改为按枪型:
`muzzleZ = type==1 ? 0.162 : type==2 ? 0.042 : 0.088`,Position=(0,0,-muzzleZ)(Godot -Z 前)。

### #18 开火后座动画(GunBase.cs Fire → PlayFireAnimation)
原作:`_Ani.Play()` 播枪 FBX legacy "Shoot"(Unity 切片帧 1-6;HandGun.cs:36/AKGun.cs:50/M4Gun.cs:36)。
实测 Godot 导入:m4/handgun.fbx 含 AnimationPlayer,唯一 take "Take 001" len=0.208s(≈帧1-6@24fps),
轨道 Gun/Slide 位移(滑套后挫)+ Gun/Handgun_Shell 位移/旋转(抛壳)—— 内容即 Shoot,**整段播放即 1:1**。
ak47.fbx 导入后无 AnimationPlayer → 等效 tween:模型沿 +Z 瞬移 0.012m 后 0.2s EaseOut 回弹(贴近 6 帧≈0.2s 观感,依据注明于此)。

### #21 掉血飘字(FireSystem.cs ShowDamage)
真值 MonstHp.showHpNumber:"- N" 纯红 (1,0,0) 14 号、初始 anchoredPosition (0,10)、
上升 5px/s、alpha 1→0.3(速率 1/s,≈0.7s)后隐藏、每怪 3 个复用池(HpReduceText)。
原 Godot:(1,0.3,0.2) 96 号、0.5m/1.2s、alpha→0、全局 8 池。
改:颜色 (1,0,0);FontSize 56(56×PixelSize0.0025 ≈ 0.14m 字高 = 原作 14px×0.01 缩放);
上升 0.035m/0.7s(=5px/s×0.7×0.01);alpha 1→0.3@0.7s;**每怪 3 个池**(按 parent 键控,
怪物释放后其池在下次新建时清理)。飘字定位仍用调用方传入的 point +0.3m(锚点归 agent A)。

### #25 特效池数量
原作 FireSystem.prefab:Wood/Metal/Blood/Dust ×3、**Concrete ×4**、_BloodEffects ×3。
改:ConcretePoolSize=4(其余 3);BloodFlowerPoolSize 5→3。

### #24 换枪字面行为(FireSystem.cs OnSwitchKey)
按原作字面(Unity ChangeGun):旧枪 0.5s 下沉 0.1 → SetActive(false) → 新枪在 CreateGun 中
**直接出现在最终位置**(原作"伸出"循环作用于已隐藏旧枪,视觉瞬现,无升起动画);
**换枪全程不挡扳机**(原作无禁火:收起中旧枪可射,0.5s 后新枪立即可射)。
删除 `_switching` 门控与新枪升起 tween;旧枪隐藏后复位其 position 备用。
(与 hud_fire #24"待确认"条目对应:按工单决策以原作字面为准。)

### #34 冰冻手暂停行为(FireSystem.cs UpdateSide)
原作:`if (Frozen && !IsGamePause) return`(暂停时冰冻手仍可触发按钮)。
改:同样条件 —— 仅非暂停才跳过冰冻手。

### #35 Dust 弹痕(assets/effects/impact_dust.tscn)
原 Hole 为 MeshInstance3D(QuadMesh),FireSystem `GetNodeOrNull<Sprite3D>("Hole")` 永远取不到。
改:Hole 换为 Sprite3D(与其余四个 impact 一致),pixel_size 0.0025(dirt_decal 32px → 0.08m,同原 quad 尺寸);
删除不再用的 mat_hole/q_hole 子资源,load_steps 8→7。

### #12 枪模型偏大(l1_visual #12)
真值手枪 ~320×180px vs 旧 ~440×250px(同 1472×668 FOV45)→ 系数 0.725(=320/440≈180/250)。
三枪同包(Weapons Pack LOW POLY)同导入管线,统一 ×0.725(ak47 原 0.4 → 0.29;M4/HandGun 原 1.0 → 0.725)。
验证:运行时打印确认三枪缩放生效;0.45/0.58/0.725 三档截图扫描(wave1_d/sweep_*.png)确认缩放管线有效。
残余不确定度:真值截图瞄准点偏右上、Godot 自瞄居中,枪体随瞄准旋转 + 近距透视使像素测量 ±20% 漂移;
0.725 与审计/工单(偏大~40%)自洽,取该值。AK/M4 无真值截图,系数为同管线推断(备查)。

## 重要跨 agent 发现(非 D 文件,未动)

**隐藏面板的按钮碰撞体仍吃射线(疑似 agent C 领域)**:
25s 实测自瞄射线(屏幕中心)打到 `/root/Level1Battle/Camera3D/InGamePanel/PausePanel/BackGameBtn`
(dist=1.00m,面板根挂相机 z=-1),当时画面并无可见暂停面板。
后果:①flash/激光视觉停在 1m 处而非场景墙;②**扳机命中隐藏按钮 → 开枪被吞**
(FireSystem 判 layer3 Button 先触发 on_shot 不开火)。
原作 Unity 面板 SetActive(false) 时碰撞体一并失效,无此问题。
建议:UiButton3D/InGamePanel 在面板隐藏时禁用按钮 collision(DisableMode 或 Visible 联动)。
调试证据:/tmp/shot5.log flashdbg 行(collider=...BackGameBtn)。

## 验证

- 构建:`dotnet build` 0 error(文件锁串行)。
- 自检三件套全绿(最终构建):level1 19 PASS / 0 FAIL;levelchoose SELFTEST PASS(fails=0);deviceconnection SELFTEST PASS(fails=0)。
- 截图:tools/screenshots/audit/wave1_d/gun_25s.png(25s,1472×668)对照 tools/screenshots/l1u_gun_25s.png:
  细红激光近细远粗 ✓、无橙团 ✓、枪占屏比按 0.725 收敛 ✓。
- 命中点尺寸实测对照:射线打到真墙(ProxyCollision,dist=3.32m)时 flashScale=0.278、
  quad 世界 0.139m、屏幕投影 36.9px@720p,视觉为小而淡的红点(与真值"深红小点"一致);
  打到隐藏面板按钮(dist=1.0m,见上节)时 quad 0.1m 贴脸必然偏大 —— 公式与原作逐式一致,
  该场景下原作也会同等放大,待面板碰撞修复后红点落回墙面。
- 命中红点颜色:右手纯红 alpha 混合(截图确认);左手绿仅在双人模式出现,本次单人截图未覆盖(代码路径同右,仅 tint 差异)。

## 遗留/边界

- 后座动画:M4/HandGun 播 FBX "Take 001"(= 原作 Shoot,0.208s 滑套后挫+抛壳);AK47 无导入动画,用 0.012m/0.2s tween 等效。
- 枪口火光有效缩放差异(AK 原作 0.32 vs 手枪/M4 0.04)未动 —— 不在工单范围,火光结构已是 1:1 组件复用。
- UiAimGuide(菜单 2D 激光)未动。
- 隐藏面板按钮吃射线问题 → 建议 agent C 处理(见上节)。
