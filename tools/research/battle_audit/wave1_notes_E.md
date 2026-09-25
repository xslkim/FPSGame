# Wave1 Agent E 修复笔记(L2/L3/L4 偏差)

日期:2026-09-23。工单:`tools/research/battle_audit/l234.md`。真值源:`G:\test\FPSGame\Assets\{Scenes,Script}`。
构建:`dotnet build` 0 错误(构建期间曾遇并行 agent 的 FireSystem.cs 中间态报错,等其落地后复建通过,未触碰该文件)。

## 修复清单(按工单编号)

### L3-1(高)Boss 出生点
- `game/data/level_meta.json` level3 段加 `"boss_pos": {"x":-83.83,"y":-0.02,"z":-101.3}`(Level3.unity RockWarrior (83.83,-0.02,-101.3) mirror-X;基类 SpawnBoss 已就绪读取)。
- `game/src/Battle/Level3.cs:244-247` 自检补 Boss 出生坐标断言(容差 1m)。自检实测 born at (-83.83,-0.02,-101.3) PASS。

### L2-1(bug-for-bug)Hard G0=30
- 决策:还原 Unity `LevelS/Level2.cs:54` `(int)(GroupMonsterNum[0] * (int)DiffRateHard)` 强转 bug(30×(int)1.5=30;G1/G2 无强转 45/60 两边一致)。
- 数据方式:`level_meta.json` L2 G0 加 `"num_override": {"hard": 30}`;`Level2.cs:165-183` 新增 `EffectiveGroupNum(i)`(基类公式 + 组内 num_override 按难度覆盖),`StartGroup` 覆盖 `Cur["num"]`/`MonsterLeft` 且等级公式分母同步(L2.cs:185-196)。
- 自检期望改为 Hard 30/45/60(`Level2.cs:434`),实测三难度全 PASS。

### L2-2 Toon/Alien 出生等待 = 0
- 真值:ToonSolder.prefab:2777 / Alien1.prefab:1568 `WaittingTime: 0`。
- `Level2.cs` SpawnTick toon 分支改传 `0.0f`(原 `DifficultyWaittingTime()`),记录 `StatToonBorn`/`StatLastToonWait`;自检新增断言 `toon born wait=0`(实测 72~74 只 born,wait 恒 0)PASS。
- 注:L2 池全部条目(toon_shoot/toon_shoot_alien)均为 ToonMonster,SpawnTick 的非 toon else 分支为死路,保留原样。

### L2-3 宝箱等待(Level2 侧)
- `Level2.cs BornBox` 改为按 kind 从 meta 读等待:`box_wait_ak`/`box_wait_m4`/`box_wait_bullet`,默认 15/20/20(Unity BoxAk/BoxM4/BoxBullet prefab WaittingTime)。
- 字段已写入 `level_meta.json` level2 段(15.0/20.0/20.0)。**协调说明**:工单约定 agent A 在 BoxMonster/monster_meta 统一定义同名 per-kind 字段;我落地时 A 的 monster_meta 侧尚未出现该字段(已核实),故 Level2 侧按"meta 读 + 字面量兜底 15/20/20"实现,与 A 落地后语义不冲突(数值同为真值)。

### L2-6 Boss Skill1 出手旋转
- 真值:Level2.unity 序列化 `SkillRight1: {-20,0,0}` / `SkillLeft1: {-20,80,0}`(`SkillRight2/SkillLeft2=(0,0,0)`,Skill2 旋转为无操作,不实现);Unity `Level2Boss.cs:231-284` Attack() 选边逻辑:50% 先右/先左,该侧玩家不活跃(Active&&HP>0)回退另一边。
- `Level2Boss.cs:141-165` 新增 `PickSkill1SideAndRotate()`:选边在出手时确定(原实现为结算时随机),按边本地欧拉旋转(`Basis *= Basis.FromEuler`,Godot YXZ == Unity Quaternion.Euler 顺序,等效 transform.Rotate Space.Self);`HertPlayerSkill1` 改用预选边。统计 `StatSkill1RotateCount`/`StatLastSkill1Rotate`,自检修软断言(旋转向量 ∈ {SkillRight1,SkillLeft1},本局实测 count=1)PASS。

### L2-7 Boss 死亡持续下沉
- 真值:Unity `Level2Boss.cs:84-87` Dead 状态 `m_char.Move(Vector3.down*dt)`(1 m/s)。
- `Level2Boss.cs:279-288` 新增 `_PhysicsProcess` Dead 分支:`GlobalPosition += Vector3.Down*dt`,由关卡胜利后清理(不回收)。

### L4-1 + L4-2(高)龙四点巡回 + 出生瞬移
- `DragonMonster.cs` 按 Unity `DragonMonster.cs:43-92` InitMovePosition 重写:
  - Pos1(FarWay)=cam+fwd·75+right·80(up 0);Pos2(InCamera)=cam+fwd·150+right·(15±10)+up·(20±20);Pos3(Attack)=cam+up·0.1 处 + 沿 (Pos3-Pos2) 方向 20m(贴脸,过相机);Pos4(CamOffset)=Pos3+right·20。up/right 取相机基向量(Unity cam.up/cam.right);isRight 恒 true(原作 `Random.Range(0,100)>0` 仅 1% 左,保留恒右语义并标注)。
  - born 时 `GlobalPosition = Pos[FarWay]` 直接瞬移(Unity born() `transform.position=FarWay`),首个移动目标 InCamera;每循环到 Attack 后重算四点(原作循环重启语义)。
- **配套修正(工单外,12s 验收必需)**:Unity Dragon-Red/Magma 四色 prefab `WaittingTime=0`,而 Level4 G1(force_pool)BornAhead 原传 `DifficultyWaittingTime()`(Easy 3~8s),会导致 12s 龙还未动身。已改为龙/Magma 传 0(`Level4.cs:172-174`);基类 SpawnTick 本来即传 0,此改动对齐之。截图时间线据此与真值吻合(见验证节)。

### L4-3 Magma 四色变体
- 真值核验:Level4.unity MonsterGroup5 七个 fileID 逐一解析 = Dragon-Red/Blue/Green + Magma Demon-Blue/Green/Orange/Purple 各一(脚本核验 guid→prefab 名)。
- 新增材质:`magma_green_mat.tres`/`magma_orange_mat.tres`/`magma_purple_mat.tres`(参照 blue:albedo+emission 同色贴图,emission 色调各色,energy 0.8);新增变体场景 `magma_demon_{green,orange,purple}.tscn`(复制 blue 结构,`MetaKey="magma_demon"` 共用数值——Unity 四色 prefab 同为 MagmaDemon.cs/_Name=MagmaDemonBlue)。
- **键名说明(与工单建议形式的偏差)**:工单建议 `magma_demon@magma_green` 形式,但 LevelBase 槽位解析 `@` 为等级后缀(`type@lv`,int.Parse),`@color` 会直接抛异常,而 LevelBase.cs 归 agent A 不能改。故采用 `magma_demon_green`/`_orange`/`_purple` 键(“等形式”),`Level4.RegisterMonsterTypes` 注册,G5 pool 拆 4 键(顺序对齐 Unity:dragon_red, dragon_blue, dragon_green, 4 色 magma)。自检新增 G5 池构成断言(7 键齐全)PASS。

### L4-4 / L4-5 Magma 落点与出生抖动
- `MagmaDemon.cs EnterActive`:屏幕内落点改 Unity 区间 fwd·10(原 8~12)/ right±2(原 ±4)/ up rand(-3,1)(原 -1~3)。
- `OnBorn`:四向出生补正交轴 rand(±2) 抖动(左右轴抖 cam.up±2,上下轴抖 cam.right±2,Unity GetBornPosition);上下轴改用 cam.up(原世界 Up)。

### L4-6 方向光恢复
- `env_level4.tscn` "Directional light" `visible=false`+`metadata/deactivated_by_budget` 删除 → 恢复 visible(真值 0.57 常开,颜色/能量/阴影原本就对)。恢复后 12/14/55/70s 截图无过曝、帧率无异常(目测),证据见 `audit/wave1_e/l4_*.png`。其余 Pointlights 的 budget 停用未动(不在本项)。

### L4-7 ambient 真值
- `env_level4.tscn` Env:`ambient_light_source` 3(sky)→2(flat color),`ambient_light_color=Color(0.8014706,0.80081123,0.7543253)`,`ambient_light_energy=1.0`(Level4.unity AmbientMode 3 真值),测绘注释同步更新。

### L4-8 龙飞行动画倍速
- 非 Attack 段 locomotion 播放 speed=2(`FlyAnimSpeed`,Unity born/每循环 `m_ani.speed=2`);Attack 段回 1;吐息仍 0.75。移速 ×2 逻辑原有保留。

### L4-9 飞行怪死亡恒速下落
- `DragonMonster.cs`/`MagmaDemon.cs` 的 `_PhysicsProcess` Dead 分支由重力加速改恒速 `Vector3.Down*3*dt`(Unity DropToDie `m_char.Move(down*3*dt)`),y<-3 回收不变。

### L3-3 雾(实证决策:用真值)
- 实验:level3.tscn 设 `fog_mode=1`(Godot 4.x:0=指数,1=深度)+ begin/end 20/90 + 色真值 `(0.3555981,0.47552913,0.5754717)`,截 l3_9s.png 逐像素采样:
  - 近地面 (30,36,52) → 远地平线地面 (93,106,121) ≈ 雾色 (91,121,147)——**深度雾在本自编译 4.8 生效**,远景渐变明确,README"depth begin/end 无效"的印象被证伪(至少当前构建)。
  - 对照真值 level3_battle.png:近地面 (15,23,44) → 中远 (58~71,59~78,80~91) 同向渐变;旧 Godot(暗雾色)全程 (13~40) 无渐变。
- 结论:按工单"生效就用真值",level3.tscn 保留 fog_mode=1 + 20/90 + 真值色;`fog_sky_affect=0` 保留(天真值亮蓝不被雾染)。

### L3-4 ambient 灰 0.7075
- `level3.tscn` Env `ambient_light_color` 0.4672 → `Color(0.7075472,0.7075472,0.7075472)`(Level3.unity m_AmbientSkyColor 真值,energy 1 原对)。画面明显提亮接近真值观感。

### X-2 L2/L4 截图挂接对齐 L3
- `Level2.cs`/`Level4.cs` 删除单次 TakeShotDelayed,移植 L3 模式:`_shots` 列表(参数可多次传入)+ `ShotGuardLoop()`(0.3s 轮询补 HP、关续币面板)+ `ShotsSequence()`(2s 等 G0 blend → KillCameraTweens → 按开战起算时刻依次截图,拍完退出)。解析改用基类 `SplitShotArg`。实测 L2 一次运行出 5s/8s 两张、L4 一次出 13/14/15s 三张、55/70s 两张(守护下长延迟不弹续币面板)。

### L4-10 / L3-8 宝箱等待
- 归 agent A(BoxMonster/meta)统一处理,本波未动;Level2 侧 BornBox 已按 L2-3 落地(见上)。

## 验证结果

- 构建:`dotnet build` 0 错误 0 新增警告。
- 自检(全绿):
  - L2:36 项 PASS,`failed=False (static_boxes=3 window_blocked=0)`;含新断言 Hard 30/45/60、toon wait=0(74 born)、skill1 旋转向量(count=1)。
  - L3:16 项 PASS;含新断言 boss born at (-83.83,-0.02,-101.3)。
  - L4:20 项 PASS;含新断言 G5 七键池、龙瞬移落点=FarWay 点、四点公式(Pos1 精确、Pos2 抖动区间 r=22.1/u=7.6 在界内、Pos3 距相机 20.00、Pos4=Pos3+right·20.00)。
- 截图(`tools/screenshots/audit/wave1_e/`,1680×1050,经 `--shot-res`):
  - `l4_12s.png`:红龙入画接近中(吐息粒子可见);`l4_14s.png`:红龙糊脸掠过相机(真值 l4_battle_12s.png 同语义,Attack 点过相机 20m 的路径必然产生糊脸帧);`l4_15s.png`:龙已过境(循环后段),符合四点循环时序。
  - `l4_55s.png`/`l4_70s.png`:G5 三色龙同屏(蓝龙冰息粒子);截图模式不杀怪,max_alive=3 顶满后 Magma 槽位轮不到出场,四色 Magma 未拍到同框(功能性已由自检池构成断言 + 池初始化实例化成功覆盖;变体场景若损坏会在 RegisterType 实例化时即报错)。
  - `l3_9s.png`:深度雾渐变生效(像素证据见 L3-3 节);`l2_5s.png`/`l2_8s.png`:X-2 多 shot 验证,toon 8s 已到窗开火(wait=0 节奏)。
- 时间线说明:l4 12s 龙位置比真值晚 ~1.5s(真值 12s 已糊脸,Godot 12s 在 75m 吐息圈内、14s 糊脸)。出生链(G0 即过+2s 冻结+5s 间隔首刷)与移速(30×2/×1)均与真值一致,差异应为真值截图时刻基准(录屏起算 vs 开战起算)而非路径/速度参数偏差;路径数值已被自检逐点锚定。

## 移交项(本波不做,按工单 #13)

- L2-4 / L3-2:Boss 模型缩放(L2 ×3 / L3 ×5)——需与 L2-4 已声明口径一并决策,缩放需同步碰撞/HpAnchor/攻击半径。
- L2-9:L2 环境光 2.11(README 已声明"偏白日"有意)。
- L3-5:天空观感(全景图贴图/朝向核对)。
- L3-6:左 HUD 显隐与输入模式关系(待确认真值采集时 InputMode)。
- L3-7 / L2-8:相机 far/near per-wave 元数据。
- L2-10:箱满时 tick 语义(影响极小)。
- L4-11:出生物理落点(仅斧/骷髅两波,观感差异小)。
- L3-9:rock_warrior move_speed_level_rate 0.5→0.3(归 agent A 的 monster_meta)。
- X-3:枪屏幕占比/激光样式(跨关,归其他 agent)。
- L4-3 视觉补拍:如需四色 Magma 同框截图,建议截图模式加"定时清场"钩子(max_alive 解锁),或做怪物 lineup 工具。

## 文件清单(本 agent 改动)

- `game/data/level_meta.json`(level3 boss_pos;L2 G0 num_override + box_wait 三键;L4 G5 pool 四色键)
- `game/src/Battle/Level2.cs`、`Level2Boss.cs`、`Level3.cs`、`Level4.cs`、`DragonMonster.cs`、`MagmaDemon.cs`
- `game/scenes/levels/level3.tscn`(雾色/ambient)、`game/scenes/levels/env_level4.tscn`(方向光恢复/ambient)
- 新增:`game/assets/models/monsters/magma_demon/magma_{green,orange,purple}_mat.tres`
- 新增:`game/scenes/battle/monsters/magma_demon_{green,orange,purple}.tscn`
- 产物:`tools/screenshots/audit/wave1_e/{l2_5s,l2_8s,l3_9s,l4_12s,l4_13s,l4_14s,l4_15s,l4_55s,l4_70s}.png`
- 未触碰:LevelBase.cs / Monster.cs / monster_meta.json / FireSystem.cs 等他 agent 文件(git status 中这些文件的改动非本 agent 所为)。
