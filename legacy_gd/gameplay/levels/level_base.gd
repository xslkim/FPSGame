extends Node3D
class_name LevelBase
## 关卡基类(7.1 通用机制):难度倍率 / 波次推进 / 机位切换+冻结刷怪 /
## 补给箱概率 / 刷怪点 / 胜负与续币信号 / BGM 切换。
## 派生(如 level1.gd)负责开场演出,结束后调 start_battle()。

signal level_victory
## 续币面板信号(M5 做 UI):is_open=true 为弹尽(FireSystem 转发),false 为玩家死亡
signal open_continue(is_open, side)

const MONSTER_SCENES := {
	"bull": preload("res://gameplay/monsters/bull.tscn"),
	"axe_zombie": preload("res://gameplay/monsters/axe_zombie.tscn"),
	"fly_axe_zombie": preload("res://gameplay/monsters/fly_axe_zombie.tscn"),
	"skeleton": preload("res://gameplay/monsters/skeleton.tscn"),
	"baotou": preload("res://gameplay/monsters/baotou.tscn"),
	"box": preload("res://gameplay/monsters/box_monster.tscn"),
}
const POOL_SIZES := {"bull": 8, "axe_zombie": 8, "fly_axe_zombie": 8, "skeleton": 8, "baotou": 1, "box": 2}
const CAM_BLEND_TIME := 1.5    # 机位切换 Tween 时长(模拟 Cinemachine blend)
const SPAWN_FREEZE_TIME := 2.0 # 切机位后冻结刷怪 2 秒
const VICTORY_DELAY := 2.0     # 胜利延迟(原作 Invoke 2s,单次触发)

@export var level_key := "level1"
@export var cam_position_paths: Array[NodePath] = []  # cam_pos_0..N,波 N ↔ 机位 N
@export var pool_path: NodePath = ^"MonsterPool"
@export var camera_path: NodePath = ^"Camera3D"
@export var fire_system_path: NodePath = ^"Camera3D/FireSystem"
@export var bgm_player_path: NodePath = ^"BGM"

var cam_positions: Array[Node3D] = []
var pool: MonsterPool
var camera: Camera3D
var fire_system: FireSystem
var bgm_player: AudioStreamPlayer

var meta: Dictionary = {}
var groups: Array = []
var diff_rate := 1.0

var cur_group := -1
var monster_left := 0
var debug_auto_kill := false  # 自检:怪出生后自动击杀,跑通全流程
var _time_scale := 1.0        # 自检加速(缩放刷怪间隔/冻结/相机 Tween)

var _cur: Dictionary = {}
var _spawn_timer := 0.0
var _freeze_timer := 0.0
var _pool_idx := 0
var _battle_active := false
var _victory_state := 0  # 0 未触发 / 1 已触发倒计时中 / 2 已发信号
var _boss: MonsterBase = null
var _cam_tween: Tween
var battle_start_time := 0.0
var last_stars := 0  # 通关结算星级(in_game_panel 胜利面板显示)

# 自检统计
var stat_cam_switches := 0
var stat_freezes := 0
var stat_box_rolls := 0
var stat_boss_bgm := false
var stat_groups_started: Array = []

func _ready() -> void:
	add_to_group("level")
	_load_meta()
	pool = get_node_or_null(pool_path) as MonsterPool
	camera = get_node_or_null(camera_path) as Camera3D
	fire_system = get_node_or_null(fire_system_path) as FireSystem
	bgm_player = get_node_or_null(bgm_player_path) as AudioStreamPlayer
	for p in cam_position_paths:
		var n := get_node_or_null(p)
		if n != null:
			cam_positions.append(n)
	if pool == null:
		pool = get_node_or_null("MonsterPool") as MonsterPool
	for key in MONSTER_SCENES:
		pool.register_type(key, MONSTER_SCENES[key], POOL_SIZES.get(key, 6))
	PlayerSystem.player_died.connect(_on_player_died)
	if fire_system != null:
		fire_system.open_continue.connect(func(is_open, side): open_continue.emit(is_open, side))
	_reset_game_state()
	_enter_level()

## 进场状态(7.4/M5):Battle + 计数清零 + 重置玩家;关卡 Start 强制 ControllerOrRight(4.4)
func _reset_game_state() -> void:
	GlobalObject.scene_state = GlobalObject.GameState.Battle
	PlayerSystem.cur_alive_monster = 0
	InputManager.set_input_mode(InputManager.InputMode.ControllerOrRight)

## 派生覆盖:开场演出,结束后调 start_battle()
func _enter_level() -> void:
	start_battle()

## 开场结束/无开场 → 开战:开输入、绑枪、播 bgm、开 G0
func start_battle() -> void:
	InputManager.fire_enabled = true
	battle_start_time = Time.get_ticks_msec() / 1000.0
	if fire_system != null:
		fire_system.bind_players()
		fire_system.set_process(true)
		fire_system.visible = true
	_play_music(str(meta.get("bgm", "")))
	_battle_active = true
	_start_group(0)

func _load_meta() -> void:
	meta = DataMgr.get_level_meta().get("levels", {}).get(level_key, {})
	groups = meta.get("groups", [])
	var rates: Dictionary = meta.get("diff_rate", {"easy": 1.0, "hard": 1.5, "hell": 2.0})
	match GlobalObject.difficulty:
		GlobalObject.Difficulty.Hard:
			diff_rate = float(rates.get("hard", 1.5))
		GlobalObject.Difficulty.Hell:
			diff_rate = float(rates.get("hell", 2.0))
		_:
			diff_rate = float(rates.get("easy", 1.0))

## 难度公式(7.1):数量=(int)(基础×倍率)、间隔=基础÷倍率、存活上限=(int)(上限×倍率)
func calc_group_params(i: int) -> Dictionary:
	var g: Dictionary = groups[i]
	return {
		"num": int(float(g.get("monster_num", 0)) * diff_rate),
		"interval": float(g.get("interval", 3.0)) / diff_rate,
		"max_alive": int(float(g.get("max_alive", 2.0)) * diff_rate),
		"bullet_box_rate": float(g.get("bullet_box_rate", 0.05)),
		"pool": g.get("pool", []),
	}

func _start_group(i: int) -> void:
	cur_group = i
	stat_groups_started.append(i)
	_cur = calc_group_params(i)
	monster_left = _cur.num
	_pool_idx = 0
	_spawn_timer = 0.0
	_freeze_timer = SPAWN_FREEZE_TIME * _time_scale  # 切机位后冻结刷怪 2 秒
	stat_freezes += 1
	_switch_camera(i)
	if i == int(meta.get("boss_group", -1)) and str(meta.get("boss", "")) != "":
		_spawn_boss()  # Boss 与本波同时出场(7.2 L1 G4)
		_play_music(str(meta.get("boss_bgm", "")))
		stat_boss_bgm = true
	print("[%s] group %d start: num=%d interval=%.2f max_alive=%d freeze=%.1fs" % [
		level_key, i, _cur.num, _cur.interval, _cur.max_alive, SPAWN_FREEZE_TIME])

func _switch_camera(i: int) -> void:
	if camera == null or i >= cam_positions.size() or cam_positions[i] == null:
		return
	stat_cam_switches += 1
	if _cam_tween != null and _cam_tween.is_valid():
		_cam_tween.kill()
	_cam_tween = create_tween()
	_cam_tween.tween_property(camera, "global_transform", cam_positions[i].global_transform,
		CAM_BLEND_TIME * maxf(_time_scale, 0.05)).set_trans(Tween.TRANS_SINE)

func _spawn_boss() -> void:
	var key := str(meta.get("boss", ""))
	_boss = pool.get_monster(key)
	if _boss == null:
		push_error("[%s] boss pool empty: %s" % [level_key, key])
		return
	var params := _boss.get_born_params(float(meta.get("born_max_fov", 33.0)),
		float(meta.get("born_max_length", 8.0)))
	_boss.born(_boss.get_born_position(params.x, params.y), 0, 0.0)
	print("[%s] boss %s born at %s" % [level_key, key, str(_boss.global_position)])
	if debug_auto_kill:
		get_tree().create_timer(0.15).timeout.connect(func():
			if _boss != null and _boss.is_active() and not _boss.is_dead():
				_boss.hit(99999.0, _boss.global_position, GlobalObject.HitType.Body,
					PlayerSystem.Side.Right))

func _process(delta: float) -> void:
	if not _battle_active or GlobalObject.is_game_pause:
		return
	if _freeze_timer > 0.0:
		_freeze_timer -= delta
	else:
		_spawn_timer += delta
		if _spawn_timer >= float(_cur.get("interval", 3.0)) * _time_scale:
			_spawn_timer = 0.0
			_spawn_tick()
	_check_progress()

## 刷怪 tick:先判子弹箱(箱未激活才出)→ 未中再以 gun_rate 判枪箱(AK/M4 各半)→ 都没中从本波 pool 取
func _spawn_tick() -> void:
	if monster_left <= 0:
		return
	if int(_cur.get("max_alive", 0)) <= 0:
		return
	if PlayerSystem.cur_alive_monster >= int(_cur.max_alive):
		return
	var pool_arr: Array = _cur.pool
	if pool_arr.is_empty():
		monster_left = 0  # 空波(L4 G0/G6 语义)
		return
	var key := ""
	var box_kind := -1
	stat_box_rolls += 1
	if pool.has_inactive("box"):
		if randf() < float(_cur.bullet_box_rate):
			key = "box"
			box_kind = BoxMonster.BoxKind.Bullet
		elif randf() < float(meta.get("gun_rate", 0.02)):
			key = "box"
			box_kind = BoxMonster.BoxKind.GunAK if randf() < 0.5 else BoxMonster.BoxKind.GunM4
	if key == "":
		key = pool_arr[_pool_idx % pool_arr.size()]
		_pool_idx += 1
	var m := pool.get_monster(key)
	if m == null:
		if box_kind < 0:
			_pool_idx -= 1  # 池满,下 tick 重试同一只
		return
	if m is BoxMonster:
		m.box_kind = box_kind
	# 出生点:±33°(FOV>50 → ±40°)/ 默认 8m,born_length_override 按波覆盖(7.1)
	var fov := float(meta.get("born_max_fov", 33.0))
	if camera != null and camera.fov > 50.0:
		fov = float(meta.get("born_max_fov_wide", 40.0))
	var length := float(meta.get("born_max_length", 8.0))
	var overrides: Dictionary = meta.get("born_length_override", {})
	if overrides.has(str(cur_group)):
		length = float(overrides[str(cur_group)])
	var params := m.get_born_params(fov, length)
	m.born(m.get_born_position(params.x, params.y), 0, difficulty_waitting_time())
	monster_left -= 1  # 箱子占本波配额
	if key == "box":
		print("[%s] box spawned kind=%d (left=%d)" % [level_key, box_kind, monster_left])
	if debug_auto_kill:
		get_tree().create_timer(0.15).timeout.connect(func():
			if m != null and m.is_active() and not m.is_dead():
				m.hit(99999.0, m.global_position, GlobalObject.HitType.Body, PlayerSystem.Side.Right))

## 等待时长按难度(6.1.2):Easy rand(3,8) / Hard rand(0,2) / Hell 0
func difficulty_waitting_time() -> float:
	match GlobalObject.difficulty:
		GlobalObject.Difficulty.Hard:
			return randf_range(0.0, 2.0)
		GlobalObject.Difficulty.Hell:
			return 0.0
		_:
			return randf_range(3.0, 8.0)

func _check_progress() -> void:
	if _victory_state != 0:
		return
	# 胜利:Boss HP≤0(有 boss 关)/ 清完最后一波(无 boss 关,7.1)
	if str(meta.get("boss", "")) != "":
		if _boss != null and not _boss.is_active():
			_trigger_victory()
			return
	elif cur_group == groups.size() - 1 and monster_left == 0 \
			and PlayerSystem.cur_alive_monster == 0:
		_trigger_victory()
		return
	# 波次推进:待刷=0 且场上存活=0 → 下一波
	if cur_group >= 0 and cur_group < groups.size() - 1 \
			and monster_left == 0 and PlayerSystem.cur_alive_monster == 0:
		_start_group(cur_group + 1)

## 胜利延迟 2s,单次触发(修原作每帧重复注册 bug)
func _trigger_victory() -> void:
	_victory_state = 1
	_settle_stars()
	print("[%s] level clear → victory in %.1fs" % [level_key, VICTORY_DELAY])
	await get_tree().create_timer(VICTORY_DELAY).timeout
	if _victory_state != 1:
		return
	_victory_state = 2
	print("[%s] level_victory emitted (stars=%d)" % [level_key, last_stars])
	level_victory.emit()

## 通关星级本地结算(8.3,替代原作服务器下发):
## 通关保底 1 星;活跃玩家平均剩余 HP ≥ 50 → +1 星;通关用时 ≤ par_time(meta 可配,默认 300s)→ +1 星。
## 分数 = 星级×1000 + 平均 HP×10;星级/分数取历史最高落盘。
func _settle_stars() -> void:
	var order: Array = DataMgr.get_level_meta().get("level_order", [])
	var idx := order.find(level_key)
	if idx < 0:
		return
	var hp_sum := 0.0
	var n := 0
	for side in [PlayerSystem.Side.Left, PlayerSystem.Side.Right]:
		var p := PlayerSystem.get_player(side)
		if p != null and p.active:
			hp_sum += p.hp
			n += 1
	var avg_hp := hp_sum / maxf(float(n), 1.0)
	var elapsed := Time.get_ticks_msec() / 1000.0 - battle_start_time
	var par := float(meta.get("par_time", 300.0))
	last_stars = 1 + (1 if avg_hp >= 50.0 else 0) + (1 if elapsed <= par else 0)
	var score := last_stars * 1000 + int(avg_hp) * 10
	DataMgr.set_level_result(idx, last_stars, score, 0)
	print("[%s] settle: stars=%d avg_hp=%.0f elapsed=%.1fs par=%.0fs" % [
		level_key, last_stars, avg_hp, elapsed, par])

func _on_player_died(side: int) -> void:
	print("[%s] player_died side=%d → open_continue(false) (M5 接续币面板)" % [level_key, side])
	open_continue.emit(false, side)

func _play_music(path: String) -> void:
	if bgm_player == null or path.is_empty():
		return
	if not ResourceLoader.exists(path):
		push_warning("[%s] music missing: %s" % [level_key, path])
		return
	bgm_player.stream = load(path)
	bgm_player.play()
