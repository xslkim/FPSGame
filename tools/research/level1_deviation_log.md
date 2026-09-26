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
