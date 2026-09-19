# Screenshot viewer for a rebuilt environment scene.
# Instances the env scene, adds ambient light if the scene has none
# (no baked lightmaps yet), moves a camera through 3 positions and saves PNGs.
# Run:  godot scenes/levels/env_viewer.tscn --quit-after 90 -- --env res://scenes/levels/env_level2.tscn
extends Node3D

const SHOT_DIR := "res://tools/screenshots/"

var _cam: Camera3D
var _frame := 0
var _shots: Array[Transform3D] = []
var _prefix := "env"
const POSITION_FRAMES := [15, 40, 65]
const CAPTURE_FRAMES := [18, 43, 68]


func _ready() -> void:
	var env_scene := "res://scenes/levels/env_school_hallway.tscn"
	var argv := OS.get_cmdline_user_args()
	var i := argv.find("--env")
	if i >= 0 and i + 1 < argv.size():
		env_scene = argv[i + 1]
	_prefix = env_scene.get_file().get_basename()
	var env: Node3D = load(env_scene).instantiate()
	add_child(env)

	if env.get_node_or_null("WorldEnvironment") == null:
		var we := WorldEnvironment.new()
		var e := Environment.new()
		e.background_mode = Environment.BG_COLOR
		e.background_color = Color(0.08, 0.08, 0.1)
		e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
		e.ambient_light_color = Color(1, 1, 1)
		e.ambient_light_energy = 0.6
		we.environment = e
		add_child(we)

		var sun := DirectionalLight3D.new()
		sun.rotation_degrees = Vector3(-50, 30, 0)
		sun.light_energy = 0.4
		add_child(sun)
	elif argv.has("--fill"):
		# Night scenes: raise ambient and add a fill light for verification shots.
		var we2 := env.get_node("WorldEnvironment") as WorldEnvironment
		if we2.environment != null:
			we2.environment.ambient_light_energy = 1.2
		var sun2 := DirectionalLight3D.new()
		sun2.rotation_degrees = Vector3(-50, 30, 0)
		sun2.light_energy = 0.8
		add_child(sun2)

	_cam = Camera3D.new()
	_cam.far = 3000.0
	add_child(_cam)
	_cam.current = true

	var aabb := _compute_aabb(env)
	_shots = _make_shots(aabb, env)
	# Optional single explicit camera: --campos x,y,z --camtarget x,y,z
	var cpi := argv.find("--campos")
	var cti := argv.find("--camtarget")
	if cpi >= 0 and cti >= 0 and cpi + 1 < argv.size() and cti + 1 < argv.size():
		_shots = [_look_at(_parse_v3(argv[cpi + 1]), _parse_v3(argv[cti + 1]))]
		_prefix += "_custom"
	print("[viewer] env: ", env_scene, " aabb: ", aabb)
	for j in _shots.size():
		print("[viewer] shot %d: pos=%s" % [j, _shots[j].origin])


func _process(_delta: float) -> void:
	_frame += 1
	var pi := POSITION_FRAMES.find(_frame)
	if pi >= 0 and pi < _shots.size():
		_cam.global_transform = _shots[pi]
	var ci := CAPTURE_FRAMES.find(_frame)
	if ci >= 0 and ci < _shots.size():
		DirAccess.make_dir_recursive_absolute(SHOT_DIR)
		var img := get_viewport().get_texture().get_image()
		var path := "%s%s_shot_%d.png" % [SHOT_DIR, _prefix, ci + 1]
		var err := img.save_png(path)
		print("[viewer] saved %s (err=%d, size=%s)" % [path, err, img.get_size()])


func _compute_aabb(root: Node) -> AABB:
	var aabb := AABB()
	var first := true
	var stack := [root]
	while not stack.is_empty():
		var n: Node = stack.pop_back()
		if n is MeshInstance3D and n.visible and n.mesh != null:
			var ga: AABB = n.global_transform * n.mesh.get_aabb()
			if first:
				aabb = ga
				first = false
			else:
				aabb = aabb.merge(ga)
		for c in n.get_children():
			stack.append(c)
	return aabb


func _make_shots(aabb: AABB, env: Node) -> Array[Transform3D]:
	var out: Array[Transform3D] = []
	var center := aabb.get_center()
	# Find a visible floor tile to stand on: guarantees an interior viewpoint.
	var floor_centers: Array[Vector3] = []
	var stack := [env]
	while not stack.is_empty():
		var n: Node = stack.pop_back()
		if n is MeshInstance3D and n.is_visible_in_tree() and n.mesh != null \
				and "Floor" in n.name:
			floor_centers.append(n.global_transform * n.mesh.get_aabb().get_center())
		for c in n.get_children():
			stack.append(c)
	var axis := "x" if aabb.size.x >= aabb.size.z else "z"
	if not floor_centers.is_empty():
		floor_centers.sort_custom(func(a, b): return a[axis] < b[axis])
		# Shot 1/2: stand on floor tiles at 15% / 85% along the corridor,
		# looking along its axis.
		for t in [0.15, 0.85]:
			var fc: Vector3 = floor_centers[int(floor_centers.size() * t)]
			var pos := fc + Vector3(0, 1.6, 0)
			var target := pos + Vector3.RIGHT * (10.0 if axis == "x" else 0.0) \
				+ Vector3.BACK * (10.0 if axis == "z" else 0.0)
			out.append(_look_at(pos, target))
	else:
		# Outdoor fallback: eye-height at both ends of the long axis, then overview.
		var eye_y := aabb.position.y + 1.7
		for t in [0.08, 0.92]:
			var pos := Vector3(center.x, eye_y, center.z)
			pos[axis] = aabb.position[axis] + aabb.size[axis] * t
			out.append(_look_at(pos, Vector3(center.x, eye_y, center.z)))
	# Shot 3: elevated overview from a corner.
	var over := center + Vector3(aabb.size.x * 0.45, aabb.size.y * 1.5 + 4.0, aabb.size.z * 0.45)
	out.append(_look_at(over, center))
	return out


func _parse_v3(s: String) -> Vector3:
	var p := s.split_floats(",")
	return Vector3(p[0], p[1], p[2])


func _look_at(pos: Vector3, target: Vector3) -> Transform3D:
	var t := Transform3D()
	t.origin = pos
	var fwd := (target - pos).normalized()
	if fwd.length() < 0.001:
		fwd = Vector3.FORWARD
	t.basis = Basis.looking_at(fwd, Vector3.UP)
	return t
