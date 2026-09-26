# Wave3:Level2 光照按真值重做(2026-09-26)

任务:Level2 观感与 Unity 真值(`tools/screenshots/level2_unity.png`,黄昏毒气镇)对齐。
前态:实时等效光照"偏白日"(README 已声明偏差);本次发现 Unity 侧 Level2.unity **有烘焙
LightingData**(`Assets/Scenes/Level2/Lightmap-*.exr`),烘焙不可跨引擎复用,但真值观感可重建。

## 真值(YAML 直读)

- `Level2.unity` RenderSettings:雾关、ambient flat 白 ×2.11(被烘焙覆盖)、无天空盒;
  主相机 clearFlags=2 纯色 bg=(0.0579,0.0583,0.0660)(近黑)。
- 灯:方向光 (1,0.896,0.726)×1(软阴影)+ **9× type2 点光 intensity=2 range=10 色 (1,0.8932,0.7075)**
  (挂 FireWindow 实例位,局部 (0,0,0))。
- vcam far:G0=50 / G1=80 / G2=100,near=0.001。

## 修复(Godot 侧)

| 项 | 改动 | 位置 |
|---|---|---|
| env 材质全亮 | 7 个 unshaded 材质(shading_mode=0)去除,全部点亮 | env_level2.tscn |
| 远景过亮 | Terrain_d_gas(含子级 335 网格)整组盖暗色材质(0.20,0.24,0.22)——VC 材质不可乘色只能盖 | Level2.cs DarkenBackdrop |
| 环境光 | 白 ×1.0 → 暗冷 (0.30,0.34,0.44)×0.22 | level2.tscn Env |
| 方向光 | energy 1.0→0.55,开阴影 | level2.tscn DirectionalLight3D |
| 窗口暖光 | 15 窗口位各加 OmniLight3D(暖 (1,0.893,0.707),energy 2,range 10) | level2.tscn WindowLights |
| 逐波 far | meta 三组 cam_far=50/80/100,基类 SwitchCamera 应用(原固定 100) | level_meta.json + LevelBase.cs |

## 验证

- `gd_l2_lit7_15s.png`(G0 15s):黄昏毒气镇观感成立(暗天/暖池/楼顶兵)。
- `--level2-shot-group:<png>:<g>`(新增挂接)G1 夜景街道;`--level2-shot-boss:<png>`(新增)盔甲武士 ×3 出场。
- L2 自检 36+2 项全过;全回归 8/8 绿。

## 遗留

- Unity 2019 batchmode 许可证当日失效(报 "not activated"),L2 无法补拍更多节拍真值;
  GUI 手动开正常——许可证恢复后建议补拍 G1/G2/Boss 波真值校准。
- MyTree 树木仍偏饱和绿(近场装饰,未压暗);酒馆窗户玻璃无自发光(真值有暖窗内透,由点光近似)。
