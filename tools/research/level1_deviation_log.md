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
