# 战斗 HUD + 开火系统 + 战斗内面板 对照审计报告

审计对象:Unity 2019.4 原作(G:\test\FPSGame,真值) vs Godot 4.8 移植(G:\FPSGame\game)。
审计方式:prefab/代码逐项对照 + 实机截图对比。
审计日期:2026(以最新会话为准)。未修改任何 game/ 与 Unity 工程文件。

## 截图证据

| 文件 | 内容 |
|---|---|
| tools/screenshots/audit/hud_fire/godot_battle_25s.png | Godot 战斗 25s(对位 l1u_gun_25s.png) |
| tools/screenshots/audit/hud_fire/godot_intro_15s.png | Godot 开场 15s(对位 l1u_intro_15s.png) |
| tools/screenshots/audit/hud_fire/godot_boss.png | Godot G4 Boss 波(爱心弱点) |
| tools/screenshots/audit/hud_fire/godot_g1.png | Godot G1 波 |
| tools/screenshots/audit/hud_fire/godot_battle_13s.png | Godot 战斗 13s(牛魔王近身) |
| 真值:tools/screenshots/l1u_gun_25s.png / l1u_intro_15s.png / l1u_gun_10s.png / l1u_battle_45s.png | 原作实测 |

**关键实测结论(真值截图重新测量)**:
- 真值游戏实际窗口为 **736×334**(l1u_battle_45s.png 原生尺寸即 736×334;1472×668 截图为 2 倍)。Unity HUD 为 CanvasScaler **ConstantPixelSize scaleFactor=1**(PlayerSystem.prefab:1921-1924),1 canvas px = 1 屏幕 px。
- 真值战斗画面(l1u_gun_25s / l1u_battle_45s):**无任何玩家 HP 绿条**(prefab 里 HP Slider 存在但画面不可见);有细红激光 + 极小深红点、裸青色双竖条暂停键(顶部居中,无可见底框无文字)、绿色子弹×3+"× 90"(底部偏右)、金币"×10"(底部居中偏左)、左粉环(无图标)右蓝环(带头像)、牛魔王头顶**红色细血条**。
- Godot 当前构建:暂停键为**不透明深蓝方块**;开场 19s 内也显示暂停键(真值无);头像下方多出**两条可见 HP 绿条**;命中点为**大橙色光团**;子弹显示 **120**(真值 90);**怪物血条完全不渲染**(godot_boss.png / godot_battle_13s.png / godot_g1.png 及旧图 l1_boss.png、l1_fix8.png 均头顶无条);战斗面板所有文字(标题/按钮/倒计时/金币数)**实际不可见**。

---

## 总结

14 项中:**基本一致 3 项**(9 枪挂点数值、13 玩家数值中的 HP/扣币/宝箱/音效数、7 枪口火光结构复用),**有偏差 11 项**。高严重度 4 条:

1. **战斗面板文字全灭**:InGamePanel 所有 Label3D `PixelSize=0.001`,面板根 `Scale=0.001`,文字被双重缩小 ~1000 倍,三面板只剩贴图框,无任何文字(InGamePanel.cs:94, 15)。
2. **怪物血条不渲染**:MonsterHpBar 代码静态完整,但实测任何怪/Boss 头顶均无血条(待确认根因)。
3. **命中红点视觉错误**:应为右手红点(Flash.prefab)/左手绿点(FlashGreen.prefab)的极小粒子点,现为双手共用 0.12m 橙色加色 Quad + 0.09m 红色光斑,截图呈 ~70px 大橙团(真值 ~28px 深红点)。
4. **玩家 HP 绿条可见且位置错**:真值画面无 HP 条;Godot 在头像环下方绘出 438×16 可见绿条。

其余中低严重度偏差 20+ 条,明细见下表。

---

## 逐项核对(1-14)

### 1. 暂停按钮 — 偏差

| 子项 | 结论 |
|---|---|
| 位置 | 一致(顶部居中,canvas 中心 (640,60)) |
| 尺寸 | 一致 120×120(但整体面板缩放 0.001 vs 原作 0.00115,实际显示小 ~13%,见 #11) |
| 热区 | 一致(Unity BoxCollider 120×120×1;Godot BoxShape3D 120×120×0.05,均可被射线命中) |
| 触发方式 | 一致(枪射线命中 Button(tag "Button" / layer 3)+ 扳机电平 → onClick/on_shot) |
| 样式 | **偏差**:①原作底框 (0,0,0.4528,**0.2353**) 半透明(InGamePanel.prefab PauseBtn Image),Godot 不透明(UiButton3D.cs:50-59 仅在贴图非空时才开 Transparency,PauseBtn 无贴图路径 → alpha 失效,截图为实心蓝块);②原作无文字(子 Text 空串 14 号),Godot 加"主菜单"40 号白字(InGamePanel.cs:146-148,且因 pixelSize 问题不可见);③原作暂停图标全幅拉伸 120×120,Godot 仅 80×80(InGamePanel.cs:149-151) |
| 可见时机 | **偏差**:真值开场(l1u_intro_15s/l1u_gun_10s)无暂停键;Godot 开场即显示(godot_intro_15s.png 顶部蓝块)——InGamePanel 节点常驻可见,无原作的开场隐藏逻辑 |

### 2. 子弹计数 / 金币 — 偏差(右子弹 X 位置、左子弹 Y、数字)

| 子项 | 结论 |
|---|---|
| 子弹图标 | 一致:Bullet.png(3 颗弹)64×64,右手绿 (0,0.7255,0.2824) / 左手黄绿 (0.7062,0.7255,0),与 prefab 色值一致(PlayerState.cs:177-180) |
| 右手位置 | 一致:底中 +(185.12,0)(PlayerState.cs:177;实测 godot_battle_25s 与真值位置吻合) |
| 左手位置 | **偏差**:原作 anchor(0.5,0) pivot(0.5,**0.5**) pos (-258.72,31.4) → 图标中心在屏底上方 31.4px;Godot MakeBulletHud 按 pivot(0.5,0) 语义摆 OffsetTop=31.4-64、OffsetBottom=31.4 → 中心仅在屏底上方 0.6px,**低 32px**(PlayerState.cs:193-197)。单人模式左手隐藏,仅双人模式可见 |
| "×"位置 | **偏差**:原作 X 相对图标中心 (+58.43,-23.58)、28 号暗绿色(在图标右下方);Godot 摆在图标正中心(PlayerState.cs:218-222),截图可见 × 叠在中间子弹上 |
| 数字 | **偏差(数值)**:Godot 显示 120(真值 90,见 #13);字体 52 白一致;数字垂直中心 Godot 在图标中心上方 3.1px,原作在下方 3.1px(±6.2px,Unity +y 向上符号未翻转,PlayerState.cs:233 `-3.1f - 30.0f`) |
| 金币 | 一致:(-26.02,0) 底中、coin.png 64×64、X 32 号暗金 (0.5189,0.4779,0)、数字 52 白(PlayerState.cs:98-145;截图一致) |
| 战斗外隐藏 | 一致:开场只见金币+头像(godot_intro_15s.png ✓,ShowBattleBullets 开战才显子弹,PlayerState.cs:385-392) |

### 3. 头像圆环 / HP 条 — 偏差

- 圆环:一致。左 headBgRed(粉) 右 HeadBg(蓝) 128×128 角落锚定、头像 Icon1.png 100×100 居中;左手未激活时左环无图标(PlayerState.cs:239-272, 374-375;截图与真值一致)。
- **HP 条偏差**:
  - 真值:prefab 内 HPRightSlider (514,333) / HPLeftSlider (-70.3,333)(全屏容器中心锚),438×16,底 2-Empty.png(暗轨)、填充 2-AppleGreen.png(绿),值=HP/100(PlayerSystem.cs:62/92)。**但真值截图满血状态左右均无可见 HP 条**(l1u_gun_25s 顶部条带逐像素确认;l1u_battle_45s 原生 736×334 同)。
  - Godot:在头像环正下方(环底中 +(-219,+8))绘制可见 TextureProgressBar 绿条(PlayerState.cs:273-285),godot_battle_25s/godot_intro_15s 顶部两条亮绿条清晰可见。
  - 结论:可见性偏差(真值不可见)+ 位置偏差(原作在屏幕顶部 y≈27px@720p,Godot 在 y≈136px 环下)。**待确认**:原作 Slider 不可见原因(可能 Fill 偏移 (-219,-4) + 暗色轨与场景融合);建议按真值截图处理——隐藏或按原作坐标摆放。

### 4. 受击红屏 — 偏差(峰值透明度/色调),逻辑基本一致

- 真值(PlayerSystem.cs:262-300 UpdateHurtEffect):贴图 bloodEffect.png/bloodEffectRight.png(prefab 色 (1,0,0,0.392));运行时物理=**黄 (1,1,0)**、冰=青 (0,1,1)、毒=绿 (0,1,0),**起始 alpha=1**,每 0.1s 减 0.05,约 2s 淡出,结束后 alpha 复位 1。冰→Frozen 至淡出结束(该手不可开火);毒→淡出结束再扣一次等额血并刷新 UI。
- Godot(PlayerState.cs:318-336):贴图一致;颜色 (1,0.85,0.2)/(0.3,0.9,1)/(0.4,1,0.3),**起始 alpha=0.392**;0.2s 保持 + 1.6s 渐隐 = 2s。Frozen 2s 定时器 + freeze_loop;Poison 2s 后二次等额扣血 ✓。
- 偏差:①峰值 alpha 0.392 vs 原作运行时 1.0;②三色为近似值非原值(黄/青/绿);③Godot 冰冻手在**暂停时也跳过**开火(FireSystem.cs:269-273),原作仅非暂停跳过(FireSystem.cs:232-237,暂停时冰冻手仍可按按钮)——边缘行为差异;④冰音效:原作 IceAS 一次性,Godot freeze_loop.wav 循环 2s(待确认听感)。

### 5. 激光 — 偏差(宽度曲线/终止行为/多余红点)

| 子项 | 真值 | Godot | 结论 |
|---|---|---|---|
| 颜色 | 右红 (0.990566,0,0,0.8157)(Lazer.mat),左绿 (0,0.9922,0.1302,0.8157)(LazerLeft.mat) | LaserSight.cs:14-15 同值 | 一致 |
| 混合 | FPSLazer.shader `Blend SrcAlpha One`、Lighting Off、ZWrite Off | StandardMaterial3D Add + Unshaded(LaserSight.cs:54-58) | 一致 |
| 生成方式 | LineRenderer 局部坐标 (0,0,0)→(0,0,200) 挂枪下,**恒 200m**(被墙体/怪遮挡部分由深度测试剔除;打怪时光束穿过怪继续延伸,怪轮廓旁可见) | 圆柱体枪口→**射线命中点**即止,未命中伸 200m(FireSystem.cs:304-311) | **偏差**:打怪时光束止于怪表面,原作穿透 |
| 宽度 | widthCurve **0.0089→0.03**(近细远粗;FireSystem.prefab LazerRight:5108/5117, LazerLeft:185/194) | 恒定半径 0.015(直径 0.03,FireSystem.cs:100-102) | **偏差**:近处约 3 倍粗(任务书所写 0.023→0.03 与 prefab 实测 0.0089→0.03 略有出入,以 prefab 为准) |
| 贴图滚动 | 双采样相乘:tex(uv+t/20×(14,0)) × tex(uv+t/20×(-12,0)) ×2;平铺 (500,1)/200m=2.5/m | 单采样 6 cycle/s 滚动;TilePerUnit 2.5(LaserSight.cs:18-19,144-150) | 轻微偏差:平铺率一致,滚动速度/双采样结构不同(视觉近似) |
| 终点红点 | 无(命中指示全靠 Flash) | 额外 0.09m billboard 光斑(FireSystem.cs:100-102 withDot:true;LaserSight.cs:84-97) | **偏差**:与橙色 Flash 叠加成双指示器 |
| 起点 | 枪原点(0.1,-0.04,0.16) | 枪口 Muzzle(原点前方 0.088) | 轻微偏差 |

### 6. 命中红点 Flash — 偏差(颜色/尺寸/双手区分),公式一致

- **公式完全一致**:len=相机→命中点距离;`flashScale = 1-clamp01(3/len)×0.8`;`Offset = 0.5-clamp01(1/len)×0.40`;位置 `hit.point - dir×Offset`;未命中隐藏(Unity FireSystem.cs:256-290;Godot FireSystem.cs:314-320)。
- **视觉偏差**:
  - 原作:右手 Flash.prefab(红)/左手 FlashGreen.prefab(绿)(GlobalObject.prefab Flash1/Flash2);根 scale 0.5,子节点 Darkness(startSize 0.5)+Shockwave 两个循环粒子;真值截图为 **~25-30px 深红点**(1472 宽下)。
  - Godot:双手共用 `QuadMesh 0.12m` × flashScale,加色 (1.0,0.85,0.35,**0.45**) flash_point19.png(FireSystem.cs:79-96),另叠 LaserSight 0.09m 红点;截图为 **~70px 橙色光团**(godot_battle_25s/godot_g1)。
  - 量化:len=8m 时 flashScale=0.7 → quad 8.4cm + 软边;真值等效亮核 ~2-3cm。偏差:颜色(橙 vs 红/绿)、尺寸(~2.5 倍)、双手无红绿区分、双指示器叠加。

### 7. 枪口火光 — 基本一致(复用同源),两处偏差

- 一致:Fire 成功即 `_flash.Fire()`(≈原作 `_FireFlash.SetActive(false→true)`);MuzzleFlash.cs 为菜单界面已 1:1 校准的同一组件(Flame 2×4 翻页 0.15s 加色 (1,0.803,0.706)、Smoke 8×8 (0.596,0.596,0.596,0.161) 1.5s、OmniLight 曲线 0→0.979(0.005s)→0(0.15s) 色 (1,0.393,0) 范围 6),战斗内复用结构一致(GunBase.cs:39-40,53)。
- 偏差①(**枪口挂点**):Godot 三枪统一 Muzzle(0,0,-0.088)(FireSystem.cs:191-193)——这只是 AK47 的真值(AK47.prefab Sphere z=0.088);原作 **HandGun Sphere z=0.042、M4 Sphere z=0.162**(Guns/HandGun.prefab、Guns/M4.prefab)。默认手枪火光/激光起点偏前 4.6cm,M4 偏后 7.4cm(相对于枪)。
- 偏差②(**后座动画缺失**):原作 Fire 时 `_Ani.Play()` 播枪自身 legacy 开火动画(HandGun.cs:36 等),Godot GunBase.Fire 无任何枪体动画(GunBase.cs:44-57)。
- 备注(待确认):原作每把枪 MuzzleFlash1 有效缩放不同(HandGun 0.01×(4,4,8)=0.04、AK47 0.08×(4,4,8)=0.32、M4 =0.04),Godot 统一尺寸,AK 火光在原作明显更大。

### 8. 换枪 — 基本一致,两处差异

- 一致:默认手枪 GunType=2(Player.cs:15,24);Born 三枪全开 0b111(Player.cs:23);NextGun 枚举顺序循环、回绕、仅一把返回 false(Player.cs:32-53 ≈ Unity Player.cs:66-86);总时长 0.5s 收 + 0.55s 出 = 1.05s(FireSystem.cs:23-24)。
- 差异①(**出枪动画对象**):原作 ChangeGun 收起旧枪后,**新枪在 CreateGun 中直接出现在最终位置**——之后的"伸出"循环作用于已 SetActive(false) 的旧枪(Unity FireSystem.cs:171-177,`gun` 局部变量仍指旧枪),即原作视觉上新枪是**瞬间出现**的;Godot 让新枪从 +0.10 升起(FireSystem.cs:388-392)。Godot 实现了文档意图,但与原作字面行为不同(**待确认**以哪边为准)。
- 差异②:Godot 换枪 1.05s 全程禁扳机(`_switching`,FireSystem.cs:324),原作不挡(新枪立即可射)。

### 9. 枪挂点 — 一致

- data/fire_meta.json 与 Resources/firemetajson.json 逐值一致:AK47 pos (0.10,-0.04,0.16)/pos60 (0.12,-0.045,0.16)/CD 0.2;M4 pos (0.10,-0.05,0.16)/CD 0.1;HandGun pos (0.10,-0.04,0.16)/CD 0.5;攻击均 10、耗弹均 1。
- 左手 x 取反 ✓(FireSystem.cs:241-242);Unity +z→Godot -z 取反 ✓;FOV≤50 用 pos:Godot 硬编码 `wide=false`(FireSystem.cs:237),战斗相机 FOV 30(开场)→45(战斗),均 ≤50,结果与原作一致(原作判 `Camera.main.fieldOfView > 50` 才用 pos60)。
- 备注:FireSystem.cs:237 注释"战斗相机 FOV 恒 45"与 tscn 初始 FOV 30 不符,仅注释陈旧;FOV>50 分支未实现(当前无该场景)。

### 10. 命中特效池 — 偏差(池数量/血颜色)

| 子项 | 真值 | Godot | 结论 |
|---|---|---|---|
| 种类/轮换 | Wood/Metal/Blood/Dust/Concrete 轮换(idx++ 取模) | 同(FireSystem.cs:33-40,418-431) | 一致 |
| 池数量 | Wood 3、Metal 3、Blood 3、Dust 3、**Concrete 4**(FireSystem.prefab:10158-10178) | 全部 3(PoolSize=3,FireSystem.cs:20) | 轻微偏差:Concrete 少 1 |
| 死亡爆血池 | _BloodEffects = Blood.prefab **×3** | blood_flower **×5**(BloodFlowerPoolSize=5,FireSystem.cs:21) | 偏差:5 vs 3 |
| 打怪关弹痕 | 打怪 `_Dankon.SetActive(false)`,打场景开(FireSystem.cs:339-347) | `Hole.Visible = !isEnemy`(FireSystem.cs:427-429) | 一致(注:impact_dust.tscn 的 Hole 是 MeshInstance3D,`GetNodeOrNull<Sprite3D>` 取不到,Dust 弹痕永不切换;怪 tag 不会返回 Dust,实际影响小,待确认) |
| 缩放 | `Vector3.one × flashScale × 3` | `Vector3.One × flashScale × 3.0`(FireSystem.cs:426) | 一致 |
| 血特效颜色 | **GreenImpact(绿血!)**(truth §7;Impacts/Mobile/GreenImpact.prefab) | impact_blood.tscn 暗红 (0.65,0.04,0.04)/(0.5,0.03,0.03) | **偏差**:打怪飙血应为绿色 |
| 怪物 tag 映射 | 默认 Blood;骷髅/牛魔王/Baotou Concrete(SKMonster/NmwMonster/BaotouMonster) | monster_meta.json 同(defaults Blood;skeleton/bull/baotou Concrete) | 一致 |

### 11. InGamePanel 三面板 — 偏差(多处)

挂点:挂相机 z=-1(Godot -Z 前)✓;面板根 Scale **0.001** vs 原作 **(0.00115,0.00115,0.01)** → 全部面板/暂停键显示尺寸小 ~13%(InGamePanel.cs:15) → 偏差。

**高严重度:面板全部文字不可见** — MakeText/UiButton3D 的 Label3D `PixelSize=0.001`(InGamePanel.cs:94),文字世界尺寸 = 字号 × 0.001(局部单位),再经面板根 ×0.001 → 双重缩小约 1000 倍。三面板只剩 messagebox 底板/按钮贴图/装饰条/胜利横幅,无任何文字;暂停键"主菜单"缺失已在 godot_battle_25s 裁剪图证实。

布局逐项(1280×720 canvas 坐标,已与 InGamePanel.prefab RectTransform 逐一核对):

| 元素 | 真值 | Godot | 结论 |
|---|---|---|---|
| Pause 面板 | 600×500,messagebox (1,1,1,0.8745) | 600×500 同色 | 一致 |
| 标题"暂停" | 60 号白 @y=185.8,装饰条 ±173.2 | 同(AddTitle 60 号) | 一致 |
| BackGameBtn | "返回游戏" 52 号 (0.914,1,0.821),400×100 @(6,45) | 同 | 一致 |
| Pause-MenuBtn | "主菜单" 52 号,400×100 @(6,-123) | 同 | 一致 |
| Continue 面板 | 500×500 | 500×500 | 一致 |
| Continue 标题 | "继续游戏" **50 号** @(5.2,202),装饰条 -190/+199 | AddTitle **60 号**,±190 | 偏差:字号 60 vs 50;右条偏移 9px |
| TimeText | 40 号 (0.852,0.858,0.981) @(0,77.4) | 同位置同色 | 一致(格式见下) |
| InfoText | 40 号 (0.939,1,0.717) @(0,-33) 400×150,**运行时动态文本** | 硬编码文本 | 见行为偏差② |
| Coin/X/金币数 | 64×64 @(-48.4,136);X 32 白;数 42 (1,0.8425,0) | 同 | 一致 |
| ConfirmBtn | 200×80 @(-125,-168),文字 40 白 | 200×80 40 白 | 一致 |
| Continue-MenuBtn | 200×80 @(137,-168),"主菜单" **40 号白** | **52 号 (0.914,1,0.821)** | 偏差(潜在,文字当前不可见) |
| Victory 面板 | 800×500,"游戏胜利" 60 号,VICTORY.png 400×100 @(0,24.5),仅 MenuBtn 300×80 @(6,-128) | 同 | 一致(热区 300×80 vs 原作 collider 250×50,轻微) |

行为偏差:
- ①**兑换子弹分支未实现**:原作 OpenContinue(bullet=true) 把标题改"兑换子弹"、Info 改"兑换子弹需要消耗一枚游戏币…"、按钮改"兑换子弹"(InGamePanel.cs:74-104);Godot OpenContinue 只存 `_continueIsBullet` 标志,文案恒为"继续游戏"(InGamePanel.cs:220-230)。
- ②**Info 文本数值错**:原作动态生成"每 **3** 分钟增加一枚游戏币,最高 **10** 枚"(AddCoinTime=180s、MaxCoin=10,UserMeta.cs:25-27);Godot 硬编码"每 **15** 分钟…最高 **5** 枚"(InGamePanel.cs:174-176)。
- ③**"币不足"未实现**:原作币不足时按钮文本"币不足"(InGamePanel.cs:90-93);Godot 仅 `SetEnabled(false)` 置灰,文字不变(InGamePanel.cs:227)。
- ④**金币回复时机**:原作仅在 Continue 面板打开期间计时加币(InGamePanel.cs:178-198 Update 门控);Godot SaveService 后台持续回复(SaveService.cs:63-66) → 经济行为偏差。
- ⑤**暂停键隐藏**:原作任一面板打开都 `PauseBtn.SetActive(false)`,关闭时恢复(InGamePanel.cs:59/87/115/215);Godot 仅 Victory 隐藏(InGamePanel.cs:253) → 暂停/续币面板打开时暂停键仍显示。
- ⑥BackToMenu 1s 防误触(`Time.unscaledTime - startOpenContinueTime < 1` 直接 return,InGamePanel.cs:32-35)未移植。
- ⑦倒计时格式:原作 `min + ":" + second` 不补零(如 "2:5");Godot `{m:D2}:{s:D2}`("02:05");Godot 另加"金币已满"文本(原作无)。
- 一致项:timeScale=0(GetTree().Paused ✓)、暂停期按钮可射(FireSystem ProcessMode Always + layer3 先判 ✓)、Victory 仅 MenuBtn → BackToMenu ✓、ContinueGame 扣 1 币 + Relife(HP=100、子弹 +=120)✓。

### 12. 怪物血条 — 偏差(实测不渲染 + 高度/飘字数值)

- 结构对照:原作 MonstHp = 世界空间 Canvas(renderMode 2)+ Slider 60×10,底 Hp_Green_bg.png、填充 Hp_Yellow.png(贴图实为紫红色)白色 tint,根 scale 0.01,每帧 `transform.LookAt(Camera.main)`(MonstHp.cs:32;HpReduceNumber.prefab)。Godot = 0.6×0.1m billboard quad ×2,同贴图,填充加红色 tint(结果同为红条)✓ 结构近似一致。
- **血条高度偏差**:真值 = HpReduceNumber 根 y + Canvas y 153.2×0.01=1.532m:骷髅/斧/飞斧 = **1.53m**(根 y=0)、牛魔王 = **1.89m**(Bull.prefab 根 y=0.36)、Baotou = **1.72m**(根 y=0.189)、宝箱 = **2.47m**(BoxBullet.prefab 根 y=0.937)。Godot:skeleton/axe/fly_axe/bull 均 **2.1**、baotou **2.5**、box **1.0**(各 tscn HpAnchor) → 普遍偏高 0.2~0.8m,宝箱反低 1.5m。
- **高严重度:实测血条不渲染**。godot_boss.png(Boss 头顶 y40-400、x300-1100 区域红/绿像素为 0)、godot_battle_13s.png(牛魔王近身无条)、godot_g1.png,以及旧图 l1_boss.png、l1_fix8.png 均无血条。MonsterHpBar._Ready/SetHp 静态检查完整(Born 调 SetHp(1) → Visible=true,Monster.cs:86),根因**待确认**(疑似渲染/可见性问题,需调试)。
- 掉血飘字偏差:原作 "- N" 红字 (1,0,0) 14 号、初始 (0,10)、**上升 5px/s、alpha 1→0.3**(约 0.7s)、每怪 3 个池(MonstHp.cs:43-85;HpReduceNumber.prefab HpNumber Text 红色);Godot "- N" (1,0.3,0.2) Label3D 96 号、**上升 0.5m/1.2s、alpha 1→0**、全局共享 8 个池(FireSystem.cs:130-146,434-452;Monster.cs:217 调用) → 颜色/速度/时长/终态 alpha 均不同。

### 13. 玩家数值 — 偏差(Debug 90 未移植)

| 子项 | 真值 | Godot | 结论 |
|---|---|---|---|
| HP | 100(Player.cs:45) | MaxHp=100(Player.cs:10) | 一致 |
| 子弹 | MaxBullet=**120**(UserMeta.cs:29);**IsDebug 时 90**(Player.cs:50-57;真值截图 ×90 即 Debug 构建) | 恒 = MaxBullet 120(Player.cs:25),Game.IsDebug=true 未用于子弹 | **偏差**:godot 截图 ×120 vs 真值 ×90 |
| 进关扣 1 币 | 进关扣 1(网络 ReduceCoinFps) | LevelChooseScreen.cs:402 SpendCoin(1) 本地扣 | 一致 |
| 宝箱子弹 | +60(BoxMonster.cs:119,BoxBullet=60) | BoxMonster.cs:71 += BoxBullet(60) | 一致 |
| 受伤音效 | 右手 6 个 AudioSource 随机 / 左手 2 个(PlayerSystem.prefab RightHurtAs×6/LeftHurtAs×2;PlayerSystem.cs:143-152) | Male_Hurt×6 / Female_Hurt×2 随机(PlayerState.cs:25-41) | 一致(数量) |
| 金币/回复 | Coin 10、MaxCoin 10、AddCoinTime 180s(UserMeta.cs:25-27) | SaveService.cs:21-24 同值 | 一致(回复时机见 #11-④) |

### 14. 加子弹/回血图标放大动画 — 偏差(未移植)

- 加子弹:原作 AddBulletAni 把 BulletRight/Left 图标 `localScale × 1.5`,然后每帧 +1 逐发增长并刷新文本,结束恢复 1.0(PlayerSystem.cs:475-516);宝箱触发(BoxMonster.cs:119)。Godot 直接 `p.Bullet += 60`(BoxMonster.cs:71),无放大/逐帧动画 → **偏差**。
- 回血:原作 AddHpAni 同样 ×1.5 + 每帧 +10 回血(PlayerSystem.cs:403-449);Level1 无调用点(死代码)。Godot 无任何 AddHp 实现 → Level1 范围内一致,代码层面缺失(备注)。

---

## 偏差汇总表

| # | 项 | 偏差 | Unity 真值(文件:行/prefab 坐标) | Godot 现状(文件:行/截图) | 严重度 | 建议修复位置 |
|---|---|---|---|---|---|---|
| 1 | 战斗面板 | **全部 Label3D 文字不可见**(pixelSize 0.001 × 面板根 scale 0.001 双重缩小) | InGamePanel.prefab 全部 Text(标题/按钮/倒计时/金币) | src/Battle/InGamePanel.cs:94(PixelSize=0.001);godot_battle_25s.png 暂停键无文字 | **高** | src/Battle/InGamePanel.cs MakeText/MakeButton 的 pixelSize 参数 |
| 2 | 怪物血条 | **实测不渲染**(Boss/牛魔王/近身怪头顶均无条) | MonstHp.cs + HpReduceNumber.prefab(世界 Canvas+Slider 60×10);l1u_gun_25s 牛魔王头顶红条 | godot_boss.png/godot_battle_13s.png/godot_g1.png 无条;旧图 l1_boss.png/l1_fix8.png 同 | **高** | src/Battle/MonsterHpBar.cs(根因待确认,需运行时调试) |
| 3 | 命中红点 | 橙色大光团 ~70px、双手同色、叠 LaserSight 红点;真值深红小点 ~28px、右红左绿 | Effect/Flash.prefab(红)/FlashGreen.prefab(绿),根 scale 0.5,粒子 startSize 0.5;GlobalObject.prefab Flash1/Flash2 | src/Battle/FireSystem.cs:79-102(0.12m quad (1,0.85,0.35,0.45) + withDot 0.09m);godot_battle_25s.png | **高** | src/Battle/FireSystem.cs _Ready 中 flash 构建(颜色/尺寸/分侧) |
| 4 | 玩家 HP 条 | 真值画面无 HP 条;Godot 头像下方两条可见绿条 438×16 | PlayerSystem.prefab HPRightSlider (514,333)/HPLeftSlider (-70.3,333),截图不可见(l1u_gun_25s/l1u_battle_45s) | src/Player/PlayerState.cs:273-285;godot_battle_25s.png/godot_intro_15s.png | **高** | src/Player/PlayerState.cs MakeHeadHud 的 HPSlider(隐藏或按原作坐标) |
| 5 | 暂停按钮样式 | 不透明深蓝方块(原作半透明 a=0.235);多"主菜单"文字;图标 80 vs 全幅 120 | InGamePanel.prefab PauseBtn color (0,0,0.4528,0.2353),子 Text 空串,图标全拉伸 | src/Battle/InGamePanel.cs:139-151;UiButton3D.cs:50-59(无贴图不开 Transparency);截图 | 中 | src/Battle/InGamePanel.cs BuildPauseBtn;src/UI/UiButton3D.cs Transparency 条件 |
| 6 | 暂停键可见时机 | 原作开场不显示;Godot 开场即显示 | l1u_intro_15s/l1u_gun_10s 无暂停键 | godot_intro_15s.png 顶部蓝块;InGamePanel 常驻 | 中 | src/Battle/InGamePanel.cs(开战火 StartBattle 才显示) |
| 7 | 子弹数 | Debug 90 分支未移植,显示 120(真值 90) | Player.cs:50-57(IsDebug→90) | src/Player/Player.cs:25(恒 MaxBullet);截图 ×120 vs ×90 | 中 | src/Player/Player.cs Born |
| 8 | 激光宽度 | 原作 widthCurve 0.0089→0.03 近细远粗;Godot 恒直径 0.03(近处 ~3× 粗) | FireSystem.prefab LazerRight:5108/5117、LazerLeft:185/194 | src/Battle/FireSystem.cs:100-102(radius 0.015) | 中 | src/Effects/LaserSight.cs(锥形/双半径) |
| 9 | 激光终止 | 原作恒 200m(穿透怪,深度剔除遮挡段);Godot 止于命中点 | Guns/FireSystem.prefab LineRenderer (0,0,0)→(0,0,200) | src/Battle/FireSystem.cs:304-311 | 中 | src/Battle/FireSystem.cs UpdateSide SetBeam 终点 |
| 10 | 激光终点红点 | 原作无;Godot 多 0.09m 光斑(与 Flash 双指示) | —(命中指示全在 Flash) | src/Battle/FireSystem.cs:100-102 withDot:true | 中 | 同 #3 一并处理 |
| 11 | 飙血特效颜色 | 绿血 GreenImpact;Godot 暗红 | truth §7;Impacts/Mobile/GreenImpact.prefab | assets/effects/impact_blood.tscn (0.65,0.04,0.04)/(0.5,0.03,0.03) | 中 | assets/effects/impact_blood.tscn 调色 |
| 12 | 兑换子弹分支 | 标题/Info/按钮文字不切换;"币不足"未实现 | InGamePanel.cs:74-104("兑换子弹"/"继续游戏"/"币不足") | src/Battle/InGamePanel.cs:220-230(仅存标志) | 中 | src/Battle/InGamePanel.cs OpenContinue |
| 13 | Info 文本数值 | 应"每 3 分钟…最高 10 枚";现硬编码"每 15 分钟…最高 5 枚" | UserMeta.cs:25-27(AddCoinTime 180/MaxCoin 10)+ InGamePanel.cs:77/82 动态生成 | src/Battle/InGamePanel.cs:174-176 | 中 | src/Battle/InGamePanel.cs BuildContinuePanel(动态生成) |
| 14 | 金币回复时机 | 原作仅 Continue 面板打开时计时加币;Godot 后台持续回复 | InGamePanel.cs:178-198 | src/Core/SaveService.cs:63-66 | 中 | src/Core/SaveService.cs 回复逻辑门控 |
| 15 | 左子弹位置 | 低 32px(pivot 中心语义误作底) | PlayerSystem.prefab BulletLeft pivot(0.5,0.5) pos(-258.72,31.4) | src/Player/PlayerState.cs:193-197 | 中(双人模式) | src/Player/PlayerState.cs MakeBulletHud 左调用 |
| 16 | 子弹"×"位置 | × 叠在图标中心;原作在图标右下 (+58.43,-23.58) | PlayerSystem.prefab Bullet X Text pos (58.43,-23.58) 28 号 | src/Player/PlayerState.cs:209-223;截图 | 中 | src/Player/PlayerState.cs MakeBulletHud X 偏移 |
| 17 | 枪口挂点 | 三枪统一 (0,0,-0.088);原作 HandGun 0.042 / M4 0.162 / AK47 0.088 | Guns/HandGun.prefab Sphere(0,0,0.042)、M4.prefab(0,0,0.162)、AK47.prefab(0,0,0.088) | src/Battle/FireSystem.cs:191-193 | 中 | src/Battle/FireSystem.cs BindSide 按枪型给 Muzzle |
| 18 | 开火后座动画 | _Ani.Play() 未移植 | HandGun.cs:36/AKGun.cs:50/M4Gun.cs:36 | src/Battle/GunBase.cs:44-57(无动画) | 中 | src/Battle/GunBase.cs Fire |
| 19 | 受击红屏峰值 | 起始 alpha 0.392;原作运行时 a=1 起淡(黄/青/绿原色) | PlayerSystem.cs:266-289 | src/Player/PlayerState.cs:324-329 | 中 | src/Player/PlayerState.cs OnHurtFlash |
| 20 | 血条高度 | 多数怪 2.1/baotou 2.5/box 1.0;原作 1.53/Bull 1.89/Baotou 1.72/Box 2.47 | 各 Monster prefab HpReduceNumber 根 y + Canvas 153.2×0.01 | scenes/battle/monsters/*.tscn HpAnchor | 中 | 各怪 tscn HpAnchor y |
| 21 | 掉血飘字 | 颜色 (1,0.3,0.2) vs (1,0,0);升 0.5m/1.2s vs 5px/s;alpha→0 vs →0.3;池 8 共享 vs 每怪 3 | MonstHp.cs:59-81;HpReduceNumber.prefab HpNumber | src/Battle/FireSystem.cs:130-146,434-452 | 中 | src/Battle/FireSystem.cs ShowDamage |
| 22 | 面板缩放 | 0.001 vs 原作 (0.00115,0.00115,0.01)(面板小 13%) | InGamePanel.prefab 根 m_LocalScale | src/Battle/InGamePanel.cs:15 | 低 | src/Battle/InGamePanel.cs CanvasScale |
| 23 | 加子弹动画 | ×1.5 放大 + 逐帧 +1 未移植 | PlayerSystem.cs:475-516 | src/Battle/BoxMonster.cs:71(直接 +=) | 低 | src/Player/PlayerState.cs(补 AddBulletAni) |
| 24 | 换枪行为 | 新枪有升起动画(原作字面为瞬现,出枪动画作用于隐藏旧枪);换枪期禁火(原作不挡) | FireSystem.cs:148-178 | src/Battle/FireSystem.cs:324,370-395 | 低(待确认以哪边为准) | src/Battle/FireSystem.cs OnSwitchKey |
| 25 | 特效池数量 | Concrete 池 3 vs 4;死亡爆血池 5 vs 3 | FireSystem.prefab:10174-10178(Concrete×4)、10145-10148(_BloodEffects×3) | src/Battle/FireSystem.cs:20-21 | 低 | src/Battle/FireSystem.cs 池常量 |
| 26 | 暂停键面板联动 | 面板打开时暂停键不隐藏(原作开任一面板都隐藏,关闭恢复) | InGamePanel.cs:59/87/115/215 | src/Battle/InGamePanel.cs(仅 Victory 隐藏,:253) | 低 | src/Battle/InGamePanel.cs OpenPause/OpenContinue/CloseAll |
| 27 | 倒计时/币满 | 格式 "02:05" vs 原作 "2:5";多"金币已满"文本 | InGamePanel.cs:185-188 | src/Battle/InGamePanel.cs:278-282 | 低 | src/Battle/InGamePanel.cs _Process |
| 28 | Continue 标题字号 | 60 vs 原作 50;右装饰条偏移 9px | InGamePanel.prefab Continue 标题 Text fontSize 50,RightImage x=+199 | src/Battle/InGamePanel.cs:117-124(AddTitle 恒 60/±offset) | 低 | src/Battle/InGamePanel.cs AddTitle 参数化字号 |
| 29 | Continue-MenuBtn 文字 | 52 号 (0.914,1,0.821) vs 原作 40 号白 | InGamePanel.prefab Continue MenuBtn 子 Text | src/Battle/InGamePanel.cs:185-186(默认参数) | 低(当前文字不可见) | 同 #12 一并 |
| 30 | BackToMenu 防误触 | 1s 内忽略未移植 | InGamePanel.cs:32-35 | src/Battle/InGamePanel.cs:257-262 | 低 | src/Battle/InGamePanel.cs BackToMenu |
| 31 | HUD 全局缩放策略 | Unity ConstantPixelSize 1:1(实测 736×334 窗口验证);Godot canvas_items+expand 1280×720(1472×668 窗口下 0.928×,随窗口比例变化不同) | PlayerSystem.prefab:1921-1924 | src/Player/PlayerState.cs:94(注释);project.godot stretch 设置 | 低 | project.godot display stretch(如需像素级一致) |
| 32 | 子弹数字垂直 | 中心上 3.1px vs 原作下 3.1px(6.2px) | PlayerSystem.prefab BulletRightText pos (83.31,-3.1)(Unity +y 上) | src/Player/PlayerState.cs:233(`-3.1f - 30.0f`) | 低 | src/Player/PlayerState.cs:233 |
| 33 | 激光滚动 | 单采样 6 cycle/s vs 原作双采样 (14,0)/(-12,0)×t/20 相乘×2 | FPSLazer.shader frag;Lazer.mat _Direction (14,0,-12) | src/Effects/LaserSight.cs:19,144-150 | 低 | src/Effects/LaserSight.cs(视觉近似,可不修) |
| 34 | 冰冻手暂停行为 | Godot 暂停时也跳过冰冻手;原作仅非暂停跳过 | FireSystem.cs:232-237 | src/Battle/FireSystem.cs:269-273 | 低 | src/Battle/FireSystem.cs UpdateSide |
| 35 | Dust 弹痕 | impact_dust.tscn Hole 为 MeshInstance3D,`GetNodeOrNull<Sprite3D>` 取不到,永不切换 | FireSystem.cs:339-347(_Dankon) | src/Battle/FireSystem.cs:427;assets/effects/impact_dust.tscn:48 | 低(待确认) | assets/effects/impact_dust.tscn Hole 节点类型 |

图例:严重度 **高** = 画面/功能与真值明显不符;**中** = 可见偏差或数值错误;**低** = 边缘/潜在/需特定条件。

---

## 一致项清单(无需修复)

1. 枪数值(data/fire_meta.json ≡ firemetajson.json:pos/pos60/CD/攻击/耗弹;左手 x 取反;FOV≤50 用 pos)。
2. 换枪顺序与默认:NextGun 枚举循环回绕、默认手枪(2)、Born 三枪全开、总时长 1.05s。
3. 命中公式:flashScale/Offset/位置/未命中隐藏逐式一致。
4. 怪物 tag→特效映射(默认 Blood;骷髅/牛魔王/Baotou Concrete)与打怪关弹痕(Hole)语义、特效缩放 ×3、轮换方式。
5. 枪口火光结构与触发(复用菜单 1:1 组件;Fire 成功即闪)。
6. 玩家 HP100、进关扣 1 币、宝箱 +60、受伤音效右 6 左 2、Coin10/MaxCoin10/AddCoinTime180。
7. HUD 主体布局:金币 (-26.02,0) 全套、右子弹 (185.12,0)、头像环角落锚定/贴图/左手未激活藏图标、受击贴图、开场只见金币+头像(子弹开战后显示)。
8. 三面板骨架:暂停/续币/胜利面板尺寸、贴图、按钮位置、Victory 仅 MenuBtn、timeScale=0 等价、暂停期按钮可射。
9. InGamePanel 挂相机 z=-1(原作 z=+1,+Z 前)。
10. 激光颜色(右红/左绿原值)与加色混合、贴图平铺率 2.5/m。

## 待确认项

- 怪物血条不渲染的根因(代码静态完整;需运行时 inspect MonsterHpBar 节点可见性/材质)。
- 原作玩家 HP Slider 不可见的原因(prefab 存在且 battle 激活;截图证实满血无显示)——以截图为真值则 Godot 应隐藏。
- 换枪"出枪动画"以原作字面行为(新枪瞬现)还是文档意图(新枪升起)为准。
- 每枪 MuzzleFlash1 有效缩放差异(AK 0.32 vs 手枪/M4 0.04)是否为原作有意效果。
- 真值截图的 Debug 构建标记(IsDebug)在正式发布包中的取值——若发布包 IsDebug=false,则子弹 120 正确、90 仅调试态。
