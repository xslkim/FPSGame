extends Node
## M5 全流程自检驱动:由 startup 在 --m5-selftest 时挂到 SceneTree.root
## (跨场景切换会释放旧场景根,驱动必须活在 root 上)。

const MENU := "res://scenes/ui/menu.tscn"

var _test_failed := false

func _ready() -> void:
	_self_test()

func _check(cond: bool, label: String) -> void:
	print(("[M5-TEST] PASS: " if cond else "[M5-TEST] FAIL: ") + label)
	if not cond:
		_test_failed = true

func _frames(n := 3) -> void:
	for i in n:
		await get_tree().process_frame

func _self_test() -> void:
	await _frames()
	get_tree().change_scene_to_file(MENU)
	await _frames()

	# ---- Menu:单人无设备 → 控制方式弹框(遥控器/手机);弹框按钮带 MessageButton 前缀 ----
	var menu := get_tree().current_scene
	_check(menu.name == "Menu", "startup → menu")
	_check(GlobalObject.scene_state == GlobalObject.GameState.UI, "menu: scene_state = UI")
	menu.one_player()
	await _frames(2)
	_check(MessageBox.is_open(), "单人无设备 → 「遥控器/手机」弹框")
	_check(MessageBox.current.ok_button.name.begins_with("MessageButton"),
		"弹框按钮 MessageButton 命名(枪瞄准过滤)")
	MessageBox.current.press_cancel()  # 遥控器
	await _frames()
	_check(InputManager.input_mode == InputManager.InputMode.ControllerOrRight,
		"遥控器 → ControllerOrRight")

	# ---- LevelChoose:解锁判定 / 币不足 / 扣币 / 难度面板 ----
	var lc := get_tree().current_scene
	_check(lc.name == "LevelChoose", "menu → level_choose")
	DataMgr.level_state[0]["star"] = 0
	lc.refresh()
	lc.select_level(1)
	await _frames(2)
	_check(MessageBox.is_open(), "L1 star=0 时选 L2 → 未解锁弹框")
	MessageBox.close_current()
	DataMgr.level_state[0]["star"] = 3
	lc.refresh()
	await get_tree().create_timer(1.1).timeout  # 1s 防抖
	DataMgr.coin = 0
	lc.select_level(0)
	await _frames(2)
	_check(MessageBox.is_open(), "coin=0 选关 → 金币不足弹框")
	MessageBox.close_current()
	await get_tree().create_timer(1.1).timeout
	DataMgr.coin = 10
	DataMgr.last_add_coin_time = Time.get_unix_time_from_system()
	lc.select_level(0)
	await _frames(2)
	_check(DataMgr.coin == 9, "选 L1 扣 1 币 (10 → 9)")
	await get_tree().create_timer(0.7).timeout
	_check(lc._diff_panel.visible, "0.5s 后弹难度面板")
	lc.choose_difficulty(GlobalObject.Difficulty.Easy)
	_check(GlobalObject.next_scene_path == "res://scenes/levels/level1_story.tscn",
		"Easy → load_scene = level1_story(剧情开场)")
	await _frames()

	# ---- Loading:≥4s + 真实进度 → level1_battle ----
	var t_load := Time.get_ticks_msec() / 1000.0
	var t0 := t_load
	# 剧情场景进场后会再跳一次 loading,过渡帧 current_scene 可能为 null
	while (get_tree().current_scene == null or get_tree().current_scene.name != "Level1") \
			and t_load - t0 < 20.0:
		await get_tree().process_frame
		t_load = Time.get_ticks_msec() / 1000.0
	var level := get_tree().current_scene
	_check(level.name == "Level1", "loading → level1_battle")
	_check(t_load - t0 >= 3.9, "loading 最少 4 秒 (%.1fs)" % (t_load - t0))
	_check(GlobalObject.scene_state == GlobalObject.GameState.Battle, "进关 scene_state = Battle")
	_check(PlayerSystem.player_right.active and not PlayerSystem.player_left.active,
		"ControllerOrRight: 右玩家活跃,左玩家隐藏")
	await _frames(5)

	# ---- 金币经济:全局回币 ----
	DataMgr.last_add_coin_time -= 200.0
	await get_tree().create_timer(0.3).timeout
	_check(DataMgr.coin == 10, "回币:180s +1 (9 → 10),封顶 10")

	# ---- 续币面板:弹尽 / 死亡两路径 relife ----
	var panel: InGamePanel = level.get_node("InGamePanel")
	var rp := PlayerSystem.player_right
	rp.hp = 30.0
	rp.bullet = 10
	panel.open_continue(true, PlayerSystem.Side.Right)
	_check(panel._continue_panel.visible and Engine.time_scale == 0.0,
		"弹尽 → 续币面板(兑换子弹),timeScale=0")
	panel.confirm_continue()
	_check(rp.hp == 100.0 and rp.bullet == 130, "弹尽 relife:HP=100, 子弹 10+120=130")
	_check(DataMgr.coin == 9, "续币扣 1 币 (10 → 9)")
	_check(Engine.time_scale == 1.0, "关面板 timeScale 恢复")
	rp.hp = 5.0
	panel.open_continue(false, PlayerSystem.Side.Right)
	_check(panel._continue_panel.visible, "死亡 → 续币面板(继续游戏)")
	panel.confirm_continue()
	_check(rp.hp == 100.0 and rp.bullet == 250, "死亡 relife:HP=100, 子弹 130+120=250")
	_check(DataMgr.coin == 8, "续币再扣 1 币 (9 → 8)")

	# ---- 通关星级结算:满血 + 速通 → 3 星落盘 ----
	rp.hp = 100.0
	GlobalObject.is_game_pause = true  # 冻结怪物,避免结算期间掉血干扰
	level._trigger_victory()
	await get_tree().create_timer(2.4, true, false, true).timeout  # 忽略 time_scale(胜利面板会置 0)
	_check(level.last_stars == 3, "星级结算:满血 + 用时<par → 3 星")
	_check(panel._victory_panel.visible, "胜利面板弹出")
	_check(int(DataMgr.level_state[0].get("star", 0)) == 3, "level_state[0].star 写入 3")
	var save_text := FileAccess.get_file_as_string(DataMgr.SAVE_PATH)
	_check(save_text.find("\"star\": 3") >= 0, "星级落盘 user://save.json")
	panel.close_all()
	_check(Engine.time_scale == 1.0 and not GlobalObject.is_game_pause, "面板关闭恢复运行")
	GlobalObject.is_game_pause = false

	print("[M5-TEST] done, failed=%s" % _test_failed)
	get_tree().quit(1 if _test_failed else 0)
