# Captures one screenshot per battle camera position (cam_pos_0..N) plus the
# intro start marker in the real level scene.
# Run:  godot scenes/levels/level_cam_shots.tscn --quit-after 140
# Options (user args):
#   --scene <path>     关卡场景(默认 level1_battle.tscn)
#   --prefix <name>    输出文件名前缀(默认 level)
extends Node3D

const LEVEL_SCENE := "res://scenes/levels/level1_battle.tscn"
const SHOT_DIR := "res://tools/screenshots/"

var _frame := 0
var _cam: Camera3D
var _markers: Array[Node3D] = []
var _names: Array[String] = []
var _prefix := "level"
var _hud: Node


func _ready() -> void:
	var args := OS.get_cmdline_user_args()
	var scene_path := LEVEL_SCENE
	var si := args.find("--scene")
	if si >= 0 and si + 1 < args.size():
		scene_path = args[si + 1]
	var pi := args.find("--prefix")
	if pi >= 0 and pi + 1 < args.size():
		_prefix = args[pi + 1]
	var level: Node = load(scene_path).instantiate()
	add_child(level)
	# 关卡 start_battle 的机位 Tween 会逐帧覆写相机,截图前杀掉
	var cam_tween: Tween = level.get("_cam_tween")
	if cam_tween != null and cam_tween.is_valid():
		cam_tween.kill()
	# FireSystem(枪模/Lazer)与 HUD 只挡画面,截图隐藏;HUD 每帧强制(战斗开始会重新显示)
	var fs: Node = level.get_node_or_null("Camera3D/FireSystem")
	if fs != null:
		fs.visible = false
	_hud = level.get_node_or_null("HUD")
	_cam = level.get_node("Camera3D") as Camera3D
	for i in range(8):
		var m := level.get_node_or_null("CamPositions/cam_pos_%d" % i) as Node3D
		if m == null:
			break
		_markers.append(m)
		_names.append("cam_pos_%d" % i)
	var intro := level.get_node_or_null("IntroMarkers/intro_from") as Node3D
	if intro != null:
		_markers.append(intro)
		_names.append("intro_from")
	print("[shots] scene=%s markers: %s" % [scene_path, str(_names)])


func _process(_delta: float) -> void:
	_frame += 1
	if _hud != null:
		_hud.visible = false
	var slot := (_frame - 20) / 20  # position at 20,40,...; capture at 22,42,...
	var phase := (_frame - 20) % 20
	if slot < 0 or slot >= _markers.size():
		return
	if phase == 0:
		_cam.global_transform = _markers[slot].global_transform
		print("[shots] %s pos=%s fwd=%s" % [_names[slot], _cam.global_position,
			-_cam.global_basis.z])
	elif phase == 2:
		DirAccess.make_dir_recursive_absolute(SHOT_DIR)
		var img := get_viewport().get_texture().get_image()
		var path := "%s%s_%s.png" % [SHOT_DIR, _prefix, _names[slot]]
		var err := img.save_png(path)
		print("[shots] saved %s (err=%d)" % [path, err])
