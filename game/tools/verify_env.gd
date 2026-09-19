# Reconciles the generated env scene against the Unity export JSON.
# Run:  godot --headless -s tools/verify_env.gd -- --json school_day --scene res://scenes/levels/env_school_hallway.tscn
extends SceneTree


func _init() -> void:
	var args := {}
	var argv := OS.get_cmdline_user_args()
	var i := 0
	while i < argv.size():
		if argv[i].begins_with("--") and i + 1 < argv.size():
			args[argv[i].trim_prefix("--")] = argv[i + 1]
			i += 2
		else:
			i += 1

	var json_path: String = args.get("json", "res://data/env_export/school_day.json")
	if not json_path.begins_with("res://"):
		json_path = "res://data/env_export/" + json_path + ".json"
	var scene_path: String = args.get("scene", "res://scenes/levels/env_school_hallway.tscn")

	var f := FileAccess.open(json_path, FileAccess.READ)
	if f == null:
		printerr("[verify] cannot open ", json_path)
		quit(1)
	var data: Dictionary = JSON.parse_string(f.get_as_text())
	f.close()

	var ps: PackedScene = load(scene_path)
	if ps == null:
		printerr("[verify] cannot load ", scene_path)
		quit(1)
	var inst := ps.instantiate()

	var counts := {"nodes": 0, "mesh": 0, "light": 0, "body": 0, "shape": 0}
	_count(inst, counts)

	var nodes: Array = data["nodes"]
	# Import report carries skipped-subtree / sky adjustments from the importer.
	var skipped_nodes := 0
	var skipped_meshes := 0
	var skipped_lights := 0
	var sky_nodes := 0
	var report_path := json_path.replace(".json", "_import_report.json")
	if FileAccess.file_exists(report_path):
		var rf := FileAccess.open(report_path, FileAccess.READ)
		var report: Dictionary = JSON.parse_string(rf.get_as_text())
		rf.close()
		var st: Dictionary = report.get("stats", {})
		skipped_nodes = int(st.get("skipped_nodes", 0))
		skipped_meshes = int(st.get("skipped_meshes", 0))
		skipped_lights = int(st.get("skipped_lights", 0))
		if String(report.get("sky", "")) != "":
			sky_nodes = 1 # WorldEnvironment added by importer
		sky_nodes += int(st.get("geo_fixed", 0)) # GeoFix wrapper nodes

	var j_mesh := nodes.filter(func(n): return n.get("meshName", "") != "").size()
	var j_light := nodes.filter(func(n): return n.get("light") != null and String(n["light"].get("type", "")) != "").size()
	var j_collvis := nodes.filter(func(n): return String(n.get("fbx", "")).contains("/Collision/") and n.get("meshName", "") != "").size()
	var j_coll := nodes.filter(func(n): return n.get("colliderMesh", "") != "" or (n.get("boxSize") is Array and n["boxSize"].size() == 3) or (String(n.get("fbx", "")).contains("/Collision/") and n.get("meshName", "") != "")).size()

	print("[verify] scene: ", scene_path)
	# Godot tree = json nodes - skipped subtrees + sky + one StaticBody3D + one CollisionShape3D per collision body.
	var expected_nodes: int = nodes.size() - skipped_nodes + sky_nodes + counts["body"] * 2
	print("[verify] nodes:   godot=%d json=%d - skipped=%d + sky=%d + collision nodes(body+shape)=%d -> expected=%d diff=%d" % [counts["nodes"], nodes.size(), skipped_nodes, sky_nodes, counts["body"] * 2, expected_nodes, counts["nodes"] - expected_nodes])
	print("[verify] meshes:  godot=%d json(mesh nodes)=%d - skipped=%d diff=%d (collision nodes become StaticBody3D)" % [counts["mesh"], j_mesh, skipped_meshes, counts["mesh"] - (j_mesh - skipped_meshes)])
	print("[verify] lights:  godot=%d json=%d - skipped=%d diff=%d" % [counts["light"], j_light, skipped_lights, counts["light"] - (j_light - skipped_lights)])
	print("[verify] bodies:  godot=%d json(collider nodes)=%d (rest are proxy bodies)" % [counts["body"], j_coll])

	var ok: bool = counts["nodes"] == expected_nodes \
		and counts["mesh"] == j_mesh - skipped_meshes - j_collvis \
		and counts["light"] == j_light - skipped_lights \
		and counts["body"] >= j_coll - skipped_meshes
	print("[verify] %s" % ("OK" if ok else "MISMATCH"))
	inst.free()
	quit(0 if ok else 1)


func _count(n: Node, c: Dictionary) -> void:
	c["nodes"] += 1
	if n is MeshInstance3D:
		c["mesh"] += 1
	elif n is Light3D:
		c["light"] += 1
	elif n is StaticBody3D:
		c["body"] += 1
	if n is CollisionShape3D:
		c["shape"] += 1
	for ch in n.get_children():
		_count(ch, c)
