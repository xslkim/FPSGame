# First-person gun screenshots (one-off verification tool).
# Instances fire_range (binds players + FireSystem), captures the default
# handgun view, then emits InputManager.key2_right twice to switch through
# AK47 / M4 via the real switch path, capturing each.
# Run (windowed, GPU):  godot --path . -s tools/gun_fp_shots.gd --quit-after 320
extends SceneTree

const SHOT_DIR := "res://tools/screenshots/"
const NAMES := ["handgun", "ak47", "m4"]

var _frame := 0
var _shot_idx := 0


func _initialize() -> void:
	var range: Node = load("res://scenes/test/fire_range.tscn").instantiate()
	get_root().add_child(range)


func _process(_delta: float) -> bool:
	_frame += 1
	# capture at frame 40, then switch + capture every 90 frames
	if _shot_idx < NAMES.size() and _frame == 40 + _shot_idx * 90:
		_capture(NAMES[_shot_idx])
		_shot_idx += 1
		if _shot_idx < NAMES.size():
			get_root().get_node("InputManager").key2_right.emit()
	elif _frame == 40 + NAMES.size() * 90:
		# 直接调用 fire() 触发枪口火光,下一帧捕获
		var fs: Node = get_root().get_node("FireRange/Camera3D/FireSystem")
		var gun = fs._current_gun[1]  # PlayerSystem.Side.Right == 1
		gun.last_fire_time = -99.0
		gun.fire()
	elif _frame == 41 + NAMES.size() * 90:
		_capture("m4_fire")
	return false


func _capture(tag: String) -> void:
	DirAccess.make_dir_recursive_absolute(SHOT_DIR)
	var img := get_root().get_texture().get_image()
	var path := "%sfp_%s.png" % [SHOT_DIR, tag]
	var err := img.save_png(path)
	print("[fp_shots] saved %s (err=%d)" % [path, err])
