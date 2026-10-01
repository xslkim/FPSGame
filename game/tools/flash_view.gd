extends SceneTree

# 枪口火光检视 v2:近距正面看枪口,打印火光节点状态
func _initialize() -> void:
	var we := WorldEnvironment.new()
	var e := Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color(0.35, 0.4, 0.45)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(0.8, 0.8, 0.8)
	e.ambient_light_energy = 0.7
	we.environment = e
	root.add_child(we)
	var light := DirectionalLight3D.new()
	light.rotation = Vector3(-0.8, 0.6, 0)
	root.add_child(light)
	var cam := Camera3D.new()
	cam.current = true
	cam.fov = 45.0
	root.add_child(cam)
	var gun := Node3D.new()
	root.add_child(gun)
	gun.position = Vector3(0, 1.2, 0)
	var muzzle := Node3D.new()
	muzzle.name = "Muzzle"
	muzzle.position = Vector3(0, 0, -0.042)
	gun.add_child(muzzle)
	var mf = load("res://src/Effects/MuzzleFlash.cs").new()
	muzzle.add_child(mf)
	var model = load("res://assets/models/guns/handgun/handgun.fbx").instantiate()
	model.rotation = Vector3(0, PI, 0)
	model.scale = Vector3.ONE * 0.725
	gun.add_child(model)
	var tip := MeshInstance3D.new()
	var sm := SphereMesh.new()
	sm.radius = 0.008
	sm.height = 0.016
	tip.mesh = sm
	var tipmat := StandardMaterial3D.new()
	tipmat.albedo_color = Color(0, 1, 0)
	tip.material_override = tipmat
	muzzle.add_child(tip)
	# 相机:斜上方俯视枪口区域;直接赋 global_transform(避免 look_at 读到未刷新的原点)
	var cam_pos := Vector3(0.55, 1.45, 0.55)
	var cam_target := Vector3(0, 1.2, -0.05)
	cam.global_transform = Transform3D(Basis.looking_at(cam_target - cam_pos, Vector3.UP), cam_pos)
	var floor := MeshInstance3D.new()
	floor.mesh = PlaneMesh.new()
	floor.mesh.size = Vector2(4, 4)
	root.add_child(floor)
	print("cam=", cam.global_position, " gun=", gun.global_position, " model_aabb=", model.get_node_or_null("Handgun") != null)
	for i in 5:
		await process_frame
	cam.make_current()
	print("viewport_cam=", root.get_viewport().get_camera_3d().get_path(), " cam_gpos=", cam.global_position)
	mf.Fire()
	var times := [0.03, 0.08, 0.14, 0.4]
	var t := 0.0
	for k in times.size():
		while t < times[k]:
			await process_frame
			t += 1.0 / 60.0
		var flame = mf.get_node_or_null("Flame")
		var smoke = mf.get_node_or_null("Smoke")
		print("t=%.3f mf.visible=%s flame=%s smoke=%s muzzle_gpos=%s flame_gpos=%s" % [
			t, mf.visible,
			flame.visible if flame else "?",
			smoke.visible if smoke else "?",
			muzzle.global_position,
			flame.global_position if flame else "?"])
		var img := root.get_viewport().get_texture().get_image()
		img.save_png("G:/FPSGame/tools/screenshots/fix4/flash_%d.png" % k)
	quit()
