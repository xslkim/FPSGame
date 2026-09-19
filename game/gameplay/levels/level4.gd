extends LevelBase
class_name Level4
## Level4(MEP 浮岛月夜,7.2 L4 七波):G0 空波即过 / G1 红龙×1(force_pool:
## 只许 dragon_red,箱子 roll 中也放弃该 tick,照原作 groupId==1 非 DragonRed 返回 null)/
## G2 三色龙×5 / G3 斧×2 / G4 骷髅×2 / G5 龙+Magma×15 / G6 空池清场即胜(2s 延迟)。
## 出生固定正前方 6m(meta born_max_fov=0 / born_max_length=6;
## 龙四点巡回、Magma 相机前 10m 四向,均忽略传入点自算)。无 boss。
## 启动参数:
##   --m6l4-selftest   headless 加速全流程断言

const L4_MONSTER_SCENES := {
	"dragon_red": preload("res://gameplay/monsters/dragon_red.tscn"),
	"dragon_green": preload("res://gameplay/monsters/dragon_green.tscn"),
	"dragon_blue": preload("res://gameplay/monsters/dragon_blue.tscn"),
	"magma_demon": preload("res://gameplay/monsters/magma_demon.tscn"),
}
const L4_POOL_SIZES := {"dragon_red": 4, "dragon_green": 4, "dragon_blue": 4, "magma_demon": 5}

var _test_failed := false
# 自检统计
var stat_g1_keys: Array = []      # G1 实际出生的怪 key
var stat_g6_left_at_start := -1   # G6 开始时的 monster_left(空池应为 0)
var stat_g6_start_time := -1.0    # G6 开始时刻(验证 2s 胜利延迟)
var _battle_t0 := 0.0

func _enter_level() -> void:
	for key in L4_MONSTER_SCENES:
		pool.register_type(key, L4_MONSTER_SCENES[key], L4_POOL_SIZES.get(key, 4))
	if OS.get_cmdline_user_args().has("--m6l4-selftest"):
		_self_test()
		return
	start_battle()

func _start_group(i: int) -> void:
	super(i)
	# 空池波(L4 G0/G6):不等刷怪 tick,直接视为刷完(清场即推进/胜利)
	if (groups[i].get("pool", []) as Array).is_empty():
		monster_left = 0
	if i == 6:
		stat_g6_left_at_start = monster_left
		stat_g6_start_time = Time.get_ticks_msec() / 1000.0

## force_pool 波(G1):跳过箱子 spawn,箱子 roll 中即放弃该 tick(配额不扣,下 tick 重试)
func _spawn_tick() -> void:
	if not bool(groups[cur_group].get("force_pool", false)):
		super()
		return
	if monster_left <= 0:
		return
	if int(_cur.get("max_alive", 0)) <= 0:
		return
	if PlayerSystem.cur_alive_monster >= int(_cur.max_alive):
		return
	var pool_arr: Array = _cur.pool
	if pool_arr.is_empty():
		monster_left = 0
		return
	stat_box_rolls += 1
	if pool.has_inactive("box") and (randf() < float(_cur.bullet_box_rate) \
			or randf() < float(meta.get("gun_rate", 0.02))):
		return  # 抽到补给箱(非本波 pool 怪)→ 放弃该 tick,照原作
	var key := str(pool_arr[_pool_idx % pool_arr.size()])
	_pool_idx += 1
	var m := pool.get_monster(key)
	if m == null:
		_pool_idx -= 1  # 池满,下 tick 重试同一只
		return
	_born_ahead(m)
	monster_left -= 1
	if cur_group == 1:
		stat_g1_keys.append(key)
	if debug_auto_kill:
		get_tree().create_timer(0.15).timeout.connect(func():
			if m != null and m.is_active() and not m.is_dead():
				m.hit(99999.0, m.global_position, GlobalObject.HitType.Body, PlayerSystem.Side.Right))

## 出生:meta 固定 fov 0 / 正前方 6m(与基类 _spawn_tick 同规则,含 born_length_override)
func _born_ahead(m: MonsterBase) -> void:
	var fov := float(meta.get("born_max_fov", 33.0))
	if camera != null and camera.fov > 50.0:
		fov = float(meta.get("born_max_fov_wide", 40.0))
	var length := float(meta.get("born_max_length", 8.0))
	var overrides: Dictionary = meta.get("born_length_override", {})
	if overrides.has(str(cur_group)):
		length = float(overrides[str(cur_group)])
	var params := m.get_born_params(fov, length)
	m.born(m.get_born_position(params.x, params.y), 0, difficulty_waitting_time())

func _check(cond: bool, label: String) -> void:
	print(("[M6L4-TEST] PASS: " if cond else "[M6L4-TEST] FAIL: ") + label)
	if not cond:
		_test_failed = true

## 难度数量断言(纯计算):Easy 0/1/5/2/2/15/15,Hard 0/1/7/3/3/22/22,Hell 0/2/10/4/4/30/30
func _check_difficulty_counts() -> void:
	var saved := GlobalObject.difficulty
	var cases := {
		GlobalObject.Difficulty.Easy: [0, 1, 5, 2, 2, 15, 15],
		GlobalObject.Difficulty.Hard: [0, 1, 7, 3, 3, 22, 22],
		GlobalObject.Difficulty.Hell: [0, 2, 10, 4, 4, 30, 30],
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

## headless 自检:godot --headless --path game scenes/levels/level4.tscn -- --m6l4-selftest
func _self_test() -> void:
	_time_scale = 0.04  # 加速刷怪间隔/冻结/相机 Tween;胜利延迟保持真实 2s 以验证
	debug_auto_kill = true
	_check_difficulty_counts()
	var victory_count := [0]
	var victory_time := [0.0]
	level_victory.connect(func():
		victory_count[0] += 1
		victory_time[0] = Time.get_ticks_msec() / 1000.0)
	# 防测试期玩家被打死弹出续币面板暂停流程
	for side in [PlayerSystem.Side.Left, PlayerSystem.Side.Right]:
		var p := PlayerSystem.get_player(side)
		if p != null:
			p.hp = 1.0e6
	open_continue.connect(func(_is_open, _side):
		var panel := get_node_or_null("InGamePanel")
		if panel != null:
			panel.close_all())
	_battle_t0 = Time.get_ticks_msec() / 1000.0
	start_battle()
	# G0 空波:应不经刷怪等待立即推进到 G1
	while cur_group < 1 and Time.get_ticks_msec() / 1000.0 - _battle_t0 < 5.0:
		await get_tree().process_frame
	var g1_delay := Time.get_ticks_msec() / 1000.0 - _battle_t0
	_check(cur_group == 1 and g1_delay < 1.0, "G0 empty wave skipped immediately (G1 at %.2fs)" % g1_delay)
	# G1 force_pool:只出红龙
	var t1 := Time.get_ticks_msec() / 1000.0
	while cur_group < 2 and Time.get_ticks_msec() / 1000.0 - t1 < 90.0:
		await get_tree().process_frame
	_check(stat_g1_keys == ["dragon_red"], "G1 force_pool spawned only dragon_red: %s" % str(stat_g1_keys))
	# 等胜利信号(G6 空池清场即胜)
	var t0 := Time.get_ticks_msec() / 1000.0
	while victory_count[0] == 0 and Time.get_ticks_msec() / 1000.0 - t0 < 120.0:
		await get_tree().process_frame
	_check(victory_count[0] == 1, "level_victory emitted exactly once")
	_check(stat_g6_left_at_start == 0, "G6 empty pool: monster_left=0 at group start (got %d)" % stat_g6_left_at_start)
	_check(stat_g6_start_time > 0.0 and victory_time[0] - stat_g6_start_time >= 1.8,
		"G6 clear → ~2s → victory (delay=%.2fs)" % (victory_time[0] - stat_g6_start_time))
	_check(stat_groups_started == [0, 1, 2, 3, 4, 5, 6], "7 groups in order: %s" % str(stat_groups_started))
	_check(stat_cam_switches == 7, "camera switched 7 times (G6 reuses cam_pos_5, got %d)" % stat_cam_switches)
	_check(stat_freezes == 7 and SPAWN_FREEZE_TIME == 2.0, "spawn freeze 2s × 7 groups")
	_check(stat_box_rolls > 0, "box probability rolls invoked (%d ticks)" % stat_box_rolls)
	print("[M6L4-TEST] done, failed=%s" % _test_failed)
	if bgm_player != null:
		bgm_player.stop()
		bgm_player.stream = null
	get_tree().quit(1 if _test_failed else 0)
