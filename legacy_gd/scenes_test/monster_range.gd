extends Node3D
## M3 怪物试验场:模拟一波刷怪(10 bull / 间 3s / 活 2)+ 补给箱概率 + 波次推进打印。
## 启动参数:
##   (无)                模拟一波 bull,键盘射击
##   --m3-baotou         单独刷 baotou Boss 测试(瞬移/火球)
##   --m3-selftest       headless 断言自检

const MONSTER_SCENES := {
	"bull": preload("res://gameplay/monsters/bull.tscn"),
	"axe_zombie": preload("res://gameplay/monsters/axe_zombie.tscn"),
	"fly_axe_zombie": preload("res://gameplay/monsters/fly_axe_zombie.tscn"),
	"skeleton": preload("res://gameplay/monsters/skeleton.tscn"),
	"baotou": preload("res://gameplay/monsters/baotou.tscn"),
	"box": preload("res://gameplay/monsters/box_monster.tscn"),
}
const POOL_SIZES := {"bull": 3, "axe_zombie": 3, "fly_axe_zombie": 2, "skeleton": 3, "baotou": 1, "box": 2}

# 波配置(模拟 L1 G1:10 只 / 间 3s / 活 2,7.1 节)
var wave_types: Array = []
var wave_pending := 0
var spawn_interval := 3.0
var alive_max := 2
var bullet_box_rate := 0.05  # 子弹箱概率(按波 0.05/0.08)
var gun_box_rate := 0.02     # L1 GunRate(其余关 0.05)

var _spawn_timer := 0.0
var _wave_active := false
var _wave_index := 0

@onready var _pool: MonsterPool = $MonsterPool
@onready var _fire_system: FireSystem = $Camera3D/FireSystem

var _test_failed := false

func _ready() -> void:
	GlobalObject.scene_state = GlobalObject.GameState.Battle
	InputManager.set_input_mode(InputManager.InputMode.RightAndLeft)
	_fire_system.bind_players()
	for key in MONSTER_SCENES:
		_pool.register_type(key, MONSTER_SCENES[key], POOL_SIZES[key])
	var args := OS.get_cmdline_user_args()
	if args.has("--m3-selftest"):
		_self_test()
	elif args.has("--m3-baotou"):
		_start_baotou_test()
	else:
		var types: Array = []
		for i in 10:
			types.append("bull")
		_start_wave(types)

func _start_wave(types: Array) -> void:
	wave_types = types
	wave_pending = types.size()
	_wave_index = 0
	_spawn_timer = spawn_interval  # 立即刷第一只
	_wave_active = true
	print("[M3] wave start: %d monsters, interval=%.1fs, alive_max=%d" % [
		wave_pending, spawn_interval, alive_max])

func _start_baotou_test() -> void:
	var bt := _pool.get_monster("baotou")
	var pos := bt.get_born_position(10.0, 8.0)
	bt.born(pos, 0, 0.0)
	print("[M3] baotou test: born at %s (瞬移/火球观察,键盘射击)" % str(pos))

func _process(delta: float) -> void:
	if not _wave_active:
		return
	_spawn_timer += delta
	if _spawn_timer >= spawn_interval:
		_spawn_timer = 0.0
		_spawn_tick()
	# 波次推进:待刷=0 且场上存活=0(25s 超时自毁也会推进,7.1 节)
	if wave_pending == 0 and PlayerSystem.cur_alive_monster == 0:
		_wave_active = false
		print("[M3] wave cleared (monsterLeft=0, cur_alive_monster=0)")

## 刷怪 tick:先判子弹箱,未中再以 GunRate 判枪箱(AK/M4 各半);箱子占本波配额(7.1 节)
func _spawn_tick() -> void:
	if wave_pending <= 0:
		return
	if PlayerSystem.cur_alive_monster >= alive_max:
		return
	var key := ""
	var box_kind := BoxMonster.BoxKind.Bullet
	var r := randf()
	if r < bullet_box_rate:
		key = "box"
		box_kind = BoxMonster.BoxKind.Bullet
	elif r < bullet_box_rate + gun_box_rate:
		key = "box"
		box_kind = BoxMonster.BoxKind.GunAK if randf() < 0.5 else BoxMonster.BoxKind.GunM4
	else:
		key = wave_types[_wave_index % wave_types.size()]
		_wave_index += 1
	var m := _pool.get_monster(key)
	if m == null:
		if key != "box":
			_wave_index -= 1  # 池满,下个 tick 重试同一只
		return
	if m is BoxMonster:
		m.box_kind = box_kind
	# 刷怪点:默认 ±33°(FOV>50 → ±40°)/ 8m;meta born_override 优先(7.1 节)
	var cam := get_viewport().get_camera_3d()
	var default_fov := 40.0 if (cam != null and cam.fov > 50.0) else 33.0
	var params := m.get_born_params(default_fov, 8.0)
	var pos := m.get_born_position(params.x, params.y)
	m.born(pos, 0, _difficulty_waitting_time())
	wave_pending -= 1
	print("[M3] spawn %s at %s (pending=%d, alive=%d)" % [
		key, str(pos), wave_pending, PlayerSystem.cur_alive_monster])

## 等待时长按难度(6.1.2):Easy rand(3,8) / Hard rand(0,2) / Hell 0
func _difficulty_waitting_time() -> float:
	match GlobalObject.difficulty:
		GlobalObject.Difficulty.Hard:
			return randf_range(0.0, 2.0)
		GlobalObject.Difficulty.Hell:
			return 0.0
		_:
			return randf_range(3.0, 8.0)

func _check(cond: bool, label: String) -> void:
	print(("[M3-TEST] PASS: " if cond else "[M3-TEST] FAIL: ") + label)
	if not cond:
		_test_failed = true

## headless 自检:godot --headless --path game scenes/test/monster_range.tscn -- --m3-selftest
func _self_test() -> void:
	await get_tree().process_frame
	await get_tree().physics_frame
	var rp := PlayerSystem.player_right
	var lp := PlayerSystem.player_left

	# born / hit / 死亡回收 / 计数
	var bull := _pool.get_monster("bull")
	var alive0 := PlayerSystem.cur_alive_monster
	bull.born(Vector3(0, 0, -4), 0, 99.0)
	_check(bull.hp == 30.0, "born: bull hp = meta 30")
	_check(PlayerSystem.cur_alive_monster == alive0 + 1, "born: cur_alive_monster +1")
	_check(bull.hit(10.0, bull.global_position, GlobalObject.HitType.Body,
		PlayerSystem.Side.Right) == "Concrete", "hit: returns impact_tag Concrete")
	_check(bull.hp == 20.0, "hit: hp 30 -> 20")
	bull.hit(25.0, bull.global_position, GlobalObject.HitType.Body, PlayerSystem.Side.Right)
	_check(bull.is_dead() and PlayerSystem.cur_alive_monster == alive0 + 1,
		"die: state DEAD, 回收前计数不变")
	await get_tree().create_timer(1.7).timeout
	_check(not bull.is_active() and PlayerSystem.cur_alive_monster == alive0,
		"recycle: 1.5s 后回池, 计数 -1")
	var bull2 := _pool.get_monster("bull")
	_check(bull2 != null and not bull2.is_active(), "pool: 回收后可取到未激活实例(随机起点扫描)")

	# 25s 超时自毁
	bull2.born(Vector3(2, 0, -4), 0, 99.0)
	bull2._life_time = 24.9
	await get_tree().create_timer(0.3).timeout
	_check(not bull2.is_active(), "timeout: 25s 超时自毁")

	# 刷怪点
	var bp := bull2.get_born_position(33.0, 8.0)
	var cam := get_viewport().get_camera_3d()
	_check(absf(bp.y) < 0.001 and cam != null
		and Vector2(bp.x - cam.global_position.x, bp.z - cam.global_position.z).length() <= 8.01,
		"get_born_position: 落点 y=0 且距离 ≤ 8m")

	# event_attack 半屏分派
	var axe := _pool.get_monster("axe_zombie")
	axe.born(Vector3(3, 0, -4), 0, 99.0)
	var hp_r := rp.hp
	var hp_l := lp.hp
	axe.event_attack()
	_check(rp.hp == hp_r - 15.0 and lp.hp == hp_l, "event_attack: 屏幕右半 → Right 玩家")
	axe.global_position = Vector3(-3, 0, -4)
	axe.event_attack()
	_check(lp.hp == hp_l - 15.0 and rp.hp == hp_r - 15.0, "event_attack: 屏幕左半 → Left 玩家")
	axe._recycle()

	# 攻击类型传递:Ice → Frozen / Poison → 二次扣血(用 dragon meta 驱动基类)
	var ice: MonsterBase = preload("res://gameplay/monsters/monster_base.tscn").instantiate()
	ice.meta_key = "dragon_blue"
	add_child(ice)
	ice.born(Vector3(3, 0, -4), 0, 99.0)
	ice.event_attack()
	_check(rp.hurt_state == PlayerSystem.Player.HurtState.Frozen, "attack_type Ice → Frozen")
	await get_tree().create_timer(2.2).timeout
	_check(rp.hurt_state == PlayerSystem.Player.HurtState.Normal, "Frozen 2s 后解除")
	ice._recycle()
	ice.queue_free()
	var psn: MonsterBase = preload("res://gameplay/monsters/monster_base.tscn").instantiate()
	psn.meta_key = "dragon_green"
	add_child(psn)
	psn.born(Vector3(3, 0, -4), 0, 99.0)
	var hp_r2 := rp.hp
	psn.event_attack()
	_check(rp.hp == hp_r2 - 15.0, "attack_type Poison → 立即 -15")
	await get_tree().create_timer(2.2).timeout
	_check(rp.hp == hp_r2 - 30.0, "attack_type Poison → 2s 后再 -15")
	psn._recycle()
	psn.queue_free()

	# 等级公式(skeleton attack_level_bonus=5)
	var sk := _pool.get_monster("skeleton")
	sk.born(Vector3(0, 0, -4), 2, 99.0)
	_check(sk.get_max_hp() == 40.0 and sk.hp == 40.0, "level: skeleton hp = 30 + 5*2")
	_check(sk.get_attack() == 25.0, "level: skeleton attack = 15 + 5*2 (bonus)")
	_check(is_equal_approx(sk.get_move_speed(), 1.6) and is_equal_approx(sk.get_attack_cd(), 4.0),
		"level: speed +0.3*2 / cd -0.5*2")
	sk._recycle()

	# 动画事件机制:占位 attack clip 的 Call Method Track → event_attack
	var bull3 := _pool.get_monster("bull")
	bull3.born(Vector3(0, 0, 4.5), 0, 0.0)  # 相机近旁,等待 0 → 立即追击进入攻击
	var hp_r3 := rp.hp
	var hp_l3 := lp.hp
	await get_tree().create_timer(1.2).timeout
	_check(rp.hp < hp_r3 or lp.hp < hp_l3, "anim method track: attack clip 0.3s → event_attack")
	bull3._recycle()

	# 补给箱掉落
	rp.guns = 1 << 2  # 只留手枪,验证解锁
	var box := _pool.get_monster("box") as BoxMonster
	box.box_kind = BoxMonster.BoxKind.GunAK
	box.born(Vector3(0, 0, -3), 0, 0.0)
	box.hit(1.0, box.global_position, GlobalObject.HitType.Body, PlayerSystem.Side.Right)
	_check((rp.guns & (1 << 0)) != 0, "box drop: GunAK → add_gun(0)")
	var bullet0 := rp.bullet
	var box2 := _pool.get_monster("box") as BoxMonster
	_check(box2 != null and box2 != box, "pool: 第二只箱(第一只死亡待回收)")
	box2.box_kind = BoxMonster.BoxKind.Bullet
	box2.born(Vector3(1, 0, -3), 0, 0.0)
	box2.hit(1.0, box2.global_position, GlobalObject.HitType.Body, PlayerSystem.Side.Right)
	_check(rp.bullet == bullet0 + 60, "box drop: Bullet → +60 (box_bullet)")
	# box 20s 自毁
	await get_tree().create_timer(1.7).timeout
	var box3 := _pool.get_monster("box") as BoxMonster
	box3.born(Vector3(0, 0, -3), 0, 0.0)
	box3._life_time = 19.9
	await get_tree().create_timer(0.3).timeout
	_check(not box3.is_active(), "box: 20s 超时自毁(life_active_time)")

	# baotou:瞬移 + 火球定时命中 Both
	var bt := _pool.get_monster("baotou") as BaotouMonster
	var bpos := Vector3(0, 0, -6)
	bt.born(bpos, 0, 0.0)
	_check(bt.hp == 100.0 and bt.is_boss, "baotou born: hp=100, is_boss")
	bt.hit(10.0, bt.global_position, GlobalObject.HitType.Body, PlayerSystem.Side.Right)
	_check(bt.hp == 90.0, "baotou hit: hp 100 -> 90")
	await get_tree().create_timer(0.7).timeout
	_check(bt.global_position.distance_to(bpos) > 0.3, "baotou: 被打 0.5s 后瞬移")
	var hp_r4 := rp.hp
	var hp_l4 := lp.hp
	bt.baotou_skill()
	await get_tree().create_timer(1.3).timeout
	_check(rp.hp < hp_r4 and lp.hp < hp_l4, "fireball: 发射 1s 后命中 Both")
	bt._recycle()

	print("[M3-TEST] done, failed=%s" % _test_failed)
	get_tree().quit(1 if _test_failed else 0)
