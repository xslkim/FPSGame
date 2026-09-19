extends SceneTree
## 打印导入枪模的层级与 AABB,确定缩放/朝向/枪口位置(一次性工具)

func _init() -> void:
	for path in [
		"res://assets/models/guns/ak47/ak47.fbx",
		"res://assets/models/guns/m4/m4.fbx",
		"res://assets/models/guns/handgun/handgun.fbx",
	]:
		print("==== ", path)
		var ps: PackedScene = load(path)
		if ps == null:
			print("LOAD FAILED")
			continue
		var root := ps.instantiate()
		_dump(root, 0, Transform3D.IDENTITY)
		root.free()
	quit()

func _dump(n: Node, depth: int, xf: Transform3D) -> void:
	var local := Transform3D.IDENTITY
	if n is Node3D:
		local = n.transform
	var world := xf * local
	var pad := "  ".repeat(depth)
	if n is MeshInstance3D and n.mesh != null:
		var aabb: AABB = n.mesh.get_aabb()
		var gs: Vector3 = world.basis.get_scale()
		print("%s%s [Mesh] aabb=%s size=%s world_scale=%s" % [pad, n.name, aabb, aabb.size, gs])
	else:
		print("%s%s (%s) origin=%s scale=%s" % [pad, n.name, n.get_class(),
			(world.origin if n is Node3D else "-"),
			(world.basis.get_scale() if n is Node3D else "-")])
	for c in n.get_children():
		_dump(c, depth + 1, world)
