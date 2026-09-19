extends LevelBase
class_name Level3
## Level3 战斗关(7.2 L3 七波,城市街区;G6 Boss rock_warrior 与小怪同刷)。
## 启动参数:
##   (无)               直接开战
##   --m6l3-selftest    headless 加速全流程断言

# L3 所需怪在基类 MONSTER_SCENES 之外,本关自行注册(不改基类,避免与 L2/L4 并行任务冲突)
const L3_MONSTER_SCENES := {
	"wolf": preload("res://gameplay/monsters/wolf.tscn"),
	"wolf_blue": preload("res://gameplay/monsters/wolf_blue.tscn"),
	# M7 批次 B 已实装 wolf_green.tscn(绿色变体材质,数值沿用 wolf_blue meta)
	"wolf_green": preload("res://gameplay/monsters/wolf_green.tscn"),
	"fat_zombie": preload("res://gameplay/monsters/fat_zombie.tscn"),
	"toon_shoot_alien": preload("res://gameplay/monsters/toon_alien.tscn"),
	"rock_warrior": preload("res://gameplay/monsters/rock_warrior.tscn"),
}
const L3_POOL_SIZES := {
	"wolf": 10, "wolf_blue": 8, "wolf_green": 8,
	"fat_zombie": 8, "toon_shoot_alien": 8, "rock_warrior": 1,
}

var _test_failed := false

func _enter_level() -> void:
	for key in L3_MONSTER_SCENES:
		pool.register_type(key, L3_MONSTER_SCENES[key], L3_POOL_SIZES.get(key, 6))
	if OS.get_cmdline_user_args().has("--m6l3-selftest"):
		_self_test()
		return
	start_battle()

## G6 InitBoss(7.2):born + LookAt 相机 + 切 boss_bgm,与本波小怪同刷
## (born/bgm 基类 _start_group 已做,这里补面向相机;胜利走基类 boss died→回收→2s)
func _spawn_boss() -> void:
	super._spawn_boss()
	if _boss != null:
		_boss.face_camera()

func _check(cond: bool, label: String) -> void:
	print(("[M6L3-TEST] PASS: " if cond else "[M6L3-TEST] FAIL: ") + label)
	if not cond:
		_test_failed = true

## 难度数量断言(纯计算):Easy/Hard/Hell = 15/22/30 × 7 波(倍率 1/1.5/2)
func _check_difficulty_counts() -> void:
	var saved := GlobalObject.difficulty
	var cases := {
		GlobalObject.Difficulty.Easy: [15, 15, 15, 15, 15, 15, 15],
		GlobalObject.Difficulty.Hard: [22, 22, 22, 22, 22, 22, 22],
		GlobalObject.Difficulty.Hell: [30, 30, 30, 30, 30, 30, 30],
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

## headless 自检:godot --headless --path game scenes/levels/level3.tscn -- --m6l3-selftest
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
		panel.close_all()  # 续币面板会暂停,自检关闭后继续跑波次
	PlayerSystem.player_right.hp = 1.0e6  # 防止测试期再次被打死触发面板暂停
	# 等 G6 boss born
	var t_wait := Time.get_ticks_msec() / 1000.0
	while _boss == null and Time.get_ticks_msec() / 1000.0 - t_wait < 180.0:
		await get_tree().process_frame
	_check(_boss != null, "G6: boss (rock_warrior) born")
	_check(cur_group == 6, "boss born at G6 (cur_group=%d)" % cur_group)
	_check(stat_boss_bgm, "G6: boss_bgm switched")
	if bgm_player != null and bgm_player.stream != null:
		_check(bgm_player.stream.resource_path.ends_with("Level3Boss.mp3"),
			"G6: bgm stream = %s" % bgm_player.stream.resource_path)
	var boss_dead_time := [-1.0]
	if _boss != null:
		_boss.died.connect(func(_m): boss_dead_time[0] = Time.get_ticks_msec() / 1000.0)
		# rock_warrior 硬化皮肤:非 atk01 状态单次 hit 只掉 1 血,自检压血线补刀
		_boss.hp = 1.0
		_boss.hit(99999.0, _boss.global_position, GlobalObject.HitType.Body,
			PlayerSystem.Side.Right)
	# 等胜利信号
	var t0 := Time.get_ticks_msec() / 1000.0
	while victory_count[0] == 0 and Time.get_ticks_msec() / 1000.0 - t0 < 90.0:
		await get_tree().process_frame
	_check(victory_count[0] == 1, "level_victory emitted exactly once")
	_check(boss_dead_time[0] > 0.0 and victory_time[0] - boss_dead_time[0] >= 1.8,
		"boss died → ~2s → victory (delay=%.2fs)" % (victory_time[0] - boss_dead_time[0]))
	_check(stat_groups_started == [0, 1, 2, 3, 4, 5, 6],
		"7 groups in order: %s" % str(stat_groups_started))
	_check(stat_cam_switches == 7, "camera switched 7 times (got %d)" % stat_cam_switches)
	_check(stat_freezes == 7 and SPAWN_FREEZE_TIME == 2.0, "spawn freeze 2s × 7 groups")
	_check(stat_box_rolls > 0, "box probability rolls invoked (%d ticks)" % stat_box_rolls)
	print("[M6L3-TEST] done, failed=%s" % _test_failed)
	if bgm_player != null:
		bgm_player.stop()
		bgm_player.stream = null
	get_tree().quit(1 if _test_failed else 0)
