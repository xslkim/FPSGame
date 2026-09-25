# Wave2 Agent F 笔记:Level1 开场 19s 演出(school_day SchoolNightTimeline)真值重写

日期:2026-06-10 · 执行:Wave2 agent F · 依据:intro_spec.md(必读规格)、level1_unity_truth.md §2.2/§9、
真值截图 l1u_intro_3s/8s/15s.png、l1u_gun_8s/10s.png、审计 l1_visual.md #9/#10、monsters.md #30~33。

## 1. 改动文件清单

| 文件 | 改动 |
|---|---|
| `game/src/Battle/IntroBadGroup.cs` | 全量重写(221→~350 行):运镜表/朝向/背负/材质/激活时序 |
| `game/assets/models/monsters/toon/Militias_B.tga` | 新增,拷自 `G:\test\FPSGame\Assets\AssetTools\ToonSoldiers_militias\models\materials\Militias_B.tga`(士兵身体贴图,TS_militia_B.mat 同源) |
| `game/assets/models/monsters/toon/toon_militia_b_mat.tres` | 新增,StandardMaterial3D albedo=Militias_B.tga(照 toon_mat.tres 先例) |
| `game/assets/models/monsters/toon/weapon_deserteagle.FBX` | 新增,拷自 Unity `…/models/weapons/weapon_deserteagle.FBX`(士兵手持真值;Godot 版 ToonSoldiers_Militias.FBX 经探针确认不含任何 weapon_* 网格,武器只能单独导入,先例 weapon_ak47.FBX) |

未改:Level1.cs、Monster*.cs、FireSystem.cs、level1_battle.tscn、任何演员 .tscn(全部代码覆盖达成,未动 tscn)。

## 2. 修复清单逐条落实(对照任务 7 项)

1. **运镜**:代码内 `VcamShot[]` 切镜表(时刻+pos+quat+fov+TrackNeck)。注意:intro_spec §1.1 轨8 实测 Cinemachine 轨**全程仅一个 shot(CM vcam1,2.7286→21.05s)**,Follow=0 机位固定 (1.35,1.86,22.44),FOV30,Composer 硬盯 Neck 无 blend——任务书"多 vcam 切镜"以 spec 为准落实为"初始姿态(0~2.7286s)→ vcam1 硬盯"两条表项,瞬切无插值。"多 vcam"实际指 19s 后 Battle0~4(LevelBase.SwitchCamera 既有,不在开场)。
   - 初始朝向:Unity quat (−0.0009182,0.9986439,−0.0189072,−0.0484978) 按 spec 换算公式 (x,y,z,w)→(z,w,−x,−y) 得 **(−0.0189072,−0.0484978,0.0009182,−0.9986439)**,与 level1_battle.tscn:44 相机基矩阵逐位吻合。**注:spec §2.1 印刷值 (0.0189,−0.0485,0.0009,0.9986) 与自身公式不符(该四元数视线偏向 +X,与 Neck 方向 −X 矛盾),按公式结果落实**;该姿态仅 0~2.73s 使用(环境隐藏期),无视觉影响。
2. **朝向**:三处 `model.Rotation=(0,π,0)` 全删,FBX 原生朝向(面朝 +Z 迎相机)。8s 截图石头人正面特写(蓝灰岩+绿藤+黄眼+金牙)与 l1u_intro_8s 同构图 ✓。
3. **背负**:RockWarrior 的 AttachGirl 删除(空手跑);f05 经 BoneAttachment3D 挂僵尸 `Bone_ R UpperArm`,local (0.235,−0.051,1.099)+quat (−0.0737453,−0.6580442,0.6833673,0.3074877) scale 1(与 school_day.unity:122170-178 原文逐位核对一致);剑女孩挂士兵 `Bip001 L UpperArm`,local (−0.273,0.635,−0.953) scale 1.4286。探针实测剑女孩世界 scale=1.000 ✓(1/0.7 抵消父缩放)。僵尸 scale 1.2→1 改回。
4. **LookAt**:`NeckLookTarget` Node3D 挂组根下,本地偏移 (0.1475,1.3899,14.0886)(§5.2 低成本方案),激活后每帧 `camera.LookAt(GlobalPosition, Up)` 无平滑;机位每帧钉 (1.35,1.86,22.44)(Follow=0 语义,顺带消除 audit 实测 z 漂移 0.39m 的残留路径)。探针实测 t=9s:cam (1.35,1.86,22.44) FOV30、Neck (0.1475,1.3899,18.41)=组z 4.32+偏移 ✓。
5. **激活时序**:0~2.7286s `Visible=false`(原 `Play()` 立即显示已改);探针 t=0.5s 实测 visible=false、组 z=−9(pre-Hold −5+offset −4)✓;2.7286s 同刻:显示+四路动画从 0 起播(Timeline 轨4/5/6 与 f05 自播的起点)。另补根运动 pre-extrapolation Hold:t≤2.95s clip=−5(原公式 2.729~2.95s 会外推到 −5.47,差 0.47m,按 spec §1.2 修正)。
6. **材质**:
   - f05:4 槽 surface 名实测 `f05_schoolwear_200_m_s/_c/f05_face_00_m/f05_hair_00_m`,按名匹配——schoolwear→`f05_schoolwear_200_m.png`,face/hair→`f05_face_00_m.png`;Unity 材质 shader=fileID 14(**Unlit/Texture**,已从 .mat 原文确认)→ Godot `ShadingMode=Unshaded`(雾仍生效)。10s 截图:黑长发/白水手服/深蓝百褶裙/白袜肤色腿 ✓,纯白腿消除。
   - 士兵:身体 Body_A/Legs_B/head_C 盖 `toon_militia_b_mat.tres`(Militias_B.tga 深色系战术服,与真值 l1u_battle_6s 黑衣墨镜一致);武器单独盖 `toon_weapon_mat.tres`(militia_weapons_texture),**不再一把盖**(spec §3.4 坑)。
   - 剑女孩:Blade_Girl_Ex.mat 同为 fileID 14 Unlit → Unshaded 盖 `blade_girl_base.png`;部件激活真值核对(school_day.unity GO 原文):headusOBJexport008 active=1 显示,headusOBJexport009(武器)/Object001 active=0 隐藏。
   - 僵尸 baotou_mat.tres / 石头人 rock_warrior_mat.tres 维持现状(spec 判定无需动)。
7. **士兵武器**:`Bip001 R Hand` BoneAttachment3D → WeaponContainer(真值 local pos (−0.22054,0.02459,0.14772)、quat (−0.07951,0.66366,0.74314,−0.03133),school_day.unity:112802 原文)→ weapon_deserteagle 实例 scale 0.42284146(:112618 原文)。探针 t=9s 实测武器世界 (−1.11,1.24,22.70),位于士兵右手侧(−X,面朝 +Z 时右手侧),量纲正常;3s 截图可见右手前伸持小黑枪。
8. 保留项:根运动 z=−4+clip(t) 分段公式、尖叫4@0.09s、救救我@11.7s 均未动;僵尸 anim_idle、士兵/石头人 locomotion 近似选型维持(humanoid .anim 不可直转,README 已记偏差);f05 新增 `f05_schoolwear_anims.tres` 唯一剪辑 `dance`(4s 循环,四肢小幅挣扎感)作被抱近似(spec §4.2 指示)。

## 3. 验证

- 构建:`dotnet build` 0 错误(文件锁协议)。
- 自检:`--level1-selftest` 18 项全 PASS(含难度表/宝箱/飞斧/Boss/5 波顺序/5 次切镜等既有断言,无回归)。
- 数值探针(headless,`--level1-intro`,已删):上表 t=0.5/t=9 各项实测。
- 截图(1472×668,产物 `tools/screenshots/audit/wave2_f/intro_3s/8s/10s/15s.png`)逐张对照:
  - **3s** vs l1u_intro_3s:同机位同朝向(走廊 −Z,左门"1-2"右窗);演员位于 12~17m 外、位置正确,但**仍可见**——真值被线性雾(5→12m)吞没为空走廊,Godot 雾弱(audit #6,WorldEnvironment 属环境/tscn 范围,本任务不动 tscn,残留)。
  - **8s** vs l1u_intro_8s/gun_8s:✓ 石头人正面充满画面中下部、双臂跑步摆动、右墙海报、左"1-2"班牌;僵尸抱 f05 在其肩后露出手臂(真值为肩后露腿,左右相反=环境 X 镜像既有约定)。
  - **10s** vs l1u_gun_10s:✓ 俯拍近景——僵尸绿皮肤/红棕包头/白绷带占据画面,f05 横挂臂弯(黑发/白衣/蓝裙/白袜)。残留:僵尸姿态为直立 anim_idle 而非弯腰报人,f05 垂挂方向与真值差约 45°(挂载 TRS 为真值原值,差异来自骨骼姿态近似,属已记偏差)。
  - **15s** vs l1u_intro_15s:✓ 相机转身望 +Z、僵尸抱人在前、石头人大轮廓尾随、左侧鞋箱;远景雾感弱于真值(同 #6),右墙可见公告板而真值为"走るな"标语(同镜像/雾能见度差异,环境范围)。

## 4. 待确认/移交

- **f05/剑女孩/武器挂骨 local TRS 在 Godot 骨骼空间的观感**:理论同 FBX 一致,截图观感成立(10s 构图对、无穿插错位);若后续要做 报人/换人抱 骨骼姿态移植,f05 垂挂方向会随之归位。
- **雾强(#6)**:3s/15s 与真值的主要残余差异全在雾能见度,需 WorldEnvironment/DirectionalLight 调参(tscn,非本 agent 范围)。
- spec §2.1 初始朝向印刷值与公式不符(见 §2.1 注),建议回写修订 intro_spec。
- 剑女孩 8 个粒子节点(剑刃拖尾)未复刻(spec 标记可选、低优先)。
