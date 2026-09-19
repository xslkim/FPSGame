extends Node3D
## M1 射击试验场:Battle 状态 + 双人(键盘控制右手)+ FireSystem 绑定。

@onready var _fire_system: FireSystem = $Camera3D/FireSystem

var _test_failed := false

func _ready() -> void:
	GlobalObject.scene_state = GlobalObject.GameState.Battle
	InputManager.set_input_mode(InputManager.InputMode.RightAndLeft)
	_fire_system.bind_players()
	_fire_system.open_continue.connect(_on_open_continue)
	if OS.get_cmdline_user_args().has("--m1-selftest"):
		_self_test()

## 弹尽 → 续币面板(M5 实现 UI,M1 仅打印)
func _on_open_continue(is_open: bool, side: int) -> void:
	print("[FireRange] open_continue: is_open=%s side=%d (续币面板 M5 实现)" % [is_open, side])

func _check(cond: bool, label: String) -> void:
	print(("[M1-TEST] PASS: " if cond else "[M1-TEST] FAIL: ") + label)
	if not cond:
		_test_failed = true

## 无头自检:godot --headless --path game -- --m1-selftest
func _self_test() -> void:
	await get_tree().process_frame
	await get_tree().physics_frame
	var rp := PlayerSystem.player_right
	var lp := PlayerSystem.player_left
	_check(rp.active and lp.active, "both players active in RightAndLeft")
	_check(rp.bullet == 120 and rp.hp == 100.0, "born: hp=100 bullet=MaxBullet")
	_check((rp.guns & 0b111) == 0b111 and rp.gun_type == 2, "born: 3 guns unlocked, start handgun")
	var gun: GunBase = _fire_system._current_gun[PlayerSystem.Side.Right]
	_check(gun != null and gun.gun_type == 2, "fire system bound handgun")
	var r1: Array = gun.fire()
	_check(r1[0] and rp.bullet == 119, "fire: success, bullet -1")
	var r2: Array = gun.fire()
	_check(not r2[0] and r2[1], "fire: blocked by CD (handgun 0.5s)")
	gun.last_fire_time -= 1.0
	var r3: Array = gun.fire()
	_check(r3[0] and rp.bullet == 118, "fire: success after CD")
	_check(rp.next_gun() and rp.gun_type == 0, "next_gun: 2 -> 0 (AK47)")
	_check(rp.next_gun() and rp.gun_type == 1, "next_gun: 0 -> 1 (M4)")
	_check(rp.next_gun() and rp.gun_type == 2, "next_gun: 1 -> 2 (wrap)")
	PlayerSystem.hit_player(10.0, GlobalObject.AttackType.Phy, PlayerSystem.Side.Right)
	_check(rp.hp == 90.0, "hit_player Phy: -10 hp")
	PlayerSystem.hit_player(10.0, GlobalObject.AttackType.Ice, PlayerSystem.Side.Right)
	_check(rp.hp == 80.0 and rp.hurt_state == PlayerSystem.Player.HurtState.Frozen,
		"hit_player Ice: -10 hp + Frozen")
	await get_tree().create_timer(2.2).timeout
	_check(rp.hurt_state == PlayerSystem.Player.HurtState.Normal, "Frozen ends after 2s")
	PlayerSystem.hit_player(10.0, GlobalObject.AttackType.Poison, PlayerSystem.Side.Right)
	_check(rp.hp == 70.0, "hit_player Poison: -10 hp now")
	await get_tree().create_timer(2.2).timeout
	_check(rp.hp == 60.0, "hit_player Poison: -10 hp again after 2s")
	_check($Target1.hit(10.0, Vector3.ZERO, GlobalObject.HitType.Body,
		PlayerSystem.Side.Right) == "Blood", "enemy hit() returns \"Blood\"")
	await get_tree().process_frame
	await get_tree().process_frame
	var flash: Sprite3D = _fire_system._flash[PlayerSystem.Side.Right]
	_check(flash.visible, "ray hits room, flash cursor visible")
	rp.relife()
	_check(rp.hp == 100.0 and rp.bullet == 238, "relife: hp=100 bullet+=120")
	print("[M1-TEST] done, failed=%s" % _test_failed)
	get_tree().quit(1 if _test_failed else 0)
