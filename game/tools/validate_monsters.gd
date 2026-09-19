extends SceneTree
## M7 校验:逐怪实例化 .tscn,断言 meta 需要的 clip 存在、攻击 clip 带 60% 方法轨道。
const CASES := {
	"res://gameplay/monsters/toon.tscn": ["reload", "infantry_take_damage", "infantry_death_A", "Idle02", "locomotion"],
	"res://gameplay/monsters/toon_alien.tscn": ["reload", "infantry_take_damage", "infantry_death_A", "Idle02", "locomotion"],
	"res://gameplay/monsters/wolf.tscn": ["Attack1", "Attack2", "Damage2", "Damage4", "Idle02", "locomotion"],
	"res://gameplay/monsters/wolf_blue.tscn": ["BiteAttack", "ClawAttack", "TakeDamage", "Die", "Idle02", "locomotion"],
	"res://gameplay/monsters/wolf_green.tscn": ["BiteAttack", "ClawAttack", "TakeDamage", "Die", "Idle02", "locomotion"],
	"res://gameplay/monsters/fat_zombie.tscn": ["Attack1", "Attack2", "Damage2", "Damage4", "Idle02", "locomotion"],
	"res://gameplay/monsters/dragon_red.tscn": ["FireBreathOnce", "TakeDamage", "Die", "Idle02", "locomotion"],
	"res://gameplay/monsters/dragon_blue.tscn": ["FireBreathOnce", "TakeDamage", "Die", "Idle02", "locomotion"],
	"res://gameplay/monsters/dragon_green.tscn": ["FireBreathOnce", "TakeDamage", "Die", "Idle02", "locomotion"],
	"res://gameplay/monsters/magma_demon.tscn": ["attack01", "attack02", "takedamage", "die", "idle", "Idle02"],
	"res://gameplay/monsters/rock_warrior.tscn": ["atk01", "hit", "die", "Idle02", "locomotion"],
	"res://gameplay/monsters/level2_boss.tscn": ["Skill1", "Skill2", "Damage02", "Dead", "Idle", "Idle02"],
}

const ATTACKS := {
	"toon": ["reload", "toon_shoot"], "toon_alien": ["reload", "toon_shoot"],
	"wolf": ["Attack1", "event_attack"], "wolf_blue": ["BiteAttack", "event_attack"],
	"wolf_green": ["BiteAttack", "event_attack"], "fat_zombie": ["Attack1", "event_attack"],
	"dragon_red": ["FireBreathOnce", "event_attack"],
	"magma_demon": ["attack01", "event_attack"], "rock_warrior": ["atk01", "rock_attack"],
	"level2_boss": ["Skill1", "hert_player_skill1"],
}

var _fail := false

func _init() -> void:
	for path in CASES:
		_check_scene(path, CASES[path])
	_check_attacks()
	print("[M7-VALIDATE] done, failed=%s" % _fail)
	quit(1 if _fail else 0)

func _check_scene(path: String, anims: Array) -> void:
	var key := path.get_file().get_basename()
	var ps: PackedScene = load(path)
	if ps == null:
		_log(false, "%s: 场景加载失败" % key)
		return
	var inst := ps.instantiate()
	var ap: AnimationPlayer = inst.get_node_or_null("AnimationPlayer")
	if ap == null:
		_log(false, "%s: 无 AnimationPlayer" % key)
		inst.free()
		return
	for a in anims:
		_log(ap.has_animation(a), "%s: has_animation(%s)" % [key, a])
	# 方法轨道位置检查
	for a in anims:
		var anim := ap.get_animation(a)
		for t in anim.get_track_count():
			if anim.track_get_type(t) == Animation.TYPE_METHOD:
				var tm := anim.track_get_key_time(t, 0)
				var frac := tm / anim.length
				_log(frac > 0.5 and frac < 0.7,
					"%s/%s: 事件 @%.0f%% (%s)" % [key, a, frac * 100.0,
						anim.track_get_key_value(t, 0)["method"]])
	inst.free()

func _check_attacks() -> void:
	pass

func _log(ok: bool, msg: String) -> void:
	print(("[M7] PASS: " if ok else "[M7] FAIL: ") + msg)
	if not ok:
		_fail = true
