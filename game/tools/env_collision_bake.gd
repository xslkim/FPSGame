extends SceneTree

# 环境碰撞补全烘焙:Unity 侧城镇 prefab 均带 MeshCollider,而 SceneExporter 烘焙的 env tscn
# 只给地形加了碰撞,建筑/廊桥全缺 → 怪物穿廊落地。本工具给每个缺碰撞的 MeshInstance3D
# 生成 StaticBody3D(trimesh,layer1/mask0 环境件约定),回写场景文件。
# 用法: $G --headless --path . -s tools/env_collision_bake.gd -- <env_tscn路径>
func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var path := "res://scenes/levels/env_level2.tscn"
	if args.size() > 0:
		path = args[0]
	print("bake collision: ", path)
	var env = load(path).instantiate()
	var added := 0
	var skipped_lod := 0
	var skipped_has := 0
	for mi in env.find_children("*", "MeshInstance3D", true, false):
		if not (mi.mesh is ArrayMesh):
			continue
		var nm := String(mi.name)
		# LOD 低模跳过(高模/唯一模型才挂);远景巨物跳过(节省 trimesh,怪走不到)
		if nm.contains("_LOD1") or nm.contains("_LOD2") or nm.contains("_LOD3"):
			skipped_lod += 1
			continue
		if nm.contains("cliff") or nm.contains("Terrain"):
			continue
		var has := false
		for c in mi.get_children():
			if c is StaticBody3D:
				has = true
				break
		if has:
			skipped_has += 1
			continue
		var shape = mi.mesh.create_trimesh_shape()
		if shape == null:
			continue
		var body := StaticBody3D.new()
		body.name = "Collision"
		body.collision_layer = 1
		body.collision_mask = 0
		var cs := CollisionShape3D.new()
		cs.shape = shape
		body.add_child(cs)
		mi.add_child(body)
		added += 1
	print("added=", added, " skip_lod=", skipped_lod, " skip_has=", skipped_has)
	if added > 0:
		var packed := PackedScene.new()
		var err := packed.pack(env)
		if err != OK:
			print("pack failed: ", err)
			quit(1)
		err = ResourceSaver.save(packed, path)
		if err != OK:
			print("save failed: ", err)
			quit(1)
		print("saved: ", path)
	quit()
