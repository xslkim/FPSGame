# Wave2 Agent I — 两 Boss 模型缩放按 Unity 真值落实(L2 ×3 / L3 ×5)

日期:2026-01-25(以系统日期为准)。对应审计:`l234.md` L2-4 / L3-2(均标注"已声明/待决策",本轮按真值落实)。

## 1. 改动文件清单

| 文件 | 改动 |
|---|---|
| `game/scenes/battle/monsters/level2_boss.tscn` | 根节点 `Level2Boss`(CharacterBody3D)加 `scale = Vector3(3, 3, 3)`;`HpAnchor` local y 2.8 → **2.112**(真值换算,见 §2/§5) |
| `game/scenes/battle/monsters/rock_warrior.tscn` | 根节点 `RockWarrior`(CharacterBody3D)加 `scale = Vector3(5, 5, 5)`;`HpAnchor` local y 2.5 → **2.322** |
| `game/scenes/levels/level3.tscn` | 追加 `BossGroundPatch` 不可见地面碰撞(连带发现的既有穿地修复,见 §6.5)+ 顶部对应 sub_resource、load_steps 11→12 |
| `game/src/Battle/Level3.cs` | 截图挂接:`--level3-freeshot` 尾部可选 `:delaySec`(向后兼容;不加 delay 时行为不变),用于拍 G6 才出场的 Boss(§6.5) |
| `game/src/Battle/MonsterHpBar.cs` | 两个血条材质加 `BillboardKeepScale = true`(billboard 默认丢世界缩放,Boss 血条否则不随 ×3/×5 放大,见 §5.5) |
| `game/src/Battle/Fireball.cs` | 火球材质加 `BillboardKeepScale = true`(Boss 火球 ×10 视觉此前被 billboard 吞掉,见 §5.5) |

未改:Boss 行为/数值代码(Level2Boss.cs/RockWarriorBoss.cs 逻辑零改动;攻击半径/移速/火球等代码数值按任务约定保持不变)、level_meta/monster_meta.json(L3 boss_pos 是 agent E 修好的,勿动)、README(注记更新归主 agent)。
一次性探针 `tools/probe_boss_scale.gd` / `tools/probe_l3_floor.gd`(项目外工具区,复跑需无游戏实例占用 UDP;输出已录于 §5/§6.5)。

## 2. Unity 真值(出处逐条可查)

### Level2 Boss(Level2.unity,Transform &146836867 / GameObject &147241369 "Level2Boss")
- Transform:pos (11.12,-8,0.74)、rot y=-51.016°、**scale (3,3,3)**、`m_IsActive: 0`(场景预摆,G2 开始 15s 后 SetActive,Unity `Level2.cs:349-371`)。
- 根组件:Animator、Level2Boss.cs(guid 90794958…)、CinemachineCollisionImpulseSource、CharacterController r=0.1 h=0 center (0,1,0.05)。
- 子级全树随根 ×3:Bip001 骨骼(各节挂 BoxHead.cs guid 063933eb + SphereCollider 的 HurtBox/Spine1Box,即部位命中体)、lod_dark_knight(SkinnedMeshRenderer)、lod_sword、DeathAs/HurtAs/Impact1/Impact2(音效)、Shandian 左右雷柱 prefab、**HpReduceNumber 血条 prefab(PrefabInstance &1359153244,直接挂 Boss 根,local pos (0,0.58,0),prefab 根 scale 0.01)**。
- Level2Boss.cs 距离/尺寸相关字段:移速 4、目标点 `cam+forward*12` y=-8、Skill1 出手旋转 SkillRight1=(-20,0,0)/SkillLeft1=(-20,80,0)、Skill2 雷柱 0.5s 后 Both 伤害×0.5、Dead 状态 `m_char.Move(down*dt)` 下沉 1m/s。meta:HP100+20/Lv、CD5。**全部为代码世界单位常量,与节点缩放无关**。
- 血条运行时语义:HpReduceNumber 是 Boss 子级 → **血条中心 = (挂点 y + Canvas anchoredY 153.2×prefab 根 0.01) × Boss 缩放**(prefab 结构:根 Transform scale 0.01 → 子 Canvas RectTransform anchored (0,153.2) → Slider 在 Canvas 原点;已逐文档核对 HpReduceNumber.prefab)。L2:(0.58+1.532)×3 = **6.336m 世界高**,条宽 60px×0.01×3=**1.8m**;同构换算与 L1 各怪 monsters.md #12 修复口径(挂点+1.532)一致。

### Level3 Boss(Level3.unity,Transform &4000012355956324 / GameObject &1000012494460548 "RockWarrior")- Transform:pos (83.83,-0.02,-101.3)、rot y=46.334°、**scale (5,5,5)**、inactive 预摆,`Level3.cs:90-96` InitBoss 只 born+LookAt+SetActive(不动位置)。
- 根组件:Animator、RockWarrior.cs(guid 650b665a…,_Name=15 树皮石头怪)、CharacterController r=0.1 h=0.1 center (0,0.05,0)。
- 子级随根 ×5:火球锚点 GameObject local (0,3,0)→PoisonFireball(BaotouFireball)→GreenFireball1(EffectSettings 粒子组);Bip001 骨骼(HitBox SphereCollider+BoxHead);monster_085(SkinnedMeshRenderer);**HpReduceNumber(&385614369,local pos (0,0.79,0),且 RectTransform m_LocalScale.x 被 override 成 0.03,y/z 保持 prefab 0.01)**。
- RockWarrior.cs:AttackRadius=36、MoveSpeed=3、CD=8、HP=300(MonsterBase.GetMeta 树皮石头怪分支);硬化皮肤(仅 atk01 期间全额掉血,否则每发 1 血);RockAttack:`fireBall.Fire()` 后 `localScale=Vector3.one*10`。
- 火球运行时语义(关键):`BaotouFireball.Fire()` 先 `fireEffect.transform.position=handTrans.position`(锚点世界坐标 = boss+15m,因锚点 local y=3 ×5)**再脱父**(`parent=null`),最后 RockAttack 把 localScale 设 10 —— 脱父后 localScale 即世界尺度,**火球起点/大小语义上独立于 Boss 缩放**。

## 3. 缩放层级选择依据(为什么放在根节点,而不是只缩 Model)

审计建议的另一种写法是"Model scale=3,碰撞/HpAnchor 同步"。本实现选择**根节点 scale**,理由:

1. **与 Unity 同构**:真值就是 `m_LocalScale` 写在 Boss 根 Transform 上,模型/命中体/血条/音效全部作为子级继承缩放。根缩放是 1:1 映射,一行覆盖全部子级,后续新增子级(特效锚点等)自动一致。
2. **命中体必须同步放大**:Godot 命中判定走物理射线打 Boss 身体的胶囊(FireSystem.cs:291,collision_layer=2);Unity 的骨骼 HurtBox 球随根 ×3/×5。若只缩 Model,Boss 视觉变大而命中胶囊不变(只能打脚)。
3. **血条同步放大**:HpReduceNumber 在两引擎都是 Boss 子级。根缩放后 Godot 血条 0.6m→L2 1.8m(与 Unity 60px×0.01×3=1.8m 逐位一致)/ L3 3.0m。
4. **代码数值不变(与原作一致)**:L2 移速4/前12m/Skill 旋转角,L3 半径36/移速3/火球×10,两边都是世界单位常量,不随节点缩放变化,故全部保持。
5. 已逐项排查根缩放对既有行为的影响(见 §4)。

## 4. 行为保持核验(代码审查 + 自检/截图)

| 行为 | 结论 |
|---|---|
| L2 Skill1 Rotate(`PickSkill1SideAndRotate`,`Basis *= FromEuler`) | 均匀缩放下 `R·S·RΔ = R·RΔ·S`,等效 Unity transform.Rotate(Space.Self),缩放保持 ✓(自检 StatSkill1RotateCount=1、向量∈{SkillRight1,SkillLeft1} PASS) |
| L2 死亡下沉(agent E 加的 `_PhysicsProcess` Dead 分支 `GlobalPosition += Down*dt`) | 世界坐标累加,与缩放无关 ✓(自检 boss died→2s victory PASS) |
| L2 移动/站位(translate 4m/s、目标 cam+12m、y=-8) | 世界单位,不变 ✓ |
| L3 硬化皮肤(Hit 分层) | 纯数值路径,不变 ✓(自检压血补刀→Die→victory PASS) |
| L3 火球(Fireball.Spawn 挂场景根 + `fb.Scale=10`) | 与 Unity"脱父后 localScale=10"同构,世界尺度与 Boss 缩放无关 ✓;起点 `GlobalPosition+(0,1.5,0)` 为代码常量,按任务约定不动(差异见 §6) |
| 出生点 | L2 走 GetBornPosition,内部 `_col.Position.Y` 用本地值(1.2)——Unity `LevelBase.cs:232` 同样用 CC.center.y 原始序列化值(1.0),语义一致;L3 用 meta boss_pos 固定点,自检断言 born at (-83.83,-0.02,-101.3) PASS ✓ |
| 对象池复用 | MonsterPool/Born/Deactivate 均不触碰 Scale,tscn 烘焙值随实例常驻 ✓ |
| 伤害飘字 | ShowDamage Label3D 挂 FireSystem 下、按世界命中点摆,不受缩放影响 ✓ |
| 物理缩放 | 根 CharacterBody3D 均匀缩放,Godot 4 物理支持(非均匀才有警告);胶囊世界尺寸 L2 r2.4/顶7.2m、L3 r4.5/顶11m(探针实测,见 §5) |

## 5. 缩放后实测尺寸(probe_boss_scale.gd 输出)

| 项 | L2 Boss(×3) | L3 Boss(×5) |
|---|---|---|
| 根 scale | (3,3,3) ✓ | (5,5,5) ✓ |
| 胶囊世界半径/顶高 | 2.4m / 7.2m | 4.5m / 11.0m |
| 血条锚点 local y / 世界高 | 2.112 / **6.336m**(= Unity (0.58+1.532)×3 逐位) | 2.322 / **11.61m**(= Unity (0.79+1.532)×5 逐位) |
| 血条世界宽 | 1.8m(= Unity 真值 60px×0.01×3) | 3.0m(Unity 因 x-override 0.03 实为 9m 宽,见 §6) |

(蒙皮模型高度用骨骼/AABB 探针测不准——绑定姿态与网格单位混合,最终以截图观感为准;由 540s 截图像素反推 L2 骑士可见身高 ≈4.5m 世界(×3 后),血条 6.34m 在其头顶上方。)

### 血条位置为什么改(HpAnchor 2.8/2.5 → 2.112/2.322)

旧值是 ×1 时代按胶囊顶(2.4/2.2m)留边调的。根缩放后旧值世界高 8.4/12.5m,首轮截图实测 **L2 血条直接出画**(相机仰角不够)。按真值公式(挂点+Canvas 内 1.532)×缩放 换算后:6.336m/11.61m,头顶上方、画面内 ✓。该口径与 L1 各怪(baotou 2.03/skeleton 1.53/bull 1.51,monsters.md #12)完全一致。

## 5.5 连带发现:billboard 材质默认丢弃世界缩放(影响血条/火球,已修;闪光光标受影响未动)

引擎实证(`godot/scene/resources/material.cpp:1238-1260`):`BILLBOARD_ENABLED` 的 MODELVIEW 只取 `MODEL_MATRIX[3]`(位移),**除非设 `FLAG_BILLBOARD_KEEP_SCALE` 否则缩放被丢弃**。lineup 探针像素实测:修复前 Boss 血条渲染 0.6m(与 ×1 狼同宽),修复后 L2 314px:狼 108px ≈ ×3 ✓、L3 277px:60px ≈ ×5 ✓。

- `MonsterHpBar.cs`:Bg/Fill 两个材质补 `BillboardKeepScale = true`。全项目怪物场景仅两个 Boss 根非 1 缩放(已逐个 grep 确认),其他怪渲染不变。
- `Fireball.cs`:火球材质补 `BillboardKeepScale = true`——**Boss 火球 ×10 视觉此前从未生效**(代码 `fb.Scale=10` 被 billboard 吞掉,lineup 实测 ×10 现为 5m 球);包头小怪火球 scale=1 不变。
- **未动(报主 agent)**:`FireSystem.cs:94` 命中闪光光标同走 billboard,而 `FireSystem.cs:309` `flash.Scale = flashScale`(距离衰减公式,照原作)当前被丢弃——开启 keep_scale 会改变 L1-L4 已校准画面,留待决策。BossHeart/Label3D 飘字/LaserSight 不受影响(不依赖节点缩放)。


## 6. 已知偏差/遗留(刻意不动,交主 agent 决策)

1. **L3 血条宽度**:Unity L3 血条 RectTransform x 缩放被 override 成 0.03(y/z 保持 0.01)→ 真值条为 60px×0.03×5=**9m 宽** ×0.5m 高的宽扁条(疑似原作调参,也可能是编辑事故)。本实现保持共享组件均宽(×5→3.0m),未复刻该 override;如需 bug-for-bug,给 MonsterHpBar 加每怪宽度参数。
2. **血条厚度**:真值可见条 0.6×0.05m(Fill/Background 锚 0.25~0.75),Godot 共享组件整面 0.6×0.1(monsters.md #14 既有项,归其范围,缩放后同比 ×3/×5)。
3. **火球起点**:Unity 锚点 local (0,3,0) 随 ×5 → 世界 +15m(疑似缩放前调参遗留);Godot `FireballHeight=1.5` 为代码常量,按"代码数值不变"约定保持。Boss 现在 11m 高,火球从腿部高度发出,与视觉手位不严格对齐。
4. **血花缩放**:Unity `SetParent`(worldPositionStays)保血花世界尺度 ×1;Godot `SpawnBloodFlower` Reparent(keep_global=false)+重置 local Transform → 血花随 Boss ×3/×5。视觉偏差小,未动。
5. **L3 Boss 移动胶囊**:世界 r=4.5m(Unity 移动 CC 仅 r=0.5,命中另走骨骼球)。街区行进理论上有卡道具风险;自检 born→追击→攻击→死亡链路全过,截图未见异常。
6. L2 Boss 出生路径(Unity 场景预摆原地激活 vs Godot 射线出生)是审计 L2-5 的既有声明项,不在本任务范围。

## 6.5 重大连带发现:L3 Boss 出生点街区块缺地面碰撞(既有,非缩放引入)

调试跑([dbg-rw] 临时日志)实测:Boss born 后 **出生点及走向相机的路径(x≈-96..-80 条带)无任何碰撞体**,Boss 原地穿地自由落体(4s 已到 y=-89,水平定格在 atk01 循环,永远进不了镜头)。探针(`probe_l3_floor.gd`)栅格检测:

- boss_pos (-83.83,-101.3) 全层射线:下方**空无一物**;邻街块 x=-98.8 列 floor y=0.10、x=-71 侧 floor y=-0.10,独缺中间条约 15m 宽;
- 对照:x=**+83.83**(未镜像位)有 floor y=0.10 —— 烘焙环境该街块缺失(镜像 bake 丢件),非 boss_pos 本身错误;
- Unity 侧 Boss 在同位置正常站立(Level3.unity 场景预摆 y=-0.02),Boss 只走 ~8m(44m→36m 攻击圈)后站定扔火球;
- 与缩放无关:×1/×5 胶囊都会掉(地面缺失与胶囊尺寸无关),自检从未覆盖(born 后即杀)。

**处理**:在 `level3.tscn` 追加 `BossGroundPatch`(不可见 StaticBody3D,盒 20×1×30,顶面 y=-0.02=Unity Boss 脚底高,layer1/mask0 照环境件),仅覆盖 Boss 出生→停步条带;注释标明"环境重导出补齐后删除"。不打出生射线(离机位 30m+,远超 8m 出生射线)、不影响其他怪路径。
顺带为验证需要给 `--level3-freeshot` 补了可选 `:delay`(原先只能开战即拍,拍不到 G6 才出场的 Boss;向后兼容,无 delay 时行为不变)。

## 7. 验证结果

- 构建:`dotnet build` 0 错误(构建锁协议执行;最终状态=两个 tscn 缩放 + level3.tscn 地面补丁 + freeshot delay + 两处 keep_scale)。
- 自检(最终状态,全绿):
  - `--level2-selftest` → `failed=False`(boss 15s 出场、Skill 出手、Skill1 旋转向量 ∈{SkillRight1,SkillLeft1} 实测 count=1、死亡→2s 胜利、胜利后清理等全 PASS)。
  - `--level3-selftest` → `failed=False`(boss born at meta boss_pos (-83.83,-0.02,-101.3) 精确 PASS、硬化皮肤压血击杀→2.4s 胜利等全 PASS)。
- 截图:见 §8。另有三只一次性探针(`tools/probe_boss_scale.gd`/`probe_l3_floor.gd`/`probe_boss_lineup.gd`)输出录于 §5/§5.5/§6.5。

## 8. 截图证据

产物 `tools/screenshots/audit/wave2_i/`(在产 1472×668,`--shot-res:1472x668`;lineup 探针为本机窗口默认 6400×2160)。
截图模式不杀怪,波次靠 25s 超时自然推进 → L2 Boss 实际于开战 ≈525~533s(G2+15s)出场;L3 Boss 于 G6 开局(≈763~795s,随宝箱 RNG 漂移)出场。bracket 截图为覆盖该时刻。

### L2(Boss 入场,~G2+17s 起)

| 文件 | 内容 | 结论 |
|---|---|---|
| `l2_ref_30s.png` | G0 参照:窗口/塔楼上的 toon 小怪 | 小怪基线体型 |
| `l2_final_540s.png` | Boss 出生即入画走向相机 | 体型已显著大于远景 toon |
| `l2_final_550s.png` / `l2_final_565s.png` | Boss 到位(相机前 12m 站桩)中央巨黑骑士,占画面纵向 70%+;**宽红血条(1.8m)在头盔正上方、画面内** | 体型 ✓ 血条 ✓(565s 帧可见 Skill1 出手倾斜姿态,Rotate 行为完好) |
| `l2_boss_540s.png`(首轮中间态) | 根 ×3 但 HpAnchor 还是旧值 2.8:血条(8.4m)出画 | "血条为什么改"的反例 |
| `l2_boss2_550s.png`/`l2_boss2_565s.png`(二轮中间态) | 血条位置已归真值但 billboard 吞缩放:0.6m 细条 | "keep_scale 为什么加"的反例 |
| `l2_lineup_boss_wolf.png` | lineup 探针:骑士 ×3 vs 狼,血条像素宽 314:108 ≈ ×3 | 缩放/血条宽度的定量裁决 |

### L3(Boss G6 开局出场)

| 文件 | 内容 | 结论 |
|---|---|---|
| `l3_boss_spawn_area.png` / `l3_boss_spawn_area2.png` | freeshot 俯瞰 boss_pos 街区:蓝色块覆盖整块、其下无碰撞(§6.5 穿地根源) | 地面补丁前的环境证据 |
| `l3_boss_845s.png`(补丁前) | Boss 已自由落体出画,只剩血条(3m 宽)从房顶后透出 | 穿地实锤(右上宽红条) |
| `l3_lineup_boss_wolf.png` | lineup 探针:石头人 ×5 vs 狼,血条像素宽 277:60 ≈ ×5;右侧火球 ×10(5m)vs ×1(0.5m)对照 | 缩放/血条/火球 ×10 定量裁决 |
| `l3_final2_body_792s.png` | 补丁后:Boss 立于街区走向相机,体型与三层砖楼相当 | 站立/行走恢复 ✓ |
| `l3_final2_bar_802s.png` | **Boss 正面全身 + 宽红血条(3m)在头顶上方 + 同屏僵尸小怪(底部,自带细条)对比 + 胸口火球刚生成(atk01)** | 体型显著大于小怪 ✓ 血条位置 ✓ 攻击行为 ✓ |
| `l3_final2_wide_815s.png` | 远景:Boss 立于蓝色块街区,血条在顶;HUD 绿闪=火球 Poison 命中玩家 | 命中结算链路 ✓ |

注:freeshot 会移动相机,Boss 的追击目标(相机)随之改变,故部分帧中 Boss 比 36m 攻击圈更近——这是截图挂接的副作用,非行为偏差(首轮纯游戏机位跑证实:Boss 按原作设计停在 36m 圈、在 G6 机位右偏 89° 画外扔火球,与 Unity 同构)。

### 火球/血条贴图遗留(非本轮范围)

- 火球 `fireball_core.png` 是 6×4 翻页图集,被整张贴到 quad 上且黑底未抠 → 画面呈"网格方块"(lineup 右侧大图)。Fireball.cs 视觉问题,影响包头小怪同款,报主 agent。
- 血条样式(厚度 0.05 vs 0.1、填充色 tint)为 monsters.md #14 既有项。
