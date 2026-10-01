extends SceneTree

# L2 掉落专项探针:窗口/SrcPosition 地面 + src→window 路径 + 出生锥 + 镇区网格 + 40s 实跑掉落监视
func _initialize() -> void:
	var lvl = load("res://scenes/levels/level2.tscn").instantiate()
	root.add_child(lvl)
	for i in 6:
		await physics_frame
	var space: PhysicsDirectSpaceState3D = lvl.get_world_3d().direct_space_state
	var mask := 0xFFFFFFF1
	# 1. 窗口与 SrcPosition
	print("---- fire windows ----")
	for path in ["Windows/G0", "Windows/G1", "Windows/G2"]:
		var grp = lvl.get_node_or_null(path)
		if grp == null:
			# WindowGroupPaths 可能不叫这个名,遍历找
			continue
		for w in grp.get_children():
			_dump_window(space, mask, w)
	# 兼容:直接从 Level2 的 WindowGroupPaths 读
	for g in lvl.get("WindowGroupPaths"):
		var grp = lvl.get_node(g)
		for w in grp.get_children():
			_dump_window(space, mask, w)
	# 2. 出生锥(箱 12m / 默认 8m)
	print("---- born cones ----")
	for cam_path in ["CamPositions/cam_pos_0", "CamPositions/cam_pos_1", "CamPositions/cam_pos_2"]:
		var cam = lvl.get_node(cam_path)
		var gt: Transform3D = cam.global_transform
		var f: Vector3 = (-gt.basis.z).normalized()
		var up: Vector3 = gt.basis.y.normalized()
		for len in [8.0, 12.0]:
			for ang in [-33.0, -16.0, 0.0, 16.0, 33.0]:
				var dir: Vector3 = f.rotated(up, deg_to_rad(ang))
				var to: Vector3 = gt.origin + dir * len
				var q := PhysicsRayQueryParameters3D.create(gt.origin, to, mask)
				var hit := space.intersect_ray(q)
				var endp: Vector3 = hit.get("position", to) if hit else to
				var g = _ground_y(space, mask, endp)
				if g == null:
					print("%s len=%.0f ang=%+.0f end=(%.1f,%.1f,%.1f) NO-GROUND" % [cam_path, len, ang, endp.x, endp.y, endp.z])
	print("born cones done")
	# 3. 镇区网格
	print("---- grid ----")
	var miss := 0
	for x in range(-40, 26, 5):
		for z in range(-45, 21, 5):
			var g = _ground_y(space, mask, Vector3(x, 0, z))
			if g == null:
				miss += 1
				print("  no-ground (%d,%d)" % [x, z])
	print("grid miss=%d" % miss)
	# 4. 实跑掉落监视 40s
	print("---- live watch ----")
	var pool = lvl.get_node("MonsterPool")
	var hist := {}
	var frames := 0
	while frames < 60 * 40:
		await physics_frame
		frames += 1
		if frames % 60 == 0:
			for m in pool.get_children():
				if m.get("CurState") == 0:
					continue
				var p: Vector3 = m.global_position
				if not hist.has(m):
					hist[m] = p.y
				var dy: float = p.y - hist[m]
				if dy < -1.5:
					print("t=%d FALL? %s y %.1f→%.1f pos=(%.1f,%.1f,%.1f)" % [frames / 60, m.name, hist[m], p.y, p.x, p.y, p.z])
				hist[m] = p.y
	print("watch done")
	# 终态审计:toon 报 当前y vs 窗口y;box 报 当前y+是否贴地
	for m in pool.get_children():
		if m.get("CurState") == 0:
			continue
		var p: Vector3 = m.global_position
		var fw = m.get("FireWindow")
		if fw != null:
			var wy: float = fw.global_position.y
			var tag := "OK" if abs(p.y - wy) < 0.4 else "MISPLACED"
			print("toon %s y=%.2f window_y=%.2f %s" % [m.name, p.y, wy, tag])
		elif "box" in String(m.name):
			var g = _ground_y(space, mask, p)
			print("box %s y=%.2f ground_below=%s floor=%s" % [m.name, p.y, str(g), str(m.is_on_floor())])
	quit()

func _ground_y(space: PhysicsDirectSpaceState3D, mask: int, pos: Vector3):
	var q := PhysicsRayQueryParameters3D.create(pos + Vector3(0, 2.5, 0), pos + Vector3(0, -60, 0), mask)
	var h := space.intersect_ray(q)
	return h.position.y if h else null

func _dump_window(space: PhysicsDirectSpaceState3D, mask: int, w: Node) -> void:
	var wp: Vector3 = w.global_position
	var src: Vector3 = w.GetSrcPosition() if w.has_method("GetSrcPosition") else wp
	var gw = _ground_y(space, mask, wp)
	var gs = _ground_y(space, mask, src)
	# src→window 路径 5 点
	var path_miss := []
	for k in range(1, 5):
		var p: Vector3 = src.lerp(wp, k / 5.0)
		if _ground_y(space, mask, p) == null:
			path_miss.append(k)
	print("%s w=(%.1f,%.1f,%.1f) gy=%s | src=(%.1f,%.1f,%.1f) gy=%s | path_miss=%s" % [
		w.name, wp.x, wp.y, wp.z, str(gw), src.x, src.y, src.z, str(gs), str(path_miss)])
