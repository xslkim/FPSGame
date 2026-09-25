# Level1 开场 19s 演出(SchoolNightTimeline)真值规格书

调研日期:2026-06-10 · 调研角色:只读调研(未改任何游戏文件)
真值来源:`G:\test\FPSGame\Assets\Scenes\SchoolNightTimeline.playable`(1257 行 YAML 全读)、
`G:\test\FPSGame\Assets\Scenes\school_day.unity`(129457 行,Transform 全量脚本合成世界坐标)、
`Assets\Actor\Motion\*.anim`(humanoid 剪辑,RootT/RootQ 已脚本提取)、真值截图 `tools/screenshots/l1u_intro_3s/8s/15s.png`、`l1u_gun_8s/10s.png`。
Godot 现状:`game/src/Battle/IntroBadGroup.cs`(221 行全读)、`game/src/Battle/Level1.cs`。

**换算约定**(沿用 README 保真注记 + level1_deviation_log.md #11/#12):环境已整体 X 镜像;位置沿用 Unity 原值(x 不取负);朝向四元数 (x,y,z,w) 先做镜像 (qy,qz 取反),再绕 Y 轴 +180°(Unity +Z 前 → Godot −Z 前),两步合并公式:**(x,y,z,w) → (z, w, −x, −y)**。

---

## 0. 一句话结论

Godot 版 IntroBadGroup 的**组根运动公式、音频时点、FOV、19s 截停流程均已正确**;错在三件事:
1. **演员朝向反 180°**(截图铁证:Godot 8s 石头人背对相机,真值为正面特写)——`BuildWalker`/`BuildSoldier` 里 `model.Rotation = (0, π, 0)` 应去掉;
2. **背负关系全系臆造**:真值只有两名被抱者——f05 女生挂**包头僵尸 `Bone_ R UpperArm`**、剑女孩挂**士兵 `Bip001 L UpperArm`**;**RockWarrior 背上没有任何人**;
3. **LookAt 目标错**:真值全程盯**被抱女生的 Neck 骨**,Godot 盯的是 RockWarrior 节点 +1.35m——这是 audit #9"10s 相机钻进石头人躯干"的直接原因。

材质侧:f05 未做代码覆盖(FBX 丢贴图→纯白腿)、士兵贴图 `Militias_B.tga` 未导入 Godot、士兵武器应为 `weapon_deserteagle`(现为不可见的 rpg_rocket→空手)、武器与身体贴图不同不能一把盖。

---

## 1. Timeline 逐轨解析(SchoolNightTimeline.playable)

Timeline 资产 `&11400000`,60fps(playable:798),共 10 轨(playable:786-795)。
导演组件:PlayableDirector 挂在场景 GO **"Timeline"**(`&2981299811354017063`,school_day.unity:108760),初始 **m_IsActive: 0**(:108765),WrapMode 2(Hold);绑定表 m_SceneBindings :108780-108810;ExposedReferences :108811-108814。
**资产自身全长 21.05s**(各 clip 2.7286+18.3214),**原作 Level1.cs 在 _Direct.time>19 强行 Stop**(truth §2.2),故演出有效时长 19s。

### 1.1 轨道总表

| # | 轨道名 | 类型 | 绑定对象(场景) | 内容 | 时间段 |
|---|---|---|---|---|---|
| 1 | Animation Track(playable:916) | 动画 | **未绑定**(value 0,:108782) | 空轨,无效果 | — |
| 2 | Activation Track(:595) | 激活 | **未绑定**(value 0,:108784) | clip "Active" 但无绑定→**无效果** | — |
| 3 | Audio Track (3)(:381) | 音频 | 未绑定(2D) | **尖叫4** `Sound/尖叫4.mp3`(guid cd784515…,:722) | **0.0902s → 0.9s**(时长 0.8098,:403-407) |
| 4 | Animation Track (2)(:969) | 动画 | **RockWarrior** Animator(`&2886304212435564634`,:106817;绑定 :108793) | clip **"run"** = `Enemy/树皮石头怪/FantasyMonster/RockWarrior/Motion/RockWarrior@run.FBX` 7400002(guid 2f302f26…,:585),loopTime 1(meta:161) | 2.7286 → 21.05s(:993-994) |
| 5 | Animation Track (1)(:271) | 动画 | **包头僵尸** Animator(`&8068214291669415042`,:128774;绑定 :108795) | clip **包头僵尸报人.anim**(guid 28a6f256…,:700),1.0s 循环(m_LoopTime 1,anim:29726) | 2.7286 → 21.05s |
| 6 | Animation Track (3)(:161) | 动画 | **士兵坏人** Animator(`&2981299812462782243`,:115799;绑定 :108797) | clip **换人抱.anim**(guid 8f1953ec…,:762),1.1667s 循环(m_LoopTime 1,anim:26018) | 2.7286 → 21.05s |
| 7 | Animation Track (4)(:1184) | 动画(根运动) | **坏人Group** Animator(`&2981299811571461408`,:109100;绑定 :108799) | 内嵌 infinite clip "Recorded"(:3-160),仅 z 曲线 | 全程(infinite) |
| 8 | Cinemachine Track(:824) | 运镜 | — | shot "CM vcam1"(:913)→ ExposedRef `36d9c8aa…`→**CM vcam1** 组件 `&2981299813259315567`(:108814→:117839) | 2.7286 → 21.05s |
| 9 | Activation Track (1)(:1079) | 激活 | **坏人Group** GO(绑定 :108801) | clip "Active",PostPlaybackState 3(LeaveAsIs) | **2.7286 → 21.05s** |
| 10 | Audio Track (5)(:477) | 音频 | 未绑定(2D) | **救救我** `Sound/救救我.wav`(guid f07c8ac8…,:1237) | **11.7s → 13.5s**(时长 1.8,:499-502) |

另有孤立 Control Track(:727)未列入 m_Tracks,无效果。音频轨参数:volume 1 / stereoPan 0 / spatialBlend 0(:473-476,:569-572)→ 2D、全音量。

### 1.2 组根运动(轨道 7 的 Recorded 剪辑,playable:17-56)

| 关键帧时间 | z 值 | 区间速度 |
|---|---|---|
| 2.95s | −5 | — |
| 10.05s | +10.194186 | 2.1457 m/s |
| 21s | +33.693916 | 2.1462 m/s |

- x、y 恒 0(:91-128 editor 曲线);pre/post extrapolation = Hold(:1207-1208);**infinite clip offset (0, 0, −4)**(:1209)。
- 合成公式:**坏人Group z(t) = −4 + clip(t)**,即 t≤2.95 时 z=−9;2.95~10.05 线性 −9→6.19;10.05~21 线性 6.19→29.69(该公式与 3s/8s/15s 三帧真值截图的演员距离自洽)。
- 该 Animator 无 avatar、ApplyRootMotion 0(:109109-113),Timeline 直接写 transform。
- **Godot 现状已一致**:`IntroBadGroup.cs:85-91` 公式与此相同 ✓。

### 1.3 各演员动画剪辑根数据(humanoid,RootT/RootQ 脚本提取自 .anim 运行时曲线)

| 剪辑 | 时长/键数/循环 | RootT(首→末,m) | RootQ(首→末) | 说明 |
|---|---|---|---|---|
| 包头僵尸报人.anim | 1.0s ×31 键,循环 | x 0.011→0.011(摆 ±0.06);y 0.936→0.936;z −0.0056→−0.0056(摆 ±0.04) | (−0.0778, ~0, 0.0058, −0.9970)→同 | **原地**;僵尸 Animator **ApplyRootMotion=1**(:128787),实际仅 4cm 级晃动 |
| 换人抱.anim | 1.1667s ×36 键,循环 | x −0.037→−0.037;y 0.877→0.877;z 0.0287→0.0287 | (0.0761, 0.1275, −0.0256, 0.9886)→同 | **原地**;士兵 ApplyRootMotion=0(:115812) |
| 黄头发被抱.anim | 7.0s,循环 | x 0.450→0.450;y 1.196→1.196;z 0.067→0.067 | (0.154, 0.713, −0.682, 0.057)→同 | **原地**;f05 自播(见下) |

- 三个剪辑均为 humanoid 肌肉曲线,骨骼级姿态 FBX/.anim 无法直接转换,**依据真值截图**描述(§4.3)。
- **f05 不在 Timeline 内**:其 Animator(`&3743449834184087334`,:118549)挂 controller **黄头发被报Controller**(`Actor/Motion/黄头发被报Controller.controller`,guid ad8fde9f…;单状态 "黄头发被抱" 循环播 `黄头发被抱.anim`,controller:10-23)——BadGroup 激活后 f05 自动循环播放 7s 被抱挣扎动画。

### 1.4 激活/隐藏时序

| 对象 | 初始状态 | 时序 |
|---|---|---|
| Timeline GO | inactive(:108765) | 原作 Level1.cs 开场 SetActive+Play(Godot `PlayIntro()` 等价) |
| 坏人Group | **inactive**(:109152) | **2.7286s 激活**(Activation Track 1),21.05s 后 LeaveAsIs;19s 被代码隐藏 |
| 环境 _Env | active | 原作开场隐藏,**3s 强制显示**(truth §1.4/§2.1) |
| 音频 | — | 0.0902s 尖叫4(0.81s);11.7s 救救我(1.8s) |
| 19s 截停 | — | _Direct.Stop→Timeline 隐藏、BadGroup 隐藏、FireSystem 激活、StartVirtualCamer 关、StartMusic 停、BGMusic 起(truth §2.2) |

### 1.5 时间轴汇总(t=0/3/8/10/15/19)

相机全程 = vcam1 语义:**机位固定 (1.35, 1.86, 22.44),FOV 30,每帧硬盯 Neck**(§5)。Neck 世界 z(t) ≈ 14.0886 + 组z(t)(§5.2)。

| t | 组z | Neck 世界(约) | 相机画面(真值截图佐证) |
|---|---|---|---|
| 0s | −9 | (0.15, 1.39, 5.09) | BadGroup 未激活;环境隐藏;0.09s 尖叫4 |
| 3s | −8.89 | (0.15, 1.39, 5.20) | 环境刚显示;演员在 12~17m 外,**全被线性雾(5~12m)吞没→空走廊**(l1u_intro_3s ✓) |
| 8s | +1.80 | (0.15, 1.39, 15.89) | **RockWarrior(z=17.76,距相机 4.7m)迎面正面特写**,双臂跑步摆动;右墙"学园祭"海报(l1u_intro_8s/gun_8s ✓) |
| 10s | +6.09 | (0.15, 1.39, 20.18) | **俯拍近景:包头僵尸(z=20.33,距相机 2.1m)弯腰抱女生从镜头下掠过**——绿皮肤/红棕包头/白绷带;女生黑长发/白水手服/深蓝百褶裙/白袜(l1u_gun_10s ✓);RockWarrior z=22.05 恰掠过相机;士兵 z=24.84 已越过 |
| 11.7s | — | — | 救救我(1.8s) |
| 15s | +16.80 | (0.15, 1.39, 30.89) | 演员已在**相机身后 8~13m**,相机转身望 +Z:**雾中远景追逃背影**——僵尸抱人在前、石头人尾随;左鞋箱、右"走るな"标语(l1u_intro_15s ✓) |
| 19s | +25.40 | (0.15, 1.39, 39.49) | 代码截停:BadGroup 隐藏;相机 2s EaseInOut blend 到 Battle0,FOV 30→45,开战 |

---

## 2. Godot 换算表(可直接抄)

### 2.1 机位

| 项 | Unity 真值 | Godot 应用值 |
|---|---|---|
| 开场相机位置 | (1.35, 1.86, 22.44)(:117832;父根 StoryLevel1GamePlay 在原点 :109559-560) | **Vector3(1.35, 1.86, 22.44)**(audit 实测现状 (1.325,1.85,22.05),z 差 0.39m;核对 level1_battle.tscn 相机初始位) |
| 初始朝向 quat (x,y,z,w) | (−0.0009182, 0.9986439, −0.0189072, −0.0484978)(:117831) | 换算后 **(0.0189072, −0.0484978, 0.0009182, 0.9986439)**(仅首帧用;之后每帧被 LookAt 覆盖) |
| FOV / near / far | 30 / 0.001 / 100(:117859-863) | **Fov=30**(已对,Level1.cs:121);near 0.01 即可;far 100 |
| 注视 | Neck 骨(:117857),Composer 默认硬盯 | 每帧 `camera.LookAt(neckGlobalPos, Vector3.Up)`,不加平滑(§5) |
| Battle0(19s 衔接) | pos (−0.7353, 1.0, −10.2706);quat (−0.040232, 0.490266, 0.022659, 0.870349);FOV 45(:109769-770,:109795-798) | pos 沿用;quat **(0.022659, 0.870349, 0.040232, −0.490266)**(与 audit 实测 (0.0227,0.8703,−0.0402,0.4903) 差整体符号=同一旋转 ✓);FOV 45 |

### 2.2 演员静态摆放(Godot 位置与 Unity 相同;镜像约定 x 不取负)

| 节点 | Godot Position | Godot 朝向 | Scale |
|---|---|---|---|
| IntroBadGroup(=坏人Group) | **(0, 0, −4) + Vector3(0,0,clipZ(t))**(公式 §1.2,现状已对) | 不动 | 1 |
| RockWarrior | (0.5, 0, 15.957) | **FBX 原生、不旋转**(§4.2) | 1 |
| 包头僵尸 | (−0.252, 0, 14.238) | 同上 | **1**(现状 ×1.2 错) |
| 士兵坏人 | (−0.674, 0, 18.752) | 同上 | **0.7**(:113016) |
| f05(挂僵尸 `Bone_ R UpperArm` 的 BoneAttachment3D) | local (0.235, −0.051, 1.099)(:122171) | local quat (−0.0737453, −0.6580442, 0.6833673, 0.3074877)(:122170;骨骼局部空间同 FBX 直接沿用,**待确认**观感) | 1 |
| 剑女孩(挂士兵 `Bip001 L UpperArm` 的 BoneAttachment3D) | local (−0.273, 0.635, −0.953) | local quat (−0.8526303, −0.1896241, −0.2744347, −0.4021813) | **1.4286**(=1/0.7 抵消父缩放) |

### 2.3 音频/时序(现状已对,列此备查)

尖叫4 `res://assets/audio/story/尖叫4.mp3` @0.09s(真值 0.0902s,时长 0.81s);救救我 `res://assets/audio/story/救救我.wav` @11.7s(1.8s);Tension.mp3 全程;19s 收队。
**补:0~2.729s BadGroup 应 Visible=false**(真值 Activation 2.7286s 才激活;现状 `Play()` 立即显示)。

---

## 3. 演员材质真值

### 3.1 f05_schoolwear_200_m(被抱女生)——当前 Godot 双腿纯白

场景内 SkinnedMeshRenderer `&3743449834188204518`(:118568)有 **4 个材质槽**(:118586-589):

| 槽 | Unity 材质 | 贴图(guid→文件) | 内容 | Godot 已导入 |
|---|---|---|---|---|
| 0 | `Actor/Satomi/Materials/f05_schoolwear_200_m_s_Ex.mat` | `Actor/Satomi/Textures/f05_schoolwear_200_m.png`(681acdcf…) | 校服(**裙/袜/腿**在此图) | ✅ `game/assets/models/actors/f05_schoolwear/f05_schoolwear_200_m.png` |
| 1 | `f05_schoolwear_200_m_c_Ex.mat` | 同上 | 校服上衣 | ✅ 同上 |
| 2 | `f05_face_00_m_Ex.mat` | `Actor/Satomi/Textures/f05_face_00_m.png`(4b4dfc77…) | 脸 | ✅ `…/f05_schoolwear/f05_face_00_m.png` |
| 3 | `f05_hair_00_m_Ex.mat` | 同上 | 发 | ✅ 同上 |

- 4 材质 shader = 内置 fileID 14(**Unlit/Texture**)→ Godot 材质建议 `ShadingMode=Unshaded`(**待确认**;走廊烘焙光下差异小)。
- **修复**:照 `MenuScreen.cs:71-84` 先例(FBX 导入丢贴图→代码 MaterialOverride),对 f05 实例逐 surface 覆盖:槽 0/1 盖 `f05_schoolwear_200_m.png`,槽 2/3 盖 `f05_face_00_m.png`。surface↔槽映射按 FBX surface 名(含 `_s`/`_c`/`face`/`hair`)匹配;匹配不到则按索引 0..3 顺序盖——**Godot 侧 surface 名/顺序待导入后确认**。
- 真值观感(l1u_gun_10s):白色短袖水手服(蓝领)、深蓝百褶裙、**白袜+肤色腿**、黑长发。

### 3.2 Chr_Zcharacter_01(包头僵尸)——审计报"通体蓝衣"

- 场景渲染器 GO "CHR_Zcharacter_001"(scale 1.029025):材质 `Enemy/包头僵尸/Materials/MAT_Zcharacter.mat`(0d4cc149…)→ 贴图 **`Enemy/包头僵尸/Textures/Tex_Chr_albedo transparency.png`**(21a083cc…)。
- Godot 侧 `baotou_mat.tres` 已引用 `assets/models/monsters/baotou/tex_chr_albedo.png`——**经图像比对与真值贴图同源**(绿皮肤、红棕包头布、白绷带、血迹、蓝绿色破衣裤俱全)。
- 结论:**贴图本身不缺**;审计所报"通体蓝衣"主因:① 真值贴图衣裤本来就是蓝绿色(中远距观感主色);② Godot 僵尸 ×1.2 放大且直立 idle,丢失真值"弯腰抱人"姿态;③ 10s 近景构图缺失(相机盯错目标)导致绿皮肤/绷带细节从未入画。**修朝向+背负+LookAt 后按 l1u_gun_10s 复查**。真值近景:绿色脸/手臂、红棕包头、白绷带缠头、蓝绿破衣。

### 3.3 RockWarrior 与士兵坏人

| 演员 | Unity 材质 | 贴图 | Godot 现状 | 判定 |
|---|---|---|---|---|
| RockWarrior(mesh GO "monster_085") | `…/RockWarrior/Mesh/Materials/monster_085.mat`(ef63004c…) | `…/RockWarrior/Texture/RockWarrior.png`(60d115c1…) | `rock_warrior/RockWarrior.png` + `rock_warrior_mat.tres`,代码已覆盖(IntroBadGroup.cs:109) | ✅ 无需动(菜单同款;真值 8s:蓝灰岩+绿藤+黄眼+金牙) |
| 士兵身体(Body_A/Legs_B/head_C) | `AssetTools/ToonSoldiers_militias/models/materials/TS_militia_B.mat`(69f9dc75…) | **`…/materials/Militias_B.tga`**(1d94e653…) | ❌ **未导入**(toon/ 只有 Militias_A_urban.png、Militias_D.tga) | 从 Unity 工程拷贝转换 `Militias_B.tga` → toon/ |
| 士兵武器(weapon_deserteagle) | `TS_militia_weapons.mat`(8dda1e8e…) | `…/materials/militia_weapons_texture.tga`(9ddf7e1c…) | ✅ `toon/militia_weapons_texture.tga` 已有 | 武器节点选错(§4.2);且**武器与身体贴图不同,OverrideMaterial 不能一把盖**(IntroBadGroup.cs:175 现状把武器也盖成身体贴图) |
| 剑女孩(mesh headusOBJexport008) | `Actor/Blade_girl/model/Materials/Blade_Girl_Ex.mat`(0ec7be55…) | `Actor/Blade_girl/Textures/Blade_Girl_base_All.PSD`(b0672fc1…) | ✅ `actors/blade_girl/blade_girl_base.png` 已有(同源转换,**待确认**观感) | 剑女孩**无 Animator**,绑定姿态随士兵骨骼;其武器挂点 Prop1/headusOBJexport009 **inactive=0 不显示**(Weapon.mat 无需导入) |

- 士兵/僵尸/石头人材质 shader fileID 10701(Legacy 系,具体名**待确认**;Godot 默认 StandardMaterial3D 即可)。
- 剑女孩自带 8 个粒子节点(Particle View 01 等,active=1,剑刃拖尾类)——复刻可选,优先级低。

### 3.4 修复先例(MenuScreen.cs:60-90 模式)

`game/src/UI/MenuScreen.cs:71-84`:FBX 导入丢贴图 → `GD.Load<Material>` 或 `new StandardMaterial3D{ AlbedoTexture=… }` → 遍历 `FindChildren("*","MeshInstance3D",true,false)` 逐 `MaterialOverride`。该文件注释强调的坑:**只盖目标子树**,勿误盖同树特效 quad(MuzzleFlash 先例);士兵案例里**武器和身体要分别盖不同材质**。

---

## 4. 演员姿态/背负关系真值

### 4.1 BadGroup 层级(脚本合成自 school_day.unity;父根 StoryLevel1GamePlay 位于原点)

```
坏人Group (GO &2981299811571461422,初始 inactive :109152;local (0,0,-4),rot identity :109126-127)
├── RockWarrior   local (0.5, 0, 15.957),rot identity,scale 1 (:107428-430)
│     └── 仅自身 Bip001 骨骼 + monster_085 网格 —— ★背上/手上没有任何人
├── 包头僵尸      local (-0.252, 0, 14.238),rot identity,scale 1 (:127082-84)
│     └── 骨骼 … Bone_ Spine2 → Bone_ R Clavicle → Bone_ R UpperArm(挂骨,:126983-998,GO :127902)
│           └── f05_schoolwear_200_m  local (0.235,-0.051,1.099)
│               quat (-0.0737,-0.6580,0.6834,0.3075),scale 1 (:122170-178)
└── 士兵坏人      local (-0.674, 0, 18.752),rot identity,scale 0.7 (:113014-16)
      ├── 激活部件:Body_A / Legs_B / head_C(其余 28 个部件全 inactive;子树脚本遍历结果)
      ├── Bip001 R Hand → WeaponContainer → weapon_deserteagle active=1(手持沙漠之鹰;
      │     weapon_rpg_rocket 虽 active=1 但其父 weapon_rpg inactive → 不可见)
      └── Bip001 L UpperArm → 剑女孩  local (-0.273,0.635,-0.953)
                              quat (-0.8526,-0.1896,-0.2744,-0.4022),scale 1.4286
```

**三人朝向真值:局部 rot 全为 identity**,即面朝 +Z(世界),随组向 +Z 跑(迎向并越过相机)。
Godot 等价:**FBX 实例不做任何 Y 旋转**。当前 `model.Rotation = (0,π,0)` 把脸转成 −Z=背离相机(audit intro_8s 截图石头人只见背影,铁证)——删除该行即可:FBX 原生朝向 +Z,与 Unity 场景 identity 语义一致。

### 4.2 与 Godot 现状(IntroBadGroup.cs:103-132)逐条对照

| 项 | Unity 真值 | Godot 现状 | 判定/修法 |
|---|---|---|---|
| RockWarrior 背负 | 空手跑 | 背上挂 casual_dressed_girl(:111-113) | ❌ 删除该 AttachGirl |
| 僵尸抱 f05 | 挂 `Bone_ R UpperArm`,local (0.235,−0.051,1.099)+四元数 | 挂僵尸根节点 (0,1.15,−0.55) scale 4(:123-125)→"头顶白柱" | ❌ 改 BoneAttachment3D 挂骨+真值 local TRS;f05 scale=1;补材质(§3.1) |
| 僵尸缩放 | 1(mesh 节点自身 1.029) | ×1.2(:122) | ❌ 改回 1 |
| 士兵抱剑女孩 | 挂 `Bip001 L UpperArm`,local (−0.273,0.635,−0.953) scale 1.4286 | 挂士兵根节点 (−0.35,0.95,−0.4) scale 4(:129-131) | ❌ 同上改挂骨+真值 TRS |
| 士兵手持 | **weapon_deserteagle active** | 只留 weapon_rpg_rocket(其父不可见)→空手(:172) | ❌ keep 名单改 `weapon_deserteagle`;武器另盖 militia_weapons 材质 |
| 士兵部件 | Body_A/Legs_B/head_C | 同(:172) | ✅ 已对 |
| 三人朝向 | 面朝 +Z(迎相机) | 面朝 −Z(背相机) | ❌ 删 model.Rotation π |
| f05 动画 | 黄头发被抱.anim 7s 循环(humanoid,无法直转) | 无(绑定姿态) | ⚠️ 保持近似:f05_schoolwear_anims.tres 挑最接近"被抱挣扎"的剪辑循环,**以 10s 截图校准** |
| 僵尸/士兵/石头人动画 | 报人(弯腰抱人)/换人抱/run | idle/locomotion 近似 | ⚠️ humanoid .anim 无法直转(README 已记偏差);以截图校准选型 |

### 4.3 三个时刻应看到的画面(对照真值截图)

- **3s**(l1u_intro_3s):空走廊。左侧实墙+门洞,右侧成排窗;尽头楼梯间全在黄绿雾中。**画面里没有任何演员、没有暂停按钮**(HUD 仅金币×10+双头像)。
- **8s**(l1u_intro_8s/gun_8s):RockWarrior **正面**充满画面中下部(蓝灰岩躯+绿藤+黄眼+金色口腔,双臂跑步抬起);其后上方隐约可见僵尸抱女生跟随;右墙"学园祭"彩页海报,左侧墙有"2"班牌。
- **15s**(l1u_intro_15s):相机望向 +Z 深处,**雾中背影**:前方僵尸(浅色)抱女生跑、RockWarrior(大黑轮廓)尾随;左侧窗台+木质鞋箱(下駄箱),右侧门+黄黑警示带"走るな"标语;远景几乎被雾纯色吞没。
- **10s 附**(l1u_gun_10s,修复挂骨后必须复现的构图):镜头斜下俯拍,僵尸绿色脊背/红棕包头占据右下,女生**头朝下、躯干水平、双腿向上翘**横在僵尸臂弯,白袜深蓝裙清晰;地面可见僵尸影子。

---

## 5. 相机 LookAt 目标真值

### 5.1 目标 = 被抱女生的 Neck 骨

- vcam1 `m_LookAt: {fileID: 3743449834193910022}`(:117857)= GO **"Neck"**(:119855)。
- **其余 5 个战斗 vcam 的 LookAt 也全是同一 Neck**(:109795/:116650/:117109/:117144/:118020,FOV 45)——Battle0~4 同样盯它(19s 后 BadGroup 隐藏,Neck 停在末帧位置,相机仍朝该点)。
- Follow=0(:117858)→ 机位不动;cm 子物体只有默认 CinemachineComposer(guid 1e8b78ac…)+ Transposer(ac0b09e7…)且全部默认参数(:118101-124)→ **硬盯,无 dead zone / damping**。Godot 等价 = 每帧 `LookAt(Neck全局位置, Vector3.Up)`,**不要加任何平滑**。

### 5.2 Neck 世界位置(脚本沿 17 级父链合成)

父链:Neck ← Spine3 ← Spine2 ← Spine1 ← Spine ← Hips ← PelvisRoot ← f05 根 ← **僵尸 Bone_ R UpperArm** ← Bone_ R Clavicle ← Bone_ Spine2 ← Spine1 ← Spine ← Pelvis ← RootJoint ← 僵尸根 ← 坏人Group ← 场景根。

- 静态(timeline 未播、组 z=−4):**Neck 世界 (0.1475, 1.3899, 10.0886)**;f05 根 (−0.1028, 0.189, 10.4365);挂骨 UpperArm (−0.0355, 1.2987, 10.2646)。
- 运动中近似:**Neck(t) ≈ (0.1475, 1.39±0.05, 14.0886 + 组z(t))**(组z 公式见 §1.2;x 不变;y 随僵尸报人动画 ±4cm 晃动,RootT.y 0.898~0.944,可忽略)。
- 即 LookAt 目标 = IntroBadGroup 节点位置 + 本地偏移 **(0.1475, 1.3899, 14.0886)**。低成本修法:IntroBadGroup 下放一个 Node3D 作 `_lookTarget`,Position 设该偏移,每帧取其 GlobalPosition 给相机 LookAt;高保真修法:f05 按 §4.1 挂骨后,直接取其 Skeleton3D 的 "Neck" 骨全局位置。
- Godot 现状 `IntroBadGroup.cs:114` `_lookTarget = rock` + 偏移 1.35m(:95)——**这就是 audit #9"10s 相机钻进石头人躯干"的直接原因**(相机追着石头人转)。

---

## 附:修复清单(按依赖排序)

1. IntroBadGroup.cs:删三处 `model.Rotation=(0,π,0)`;删 RockWarrior 的 AttachGirl;僵尸 scale 1.2→1。
2. f05 改挂僵尸 `Bone_ R UpperArm`(BoneAttachment3D),local TRS 按 §2.2;剑女孩改挂士兵 `Bip001 L UpperArm`,TRS 按 §2.2。
3. f05 材质按 §3.1 四槽覆盖;士兵导入 Militias_B.tga,身体/武器分别覆盖(§3.3);士兵 keep `weapon_deserteagle`。
4. `_lookTarget` 改 §5.2 的 Neck 近似点;LookAt 不加平滑。
5. 0~2.729s BadGroup Visible=false(现状 Play 即显示)。
6. 相机初始位置核对为 (1.35, 1.86, 22.44)(现状 z 差 0.39m)。
7. 验收截图:同机位 3s/8s/10s/15s 对照 `l1u_intro_3s/8s/15s.png`、`l1u_gun_10s.png`。

**待确认项**(正文已逐一标注):f05 在 Godot FBX 的 surface 名/顺序;f05/剑女孩挂骨 local TRS 在 Godot 骨骼空间的观感(理论同 FBX 一致,以 10s 截图校准);f05 材质 Unlit 与否;Blade_Girl_base_All.PSD→blade_girl_base.png 同源观感;infinite clip offset −4 的合成方向(本报告按 z_group=−4+clip 推导,与 3s/8s/15s 三帧截图距离自洽)。
