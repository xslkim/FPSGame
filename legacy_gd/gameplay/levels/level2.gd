extends LevelBase
class_name Level2
## Level2 木镇(7.2 L2 三波):FireWindow 窗口刷怪 + 怪物等级体系 + G2 开始 15s 后 Boss 出场。
## 启动参数:
##   (无)                直接开战(L2 无开场演出)
##   --m6l2-selftest     headless 加速全流程断言
##   --m6l2-markers      在窗口(黄)/SrcPosition(红)放调试球,截图目检用

const L2_MONSTER_SCENES := {
	"toon_shoot": preload("res://gameplay/monsters/toon.tscn"),
	"toon_shoot_alien": preload("res://gameplay/monsters/toon_alien.tscn"),
	"level2_boss": preload("res://gameplay/monsters/level2_boss.tscn"),
}
const L2_POOL_SIZES := {"toon_shoot": 10, "toon_shoot_alien": 6, "level2_boss": 1}
const SHAKE_STRENGTH := 0.25  # 出场震屏 h/v_offset 幅度(随时间衰减)

@export var window_group_paths: Array[NodePath] = []  # Windows/group0..2,波 N ↔ 窗口组 N

var _window_groups: Array = []   # Array[Array[FireWindow]]
var _active_windows: Array = []  # 当前波可用窗口
var _boss_group := -1
var _boss_delay_left := -1.0
var _shake_left := 0.0
var _shake_duration := 0.8
var _boss_cleaned := false
var _test_failed := false

# 自检统计
var stat_window_blocked := 0
var stat_windows_used: Array = []
var stat_shake_seen := false
var stat_static_boxes := 0
var stat_boss_anim_rate := 0.0
var stat_boss_level := -1
var stat_boss_dead_time := -1.0
var _boss_group_start_time := -1.0
var _boss_spawn_time := -1.0

func _load_meta() -> void:
	super()
	meta = meta.duplicate(true)  # 不污染 DataMgr 共享表
	_boss_group = int(meta.get("boss_group", -1))
	meta["boss_group"] = -1  # Boss 不走基类"波开始即出场":本类按 boss_delay 延迟处理

## 派生覆盖:注册 L2 怪 → 收集窗口 → 开战/自检(L2 无开场演出)
func _enter_level() -> void:
	for key in L2_MONSTER_SCENES:
		pool.register_type(key, L2_MONSTER_SCENES[key], L2_POOL_SIZES.get(key, 6))
	_collect_windows()
	var args := OS.get_cmdline_user_args()
	if args.has("--m6l2-markers"):
		_add_debug_markers()
	if args.has("--m6l2-selftest"):
		_self_test()
		return
	start_battle()

func _collect_windows() -> void:
	_window_groups.clear()
	for p in window_group_paths:
		var n := get_node_or_null(p)
		var arr: Array = []
		if n != null:
			for c in n.get_children():
				if c is FireWindow:
					arr.append(c)
		_window_groups.append(arr)

func calc_group_params(i: int) -> Dictionary:
	var p := super(i)
	var g: Dictionary = groups[i]
	p["windows"] = int(g.get("windows", 0))
	p["base_level"] = int(g.get("base_level", 0))
	return p

func _start_group(i: int) -> void:
	super(i)
	_active_windows = _window_groups[i] if i < _window_groups.size() else []
	stat_windows_used.append(_active_windows.size())
	if i == _boss_group and _boss_delay_left < 0.0 and _boss == null:
		_boss_group_start_time = Time.get_ticks_msec() / 1000.0
		_boss_delay_left = float(meta.get("boss_delay", 15.0))
		print("[%s] group %d: boss in %.0fs" % [level_key, i, _boss_delay_left])

func _process(delta: float) -> void:
	if _battle_active and not GlobalObject.is_game_pause:
		if _boss_delay_left > 0.0:
			_boss_delay_left -= delta
			if _boss_delay_left <= 0.0:
				_spawn_boss()
		if _shake_left > 0.0:
			_shake_left -= delta
			if camera != null:
				if _shake_left > 0.0:
					var a := SHAKE_STRENGTH * (_shake_left / maxf(_shake_duration, 0.01))
					camera.h_offset = randf_range(-a, a)
					camera.v_offset = randf_range(-a, a)
				else:
					camera.h_offset = 0.0
					camera.v_offset = 0.0
	super(delta)

## 刷怪 tick:箱子判定同基类;Toon 怪走窗口分配(FireWindow.pick_free,无空窗不刷)
func _spawn_tick() -> void:
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
	var key := ""
	var box_kind := -1
	stat_box_rolls += 1
	if pool.has_inactive("box"):
		if randf() < float(_cur.bullet_box_rate):
			key = "box"
			box_kind = BoxMonster.BoxKind.Bullet
		elif randf() < float(meta.get("gun_rate", 0.05)):
			key = "box"
			box_kind = BoxMonster.BoxKind.GunAK if randf() < 0.5 else BoxMonster.BoxKind.GunM4
	if key == "":
		key = pool_arr[_pool_idx % pool_arr.size()]
		_pool_idx += 1
	# Toon 怪先占窗(7.2):无空窗本 tick 不刷,下 tick 重试同一只
	var w: FireWindow = null
	if key != "box":
		w = FireWindow.pick_free(_active_windows)
		if w == null:
			stat_window_blocked += 1
			if box_kind < 0:
				_pool_idx -= 1
			return
	var m := pool.get_monster(key)
	if m == null:
		if box_kind < 0:
			_pool_idx -= 1
		return
	if m is BoxMonster:
		m.box_kind = box_kind
		_born_box(m)
	elif m is ToonMonster:
		var lv := _calc_monster_level()
		w.fire_monster = m
		m.fire_window = w
		m.born(w.get_src_position(), lv, difficulty_waitting_time())  # 从 SrcPosition 翻窗爬入
	monster_left -= 1
	if key == "box":
		print("[%s] box spawned kind=%d (left=%d)" % [level_key, box_kind, monster_left])
	if debug_auto_kill:
		get_tree().create_timer(0.15).timeout.connect(func():
			if m != null and m.is_active() and not m.is_dead():
				m.hit(99999.0, m.global_position, GlobalObject.HitType.Body,
					PlayerSystem.Side.Right))

## 补给箱出生(box_born 参数,不占窗口);G0 静态(原作 BoxMonster 静态标记:
## 不改 box_monster.gd,born 后把移速清零,G1 起恢复默认逃跑跳)
func _born_box(m: MonsterBase) -> void:
	var bb: Dictionary = meta.get("box_born", {})
	var params := m.get_born_params(float(bb.get("max_fov", 33.0)),
		float(bb.get("max_length", 12.0)))
	m.born(m.get_born_position(params.x, params.y), 0, difficulty_waitting_time())
	if cur_group == 0:
		m.move_speed = 0.0
		stat_static_boxes += 1

## 怪物等级(7.2):level = maxLevel - ceil(maxLevel*monsterLeft/groupNum) + baseLevel,封顶 level_cap(6)
func _calc_monster_level() -> int:
	return _level_formula(_monster_max_level(), monster_left,
		maxi(int(_cur.num), 1), int(_cur.get("base_level", 0)))

func _level_formula(max_lv: int, left: int, num: int, base: int) -> int:
	var lv := max_lv - int(ceil(float(max_lv) * float(left) / maxf(float(num), 1.0))) + base
	return mini(lv, int(meta.get("level_cap", 6)))

func _monster_max_level() -> int:
	var d: Dictionary = meta.get("monster_max_level", {"easy": 3, "hard": 4, "hell": 5})
	match GlobalObject.difficulty:
		GlobalObject.Difficulty.Hard:
			return int(d.get("hard", 4))
		GlobalObject.Difficulty.Hell:
			return int(d.get("hell", 5))
		_:
			return int(d.get("easy", 3))

## Boss 难度动画倍率(6.3):easy 1 / hard 2.25 / hell 4
func _boss_anim_rate() -> float:
	match GlobalObject.difficulty:
		GlobalObject.Difficulty.Hard:
			return 2.25
		GlobalObject.Difficulty.Hell:
			return 4.0
		_:
			return 1.0

func _boss_level() -> int:
	var d: Dictionary = meta.get("boss_level", {"easy": 0, "hard": 3, "hell": 6})
	match GlobalObject.difficulty:
		GlobalObject.Difficulty.Hard:
			return int(d.get("hard", 3))
		GlobalObject.Difficulty.Hell:
			return int(d.get("hell", 6))
		_:
			return int(d.get("easy", 0))

## G2 开始 15s 后出场:难度动画倍率 + born(boss_level)+ 切 boss_bgm + 震屏
func _spawn_boss() -> void:
	var key := str(meta.get("boss", ""))
	_boss = pool.get_monster(key)
	if _boss == null:
		push_error("[%s] boss pool empty: %s" % [level_key, key])
		return
	var rate := _boss_anim_rate()
	stat_boss_anim_rate = rate
	stat_boss_level = _boss_level()
	var l2b := _boss as Level2BossMonster
	if l2b != null:
		l2b.set_difficulty_anim_rate(rate)
		if not l2b.entrance_shake.is_connected(_on_entrance_shake):
			l2b.entrance_shake.connect(_on_entrance_shake)
	if not _boss.died.is_connected(_on_boss_died):
		_boss.died.connect(_on_boss_died)
	var params := _boss.get_born_params(float(meta.get("born_max_fov", 33.0)),
		float(meta.get("born_max_length", 8.0)))
	_boss.born(_boss.get_born_position(params.x, params.y), stat_boss_level, 0.0)
	_boss_spawn_time = Time.get_ticks_msec() / 1000.0
	_play_music(str(meta.get("boss_bgm", "")))
	stat_boss_bgm = true
	print("[%s] boss %s born (delay=%.1fs anim_rate=%.2f level=%d)" % [
		level_key, key,
		_boss_spawn_time - _boss_group_start_time if _boss_group_start_time > 0.0 else -1.0,
		rate, stat_boss_level])
	if debug_auto_kill:
		get_tree().create_timer(0.15).timeout.connect(func():
			if _boss != null and _boss.is_active() and not _boss.is_dead():
				# L2 Boss 只头部掉血(6.3),自动击杀必须打头
				_boss.hit(99999.0, _boss.global_position, GlobalObject.HitType.Head,
					PlayerSystem.Side.Right))

## 出场震屏(0.8s):h/v_offset 随机偏移,随剩余时间衰减
func _on_entrance_shake(duration: float) -> void:
	_shake_duration = duration
	_shake_left = duration
	stat_shake_seen = true

## Boss died → 2s 后 level_victory(走基类 _trigger_victory 单次延迟);
## L2 Boss 死亡不自动回收,死亡演出播完(胜利信号后)由关卡清理
func _on_boss_died(_m: MonsterBase) -> void:
	if _boss_cleaned:
		return
	_boss_cleaned = true
	stat_boss_dead_time = Time.get_ticks_msec() / 1000.0
	PlayerSystem.cur_alive_monster -= 1  # born 时 +1,boss 不回收,关卡补上
	print("[%s] boss died → victory in %.1fs" % [level_key, VICTORY_DELAY])
	_trigger_victory()
	await level_victory
	if _boss != null and is_instance_valid(_boss) and _boss.is_active():
		_boss._deactivate()
		print("[%s] boss cleaned by level" % level_key)

## --m6l2-markers:窗口黄球 / SrcPosition 红球(截图目检用)
func _add_debug_markers() -> void:
	for arr in _window_groups:
		for w in arr:
			_add_marker(w, Color(1.0, 0.85, 0.1), 0.35)
			var src := w.get_node_or_null(w.src_position_path) as Node3D
			if src != null:
				_add_marker(src, Color(0.9, 0.15, 0.1), 0.25)

func _add_marker(parent: Node3D, color: Color, radius: float) -> void:
	var mesh := MeshInstance3D.new()
	var sphere := SphereMesh.new()
	sphere.radius = radius
	sphere.height = radius * 2.0
	var mat := StandardMaterial3D.new()
	mat.albedo_color = color
	mat.emission_enabled = true
	mat.emission = color
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	sphere.material = mat
	mesh.mesh = sphere
	parent.add_child(mesh)

# ---- 自检(--m6l2-selftest)----

func _check(cond: bool, label: String) -> void:
	print(("[M6L2-TEST] PASS: " if cond else "[M6L2-TEST] FAIL: ") + label)
	if not cond:
		_test_failed = true

## 难度数量断言(方案 bug 表修正版:Hard G0 = (int)(30×1.5)=45,不是原作的 30)
func _check_difficulty_counts() -> void:
	var saved := GlobalObject.difficulty
	var cases := {
		GlobalObject.Difficulty.Easy: [30, 30, 40],
		GlobalObject.Difficulty.Hard: [45, 45, 60],
		GlobalObject.Difficulty.Hell: [60, 60, 80],
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

func _check_level_formula() -> void:
	_check(_level_formula(3, 30, 30, 0) == 0, "G0 easy first spawn lv=0")
	_check(_level_formula(3, 30, 30, 1) == 1, "G1 base+1: first spawn lv=1")
	_check(_level_formula(3, 1, 30, 0) == 2, "G0 ramp last lv=2 (maxLv-1)")
	_check(_level_formula(4, 45, 45, 0) == 0, "G0 hard first lv=0")
	_check(_level_formula(4, 1, 45, 0) == 3, "G0 hard last lv=3")
	_check(_level_formula(5, 1, 10, 9) == 6, "level cap 6 (5-1+9=13 → 6)")
	_check(_level_formula(6, 1, 10, 6) == 6, "level cap 6 boundary")

func _check_boss_params() -> void:
	var saved := GlobalObject.difficulty
	GlobalObject.difficulty = GlobalObject.Difficulty.Easy
	_check(_boss_anim_rate() == 1.0 and _boss_level() == 0, "easy: anim_rate=1 boss_lv=0")
	GlobalObject.difficulty = GlobalObject.Difficulty.Hard
	_check(_boss_anim_rate() == 2.25 and _boss_level() == 3, "hard: anim_rate=2.25 boss_lv=3")
	GlobalObject.difficulty = GlobalObject.Difficulty.Hell
	_check(_boss_anim_rate() == 4.0 and _boss_level() == 6, "hell: anim_rate=4 boss_lv=6")
	GlobalObject.difficulty = saved

## 窗口分配与占用释放:占满 → pick_free null(不刷);杀光 → 全部释放
func _unit_test_windows() -> void:
	var ws: Array = _window_groups[0]
	_check(ws.size() == 7 and _window_groups[1].size() == 5 and _window_groups[2].size() == 3,
		"window groups 7/5/3")
	var toons: Array = []
	for w in ws:
		var t := pool.get_monster("toon_shoot")
		if t == null:
			break
		w.fire_monster = t
		(t as ToonMonster).fire_window = w
		t.born(w.get_src_position(), 0, 0.0)
		toons.append(t)
	_check(toons.size() == 7, "7 toons born at windows")
	var occupied := 0
	for w in ws:
		if not w.is_free():
			occupied += 1
	_check(occupied == 7, "all 7 windows occupied")
	_check(FireWindow.pick_free(ws) == null, "no free window → pick_free null (无空窗不刷)")
	for t in toons:
		t.hit(9999.0, t.global_position, GlobalObject.HitType.Body, PlayerSystem.Side.Right)
	await get_tree().process_frame
	var freed := 0
	for w in ws:
		if w.is_free():
			freed += 1
	_check(freed == 7, "windows released on death (%d/7)" % freed)
	_check(FireWindow.pick_free(ws) != null, "pick_free works again after release")
	await get_tree().create_timer(1.8).timeout  # 等 1.5s 回收回池,不影响后续刷怪

## headless 自检:godot --headless --path game scenes/levels/level2.tscn -- --m6l2-selftest
func _self_test() -> void:
	_time_scale = 0.04  # 加速刷怪间隔/冻结/相机 Tween;boss 15s 与胜利 2s 保持真实以验证
	debug_auto_kill = true
	_check_difficulty_counts()
	_check_level_formula()
	_check_boss_params()
	await _unit_test_windows()
	PlayerSystem.player_right.hp = 1.0e6
	PlayerSystem.player_left.hp = 1.0e6
	var victory_count := [0]
	var victory_time := [0.0]
	level_victory.connect(func():
		victory_count[0] += 1
		victory_time[0] = Time.get_ticks_msec() / 1000.0)
	start_battle()
	# 等 G2 boss 出场(15s 真实延迟)
	var t_wait := Time.get_ticks_msec() / 1000.0
	while _boss == null and Time.get_ticks_msec() / 1000.0 - t_wait < 90.0:
		await get_tree().process_frame
	_check(_boss != null, "G2: boss (level2_boss) born after 15s")
	var delay := _boss_spawn_time - _boss_group_start_time
	_check(_boss != null and delay >= 14.0 and delay <= 20.0,
		"boss delay ~15s (measured %.2fs)" % delay)
	_check(stat_boss_bgm, "boss_bgm switched at boss entrance")
	_check(stat_boss_anim_rate == 1.0, "easy anim rate 1.0 applied to boss")
	_check(stat_boss_level == 0, "easy boss level 0 applied")
	_check(stat_shake_seen, "entrance_shake → camera shake")
	# 等胜利信号(debug_auto_kill 打头击杀 boss)
	var t0 := Time.get_ticks_msec() / 1000.0
	while victory_count[0] == 0 and Time.get_ticks_msec() / 1000.0 - t0 < 90.0:
		await get_tree().process_frame
	_check(victory_count[0] == 1, "level_victory emitted exactly once")
	_check(stat_boss_dead_time > 0.0 and victory_time[0] - stat_boss_dead_time >= 1.8,
		"boss died → ~2s → victory (delay=%.2fs)" % (victory_time[0] - stat_boss_dead_time))
	_check(stat_groups_started == [0, 1, 2], "3 groups in order: %s" % str(stat_groups_started))
	_check(stat_cam_switches == 3, "camera switched 3 times (got %d)" % stat_cam_switches)
	_check(stat_freezes == 3 and SPAWN_FREEZE_TIME == 2.0, "spawn freeze 2s × 3 groups")
	_check(stat_windows_used == [7, 5, 3], "windows per wave 7/5/3: %s" % str(stat_windows_used))
	_check(stat_box_rolls > 0, "box probability rolls invoked (%d ticks)" % stat_box_rolls)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(_boss == null or not _boss.is_active(), "boss cleaned by level after victory")
	print("[M6L2-TEST] done, failed=%s (static_boxes=%d window_blocked=%d)" % [
		_test_failed, stat_static_boxes, stat_window_blocked])
	if bgm_player != null:
		bgm_player.stop()
		bgm_player.stream = null
	get_tree().quit(1 if _test_failed else 0)
