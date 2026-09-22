# Unity 第一关战斗关卡机制规格书(移植真值表)

调研对象:`G:\test\FPSGame`(Unity 工程)。所有结论均标注出处(文件:行号);找不到的明确写"未找到"。

---

## 0. 关键澄清:第一关由两个场景组成

- **`Assets/Scenes/Level1.unity` = 开场剧情场景**(不是战斗场景)。
  - 挂 `StoryStart.cs`(Level1.unity:99674),播放 `StoryStartTimeline.playable`(全长 ≈51.88s,`Assets/Scenes/StoryStartTimeline.playable:44`),播放到 `duration-0.5` 或点 Skip 按钮 → `SceneManager.LoadScene("school_day")`(`Assets/Script/StoryStart.cs:22-34`)。
  - 场景内容:School_Hallway_Day 环境 + CM vcam1~10 运镜 + Canvas(Skip 按钮)+ 演员 prefab 实例(blade_girl、casual_dressed_girl、f05_schoolwear_200_m、RockWarrior 各 1)。主相机 FOV 45(Level1.unity:79890),雾关闭(m_Fog: 0),无天空盒。
- **`Assets/Scenes/school_day.unity` = 真正的第一关战斗场景**,挂 `Level1.cs`(school_day.unity:109253)。
- Build 列表顺序(ProjectSettings/EditorBuildSettings.asset):StartUp → school_night → DeviceConnection → LevelChoose → LoadingScene → Menu → Level4 → **Level1(剧情)** → **school_day(战斗)** → Level2 → Level3。
- 选关:LevelChoose 按钮名字即场景名(`Assets/Script/LevelChoose.cs:77-110`),按钮 "Level1" 加载剧情场景,剧情完自动进 school_day。选关先扣 1 枚游戏币(网络请求 ReduceCoinFps,LevelChoose.cs:131-150),再选难度(Easy/Hard/Hell,LevelChoose.cs:113-127)。
- Loading:`LoadingScene.cs` 最少 4 秒(`LoadingMinTime = 4`,LoadingScene.cs:27),进战斗场景前 `PlayerSystem.CurAliveMonster = 0`、`ResetPlayer()`(HP=100、子弹=MaxBullet)(LoadingScene.cs:35-42)。

---

## 1. school_day.unity 场景结构

### 1.1 渲染设置(school_day.unity:14-46)
| 项 | 值 |
|---|---|
| 雾 | 开启,线性(m_FogMode: 1),颜色 (0.581, 0.585, 0.430),Start 5 / End 12 |
| 环境光 | m_AmbientMode: 3(Flat 纯色),颜色白 (1,1,1) |
| 天空盒 | 无(m_SkyboxMaterial: fileID 0) |
| 太阳光 | 未指定(m_Sun: 0) |
| 烘焙 | 有 LightingDataAsset(guid 4a52afed…),烘焙开、实时 GI 关 |

### 1.2 相机
- 主相机 `Camera`(tag MainCamera,GO `&2981299812864342126`,school_day.unity:117023 附近):
  - 位置 **(1.35, 1.86, 22.44)**,旋转四元数 (-0.0009, 0.9986, -0.0189, -0.0485)(≈yaw 185°,看向 -Z 走廊)。
  - **FOV 30**,near **0.001**,far **100**,ClearFlags=2(纯色),背景 (0.569, 0.588, 0.427),Depth 0,HDR 开。
  - 挂 **CinemachineBrain**(默认 Blend = EaseInOut 2 秒;自定义 Blend 资产 `Assets/Scenes/camear/Level1.asset`)。
  - 子物体:
    - `FireTarget`(0,-0.1,0)(代码未检索到引用,疑似遗留);
    - `Cube`:**CameraWall**,layer 9,trigger BoxCollider 0.5×0.5×1、缩放 (16, 9, 0.1)、相机前 z=0.3(school_day.unity:117346-117400)——Boss 火球的碰撞目标;
    - `FireSystem`(PrefabInstance,localPos 0);
    - `InGamePanel`(PrefabInstance,localPos (0,0,1))。
- 虚拟相机(全是 CinemachineVirtualCamera,**FOV 全 45**,near 0.001 / far 20,LookAt = 开场女演员骨骼 "Neck",Follow=空,即固定机位+固定注视点):
  | 机位 | 位置 | 备注 |
  |---|---|---|
  | CM vcam1(StartVirtualCamer) | (1.35, 1.86, 22.44) | FOV 30,开场/待机镜头,战斗开始后关闭 |
  | CM vcam Battle0 | (-0.735, 1.0, -10.27),yaw≈58° | 第 1 波 |
  | CM vcam Battle1 | (-0.085, 1.051, -3) | 第 2 波 |
  | CM vcam Battle2 | (-0.085, 1.051, 3.01) | 第 3 波 |
  | CM vcam Battle3 | (-0.085, 1.051, 21.02) | 第 4 波 |
  | CM vcam Battle4 | (-0.085, 1.051, 60) | Boss 波 |
- 切镜头方式:目标 vcam `SetActive(false)→SetActive(true)` 让 CinemachineBrain 重新混合(`LevelBase.cs:72-76`),混合 2 秒内不刷怪(`Level1.cs:110-117`)。
- **没有任何屏幕震动/受击镜头动画**(全工程未见相关代码)。
- `LevelBase.InitCameraFOV()`(LevelBase.cs:37-46):相机 FOV≥50 时 InGamePanel 挂点 z=0.84,否则 z=1。本关 vcam FOV=45 → **z=1**。

### 1.3 灯光
- `Directional light`(环境内,激活):颜色 **(1, 0.965, 0.765) 暖色**,强度 **2**,软阴影,旋转四元数 (0.263, 0.345, 0.214, 0.875)。
- `Directional Light`(StoryLevel1GamePlay 子级):白色、强度 1、无阴影、**未激活**。
- 45 盏 Point light(走廊套件自带)。

### 1.4 环境模型
- 根对象 `School_Hallway_Day`(激活),套件 `Assets/AssetTools/SceneRes/Assets_School_Hallway/`,约 3200 个 PrefabInstance:走廊墙/窗/门/天花板/地板/立柱/楼梯(1F~3F)/储物柜/消防栓/标语牌/纸张等;`Add_effect`(SHW_Add_effect_r_01_01/02_evening 各 156 个)是玻璃/光效贴片。
- 开场 `_Env.SetActive(false)`,3 秒后 `ActiveEnv()` 强制显示(Level1.cs:70-79)。

### 1.5 场景顶层结构(StoryLevel1GamePlay 根)
```
StoryLevel1GamePlay
├── Timeline (初始 inactive; PlayableDirector → SchoolNightTimeline.playable)
├── 坏人Group (BadGroup, 初始 inactive): RockWarrior、士兵坏人、包头僵尸(骨骼内嵌女主 f05_schoolwear_200_m)
├── Level1 (挂 Level1.cs)
├── Level1Boss (Boss 组织节点; Baotou 位置 (0, 0.885, 66))
├── MonsterGroup0~3、MonsterGroup5 (怪物池组织节点)
├── Camera (主相机)
├── CM vcam1、CM vcam Battle0~4
├── BGMusic / BossMusic / StartMusic (AudioSource)
├── Directional Light (inactive)
└── EventSystem
```
(School_Hallway_Day 是另一个场景根。)

### 1.6 音乐(AudioSource,全部 Loop=1、PlayOnAwake=0、Volume=1)
| 字段 | 剪辑 | 时机 |
|---|---|---|
| StartMusic | `Assets/Sound/MusicBG/Tension.mp3` | 开场 timeline 期间 |
| BGMusic | `Assets/Sound/Level/Level1Ex.mp3` | 战斗 G0~G3 |
| BossMusic | `Assets/Sound/Level/Level1Boss.mp3` | G4(Boss 波) |
---

## 2. 关卡流程(Level1.cs + LevelBase.cs 完整状态机)

### 2.1 启动(Level1.cs:32-71)
- Debug 分支(`GlobalObject.IsDebug`,常量 false,GlobalObject.cs:69):跳过剧情直接开战。正常流程不走。
- 正常分支:BadGroup 激活(开场演坏人)、FireSystem 关闭、Timeline 激活、环境隐藏、`_Direct.Play()`、StartMusic 播放;3 秒后环境强制显示。
- 玩家创建:由 `InputManager.SetInputMode` 触发 `PlayerSystem.Ins.CreatePlayer()`(InputManager.cs:262-286),`Player.Born()`:HP=100、子弹=UserData.MaxBullet(默认 **120**,UserMeta.cs:29)、**三把枪 HandGun/AK47/M4 全部解锁、默认 HandGun**(Player.cs:43-59)。

### 2.2 开场 → 战斗(Level1.cs:89-107)
- timeline 播放到 **_Direct.time > 19 秒**:_Direct.Stop()、Timeline 隐藏、环境显示、**FireSystem 激活**、`PlayerSystem.Ins.Show()`、StartVirtualCamer 关闭、BadGroup 隐藏、`curGroupState=0`、`StartMonsterGroup(0)`、StartMusic 停、BGMusic 起。
- 开场 timeline 内容(SchoolNightTimeline.playable):RockWarrior 跑步动画、包头僵尸抱人/换人抱动画(`Assets/Actor/Motion/包头僵尸报人.anim`、`换人抱.anim`)、尖叫音效 `Sound/尖叫4.mp3`、Cinemachine 运镜、Activation 轨道。**剧情 = Boss 掳走女生**。

### 2.3 波次循环(Level1.cs:138-182,LevelBase.cs:54-256)
- `StartMonsterGroup(g)`:按难度取 `groupMonsterNum = GroupMonsterNum[g] × DiffRate`(取整),`timeInternal = GroupMonsterInternal[g] ÷ DiffRate`;切换对应 Battle vcam;`IsCameraTransform=true` 2 秒(此间 Update 直接 return,不刷怪)。
- 刷怪(UpdateMonsterBorn,LevelBase.cs:207-244):每隔 `timeInternal` 秒尝试刷 1 只:
  - `FindReadyMonster`(LevelBase.cs:133-205):
    1. 场上存活数 `PlayerSystem.CurAliveMonster` ≥ maxAlive(=`GroupMaxAlifeNum[g]×DiffRate` 取整)→ 不刷;
    2. monsterLeft>0 时:先掷 `GroupBulletBoxRate`(5%)→ 若 BoxBullet 未激活则出**子弹箱**;否则掷 `GunRate`(Level1 未设置,用默认 **0.02**,LevelMeta.cs:15)→ 50/50 出 **AK 箱或 M4 箱**(若对应箱未激活);
    3. 否则从本波怪物池随机起点找第一个 inactive 的怪(**对象池复用**,不实例化);
  - 出生位置(`MonsterBase.GetBornPosition`,MonsterBase.cs:145-164):相机 forward 绕 Y 轴随机偏 ±maxFov 度(g0 默认 33°、相机 FOV>50 时 40°;**g1~g4 被场景覆盖为 15°**,Level1 组件 GroupMaxBornFov={1:15,2:15,3:15,4:15},school_day.unity:109300-109350);从相机沿该方向射线(掩码排除 Enemy 层和 layer 9),最长 **8m**(LevelBase.cs:214);命中点回退 0.5m;未命中取 8m 处。然后 `pos.y -= CharacterController.center.y; pos.y += 0.1`。
  - 飞斧覆盖:`GetBornPosition(15, 12)`(FlyAxeMonster.cs:27-30)——±15°、12m。
  - 出生:`m.born()`(HP 回满、CurAliveMonster+1、25 秒自毁定时)→ SetActive(true)→ `monsterLeft -= 1`(**宝箱也占刷怪配额**)。
- 出生后修正(Level1.cs:140-168):怪物 LookAt 相机;**小骷髅和斧头僵尸** y+0.2、x<0 时 x+0.3;按难度改 `WaittingTime`(出生后原地等待):**Easy 3~8s / Hard 0~2s / Hell 0**;`ResetMaxLifeTimeDeath(25)`——注意 bug:实现忽略入参,恒为 25 秒(MonsterBase.cs:73-77)。
- 波次完成判定(IsCurGroupFinished,LevelBase.cs:246-256):场上存活=0 且 monsterLeft≤0 → 下一波。
- **G4(Boss 波)**:`InitBoss()` —— Baotou.born()+SetActive,BGMusic 停、BossMusic 起(Level1.cs:175-180)。Boss 波同时正常刷 G4 小怪(20 只)。
- **胜利**:`curGroupState==4` 时每帧检查 `Baotou.HP <= 0` → `Invoke("FinishLevel", 2)` → `InGamePanel.Ins.Victory()`(Level1.cs:119-125,184-187)。细节:HP≤0 后每帧重复 Invoke,但 Victory() 首次执行后 timeScale=0,后续 scaled-time Invoke 不再触发;Victory() 内还有 `SceneState != Battle` 守卫。
- **失败**:无关卡级失败条件;玩家 HP≤0 → CheckDead → `OpenContinue(false, side)` 复活面板(PlayerSystem.cs:247-260)。
- **结算**:`Victory()` 仅 timeScale=0 + 显示 SuccessPanel(InGamePanel.cs:205-218);面板只有 **MenuBtn → BackToMenu()**(LoadScene("Menu"),1 秒防误触,InGamePanel.cs:30-41)。**未找到任何星星/分数/金币结算代码**(LevelStateData.Star 全工程只有读取没有写入)。**没有"下一关"按钮**。
### 2.4 关卡数值表(LevelBase.GetLevelMeta,LevelBase.cs:272-300,LevelId=1)
| 波 | 怪物数(Easy) | 间隔(s) | 同屏上限(Easy) | 宝箱率 |
|---|---|---|---|---|
| G0 | 10 | 5 | (int)(1.5×1)=**1** | 0.05 |
| G1 | 10 | 3 | 2 | 0.05 |
| G2 | 15 | 3 | 2 | 0.05 |
| G3 | 15 | 3 | 2 | 0.05 |
| G4 | 20 | 3 | 3 | 0.05 |

- 难度倍率:Easy ×1 / Hard ×1.8 / Hell ×2.6(乘数量与上限、除间隔)。例:Hell G4 = 52 只、间隔 ≈1.15s、同屏 7。
- 总怪物量 Easy = 70 只次(池子小于数量,循环复用)。

### 2.5 每波怪物构成(场景序列化实测,school_day.unity:109420-109455)
| 波 | 对象池 | 相机 | 相机 z |
|---|---|---|---|
| G0 | 5× **Bull(牛魔王)** | Battle0 | -10.27 |
| G1 | 5× **斧头僵尸** | Battle1 | -3 |
| G2 | 3× **飞斧头僵尸** | Battle2 | +3.01 |
| G3 | 2× 斧头僵尸 + 4× 飞斧头僵尸 | Battle3 | +21.02 |
| G4 | 4× **小骷髅**(其中 2 只 MonsterLevel=1)+ 2× 飞斧头僵尸 + **Boss 包头僵尸** | Battle4 | +60 |
- 宝箱(全局 3 个,不属任何波):**BoxAK(注意:是 BoxBullet.prefab 改名为 BoxAK 的实例,`_Type=1`,不是 BoxAk.prefab)**、BoxM4、BoxBullet;初始全 inactive,WaittingTime=20。
- MonsterGroup2/3 序列化数组含 `fileID: 0` 空槽(G2 六槽三空、G3 八槽两空);Unity 反序列化为 null,`FindReadyMonster` 里 `mPool[index].gameObject` 遇 null 理论上会抛异常——移植时跳过空槽即可。
- 怪物初始摆放位置仅编辑器参考,出生时被 GetBornPosition 重定位。
- 怪物默认初始 m_IsActive=0(池化待命)。

---

## 3. 怪物详细规格

通用基类 `MonsterBase`(`Assets/Script/Monster/MonsterBase.cs`):
- 出生 `born()`:HP=GetMaxHP()、按 _meta.AttackType 设攻击属性(0 物理/1 冰/2 毒)、CurAliveMonster+1、CharacterController 启用、25 秒未打死自动消失(BOSS 除外,IsBoss=1 跳过,MonsterBase.cs:62-71)。
- 等待 `UpdateWaitting("Idle02")`:WaittingTime 内原地(每 Idle02Time 秒播一次 Idle02 小动作),持续 3m/s 下坠;等待结束才行动(MonsterBase.cs:91-110)。
- 近战 `MoveToPlayerAndAttack(IdleName, maxDistance)`:距相机(水平)< AttackRadius 且在 Locomotion 状态且过了 AttackCD → 随机播 attackNames 之一;**伤害由动画事件 `EventAttack` 触发**(MonsterBase.cs:221-227):按怪物屏幕左/右半打右/左手玩家。否则以 GetMoveSpeed 走向相机(1m/s 重力、转身 TurnSpeed);**距目标 3m 内移速钳到 1**(MonsterBase.cs:242-248)。
- 受击 `Hit()`(MonsterBase.cs:274-314):HP -= 枪攻击(10);死亡 → 挂 Blood.prefab 特效、CrossFade 死亡动画(0.1s)、DieSound、CC 禁用、**1.5 秒后 DestorySelf**(CurAliveMonster-1、SetActive false);未死 → CrossFade 受伤动画 + HurtSound。返回命中特效 tag。
- MonsterLevel 成长:HP +5×Lv、攻击 +5×Lv(SK/Nmw)、移速 +0.3×Lv、CD -0.5×Lv。
- **HitType(头/身体)无实际作用**:所有 prefab 的 BoxHead.HitType 都是 0(Body),`MonsterBase.Hit` 不读 hType。无爆头加成。
- 血条:斧头/飞斧/骷髅/Baotou 嵌套 `Monster/HpReduceNumber.prefab`(世界空间 Canvas+Slider+MonstHp 脚本);Bull 内嵌同款 MonstHp Canvas。每帧 LookAt 相机,掉血飘 "- N"(上升 5px/s、透明度 1→0.3 淡出,MonstHp.cs:43-85)。宝箱无血条。
### 3.1 怪物数值表(数值 = prefab 序列化 _Name 经 GetMeta() 得到的真实值)

| 怪 | prefab | HP | 攻击 | 移速 | 转身 | 攻击半径 | 攻击CD | 行为 | 受伤/死亡动画 | 命中特效tag | CC 高/半径 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 小骷髅 | MonsterSkeleton.prefab | 30(+5/lv) | 15(+5/lv) | 1(+0.3/lv) | 2 | 2 | 5(-0.5/lv) | 等待→走向玩家近战(maxDist 6) | Damage1 / **Damage4** | Concrete | 1.2 / 0.25 |
| 斧头僵尸 | MonsterAxeZombie.prefab | 30 | 15 | 1 | 2 | 2 | 5 | 同上,攻击动画 Attack1/2/L/R 四随机 | Damage1 / Damage3 | Blood(绿血) | 1.8 / 0.3 |
| 飞斧头僵尸 | FlyAxeMonster1.prefab | 30 | 15 | 1 | 2 | **7+rand(0,3)** | 5 | 远处停留,Skill 动画扔斧 | Damage1 / Damage3 | Blood | 2 / 0.3 |
| 牛魔王(G0) | Bull.prefab | 30 | 15 | 1 | 2 | 2 | 5 | 近战;死亡后动画速度×2 | damage / die | Concrete | 1 / 0.5 |
| 牙齿宝箱 | BoxBullet/BoxM4.prefab(BoxAK 为改名实例) | **1** | 不攻击 | 3 | 2 | — | — | **逃跑**:每 2s 跳起远离玩家;20s 自灭(HP=0 无掉落) | TakeDamage / Die | Blood | 0.4 / 0.4 |
| 包头僵尸(Boss) | Baotou.prefab | **100** | 15 | 不走位 | — | — | 5 | 定点 z=66;每 5s anim_attack → 动画事件 BaotouSkill → 发火球;**被打 0.5s 后瞬移**(±1.8m X, ±2m Z)+星星特效+重置攻击计时 | Hurt / anim_death | Concrete | 0.2 / 0.2(仅重力) |

模型/动画控制器:小骷髅=MonsterSkeleton.FBX(globalScale 0.01)/MonsterSkeletonAnimator.controller;斧头&飞斧=MonsterAxeZombie.FBX(0.01)/MonsterAxeZombieAnimator.controller;牛魔王=bull_king@*.FBX(globalScale 1)/nmw.controller;宝箱=Treasure Chest Monster.FBX/Treasure Chest Monster Controller;Boss=Chr_Zcharacter_01.FBX/Chr_Animator Controller。

补充说明:
- **斧头僵尸/牛魔王/小骷髅的 _Name 序列化值都是 0(小骷髅)**,GetMeta() 小骷髅分支为空 → 三种怪全用 MonsterMeta 默认值(HP30/攻15/速1/转身2/半径2/CD5)。AxeMonster 构造函数写 `_Name=斧头僵尸` 但被 prefab 序列化覆盖(AxeMonster.cs:20 vs MonsterAxeZombie.prefab 中 `_Name: 0`)。移植按默认值即可。
- 飞斧 `_Name: 2`、宝箱 `_Name: 3`、Baotou `_Name: 4` 生效(MonsterBase.cs:329-344)。
- 飞斧扔斧(ZombieSkill.cs + ProjectileEx.cs):Skill 动画事件 TakeHandAxe(0.224s)/ThrowAxe(0.600s);命中率 `HitRate=0.15`(Hard×1.5、Hell×2),未命中目标点偏移 3~6m;斧头 `ProjectileAxe.prefab`,**速度 2m/s**、寿命 10s、自旋;命中判定 = 进入相机空间 |z|<0.5 且 |x|<1 且 |y|<1 → 按屏幕左右分边扣血(ProjectileEx.cs:96-111)。斧头无碰撞体,纯距离判定。
- Boss 火球:`Monster/BaotouFireball.prefab` 嵌套 Realistic Effects Pack `Fireball1.prefab`(EffectSettings,LayerMask=512 即仅 layer 9 CameraWall,DeactivateAfterCollision=1)。从手部脱父飞行;撞上相机前 16×9 隐形墙 → 爆炸音(FireExplosion4)+ 0.1s 后 `HitPlayer(Both)` **双手同时扣 15**(BaotouFireball.cs:41-51)。
- **Boss 本体 tag 是 Untagged,子弹打不到**;弱点是身旁悬浮的 `FX_Pickup_Heart_01`(爱心模型,tag 覆盖为 Enemy,BoxCollider 0.5³,挂 BoxHead 转发到 BaotouMonster)(Baotou.prefab:6994-7004、96-122)。**打爱心=打 Boss**。
- 层约定:layer 8 = Enemy,layer 9 = CameraWall(ProjectSettings/TagManager.asset)。
- 近战命中帧示例:MonsterAZ@Attack01 的 EventAttack 在 0.335s(MonsterAZ@Attack01.FBX.meta:299-300)。

---

## 4. 开火系统(FireSystem.cs + Guns/*)

### 4.1 结构(Guns/FireSystem.prefab,挂主相机下)
- 左右手各 3 把枪(嵌套 prefab 实例):右手 AK47Right/M4Right/HandGunRight,左手 AK47Left/M4Left/HandGunLeft;初始全 inactive,`CreateGun` 按当前枪激活并挂为相机子物体(FireSystem.cs:106-146)。
- 枪挂点(`Assets/Resources/firemetajson.json`):

| 枪(type) | pos(FOV≤50) | pos60(FOV>50) | FireCD | 攻击 | 耗弹/发 | 枪声 | 模型 |
|---|---|---|---|---|---|---|---|
| AK47(0) | (0.10, -0.04, 0.16) | (0.12, -0.045, 0.16) | **0.2s** | 10 | 1 | AutoGun_3p_01.wav | AK-47.fbx |
| M4(1) | (0.10, -0.05, 0.16) | (0.12, -0.045, 0.16) | **0.1s** | 10 | 1 | AutoGun_3p_01.wav | ColtM4.fbx |
| HandGun(2) | (0.10, -0.04, 0.16) | (0.12, -0.045, 0.16) | **0.5s** | 10 | 1 | AutoGun_1p_01.wav | Handgun.fbx |

- 左手枪 pos.x 取反(FireSystem.cs:137)。三把枪攻击都 10、每发 1 子弹,**区别仅射速/音效/模型**;枪 prefab 内含 MuzzleFlash(火光,Fire 时 SetActive 闪烁)、Flame/Smoke 粒子、Point light、legacy Animation(开火后座动画)。
- 激光:LazerRight/LazerLeft = **LineRenderer (0,0,0)→(0,0,200)**,材质 `FPS Pack/Materials/Effects/Lazer.mat`,**红色 _TintColor (0.99, 0, 0, 0.82)**;CreateGun 时挂到枪下(FireSystem.cs:121-123)。
- 命中红点:Flash1=`Effect/Flash.prefab`(右手)、Flash2=`Effect/FlashGreen.prefab`(左手)(Resources/GlobalObject.prefab 嵌套)。每帧射线命中后:位置 = hit.point - dir×Offset,Offset = 0.5 - clamp01(1/len)×0.40;缩放 = 1 - clamp01(3/len)×0.8(越近越小);未命中隐藏(FireSystem.cs:256-290)。
- 特效池:_BloodEffects=Blood.prefab ×3(死亡爆血,挂怪身上);EnemyBullet ×5(Level1 未用,ToonMonster 才用);命中特效轮换池:**Wood=WoodImpact×3、Metal=MetalImpact×3、Blood=GreenImpact×3(绿血!)、Dust=RockImpact×3、Concrete=ConcreteImpact×4**(均 AssetTools/FPS Pack/Prefab/Impacts/Mobile/)。
- 音效节点:HurtSound1=`Sound/DefaultHurt.ogg`(怪受伤默认)、DieSound1=`Sound/ZombieHorrorPackageFree/MP3/VO/Zombie01/Zombie001_Hurt_A_002.mp3`(怪死亡默认)、ShootSound1=`Sound/Bullet Flybys/Bullet Flyby 7.wav`。

### 4.2 每帧流程(FireSystem.cs:213-381,LateUpdate)
1. 该手玩家 **Frozen(冰冻)** 且非暂停 → 跳过(不能瞄准/开火)。
2. 枪 localRotation = `GetRingRotation()/GetLegRotation()`:体感原始四元数 × forward,把 z 分量改写为 **PhoneMoveRate=0.5**,再 FromToRotation(forward, v3) —— **减幅瞄准映射**(GlobalObject.cs:83-95)。
3. 射线:从**枪口位置**沿枪 forward,**2000m**,掩码 `~(1<<9)`(**排除 CameraWall**,其他全打)(FireSystem.cs:36,258)。
4. 扳机 `GetCurKeyRing/Leg()`(电平,按住连发):
   - 命中 tag "Button" → 直接 `Button.onClick.Invoke()`(**UI 按钮是被开枪触发的**);
   - 否则 `gb.Fire()`:CD 检查 → `UseBullet(1)` → 成功则火光闪+动画+枪声(M4Gun.cs:17-49,AK/HandGun 同构);
   - Fire 成功才结算命中:tag "Enemy" → `MonsterBase.Hit()`(经 BoxHead 转发);按返回 tag 取命中特效,缩放 = flashScale×3,放命中点、朝法线,SetActive(false→true) 重播;**打中怪时关闭特效的 _Dankon(弹痕)子物体**,打场景时打开(ImpactController 只有这一个字段);
   - Fire 失败且因子弹不足 → `InGamePanel.OpenContinue(true, side)` 弹"兑换子弹"面板(暂停);
   - 每次开枪后 `PlayerSystem.Ins.UpdateUI()` 刷新子弹数。
5. 换枪:OnKey2_Ring/Leg 事件(或键盘 **LeftAlt 抬起**=右手)→ `Player.NextGun()`(按 GunType 枚举顺序循环找已解锁枪)→ `ChangeGun` 协程:枪沿 z 每 0.05s 退 0.01×10 步(0.5s 收枪)→ 换枪 → 再伸出(≈0.55s)。**总耗时约 1.05 秒**(FireSystem.cs:148-178)。
---

## 5. 玩家系统(PlayerSystem.prefab,嵌在 Resources/GlobalObject.prefab 内,DontDestroyOnLoad 常驻)

- 双手玩家 RightPlayer/LeftPlayer(Player 类):HP 100、子弹初始=UserData.MaxBullet=**120**(Debug 90)、枪械位掩码(Born 时 HandGun|AK|M4 全开)。
- 受击 `HitPlayer(mb, side)`(PlayerSystem.cs:187-245):按怪物屏幕左右半决定打哪只手(单手模式 fallback 到激活手);`HurtPlayer`:
  - 随机播受伤音(右手 6 个 AudioSource / 左手 2 个);冰属性附加 IceAS;
  - HP -= 怪攻击(15);
  - **红屏**:HurtEffectRight/Left 红色 Image(1,0,0,0.392),透明度从 1 每 0.1s 减 0.05(≈2 秒淡出)(PlayerSystem.cs:262-300);**冰→青色+Frozen(该手冻结不能开火直到淡出结束);毒→绿+Poison,淡出结束再扣一次血**;
  - 0.5s 后 CheckDead → HP≤0 → OpenContinue 复活面板(timeScale=0):消耗 1 游戏币 `Relife()`(HP=100、子弹+=120);币不足按钮显示"币不足";面板带倒计时(每 180s +1 币,上限 10,InGamePanel.cs:176-201)。
- 金币:UserData.Coin 默认 10/MaxCoin 10/AddCoinTime 180s(UserMeta.cs:25-27);进关卡扣 1 币;**打怪不给金币/分数**(金币只来自时间恢复/服务器)。
- 宝箱掉落:子弹箱 → `AddBullet(60)`(UserData.BoxBullet=60);枪箱 → `AddGun(AK|M4)`;加子弹/回血有图标放大 1.5 倍+逐帧增长动画(PlayerSystem.cs:403-516)。
- UI 模式 `UpdateUIMode()`:战斗场景显金币+子弹+头像+HP;Menu/LevelChoose 只显金币;Loading/DeviceConnection 全隐藏;并同步 `GlobalObject.SceneState`(PlayerSystem.cs:322-364)。

---

## 6. HUD / UI

### 6.1 PlayerSystem 常驻 HUD(ScreenSpaceOverlay Canvas)
- Coin(金币数)、BulletRight/BulletLeft(子弹数字)、HeadRight/HeadLeft(头像 Icon + HP Slider,值=HP/100)、RightHurt/LeftHurt 红屏图。
- 头像来自 `Resources/head/Icon{N}`(UserData.HeadIcon)。

### 6.2 InGamePanel(战斗内面板,UI/InGamePanel.prefab)
- **WorldSpace Canvas 1280×720**,挂主相机下 z=1,根缩放 **(0.00115, 0.00115, 0.01)**(≈1.47m×0.83m 位于 1m 处),渲染相机引用 `Guns/Camera.prefab`(FOV 30)。
- 元素:
  - `PauseBtn` → OpenPause(timeScale=0,IsGamePause=true);
  - `Pause` 面板:BackGameBtn(BackToGame)、MenuBtn(BackToMenu);
  - `Continue` 面板:复活/兑换子弹共用,标题 "继续游戏"/"兑换子弹",ConfirmBtn(ContinueGame,网络扣币后 Relife)、MenuBtn、倒计时 TimeText;
  - `SuccessPanel`(VectoryPanel):胜利面板,**只有 MenuBtn → BackToMenu**;
  - 所有按钮 onClick 附加 `PlayMenuSound()`(Sound/UI.mp3)。
- **没有准星 UI**——准星 = 3D 激光线 + 命中红点 Flash。
- 按钮触发:场景/UI 按钮带 collider、tag "Button",FireSystem 射线命中+扳机 → onClick(战斗中 PauseBtn 这么按下)。

### 6.3 怪物血条
见第 3 节。`ToonMonstHp.cs`(屏幕空间投影版)Level1 未使用(仅 Alien/ToonSolder 用)。

---

## 7. 特效与音频清单

| 用途 | 资源 | 触发 |
|---|---|---|
| 枪口火光 | 枪 prefab 内 MuzzleFlash(+Flame/Smoke/Point light) | 每次 Fire 成功 SetActive 闪 |
| 激光瞄准线 | LineRenderer + Lazer.mat(红 0.99,0,0,0.82) | 玩家激活时常显 |
| 命中红点 | Effect/Flash.prefab(右)/ FlashGreen.prefab(左) | 射线命中时每帧 reposition |
| 命中-血(怪) | GreenImpact ×3(绿) | tag Blood(斧头/飞斧/宝箱) |
| 命中-混凝土 | ConcreteImpact ×4 | tag Concrete(骷髅/牛魔王/Boss) |
| 命中-木/金属/尘土 | WoodImpact/MetalImpact/RockImpact 各×3 | 按场景物体 tag |
| 死亡爆血 | Effect/Blood.prefab ×3 | 怪死亡挂身上 1.5s |
| Boss 瞬移星星 | Baotou.prefab 内 ChangePositionStar | Boss 被打后 |
| Boss 火球+爆炸 | Fireball1.prefab + FireExplosion4 音效 | Boss 每 5s |
| 受击红屏 | PlayerSystem Canvas 内红色 Image | 玩家掉血 |
| 枪声 | AutoGun_3p_01.wav(AK/M4)、AutoGun_1p_01.wav(手枪) | 每发 |
| 怪受伤/死亡 | DefaultHurt.ogg / Zombie001_Hurt_A_002.mp3(prefab 未覆盖时用) | Hit |
| 玩家受伤 | PlayerSystem.prefab 内 6+2 个 AudioSource(剪辑未逐一解析) | HitPlayer |
| BGM | Tension.mp3(开场)→ Level1Ex.mp3(战斗)→ Level1Boss.mp3(Boss) | 见 2.2/2.3 |
| 胜利音 | **未找到**(Victory 不播音效;Sound/Jingle_Win_00.mp3 存在但 Level1 未引用) | — |
| 尖叫(剧情) | 尖叫4.mp3 等 | 开场 timeline |

材质 blend:命中特效均来自 "FPS Pack"/"Realistic Effects Pack" 粒子包,未逐一解析 shader;Lazer.mat 用自定义 shader(guid a0feb4c7…)。Godot 侧按加色/透明粒子近似即可。
---

## 8. 输入映射(InputManager.cs)

- 体感枪 UDP 协议:首字节 'R'(右手戒指)/'L'(左手腿环),随后 float x/y/z/w 四元数 + int 模拟量 + byte k(扳机)+ byte k2(换枪)(InputManager.cs:319-456)。
- **扳机 = 电平**:`GetCurKeyRing()` 返回 k==1(按住每帧 true,配合 FireCD 连发);**换枪 = 边沿事件** OnKey2。
- 心跳:每秒向设备发 "Heart";2 秒无包判掉线。
- 无实体枪(ControllerOrRight 模式):**方向键移准星**(yaw/pitch ±45° 内,每帧 0.5°),Enter/Joystick 确认 = 扳机,LeftAlt 抬起 = 右手换枪(FireSystem.cs:207-210)。
- 掉线时准星平滑回中:每帧把角速度差 ×dt×0.1 回退(InputManager.cs:84-122)。
- 暂停(timeScale=0)时仍可开枪触发 Button(代码不挡);Frozen 手在非暂停时直接 return。

---

## 9. 镜头

- 开场 ~19s timeline 运镜(SchoolNightTimeline:Cinemachine 轨道 + 石头人跑/抱人动画 + 尖叫)。
- 战斗 = **固定机位序列**:5 个 Battle vcam 沿走廊 z=-10.27→60 排开,全 FOV 45、LookAt 固定点;切波时 CinemachineBrain 2s EaseInOut 混合,这 2s 不刷怪。
- 无屏幕震动;枪后座靠枪自身 legacy Animation。
- 主相机 FOV 30 与 vcam FOV 45 的差异影响 UI 挂点(InitCameraFOV,FOV≥50 才用 0.84)。

---

## 10. 移植时最容易漏掉的 10 个细节

1. **场景对应关系**:战斗关卡是 school_day.unity 而非 Level1.unity;Level1.unity 是 ~52s 剧情场景,结束自动进 school_day;选关按钮 "Level1" 进的是剧情场景。
2. **怪物数值几乎全部来自 MonsterMeta 默认值**(HP30/攻15/速1/转身2/半径2/CD5),因 prefab 序列化 `_Name=0` 覆盖了构造函数——不要按类名猜数值;例外:飞斧(半径 7+rand3)、宝箱(HP1/速3)、Boss(HP100/CD5)。
3. **出生算法**:相机 forward 随机偏 ±15°(g1~g4;g0 为 33°/40°)射线打场景 8m 找落点(命中点回退 0.5m、y 按 CC.center 调整 +0.1);**宝箱/枪箱占刷怪配额**(先掷 5% 子弹箱、再 2% 枪箱,且要求对应箱当前 inactive);同屏上限 Easy G0 只有 1 只(1.5 取整)。
4. **25 秒强制消失**:非 Boss 怪出生 25s 后自动消失(存活数-1);`ResetMaxLifeTimeDeath(25)` 入参被忽略恒为 25;宝箱 20s 自灭不掉落。
5. **胜利只看 Boss HP**,G4 小怪不用清;胜利面板只有"回菜单",无结算数值、无下一关。
6. **Boss 弱点是悬浮爱心**(FX_Pickup_Heart_01,tag Enemy + BoxHead 转发),Boss 本体 Untagged 打不到;Boss 被打 0.5s 后瞬移(±1.8/±2m)并重置攻击 CD;火球撞相机前 0.3m 的 16×9 隐形 CameraWall(layer 9,trigger),命中双手各扣 15。
7. **枪全部攻击 10、耗弹 1,只区分射速**(AK 0.2s/M4 0.1s/手枪 0.5s);初始三枪全解锁、默认手枪;换枪有 1.05s 收/出枪动画;子弹 120,打完弹"兑换子弹"暂停面板(扣 1 币)。
8. **准星不是 UI**:枪上 LineRenderer 激光(红、200m)+ 命中点 Flash(scale=1-clamp01(3/len)×0.8,offset=0.5-clamp01(1/len)×0.4);UI 按钮靠射线+扳机触发(tag "Button")。
9. **怪物伤害由动画事件 EventAttack 触发**(非碰撞),按怪物屏幕 x 决定打左/右手;受击反馈 = 红色全屏 Image 2s 淡出;冰=青+冻结该手、毒=绿+二次扣血。
10. **固定机位+2s 混合**:切波镜头混合期间不刷怪;主相机 FOV30 vs vcam FOV45 影响 UI 挂点;无任何镜头震动。

其他易漏:怪物 CC 尺寸(骷髅 1.2/0.25、斧 1.8/0.3、飞斧 2/0.3、宝箱 0.4/0.4)决定命中判定;怪物距玩家 3m 内移速钳 1;命中特效打怪时关闭弹痕子物体(_Dankon);"血"特效其实是绿色 GreenImpact;InGamePanel 世界画布缩放 (0.00115,0.00115,0.01);进关扣 1 币 + Loading 最少 4s;玩家受伤音效右手 6 个/左手 2 个随机。

---

## 11. 附录:重要文件清单

- 关卡:`Assets/Script/LevelS/Level1.cs`、`LevelBase.cs`(`MonsterGroup.cs`、`FireWindow.cs` 是空壳,不参与逻辑)
- 怪物:`Assets/Script/Monster/{MonsterBase,SKMonster,AxeMonster,FlyAxeMonster,NmwMonster,BaotouMonster,BoxMonster}.cs`、`Assets/Enemy/斧子僵尸/CommonResources/Scripts/ZombieSkill.cs`、`Assets/Enemy/斧子僵尸/CommonScripts/ProjectileEx.cs`、`Assets/BaotouFireball.cs`、`Assets/Script/BoxHead.cs`
- 开火:`Assets/Script/FireSystem.cs`、`Assets/Script/FireSystem/{GunBase,M4Gun,AKGun,HandGun,ImpactController}.cs`、数值 `Assets/Resources/firemetajson.json`
- 玩家/UI:`Assets/Script/PlayerSystem/{PlayerSystem,Player}.cs`、`Assets/Script/InGamePanel.cs`、`Assets/Script/MonstHp.cs`、`Assets/Script/{LoadingScene,LevelChoose,MenuController,StoryStart}.cs`
- 全局:`Assets/Script/Utils/{GlobalObject,InputManager,DataManager,Utils}.cs`;Meta:`Assets/Script/Meta/{MonsterMeta,LevelMeta,FireMeta,GunInfo,UserMeta}.cs`
- 场景/资源:`Assets/Scenes/{school_day.unity,Level1.unity}`、`Assets/Scenes/{SchoolNightTimeline,StoryStartTimeline}.playable`、`Assets/Scenes/camear/Level1.asset`(Cinemachine 混合)、环境套件 `Assets/AssetTools/SceneRes/Assets_School_Hallway/`
