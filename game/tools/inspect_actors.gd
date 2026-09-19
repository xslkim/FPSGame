extends SceneTree
## M7a 一次性检查:打印演员 FBX 的骨架骨骼名、网格 AABB、材质名。
## 用法: godot --headless --path game -s tools/inspect_actors.gd

const ACTORS := {
	"f05": "res://assets/models/actors/f05_schoolwear/f05_schoolwear_200_m.fbx",
	"casual": "res://assets/models/actors/casual_dressed_girl/casual_dressed_girl.FBX",
	"blade": "res://assets/models/actors/blade_girl/blade_girl.FBX",
}

func _init() -> void:
	for key in ACTORS:
		var ps: PackedScene = load(ACTORS[key])
		if ps == null:
			print("[INSPECT] %s: LOAD FAILED" % key)
			continue
		var root := ps.instantiate()
		print("[INSPECT] === %s root=%s ===" % [key, root.name])
		var sk: Skeleton3D = null
		for n in _find(root, "Skeleton3D"):
			sk = n
			break
		if sk == null:
			print("[INSPECT] %s: NO Skeleton3D" % key)
		else:
			var names := []
			for i in sk.get_bone_count():
				names.append(sk.get_bone_name(i))
			print("[INSPECT] %s bones(%d): %s" % [key, sk.get_bone_count(), str(names)])
		for mi in _find(root, "MeshInstance3D"):
			var aabb: AABB = mi.get_aabb()
			print("[INSPECT] %s mesh=%s aabb=%s mats=%s" % [key, mi.name, aabb,
				str(mi.mesh.get("_materials") if false else _mat_names(mi))])
		for ap in _find(root, "AnimationPlayer"):
			print("[INSPECT] %s anims=%s" % [key, str(ap.get_animation_list())])
		root.free()
	quit()

func _mat_names(mi: MeshInstance3D) -> Array:
	var out := []
	for i in mi.get_surface_override_material_count() if false else range(mi.mesh.get_surface_count()):
		var m := mi.mesh.surface_get_material(i)
		out.append(m.resource_name if m != null else "null")
	return out

func _find(n: Node, cls: String) -> Array:
	var out := []
	if n.is_class(cls):
		out.append(n)
	for c in n.get_children():
		out.append_array(_find(c, cls))
	return out
