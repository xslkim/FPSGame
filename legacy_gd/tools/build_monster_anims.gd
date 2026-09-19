extends SceneTree
## M7 工具:把各怪 @动画 FBX 的 clip 抽取、轨道重映射到模型实例(Model/ 前缀)、
## 改名对齐 meta、补 Call Method Track(攻击 clip 60% 处),保存为 AnimationLibrary .tres。
## 用法: godot --headless --path game -s tools/build_monster_anims.gd

const D := "res://assets/models/monsters"

# 每项: model(模型 FBX,用于校验轨道路径), out(输出库), anims:[{file,clip,loop,event,slice}]
var CONFIGS := {
	"wolf": {
		"model": D + "/wolf/MonsterWerewolf.FBX",
		"out": D + "/wolf/wolf_anims.tres",
		"anims": [
			{"file": "MonsterW@Attack01.FBX", "clip": "Attack1", "event": &"event_attack"},
			{"file": "MonsterW@Attack02.FBX", "clip": "Attack2", "event": &"event_attack"},
			{"file": "MonsterW@Damage02.FBX", "clip": "Damage2"},
			{"file": "MonsterW@Damage04.FBX", "clip": "Damage4"},
			{"file": "MonsterW@Idle01.FBX", "clip": "Idle01", "loop": true},
			{"file": "MonsterW@Idle02.FBX", "clip": "Idle02", "loop": true},
			{"file": "MonsterW@Run.FBX", "clip": "locomotion", "loop": true},
			{"file": "MonsterW@Walk.FBX", "clip": "Walk", "loop": true},
			{"file": "MonsterW@Howl.FBX", "clip": "Howl"},
		],
	},
	"wolf_demon": {
		"model": D + "/wolf_demon/Demon Wolf.FBX",
		"out": D + "/wolf_demon/wolf_demon_anims.tres",
		"anims": [
			{"file": "Demon Wolf@BiteAttack.FBX", "clip": "BiteAttack", "event": &"event_attack"},
			{"file": "Demon Wolf@ClawAttack.FBX", "clip": "ClawAttack", "event": &"event_attack"},
			{"file": "Demon Wolf@TakeDamage.FBX", "clip": "TakeDamage"},
			{"file": "Demon Wolf@Die.FBX", "clip": "Die"},
			{"file": "Demon Wolf@Idle.FBX", "clip": "Idle02", "loop": true},
			{"file": "Demon Wolf@Run.FBX", "clip": "locomotion", "loop": true},
		],
	},
	"fat_zombie": {
		"model": D + "/fat_zombie/MonsterFatZombie.FBX",
		"out": D + "/fat_zombie/fat_zombie_anims.tres",
		"anims": [
			{"file": "MonsterFZ@Attack01.FBX", "clip": "Attack1", "event": &"event_attack"},
			{"file": "MonsterFZ@Attack02.FBX", "clip": "Attack2", "event": &"event_attack"},
			{"file": "MonsterFZ@Damage02.FBX", "clip": "Damage2"},
			{"file": "MonsterFZ@Damage04.FBX", "clip": "Damage4"},
			{"file": "MonsterFZ@Idle02.FBX", "clip": "Idle02", "loop": true},
			{"file": "MonsterFZ@Run.FBX", "clip": "locomotion", "loop": true},
			{"file": "MonsterFZ@Walk.FBX", "clip": "Walk", "loop": true},
		],
	},
	"dragon": {
		"model": D + "/dragon/Fantasy Dragon.FBX",
		"out": D + "/dragon/dragon_anims.tres",
		"anims": [
			{"file": "Fantasy Dragon@FireBreathOnce.FBX", "clip": "FireBreathOnce", "event": &"event_attack"},
			{"file": "Fantasy Dragon@TakeDamage.FBX", "clip": "TakeDamage"},
			{"file": "Fantasy Dragon@Die.FBX", "clip": "Die"},
			{"file": "Fantasy Dragon@Idle.FBX", "clip": "Idle02", "loop": true},
			{"file": "Fantasy Dragon@FlyForward.FBX", "clip": "locomotion", "loop": true},
			{"file": "Fantasy Dragon@BiteAttack.FBX", "clip": "BiteAttack"},
		],
	},
	"magma_demon": {
		"model": D + "/magma_demon/Magma Demon.FBX",
		"out": D + "/magma_demon/magma_demon_anims.tres",
		"anims": [
			{"file": "Magma Demon@attack01.FBX", "clip": "attack01", "event": &"event_attack"},
			{"file": "Magma Demon@attack02.FBX", "clip": "attack02", "event": &"event_attack"},
			{"file": "Magma Demon@takedamage.FBX", "clip": "takedamage"},
			{"file": "Magma Demon@die.FBX", "clip": "die"},
			{"file": "Magma Demon@idle.FBX", "clip": "idle", "loop": true},
			{"file": "Magma Demon@move.FBX", "clip": "locomotion", "loop": true},
		],
		"aliases": {"Idle02": "idle"},
	},
	"rock_warrior": {
		"model": D + "/rock_warrior/RockWarrior.FBX",
		"out": D + "/rock_warrior/rock_warrior_anims.tres",
		"anims": [
			{"file": "RockWarrior@atk01.FBX", "clip": "atk01", "event": &"rock_attack"},
			{"file": "RockWarrior@hit.FBX", "clip": "hit"},
			{"file": "RockWarrior@die.FBX", "clip": "die"},
			{"file": "RockWarrior@idle.FBX", "clip": "Idle02", "loop": true},
			{"file": "RockWarrior@run.FBX", "clip": "locomotion", "loop": true},
		],
	},
	"toon": {
		"model": D + "/toon/ToonSoldiers_Militias.FBX",
		"out": D + "/toon/toon_anims.tres",
		"anims": [
			{"file": "infantry_combat_reload.FBX", "clip": "reload", "event": &"toon_shoot"},
			{"file": "infantry_take_damage.FBX", "clip": "infantry_take_damage"},
			{"file": "infantry_death_A.FBX", "clip": "infantry_death_A"},
			{"file": "infantry_combat_idle.FBX", "clip": "Idle02", "loop": true},
			{"file": "infantry_combat_walk.FBX", "clip": "locomotion", "loop": true},
			{"file": "infantry_combat_run.FBX", "clip": "Run", "loop": true},
		],
	},
	"toon_alien": {
		"model": D + "/toon_alien/stardudes_suit_mk1.fbx",
		"out": D + "/toon_alien/toon_alien_anims.tres",
		"prefix": "Model/Suit",
		"anims": [
			{"file": "StarDudes@Rifle_SingleShot.fbx", "clip": "reload", "event": &"toon_shoot"},
			{"file": "StarDudes@Collapse_GutShot.fbx", "clip": "infantry_death_A"},
			{"file": "StarDudes@Collapse_GutShot.fbx", "clip": "infantry_take_damage", "slice": Vector2(0.0, 0.6)},
			{"file": "StarDudes@Rifle_Idle_0.fbx", "clip": "Idle02", "loop": true},
			{"file": "StarDudes@Rifle_Walk.fbx", "clip": "locomotion", "loop": true},
		],
	},
}

var _fail := false

func _init() -> void:
	for key in CONFIGS:
		_build(key, CONFIGS[key])
	_build_boss()
	print("[BUILD] done, failed=%s" % _fail)
	quit(1 if _fail else 0)

func _build(key: String, cfg: Dictionary) -> void:
	var model_ps: PackedScene = load(cfg["model"])
	if model_ps == null:
		push_error("[BUILD] %s: model load failed" % key)
		_fail = true
		return
	var model := model_ps.instantiate()
	var prefix: String = cfg.get("prefix", "Model")
	var lib := AnimationLibrary.new()
	var dir: String = cfg["model"].get_base_dir()
	for a in cfg["anims"]:
		var path: String = dir + "/" + a["file"]
		var anim := _extract(path, model, key, prefix)
		if anim == null:
			_fail = true
			continue
		if a.has("slice"):
			anim = _slice(anim, a["slice"].x, a["slice"].y)
		if a.get("loop", false):
			anim.loop_mode = Animation.LOOP_LINEAR
		if a.has("event"):
			_add_method_track(anim, a["event"], anim.length * 0.6)
		lib.add_animation(StringName(a["clip"]), anim)
	for alias in cfg.get("aliases", {}):
		var src := StringName(cfg["aliases"][alias])
		if lib.has_animation(src):
			lib.add_animation(StringName(alias), lib.get_animation(src))
	var err := ResourceSaver.save(lib, cfg["out"])
	print("[BUILD] %s -> %s (%d clips) err=%d" % [key, cfg["out"], lib.get_animation_list().size(), err])
	if err != OK:
		_fail = true
	model.free()

## 从动画 FBX 场景抽出第一个非 RESET 动画,轨道重映射到 Model/ 前缀
func _extract(path: String, model: Node, key: String, prefix: String) -> Animation:
	var ps: PackedScene = load(path)
	if ps == null:
		push_error("[BUILD] %s: anim load failed %s" % [key, path])
		return null
	var root := ps.instantiate()
	var ap: AnimationPlayer = null
	for n in _find(root, "AnimationPlayer"):
		ap = n
		break
	if ap == null:
		push_error("[BUILD] %s: no AnimationPlayer in %s" % [key, path])
		root.free()
		return null
	var src: Animation = null
	for name in ap.get_animation_list():
		if name != &"RESET":
			src = ap.get_animation(name)
			break
	if src == null:
		root.free()
		return null
	var anim := src.duplicate(true)
	# 轨道重映射:动画 FBX 与模型 FBX 内部结构一致,优先同路径校验,失败按节点名搜索
	var ap_root: Node = ap.get_node(ap.root_node)
	var dropped := []
	for i in anim.get_track_count():
		var p: NodePath = anim.track_get_path(i)
		var pstr := String(p)
		var colon := pstr.find(":")
		var node_part := pstr if colon < 0 else pstr.substr(0, colon)
		var sub := "" if colon < 0 else pstr.substr(colon)
		var new_path := ""
		if node_part == "." or node_part == "..":
			new_path = prefix
		elif model.has_node(node_part):
			new_path = prefix + "/" + node_part
		else:
			var node_name := node_part.get_file() if node_part.contains("/") else node_part
			# get_file 对 NodePath 段不适用,改手取最后一段
			node_name = node_part.split("/")[-1]
			var found := _find_by_name(model, node_name)
			if found != null:
				new_path = prefix + "/" + str(model.get_path_to(found))
		if new_path == "":
			dropped.append(node_part)
			continue
		anim.track_set_path(i, NodePath(new_path + sub))
	if not dropped.is_empty():
		print("[BUILD] %s %s: %d 轨道无对应节点(丢弃): %s" % [key, path.get_file(), dropped.size(), str(dropped.slice(0, 5))])
	root.free()
	return anim

func _add_method_track(anim: Animation, method: StringName, time: float) -> void:
	var mt := anim.add_track(Animation.TYPE_METHOD)
	anim.track_set_path(mt, NodePath("."))
	anim.track_insert_key(mt, time, {"method": method, "args": []})

## 截取动画 [t0,t1] 并平移到 0 起点(用于 alien 受击=倒地前段)
func _slice(src: Animation, t0: float, t1: float) -> Animation:
	var out := Animation.new()
	out.length = t1 - t0
	for ti in src.get_track_count():
		var type := src.track_get_type(ti)
		var nt := out.add_track(type)
		out.track_set_path(nt, src.track_get_path(ti))
		for k in src.track_get_key_count(ti):
			var t := src.track_get_key_time(ti, k)
			if t >= t0 - 0.0001 and t <= t1 + 0.0001:
				out.track_insert_key(nt, t - t0, src.track_get_key_value(ti, k),
					src.track_get_key_transition(ti, k))
	return out

# ---- level2_boss:Unity humanoid .anim 无法转换,程序化骨架 clip ----
func _build_boss() -> void:
	var model_ps: PackedScene = load(D + "/level2_boss/Modle.FBX")
	var model := model_ps.instantiate()
	var sk: Skeleton3D = null
	for n in _find(model, "Skeleton3D"):
		sk = n
		break
	if sk == null:
		push_error("[BUILD] boss: no skeleton")
		_fail = true
		return
	var lib := AnimationLibrary.new()
	var p: String = "Model/" + str(model.get_path_to(sk))
	# 打印 rest 供调参
	for b in ["Bip001", "Bip001 Spine", "Bip001 R UpperArm", "Bip001 L UpperArm", "Bip001 Head"]:
		var idx := sk.find_bone(b)
		if idx >= 0:
			var rest := sk.get_bone_rest(idx)
			print("[BOSS-REST] %s pos=%s euler=%s" % [b, rest.origin, rest.basis.get_euler()])
	var mk := func(bone: String) -> String: return "%s:%s" % [p, bone]
	# Idle 2s loop:脊柱微摆
	var idle := Animation.new()
	idle.length = 2.0
	idle.loop_mode = Animation.LOOP_LINEAR
	_rot_keys(idle, sk, mk.call("Bip001 Spine"), [0.0, 1.0, 2.0],
		[Vector3(0, 0, 0.04), Vector3(0, 0, -0.04), Vector3(0, 0, 0.04)])
	_rot_keys(idle, sk, mk.call("Bip001 Head"), [0.0, 1.0, 2.0],
		[Vector3(0.05, 0, 0), Vector3(-0.03, 0, 0), Vector3(0.05, 0, 0)])
	lib.add_animation(&"Idle", idle)
	lib.add_animation(&"Idle02", idle)
	# Skill1 1.2s:右臂高举劈下(60% 处事件)
	var s1 := Animation.new()
	s1.length = 1.2
	_rot_keys(s1, sk, mk.call("Bip001 R UpperArm"), [0.0, 0.4, 0.75, 1.2],
		[Vector3.ZERO, Vector3(0, 0, -2.4), Vector3(0, 0, 0.6), Vector3.ZERO])
	_rot_keys(s1, sk, mk.call("Bip001 Spine"), [0.0, 0.4, 0.75, 1.2],
		[Vector3.ZERO, Vector3(0, -0.25, 0), Vector3(0, 0.18, 0), Vector3.ZERO])
	_add_method_track(s1, &"hert_player_skill1", 1.2 * 0.6)
	lib.add_animation(&"Skill1", s1)
	# Skill2 1.4s:双臂前刺 + 脊柱前倾(60% 处事件)
	var s2 := Animation.new()
	s2.length = 1.4
	_rot_keys(s2, sk, mk.call("Bip001 R UpperArm"), [0.0, 0.5, 0.9, 1.4],
		[Vector3.ZERO, Vector3(-1.5, 0, 0), Vector3(-1.5, 0, 0), Vector3.ZERO])
	_rot_keys(s2, sk, mk.call("Bip001 L UpperArm"), [0.0, 0.5, 0.9, 1.4],
		[Vector3.ZERO, Vector3(-1.5, 0, 0), Vector3(-1.5, 0, 0), Vector3.ZERO])
	_rot_keys(s2, sk, mk.call("Bip001 Spine"), [0.0, 0.5, 0.9, 1.4],
		[Vector3.ZERO, Vector3(0.3, 0, 0), Vector3(0.3, 0, 0), Vector3.ZERO])
	_add_method_track(s2, &"hert_player_skill2", 1.4 * 0.6)
	lib.add_animation(&"Skill2", s2)
	# Damage02 0.5s:后仰顿挫
	var dmg := Animation.new()
	dmg.length = 0.5
	_rot_keys(dmg, sk, mk.call("Bip001 Spine"), [0.0, 0.15, 0.5],
		[Vector3.ZERO, Vector3(-0.3, 0, 0), Vector3.ZERO])
	_rot_keys(dmg, sk, mk.call("Bip001 Head"), [0.0, 0.15, 0.5],
		[Vector3.ZERO, Vector3(-0.4, 0, 0), Vector3.ZERO])
	lib.add_animation(&"Damage02", dmg)
	# Dead 2.2s:整体后倒(根骨旋转 + 下沉)
	var dead := Animation.new()
	dead.length = 2.2
	_rot_keys(dead, sk, mk.call("Bip001"), [0.0, 0.6, 1.2, 2.2],
		[Vector3.ZERO, Vector3(-0.5, 0, 0), Vector3(-1.5, 0, 0), Vector3(-1.55, 0, 0)])
	_pos_keys(dead, sk, mk.call("Bip001"), [0.0, 1.2, 2.2],
		[Vector3.ZERO, Vector3(0, -0.2, 0.3), Vector3(0, -0.25, 0.35)])
	_rot_keys(dead, sk, mk.call("Bip001 R UpperArm"), [0.0, 1.0],
		[Vector3.ZERO, Vector3(0, 0, -1.2)])
	_rot_keys(dead, sk, mk.call("Bip001 L UpperArm"), [0.0, 1.0],
		[Vector3.ZERO, Vector3(0, 0, 1.2)])
	lib.add_animation(&"Dead", dead)
	var err := ResourceSaver.save(lib, D + "/level2_boss/level2_boss_anims.tres")
	print("[BUILD] level2_boss -> %d clips err=%d" % [lib.get_animation_list().size(), err])
	if err != OK:
		_fail = true
	model.free()

## 在 rest 姿态基础上叠加欧拉偏移,生成旋转轨道
func _rot_keys(anim: Animation, sk: Skeleton3D, path: String, times: Array, deltas: Array) -> void:
	var bone := String(path).split(":")[-1]
	var idx := sk.find_bone(bone)
	if idx < 0:
		push_error("[BUILD] bone missing: " + bone)
		_fail = true
		return
	var rest_q := sk.get_bone_rest(idx).basis.get_rotation_quaternion()
	var t := anim.add_track(Animation.TYPE_ROTATION_3D)
	anim.track_set_path(t, NodePath(path))
	for i in times.size():
		var e: Vector3 = deltas[i]
		var q := rest_q * Quaternion.from_euler(e)
		anim.track_insert_key(t, times[i], q)

func _pos_keys(anim: Animation, sk: Skeleton3D, path: String, times: Array, offsets: Array) -> void:
	var bone := String(path).split(":")[-1]
	var idx := sk.find_bone(bone)
	if idx < 0:
		return
	var rest_p: Vector3 = sk.get_bone_rest(idx).origin
	var t := anim.add_track(Animation.TYPE_POSITION_3D)
	anim.track_set_path(t, NodePath(path))
	for i in times.size():
		anim.track_insert_key(t, times[i], rest_p + offsets[i])

func _find(n: Node, cls: String) -> Array:
	var out := []
	if n.is_class(cls):
		out.append(n)
	for c in n.get_children():
		out.append_array(_find(c, cls))
	return out

func _find_by_name(n: Node, node_name: String) -> Node:
	if n.name == node_name:
		return n
	for c in n.get_children():
		var r := _find_by_name(c, node_name)
		if r != null:
			return r
	return null
