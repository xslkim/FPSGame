# Gun model orientation viewer (one-off verification tool).
# Renders each imported gun FBX from side/front-top angles with axis markers:
# red thin box along -Z, blue along +Z, so barrel direction can be read off.
# Run (windowed, GPU):  godot --path . -s tools/gun_viewer.gd --quit-after 80
extends SceneTree

const SHOT_DIR := "res://tools/screenshots/"
const GUNS := [
	"res://assets/models/guns/ak47/ak47.fbx",
	"res://assets/models/guns/m4/m4.fbx",
	"res://assets/models/guns/handgun/handgun.fbx",
]

var _frame := 0
var _cam: Camera3D
var _shots: Array = []  # [Transform3D, name]


func _initialize() -> void:
	var root := get_root()
	var we := WorldEnvironment.new()
	var e := Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color(0.1, 0.1, 0.12)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(1, 1, 1)
	e.ambient_light_energy = 0.8
	we.environment = e
	root.add_child(we)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-50, 30, 0)
	sun.light_energy = 0.6
	root.add_child(sun)

	var spacing := 1.4
	for i in GUNS.size():
		var gun: Node3D = load(GUNS[i]).instantiate()
		gun.position = Vector3((i - 1) * spacing, 0, 0)
		root.add_child(gun)
		# axis markers at gun origin
		var mk_z := _marker(Color(0.1, 0.3, 1.0), Vector3(0.02, 0.02, 0.5))
		mk_z.position = gun.position + Vector3(0, 0, 0.25)  # +Z blue
		root.add_child(mk_z)
		var mk_zn := _marker(Color(1.0, 0.1, 0.1), Vector3(0.02, 0.02, 0.5))
		mk_zn.position = gun.position + Vector3(0, 0, -0.25)  # -Z red
		root.add_child(mk_zn)
		var mk_y := _marker(Color(0.1, 1.0, 0.1), Vector3(0.02, 0.3, 0.02))
		mk_y.position = gun.position + Vector3(0, 0.15, 0)  # +Y green
		root.add_child(mk_y)

	_cam = Camera3D.new()
	root.add_child(_cam)
	_cam.current = true
	# shot 0: side view from +X looking -X (z axis runs horizontally in frame)
	_shots.append([_look_at(Vector3(3.0, 0.2, 0), Vector3(0, 0, 0)), "side_plusx"])
	# shot 1: side view from -X looking +X
	_shots.append([_look_at(Vector3(-3.0, 0.2, 0), Vector3(0, 0, 0)), "side_minusx"])
	# shot 2: top-down
	_shots.append([_look_at(Vector3(0, 3.0, 0.001), Vector3(0, 0, 0)), "top"])
	# shot 3: rear-quarter (from +Z behind, like a player holding it)
	_shots.append([_look_at(Vector3(0.8, 0.4, 1.6), Vector3(0, 0, 0)), "rear_quarter"])


func _marker(color: Color, size: Vector3) -> MeshInstance3D:
	var m := MeshInstance3D.new()
	var b := BoxMesh.new()
	b.size = size
	var mat := StandardMaterial3D.new()
	mat.albedo_color = color
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	b.material = mat
	m.mesh = b
	return m


func _process(_delta: float) -> bool:
	_frame += 1
	var slot := (_frame - 10) / 15
	var phase := (_frame - 10) % 15
	if slot < 0 or slot >= _shots.size():
		return false
	if phase == 0:
		_cam.transform = _shots[slot][0]
	elif phase == 2:
		DirAccess.make_dir_recursive_absolute(SHOT_DIR)
		var img := get_root().get_texture().get_image()
		var path := "%sgun_%s.png" % [SHOT_DIR, _shots[slot][1]]
		var err := img.save_png(path)
		print("[gun_viewer] saved %s (err=%d)" % [path, err])
	return false


func _look_at(pos: Vector3, target: Vector3) -> Transform3D:
	var t := Transform3D()
	t.origin = pos
	t.basis = Basis.looking_at((target - pos).normalized(), Vector3.UP)
	return t
