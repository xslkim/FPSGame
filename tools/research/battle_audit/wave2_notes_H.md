# Wave2 Agent H — Level1(school_day)雾/光照/色调校准

日期:2026-01-25(会话日期以系统为准)。对应审计:`l1_visual.md` #6(光线/雾/色调 S7)。

## 改动(仅 `game/scenes/levels/level1_battle.tscn` Env 子资源,其他未动)

| 项 | 改前 | 改后 | 真值(school_day.unity) |
|---|---|---|---|
| 雾模式 | 指数(fog_density=0.08) | `fog_mode = 1`(深度雾) | m_FogMode:1 线性 |
| fog_depth_begin | —(指数无此参数) | `5.0` | Start 5 |
| fog_depth_end | — | `12.0` | End 12 |
| fog_light_color | (0.581, 0.585, 0.43) | 不变 | (0.581, 0.585, 0.430) |
| 环境光 | `ambient_light_source=2`(Flat 等效)、白 ×1.0 | 不变(已是真值) | Flat 纯白 (1,1,1) |
| 背景 | `background_mode=1` 纯色 (0.569, 0.588, 0.427) | 不变(已是真值) | ClearFlags 纯色同值,无天空盒 |
| 方向光 | (1, 0.9647, 0.7647) × 2.0,shadow on | 不变(已是真值) | (1,0.965,0.765) ×2 软阴影 |

深度雾 `fog_depth_curve` 默认 1.0 = 线性,与 Unity 线性雾一致,无需设置。落地后与真值观感吻合,**未做任何 density/能量级微调**(色值/起止严格保持真值)。

## 关键前提

README:146 旧注记"本引擎 fog_depth_begin/end 无效,以指数 0.08 校准"已被证伪:本自编译 4.8 引擎 `fog_mode=1` 生效(level3.tscn 先例 + 本次 Level1 截图实证)。README 未动(任务约定),后续可更正该注记。

## 截图对照(产物:`tools/screenshots/audit/wave2_h/`)

命令:`godot.windows.editor.x86_64.mono.exe --path . scenes/levels/level1_battle.tscn -- "--level1-shot:<path>:<sec>" --shot-res:1472x668`

| 机位 | 前(l1_visual,指数 0.08) | 后(wave2_h,深度 5→12) | 真值 | 结论 |
|---|---|---|---|---|
| intro_15s | 走廊尽头可读、仅薄霾 | 走廊尽头全被黄绿雾吞没成雾墙,~10m 处抱人演员没入雾中 | l1u_intro_15s(远景全没) | ✅ 吻合 |
| intro_21s | 楼梯间清晰、整体偏白 | 楼梯台阶/上墙明显黄绿雾霾,远景洗成雾色 | l1u_intro_21s(中景雾感+暖光斑) | ✅ 吻合(本轮 RNG 牛魔王贴脸入画在左缘,属 #8 时机问题,非雾) |
| gun_25s | 远景能见度偏高 | 楼梯间背景重度雾化、整体暖黄绿调 | l1u_gun_25s | ✅ 吻合 |

审计 #6 三条症状(整体偏亮偏白 / 中景黄绿雾不足 / 远景能见度高于真值)均消除:5–12m 线性雾带恢复了中景黄绿包裹感,12m 外为纯色雾墙,近景(<5m)无雾故不过奶,阳光光斑对比恢复。

## 验证

- 自检:`--headless ... -- --level1-selftest` → **18 PASS / 0 FAIL**(波次数值、Boss、victory、相机切换、HUD 等全绿)。
- 窗口模式截图跑会弹窗且日志有一条 `InputRouter._Process` 非致命异常(既有现象,与本次改动无关),shot 均正常 saved。

## 遗留(非本任务范围)

- 21s/25s 截图中央大红点为审计 #4 瞄准点尺寸问题(其他 agent 范围),遮挡了部分雾评估区域,但周边区域已足够判定。
- l1_visual.md #6 可由审计方复核后关闭。
