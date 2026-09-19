extends SceneTree
## M7 工具:dump 各怪 FBX 导入场景的结构/动画/AABB,供合并工具与 tscn 改造参考。
## 用法: godot --headless --path game -s tools/inspect_monsters.gd

const MODELS := {
	"toon": "res://assets/models/monsters/toon/ToonSoldiers_Militias.FBX",
	"toon_alien_suit": "res://assets/models/monsters/toon_alien/stardudes_suit_mk1.fbx",
	"toon_alien_helmet": "res://assets/models/monsters/toon_alien/helmet_m_alpha.fbx",
	"toon_alien_rifle": "res://assets/models/monsters/toon_alien/rifle_battlerifle.fbx",
	"wolf": "res://assets/models/monsters/wolf/MonsterWerewolf.FBX",
	"wolf_demon": "res://assets/models/monsters/wolf_demon/Demon Wolf.FBX",
	"fat_zombie": "res://assets/models/monsters/fat_zombie/MonsterFatZombie.FBX",
	"dragon": "res://assets/models/monsters/dragon/Fantasy Dragon.FBX",
	"magma_demon": "res://assets/models/monsters/magma_demon/Magma Demon.FBX",
	"rock_warrior": "res://assets/models/monsters/rock_warrior/RockWarrior.FBX",
	"level2_boss": "res://assets/models/monsters/level2_boss/Modle.FBX",
	"level2_boss_weapon": "res://assets/models/monsters/level2_boss/Weapon.FBX",
}

const ANIM_SAMPLES := [
	"res://assets/models/monsters/wolf/MonsterW@Attack01.FBX",
	"res://assets/models/monsters/wolf_demon/Demon Wolf@Idle.FBX",
	"res://assets/models/monsters/fat_zombie/MonsterFZ@Run.FBX",
	"res://assets/models/monsters/dragon/Fantasy Dragon@FireBreathOnce.FBX",
	"res://assets/models/monsters/magma_demon/Magma Demon@attack01.FBX",
	"res://assets/models/monsters/rock_warrior/RockWarrior@atk01.FBX",
	"res://assets/models/monsters/toon/infantry_combat_reload.FBX",
	"res://assets/models/monsters/toon_alien/StarDudes@Rifle_Idle_0.fbx",
]

func _init() -> void:
	for key in MODELS:
		_dump(key, MODELS[key])
	print("==== ANIM SAMPLES ====")
	for p in ANIM_SAMPLES:
		_dump_anims(p)
	quit()

func _dump(key: String, path: String) -> void:
	print("==== %s : %s ====" % [key, path])
	var ps: PackedScene = load(path)
	if ps == null:
		print("  !! load failed")
		return
	var root := ps.instantiate()
	get_root().add_child(root)
	_dump_tree(root, 1)
	# 合并 AABB(所有 MeshInstance3D)
	var aabb := AABB()
	var first := true
	for mi in _find_all(root, "MeshInstance3D"):
		var m: AABB = mi.get_aabb()
		var gt: Transform3D = mi.global_transform
		var corners := AABB(gt * m.position, Vector3.ZERO)
		for i in 8:
			corners = corners.expand(gt * (m.position + Vector3(
				m.size.x if i & 1 else 0.0,
				m.size.y if i & 2 else 0.0,
				m.size.z if i & 4 else 0.0)))
		if first:
			aabb = corners
			first = false
		else:
			aabb = aabb.merge(corners)
	print("  AABB pos=%s size=%s" % [aabb.position, aabb.size])
	root.free()

func _dump_tree(n: Node, depth: int) -> void:
	var extra := ""
	if n is AnimationPlayer:
		var names := []
		for a in n.get_animation_list():
			var anim: Animation = n.get_animation(a)
			names.append("%s(%.2fs)" % [a, anim.length])
		extra = " anims=" + str(names) + " root_node=" + str(n.root_node)
	elif n is Skeleton3D:
		extra = " bones=%d" % n.get_bone_count()
	elif n is MeshInstance3D:
		extra = " surfaces=%d" % n.mesh.get_surface_count() if n.mesh else " nomesh"
	print("  ".repeat(depth) + "%s [%s]%s" % [n.name, n.get_class(), extra])
	if depth < 4:
		for c in n.get_children():
			_dump_tree(c, depth + 1)

func _dump_anims(path: String) -> void:
	var ps: PackedScene = load(path)
	if ps == null:
		print("!! load failed: ", path)
		return
	var root := ps.instantiate()
	print("-- %s (root=%s)" % [path.get_file(), root.name])
	for ap in _find_all(root, "AnimationPlayer"):
		for a in ap.get_animation_list():
			var anim: Animation = ap.get_animation(a)
			var track0 := ""
			if anim.get_track_count() > 0:
				track0 = str(anim.track_get_path(0))
			print("   anim=%s len=%.2f tracks=%d t0=%s root_node=%s" % [
				a, anim.length, anim.get_track_count(), track0, ap.root_node])
	# 骨架节点路径
	for sk in _find_all(root, "Skeleton3D"):
		print("   skeleton at: %s bones=%d" % [root.get_path_to(sk), sk.get_bone_count()])
	root.free()

func _find_all(n: Node, cls: String) -> Array:
	var out := []
	if n.is_class(cls):
		out.append(n)
	for c in n.get_children():
		out.append_array(_find_all(c, cls))
	return out
