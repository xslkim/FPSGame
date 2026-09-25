# Wave1 Agent A 修复笔记(怪物本体 + Boss + 投射物)

日期:2026-06-24 · 范围:Monster.cs / BaotouMonster.cs / BoxMonster.cs / FlyAxeMonster.cs / Fireball.cs /
ProjectileAxe.cs(新)/ scenes/battle/monsters/*.tscn / scenes/battle/projectile_axe.tscn(新)/
data/monster_meta.json / LevelBase.cs(授权两区域)/ Level1.cs(仅 SelfTest 断言,工单授权)
真值来源:G:\test\FPSGame(Unity 脚本/prefab/场景逐一核对,行号见下)

## 修复清单(真值出处 → 改动文件:行)

1. **近战怪 CD 期站桩(monsters.md #1,高)**
   真值 `MonsterBase.cs:179-209`(半径内只在"过 CD 且在 Locomotion 且 maxDistance 内"时 Attack,否则什么都不做;移动只在 else 分支)。
   → `game/src/Battle/Monster.cs:155-210` `MoveToPlayerAndAttack()`:半径内 CD 未到原地等待(只重力,不转身不移动);
   攻击/受伤动画播完自动回 locomotion(原作 Animator exit 转换等效);半径外才转身+移动(3m 内钳速 1 保持)。
   **附带修复隐藏 bug**:`IsPlayingAny()` 旧实现 `Array.IndexOf(names, Anim.CurrentAnimation)` 把 StringName 装箱后与 string 比,
   恒为 false → 攻击动画从不阻断移动、DoAttack 每帧重触发(事件 Tween 每帧被杀,攻击事件永远不发)。
   改逐元素 `StringName==string` 内容比较(Monster.cs:352-360)。这是"怪边走边打钻相机"的底层根因之一。
2. **每波首怪晚一个 interval(monsters.md #6,高)**
   真值 `LevelBase.cs:51`(lastBornTime=0)+ `:207-244`(首 tick 即满足;**lastBornTime 仅 born 成功才更新**)。
   → `game/src/Battle/LevelBase.cs:194-196` StartGroup 预置 `SpawnTimer = interval`(冻结结束首帧即刷,
   G0 首怪 ~21s,对位 l1u_intro_21s.png);`:281-287` SpawnTick 未刷出(槽位忙/达上限)时保持计时、下帧重试(原作同语义)。
3. **飞斧投射物(monsters.md #2/#3,高/中)**
   真值 `ZombieSkill.cs:32-101` + `ProjectileEx.cs:14-111` + `ProjectileAxe.prefab`(m_rotationMin/Max -360/-720)+
   `MonsterAZ@Skill.FBX.meta:298-308`(TakeHandAxe@0.22410919s / ThrowAxe@0.60037744s)。
   → `FlyAxeMonster.cs` 全量重写:Skill(零混合)+ 0.224s 切手斧(axe_02 显/axe_01 隐)+ 0.600s 双隐扔斧 + 4s 复原
   (epoch 取消,原作 CancelInvoke);命中率 `HitRate=0.15 × 本关 diff_rate`(见保真注记 4);
   未命中目标点上下±且左右±各偏 `3+rand(0,3)`(原作 int 重载 → 3/4/5m);出手点=axe_02 网格世界位置。
   → 新增 `game/src/Battle/ProjectileAxe.cs` + `game/scenes/battle/projectile_axe.tscn`:速度 2m/s 直线、寿命 10s、
   三轴自旋各 Random(-360,-720)°/s、相机空间 |z|<0.5 且 |x|<1 且 |y|<1 命中、按屏幕 x 分侧扣血、命中后 1s 消失;
   视觉复用手斧网格(原作 ProjectileAxe.prefab 同源斧网格)。
   **原作 Skill 无 EventAttack 事件 → lastAttackTime 恒 -99,CD 对飞斧无效**(回 Locomotion 即再攻),已按真值实现(注记 6)。
4. **宝箱行为(monsters.md #4/#5/#28,高/中/低)**
   真值 `BoxMonster.cs:40-130`(Unity)+ 场景三箱 PrefabInstance 覆盖(school_day.unity:BoxAK 实例
   WaittingTime 显式覆盖 20、_Type=1;BoxM4/BoxBullet 20;Idle02Time:BoxBullet/BoxAK=3、BoxM4=5;LifeActiveTime=20)。
   → `BoxMonster.cs` 重写:原地等待 20s(不再恒速移动/反向跳;RunAway 与自灭同帧原作永不触发,不移植);
   等待期每 Idle02Time 插播 LickAttack(meta idle2_anim);20s 到 → HP=0 → 1.5s 缓期回收(原作 Invoke DestorySelf,
   无掉落无 Die/血花演出,Monster.cs 新增 OnLifeTimeout 钩子,默认怪仍即刻回收);
   掉落只发**受击侧**(基类 Hit 记录 LastHitSide,原作 OnDead(bool Right)):子弹箱 +60(SaveService.BoxBullet=UserData.BoxBullet)、
   AK/M4 箱 AddGun(0/1);删除 pickup_drop 特效+pickup.wav(原作无)。
   → `LevelBase.cs:362-364` 按箱 kind 传 WaittingTime(数据在 monster_meta.json `box.kinds`)。
   → `monster_meta.json` box 增 born_anim/idle2_anim/kinds(waitting_time 20/20/20 + idle2_interval 3/3/5)。
5. **Boss 攻击重置 + Hurt 动画(monsters.md #7/#8,高/中)**
   真值 `BaotouMonster.cs:71`(Hit 里 lastAttackTime=Time.time)+ 基类 CrossFade("Hurt",0.1)。
   → `BaotouMonster.cs` OnHurt 重置 LastAttackTime(被打 5s 内不反击);UpdateActive 在 DamageAnim 播放期间不抢回 anim_idle。
   **Hurt clip 原本缺失**:baotou_anims.tres 无 "Hurt"。真值 Chr_Animator Controller 的 Hurt 状态 = Anim_DEATH_01.FBX
   165-170 帧(不循环)→ 用脚本从 tres 里 anim_death(=165-230 帧切片)裁前 5 帧(0.1667s,46 条轨道)生成 Hurt clip 入库。
6. **Boss 瞬移(monsters.md #11,低)**
   真值 `BaotouMonster.cs:49-53`:x+=Random.Range(-1.8,1.8)、z+=Random.Range(-2,2)(满幅);ChangePositionStar
   在被打瞬间旧位重触发一次。
   → Teleport 改 ±1.8/±2.0 满幅(原夹紧最小 0.5);闪现改为 OnHurt 时旧位一次(删出现点第二次)。
   星星音效待确认(注记 5)。
7. **血条高度 + 宝箱血条(monsters.md #12/#13,高/中)**
   真值 `HpReduceNumber.prefab`(根 scale 0.01,Canvas anchored y=153.2 → 挂点+1.532m)+ 各 prefab 挂点
   (骷髅/斧/飞斧 0、牛魔王 0.36、Boss 0.5)。
   → tscn HpAnchor y:骷髅/斧/飞斧=1.53、牛魔王=**1.51**(=(0.36+1.532)×根 0.8)、Baotou=**2.03**(=0.5+1.532,局部不含根 y0.885)。
   与工单数值(1.89/1.72)不一致,按 prefab 推导落实(注记 3)。
   → box_monster.tscn 删 HpAnchor(原作 BoxBullet.prefab 无 HpReduceNumber);Monster._Ready 改 GetNodeOrNull + 全程 `HpBar?.`。
8. **命中胶囊 + Boss 身体挡弹(monsters.md #17/#18 + 工单 #9,中)**
   真值各 prefab CharacterController 实测:骷髅 1.2/0.25 c0.6、斧 1.8/0.3 c0.9、飞斧 2/0.3 c1.0、bull 1/0.5 c0.5、
   箱 0.4/0.4 c0.2、Baotou **0.2/0.2 c0.2**(仅脚下,身体不挡弹)。
   → 六个 tscn BodyShape/中心逐值改。**工单 #9 核查结论:不符合**(现状 baotou 0.5/2.0 整身挡弹)→ 已改 0.2/0.2 c0.2,
   layer5(16)保持 → FireSystem 射线穿过身体打身后环境出 Dust,仅爱心(layer2,BossHeart 转发)掉血。
9. **模型缩放(monsters.md #19,中)**
   真值 `Bull.prefab:952` 根 0.8、`Baotou.prefab:529` CHR_Zcharacter_001 1.029025、`MonsterSkeleton.prefab:1446` 根 1.0。
   → bull.tscn Model ×0.8、baotou.tscn ×1.029025、skeleton.tscn ×1.0(原 0.8)。"待确认"项按 prefab 序列化值落实(依据即上述)。
10. **火球(monsters.md #9/#10,中/低)**
    真值 `BaotouFireball.cs:41-69`(FireFireAS 发射音 + CollisionEnter→FireExposionAS(FireExplosion4)+Invoke 0.1s
    HitPlayer(Both))+ `Fireball1.prefab` EffectSettings(**MoveSpeed=4**、LayerMask=512 仅 CameraWall、
    DeactivateAfterCollision)+ school_day.unity:117346-117400(CameraWall 16×9 于相机前 0.3m)。
    → `Fireball.cs` 重写:发射播 fireball_launch.wav(已有移植资产,对应 FireFireAS);速度 **4m/s**(读到了,
    工单"读不到保持现值"分支未触发);直线飞向相机(战斗中相机静止,等价原作脱父直飞);进入相机前 0.35m 视为撞墙
    → 爆炸音 fireball_hit.wav(对应 FireExplosion4)+ 命中点爆炸闪光(放大淡出 0.3s)→ 0.1s 后 HitPlayer(Both) 双手各 15。
    → 发射点:BaotouMonster 挂 **Bone_ L Hand** 骨(Baotou.prefab 实测 handTrans 父骨),BoneAttachment3D 懒建,兜底 (0,1.2,0)。
11. **rock_warrior 移速成长(工单 #11)**
    真值 `MonsterBase.cs:419-426`(树皮石头怪分支未设 MoveSpeedLevelRate → MonsterMeta 默认 0.3,MonsterMeta.cs:49)。
    → monster_meta.json rock_warrior.move_speed_level_rate 0.5 → **0.3**。
12. **中低项(monster 本体/数值)逐项**
    - #20 出生射线:`MonsterBase.cs:145-164`+`LevelBase.cs:231-234` → Monster.cs GetBornPosition(:300-323):
      3D forward(带俯仰)绕相机 up ±fov;命中退 0.5m;落点 `y -= CC.center.y; y += 0.1`,**取消贴地探测**(悬空出生,
      等待期 3m/s 落地,"空降"可见)。末尾加防嵌地抬升(注记 1)。
      **重大发现**:BornRayMask 0xFFFFFFF5 → **0xFFFFFFF1** 加排 layer3(UI 按钮)——隐藏的暂停/续币/胜利面板按钮
      (BackGameBtn 等)碰撞体在相机前 1m 仍激活,出生射线频繁打中 → 怪刷在 1m 贴脸(实测采样多次 dist=1.00 hit=BackGameBtn)。
      原作隐藏 UI 不参与物理射线。移交项 2 跟进根本修复。
    - #21 MaxDistance:meta 增 max_distance——骷髅/斧/牛=6(`SKMonster.cs:31`/`AxeMonster.cs:35`/`NmwMonster.cs:36`)、
      飞斧=20(`FlyAxeMonster.cs:38`);基类 UpdateActive 用 Info.MaxDistance,删 2000 常量字段。
    - #22 飞斧半径整数:`7 + GD.RandRange(0,3)`(原作 int 重载,7/8/9)。
    - #24 出生动画:Born 不再抢播 Idle02,改播控制器默认态(meta born_anim;空→idle_anim→"locomotion";
      宝箱 born_anim="Idle02" ≈ 原作 Treasure Chest Controller 默认态 "Idle",Godot 资产无同名 clip,注记 2);
      等待期每 Idle02Time(meta idle2_interval,默认 5)插播一次 idle2(CrossFade 0.3),播完回默认态。
    - #25 牛魔王无 Idle02:meta bull idle2_anim=""(nmw.controller 无 Idle02 状态),等待期保持 locomotion。
    - #26 攻击零混合:DoAttack `Anim.Play(anim, 0.0f)`(原作 `CrossFade(att,0)`,MonsterBase.cs:217)。
    - #27 死亡血花挂点:(0,1,0)→**Vector3.Zero**(原作 `MonsterBase.cs:283-284` localPosition=zero)。
    - 攻击计时点:LastAttackTime 移到 TriggerAttackEvent(原作 EventAttack 里 `lastAttackTime=Time.time`,MonsterBase.cs:225);
      飞斧/Boss 覆盖不再走时各自按原作(Boss BaotouSkill 里置位;飞斧不置位)。
    - 方法轨剥离:基类 _Ready 统一 `AnimTrackUtil.StripMethodTracks(Anim)`(clip 自带 event_attack/baotou_skill 方法轨
      与 0.3s 事件 Tween 重复;同 WolfMonster 既有先例)。

## 验证结果

- **构建**:`dotnet build` 0 错误(文件锁串行,4 个既有 warning)。
- **L1 自检**:18/18 PASS(新增 5 条断言,Level1.SelfTest:箱等待 20/20/20、Idle02Time 3/3/5、飞斧 hit_rate/max_distance、
  近战 max_distance 6×3、bull 无 Idle02)。
- **L3 自检**:17/17 PASS(wolf/fly_axe 路径回归无破)。
- **行为实测**(带日志窗口运行):牛魔王 21.2s 首刷(19s 开场+2s 冻结+首 tick 立即)→ 等待 3~8s → 站桩于出生距离
  (位置日志恒定,不再收脚钻相机)→ 每 ~5.3s 一击 15 血(hpR 100→85→70→55→40)→ 25s 超时回收 → 下一只立即刷。
  宝箱首刷局:原地 20s 不动不攻击,~41.5s 自灭后才刷下一只(与原作一致)。
- **截图**(tools/screenshots/audit/wave1_a/):
  `fix_melee_26s.png` 牛魔王正面贴脸居中可见、不钻相机(对照 l1u_intro_26s.png);
  `fix_melee_35s.png` 左下角攻击位(对照 l1u_battle_45s.png 构图)。
  注意:G0 出生点受移交项 1 的机位镜像问题影响,多数滚点被左墙遮挡,这两张为可见滚点;行为正确性以日志为准。

## 移交项(非我文件,已定位待处理)

1. **Battle0 机位视图与真值互为镜像**(重大发现,疑似机位换算公式问题):真值 l1u_gun_25s/l1u_intro_21s 左侧为
   开阔地+玻璃门、楼梯偏右;Godot 同机位左侧为整墙、楼梯居中。l1_visual 机位表的"自洽 ✓"验证的是公式内部一致性,
   未对真值像素。后果:G0 出生锥 ±33° 系统性打在左墙/柱 1.3~2.2m(采样 ~30 次全 <2.3m),怪常被墙挡;
   `Level1.OnMonsterBorn` 的 `x<0 → x+0.3`(原作 Level1.cs:148-151)在原坐标系是把怪推离墙,镜像环境下变成推向墙内。
   建议复核 Battle0 quat 换算(真值 (−0.0402,0.4903,0.0227,0.8703) → Godot (0.0227,0.8703,−0.0402,0.4903))
   与镜像约定。涉及 level1_battle.tscn CamPositions / Level1.cs,非我文件。
2. **UI 隐藏面板按钮碰撞体仍激活**(UiButton3D/InGamePanel,agent B/D):隐藏按钮在相机前 1m 挡世界射线;
   我已把出生射线 mask 排除 layer3 规避,但开枪射线(FireSystem RayMask 0xFFFFFFF7)仍会打到隐藏按钮
   (on_shot 误触发),建议隐藏时禁用碰撞。
3. 按工单划走的项:#15 掉血飘字(FireSystem.cs,agent B)、#14 血条厚度/白 tint(MonsterHpBar.cs,不在我清单)、
   #16 绿血颜色(impact_blood.tscn,agent D)、#29 胜利时机(LevelBase.CheckProgress 在我授权区域外,原作 HP≤0 起 2s)、
   #23 出生朝向俯仰(audit 自标"可不动")、#34 怪物共享默认音效(低)、#35 wolf 轨道前缀(L3 附带)。
4. 既有观察:自检里 `Relife()` 会真实扣并存档金币(×10→×9),非我改动,留给主 agent 知悉。

## 保真注记

1. GetBornPosition 末尾的"防嵌地抬升"(埋进脚下 1.6m 内地面才抬到地面表面)是 Unity CC.Move 自动 depenetrate 的
   等效补偿(Godot CharacterBody3D 不会自动推出嵌入),非原作显式代码;悬空出生/空降下落行为保持原作。
2. 宝箱 born_anim="Idle02" 为原作 controller 默认态 "Idle" 的最近似(Godot 宝箱动画库无 "Idle" clip;Idle02 为 1.33s 循环)。
3. 工单血条高度(牛 1.89/Baotou 1.72)与 prefab 推导不符:1.89=(0.36+1.532) 未乘 Bull 根缩放 0.8;1.72 出处不明。
   按 prefab 推导的世界高度 1.51/2.03 落实(与 monsters.md #12 建议值一致)。
4. 飞斧命中率难度倍率:工单/审计写 Hard×1.5/Hell×2(LevelMeta.cs 默认值),但 `LevelBase.GetLevelMeta()` 对 Level1
   覆盖为 **1.8/2.6**(LevelBase.cs:274-276),ZombieSkill 用的正是该字段 → 按真值取本关 level_meta diff_rate
   (level1=1.8/2.6,其余关 1.5/2.0,逐关正确)。
5. ChangePositionStar 是否含音效:Baotou.prefab 里为嵌套 prefab 的 stripped 引用,未解析到 AudioSource,保留
   teleport_flash.tscn 自带 teleport.wav,标待确认。
6. 飞斧再攻不受 AttackCD 约束(原作 Skill clip 无 EventAttack,lastAttackTime 恒 -99);audit"已核对一致"里的 CD5
   为数值层一致,行为层按原作代码。
7. 宝箱自灭不播 Die/血花(原作 HP=0 → Invoke DestorySelf 直接 DisActive);被打死才走 Hit→Die 演出+掉落。
8. #23 出生朝向俯仰未开(audit 标"观感影响小,可不动");FaceCamera 保持 yaw-only。
