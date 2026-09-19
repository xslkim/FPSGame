extends Node3D
## M7 工具:怪物陈列 + 截图(需 GPU,非 headless)。
## 用法: godot --path game tools/monster_gallery.tscn -- [--only wolf,toon] [--views front,left,back34]
## 输出: tools/screenshots/m7_<name>_<view>.png

const OUT_DIR := "G:/FPSGame/tools/screenshots"

# name -> [tscn, cam_dist, cam_h, look_y, pose_clip, pose_time]
const MONSTERS := {
	"toon": ["res://gameplay/monsters/toon.tscn", 3.5, 1.3, 0.9, "Idle02", 0.5, "reload", 1.2],
	"toon_alien": ["res://gameplay/monsters/toon_alien.tscn", 3.5, 1.3, 0.9, "Idle02", 0.5, "reload", 0.3],
	"wolf": ["res://gameplay/monsters/wolf.tscn", 4.5, 1.2, 0.7, "Idle02", 0.5, "Attack1", 0.6],
	"wolf_blue": ["res://gameplay/monsters/wolf_blue.tscn", 5.0, 1.5, 1.0, "Idle02", 0.5, "BiteAttack", 0.5],
	"wolf_green": ["res://gameplay/monsters/wolf_green.tscn", 5.0, 1.5, 1.0, "Idle02", 0.5, "BiteAttack", 0.5],
	"fat_zombie": ["res://gameplay/monsters/fat_zombie.tscn", 4.5, 1.4, 0.9, "Idle02", 0.5, "Attack1", 0.8],
	"dragon_red": ["res://gameplay/monsters/dragon_red.tscn", 14.0, 3.0, 1.5, "locomotion", 0.5, "FireBreathOnce", 1.0],
	"dragon_blue": ["res://gameplay/monsters/dragon_blue.tscn", 14.0, 3.0, 1.5, "locomotion", 0.5, "FireBreathOnce", 1.0],
	"dragon_green": ["res://gameplay/monsters/dragon_green.tscn", 14.0, 3.0, 1.5, "locomotion", 0.5, "FireBreathOnce", 1.0],
	"magma_demon": ["res://gameplay/monsters/magma_demon.tscn", 9.0, 2.5, 1.8, "idle", 0.5, "attack01", 0.6],
	"rock_warrior": ["res://gameplay/monsters/rock_warrior.tscn", 5.5, 1.8, 1.1, "Idle02", 0.5, "atk01", 0.7],
	"level2_boss": ["res://gameplay/monsters/level2_boss.tscn", 7.0, 2.2, 1.5, "Idle", 0.5, "Skill1", 0.5],
}

const VIEWS := {"front": 0.0, "left": 90.0, "back34": 210.0}

var _cam: Camera3D

func _ready() -> void:
	var args := OS.get_cmdline_user_args()
	var only := _arg_list(args, "--only")
	var views := _arg_list(args, "--views")
	DirAccess.make_dir_recursive_absolute(OUT_DIR)
	get_window().size = Vector2i(960, 540)
	_setup_env()
	for name in MONSTERS:
		if not only.is_empty() and not only.has(name):
			continue
		await _shoot(name, MONSTERS[name], views)
	print("[GALLERY] done")
	get_tree().quit()

func _arg_list(args: PackedStringArray, key: String) -> Array:
	var i := args.find(key)
	if i >= 0 and i + 1 < args.size():
		return Array(args[i + 1].split(",", false))
	return []

func _setup_env() -> void:
	var env_node := WorldEnvironment.new()
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.18, 0.2, 0.24)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.7, 0.72, 0.78)
	env.ambient_light_energy = 0.8
	env_node.environment = env
	add_child(env_node)
	var sun := DirectionalLight3D.new()
	sun.rotation = Vector3(deg_to_rad(-50.0), deg_to_rad(30.0), 0.0)
	sun.light_energy = 1.6
	sun.shadow_enabled = true
	add_child(sun)
	_cam = Camera3D.new()
	_cam.current = true
	_cam.fov = 50.0
	add_child(_cam)
	var floor := MeshInstance3D.new()
	var pm := PlaneMesh.new()
	pm.size = Vector2(60, 60)
	floor.mesh = pm
	var fm := StandardMaterial3D.new()
	fm.albedo_color = Color(0.3, 0.32, 0.35)
	pm.material = fm
	add_child(floor)

func _shoot(name: String, cfg: Array, views: Array) -> void:
	var ps: PackedScene = load(cfg[0])
	var inst: Node3D = ps.instantiate()
	add_child(inst)
	inst.visible = true
	# 摆姿势
	var ap: AnimationPlayer = inst.get_node_or_null("AnimationPlayer")
	if ap != null and ap.has_animation(StringName(cfg[4])):
		ap.play(StringName(cfg[4]))
		ap.advance(float(cfg[5]))
	if inst.has_method("_change_appearance"):
		inst.call("_change_appearance")
	var dist: float = cfg[1]
	var h: float = cfg[2]
	var look := Vector3(0, cfg[3], 0)
	for view in (views if not views.is_empty() else VIEWS.keys()):
		var ang := deg_to_rad(float(VIEWS.get(view, 0.0)))
		_cam.position = Vector3(sin(ang) * dist, h, cos(ang) * dist)
		_cam.look_at(look)
		await get_tree().process_frame
		await get_tree().process_frame
		await get_tree().process_frame
		var img := get_window().get_texture().get_image()
		var path := "%s/m7_%s_%s.png" % [OUT_DIR, name, view]
		img.save_png(path)
		print("[GALLERY] saved %s" % path)
	# 可选第二姿势(攻击 clip):front 视角一拍
	if cfg.size() > 6 and ap != null and ap.has_animation(StringName(cfg[6])):
		ap.play(StringName(cfg[6]))
		ap.advance(float(cfg[7]))
		_cam.position = Vector3(0, h, dist)
		_cam.look_at(look)
		await get_tree().process_frame
		await get_tree().process_frame
		await get_tree().process_frame
		var img2 := get_window().get_texture().get_image()
		var path2 := "%s/m7_%s_attack.png" % [OUT_DIR, name]
		img2.save_png(path2)
		print("[GALLERY] saved %s" % path2)
	inst.queue_free()
	await get_tree().process_frame
