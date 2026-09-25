# Wave2 Agent G 笔记:怪物血条完全不渲染 — 根因查明与修复

## 结论(TL;DR)

"血条完全不渲染"的**功能性 bug 在 HEAD 提交 896e231 已修掉**(2026-09-22 17:27,早于审计截图时间 2026-09-23 23:09)。审计观测到"无红条"是因为**跑的是修复前的旧构建**(编辑器复用 `.godot/mono/temp/bin` 下未重新 `dotnet build` 的旧 DLL,或旧的 dist 导出)。当前工作区代码 + 全新构建下,血条在骷髅/牛魔王/Boss 头顶均正常渲染(证据截图见下)。

本轮在 `game/src/Battle/MonsterHpBar.cs` 内只做了一处加固:填充材质 `RenderPriority = 1`,消除底/填充两个同深度透明 quad 绘制次序不定的隐患。未改任何 tscn。

## 根因分析

### 旧逻辑(a7677fa,2026-09-21)= 审计看到的现象

`MonsterHpBar.SetHp` 旧版最后一行:

```csharp
Visible = value < 1.0f && value > 0.0f;   // 满血隐藏,只有被打掉血后才出现
```

且旧版填充材质**没有红色 tint**(直接显示 Hp_Yellow.png 原色)。后果链:

1. `Monster.Born()` → `HpBar.SetHp(1.0f)` → 满血 → `Visible=false` → 出生头顶无条;
2. 审计截图里怪都是满血/未受击(或 Boss 被打的是爱心弱点,不经 `Monster.Hit` 掉 Hp)→ 全程无条;
3. 逐像素确认"无红条"与此完全吻合。

896e231 已改为 `Visible = value > 0.0f`(常显,1:1 原作 Slider 常显)并补 `AlbedoColor=(1,0,0,1)` 红色 tint(真值 HpReduceNumber.prefab 填充 `m_Color: {r:1,g:0,b:0,a:1}` 已核对)。

### 排查过程(排除法记录)

- tscn 挂接正确:各怪 `HpAnchor` = Node3D + MonsterHpBar 脚本(bull.tscn 等逐一核对);`Monster.cs:62` `GetNodeOrNull<MonsterHpBar>("HpAnchor")` 拿得到。
- 贴图正常:`Hp_Green_bg.png` 92×16 RGBA 全像素 alpha=179(半透明暗轨)、`Hp_Yellow.png` 全像素 alpha=255;`.import` 正常,`GD.Load` 均成功。
- 临时调试输出(修完已删)实测:`_Ready` 对池内每只怪都执行;Born 后 `IsVisibleInTree=True`、`layers=1`、fill scale=(1,1,1)、锚点全局坐标正确(如 baotou (0, 2.83, 66)、skeleton (-1.34, 2.08, 67.8))。
- 日志里反复出现的 `CSharpInstanceBridge` 栈只是 `PlayerState._Ready` 加载 ogg 的注释告警,与血条无关。

### 为什么审计"代码静态完整但截图无条"

静态看的是当前代码(Born→SetHp(1)→Visible=true 确实会调),但运行时是旧 DLL —— 编辑器不会自动重编译,必须 `dotnet build`。教训:**截图审计前务必先构建**。

## 本轮改动(仅 MonsterHpBar.cs,+1 行)

```csharp
RenderPriority = 1, // 底/填充同深度:填充后画,稳定压过暗轨(透明同深度次序不定)
```

Bg 与 Fill 两个 quad 共面、均 `Transparency.Alpha`(不写深度),Godot 透明排序按深度、同深度次序未定义,存在填充被暗轨盖住的概率;RenderPriority=1 让填充恒定后画在上层。临时调试代码已全部删除,`git diff HEAD` 只剩这一行。

## 掉血刷新链路核对(通畅)

- `Monster.Hit`(Monster.cs:245-246):`Hp -= attack` → `HpBar?.SetHp(Max(Hp,0)/GetMaxHp())` → SetHp 内填充 `Scale.x=value`、`Position.x` 左锚补偿(照 Slider L→R);击杀时 `SetHp(0)` → `Visible=false`。
- `RockWarriorBoss.Hit` 硬化分支(RockWarriorBoss.cs:108)与 `Level2Boss`(Level2Boss.cs:246)同样调 SetHp;非硬化分支走 `base.Hit`。
- 原作 MonstHp.Update 是每帧轮询 `curHp<hp` 才刷 Slider;我们改成 Hit 事件驱动 —— Hp 只在 Hit 里变,效果等价。

## 验证

- 自检:`--level1-selftest` 全 PASS(18/18,`failed=False`,exit 0)。
- 截图(均在 `tools/screenshots/audit/wave2_g/`,1472×668):
  - `boss.png`(--level1-shot-boss):Boss 头顶/门楣上方红条清晰(另有骷髅头顶条),G4 机位近距。
  - `battle_10s.png`(--level1-shot-battle:…:10):G0 牛魔王近身(画面左下牛角),头顶红填充细条(贴图横向渐变 ×红 tint,与真值 l1u_gun_25s.png 角间细红条同款色相)。
  - `battle_14s.png`:怪贴脸时血条放大出画框上沿 —— 世界空间条近大远小,行为正确。
  - `g0.png`:G0 波挂接机位朝楼梯间,怪未入镜(预期)。
- 样式对真值:世界空间 0.6×0.1(60×10×0.01)、底暗轨半透明 + 红填充、billboard 每帧面向相机(= 原作 `transform.LookAt(Camera.main)`)、满血常显。

## 遗留/边界

- 锚点高度归另一 agent(tscn 未动)。注意血条是世界空间带深度测试(1:1 Unity 世界画布):锚点若埋进头部网格会被遮挡,调锚点时需保证条在头顶之上。
- 未做:bar 的 RenderPriority 只保证底/填充两层次序;多只怪血条互相遮挡时仍按深度排序(与 Unity 一致)。
