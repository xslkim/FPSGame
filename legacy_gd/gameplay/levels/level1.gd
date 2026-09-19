extends LevelBase
class_name Level1
## Level1 战斗关(7.2 L1 五波 + 7.4 简化版 19 秒开场)。
## 启动参数:
##   (无)              debug 跳过开场直接开战
##   --m4-intro        强制播放 19s 开场(即使 debug)
##   --m4-selftest     headless 加速全流程断言

@export var intro_from_path: NodePath = ^"IntroMarkers/intro_from"
@export var intro_to_path: NodePath = ^"IntroMarkers/intro_to"

var intro_from: Node3D
var intro_to: Node3D
var _intro_playing := false
var _test_failed := false

func _enter_level() -> void:
	intro_from = get_node_or_null(intro_from_path)
	intro_to = get_node_or_null(intro_to_path)
	var args := OS.get_cmdline_user_args()
	if args.has("--m4-selftest"):
		_self_test()
		return
	if GlobalObject.is_debug and not args.has("--m4-intro"):
		print("[L1] debug: 跳过 19s 开场直接开战")
		start_battle()
		return
	_play_intro()

## 19 秒开场(7.4 简化版):禁射击 → Tension → 相机缓推 → 3s 开环境(占位)→ 19s 开战
func _play_intro() -> void:
	_intro_playing = true
	InputManager.fire_enabled = false
	if fire_system != null:
		fire_system.set_process(false)
		fire_system.visible = false
	_play_music(str(meta.get("start_music", "")))  # Tension.mp3
	var dur := float(meta.get("intro_duration", 19.0))
	if camera != null and intro_from != null and intro_to != null:
		camera.global_transform = intro_from.global_transform
		var tw := create_tween()
		tw.tween_property(camera, "global_transform", intro_to.global_transform, dur)
	get_tree().create_timer(3.0).timeout.connect(func(): print("[L1] env active (第3秒开环境,占位)"))
	print("[L1] intro start: %.0fs (Tension)" % dur)
	await get_tree().create_timer(dur).timeout
	_intro_playing = false
	print("[L1] intro done → battle (Level1Ex)")
	start_battle()

func _check(cond: bool, label: String) -> void:
	print(("[M4-TEST] PASS: " if cond else "[M4-TEST] FAIL: ") + label)
	if not cond:
		_test_failed = true

## 难度数量断言(纯计算,不改流程):Easy 10/10/15/15/20,Hard 18/18/27/27/36,Hell 26/26/39/39/52
func _check_difficulty_counts() -> void:
	var saved := GlobalObject.difficulty
	var cases := {
		GlobalObject.Difficulty.Easy: [10, 10, 15, 15, 20],
		GlobalObject.Difficulty.Hard: [18, 18, 27, 27, 36],
		GlobalObject.Difficulty.Hell: [26, 26, 39, 39, 52],
	}
	for diff in cases:
		GlobalObject.difficulty = diff
		_load_meta()
		var nums: Array = []
		for i in groups.size():
			nums.append(calc_group_params(i).num)
		_check(nums == cases[diff], "difficulty %d group nums = %s" % [diff, str(nums)])
	GlobalObject.difficulty = saved
	_load_meta()

## headless 自检:godot --headless --path game scenes/levels/level1_battle.tscn -- --m4-selftest
func _self_test() -> void:
	_time_scale = 0.04  # 加速刷怪间隔/冻结/相机 Tween;胜利延迟保持真实 2s 以验证
	debug_auto_kill = true
	_check_difficulty_counts()
	var victory_count := [0]
	var victory_time := [0.0]
	level_victory.connect(func():
		victory_count[0] += 1
		victory_time[0] = Time.get_ticks_msec() / 1000.0)
	var continue_args: Array = []
	open_continue.connect(func(is_open, side): continue_args.append([is_open, side]))
	start_battle()
	# 玩家死亡 → open_continue(false, side)
	while cur_group < 1:
		await get_tree().process_frame
	PlayerSystem.hit_player(999.0, GlobalObject.AttackType.Phy, PlayerSystem.Side.Right)
	await get_tree().create_timer(0.7, true, false, true).timeout  # 忽略 time_scale(面板会置 0)
	_check(continue_args.any(func(c): return c[0] == false and c[1] == PlayerSystem.Side.Right),
		"player_died → open_continue(false, side)")
	PlayerSystem.player_right.relife()
	var panel := get_node_or_null("InGamePanel")
	if panel != null:
		panel.close_all()  # M5 起续币面板会暂停,自检关闭后继续跑波次
	PlayerSystem.player_right.hp = 1.0e6  # 防止测试期再次被打死触发面板暂停
	# 等 G4 boss born
	var t_wait := Time.get_ticks_msec() / 1000.0
	while _boss == null and Time.get_ticks_msec() / 1000.0 - t_wait < 90.0:
		await get_tree().process_frame
	_check(_boss != null, "G4: boss (baotou) born")
	_check(stat_boss_bgm, "G4: boss_bgm switched")
	var boss_dead_time := [-1.0]
	if _boss != null:
		_boss.died.connect(func(_m): boss_dead_time[0] = Time.get_ticks_msec() / 1000.0)
	# 等胜利信号
	var t0 := Time.get_ticks_msec() / 1000.0
	while victory_count[0] == 0 and Time.get_ticks_msec() / 1000.0 - t0 < 90.0:
		await get_tree().process_frame
	_check(victory_count[0] == 1, "level_victory emitted exactly once")
	_check(boss_dead_time[0] > 0.0 and victory_time[0] - boss_dead_time[0] >= 1.8,
		"boss dead → ~2s → victory (delay=%.2fs)" % (victory_time[0] - boss_dead_time[0]))
	_check(stat_groups_started == [0, 1, 2, 3, 4], "5 groups in order: %s" % str(stat_groups_started))
	_check(stat_cam_switches == 5, "camera switched 5 times (got %d)" % stat_cam_switches)
	_check(stat_freezes == 5 and SPAWN_FREEZE_TIME == 2.0, "spawn freeze 2s × 5 groups")
	_check(stat_box_rolls > 0, "box probability rolls invoked (%d ticks)" % stat_box_rolls)
	print("[M4-TEST] done, failed=%s" % _test_failed)
	if bgm_player != null:
		bgm_player.stop()
		bgm_player.stream = null
	get_tree().quit(1 if _test_failed else 0)
