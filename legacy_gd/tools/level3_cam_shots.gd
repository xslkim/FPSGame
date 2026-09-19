# Captures one screenshot per battle camera position (cam_pos_0..6) in level3.
# Run:  godot scenes/levels/level3_cam_shots.tscn --quit-after 170
extends Node3D

const LEVEL_SCENE := "res://scenes/levels/level3.tscn"
const SHOT_DIR := "res://tools/screenshots/"

var _frame := 0
var _cam: Camera3D
var _markers: Array[Node3D] = []
var _names: Array[String] = []


func _ready() -> void:
	var level: Node = load(LEVEL_SCENE).instantiate()
	add_child(level)
	var hud: Node = level.get_node_or_null("HUD")
	if hud is CanvasItem:
		hud.visible = false
	# 占位枪盒会挡半屏,截图只隐藏枪身盒体,保留 Lazer 确认枪口朝向
	var fs: Node = level.get_node_or_null("Camera3D/FireSystem")
	if fs != null:
		for anchor in fs.get_children():
			if "GunAnchor" in anchor.name:
				for gun in anchor.get_children():
					var body := gun.get_node_or_null("Body") as MeshInstance3D
					if body != null:
						body.visible = false
	_cam = level.get_node("Camera3D") as Camera3D
	for i in 7:
		var m := level.get_node_or_null("CamPositions/cam_pos_%d" % i) as Node3D
		if m != null:
			_markers.append(m)
			_names.append("cam_pos_%d" % i)
	print("[shots] markers: ", _names)


func _process(_delta: float) -> void:
	_frame += 1
	var slot := (_frame - 20) / 20  # position at 20,40,...; capture at 22,42,...
	var phase := (_frame - 20) % 20
	if slot < 0 or slot >= _markers.size():
		return
	if phase == 0:
		_cam.global_transform = _markers[slot].global_transform
	elif phase == 2:
		DirAccess.make_dir_recursive_absolute(SHOT_DIR)
		var img := get_viewport().get_texture().get_image()
		var path := "%slevel3_%s.png" % [SHOT_DIR, _names[slot]]
		var err := img.save_png(path)
		print("[shots] saved %s (err=%d)" % [path, err])
