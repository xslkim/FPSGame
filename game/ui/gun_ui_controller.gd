extends Node3D
class_name GunUIController
## 射击触发 UI 框架(对应原作 UIController.cs)。
## 挂在 UI 场景的相机下:每帧按 InputManager 瞄准(UDP 四元数/键盘方向键)算射线方向,
## 扳机边沿(回车/手机扳机)或鼠标左键 → 射线命中 layer 3 且有 on_shot() 的物体 → 调 on_shot()。
## MessageBox 打开时只响应名字带 "MessageButton" 前缀的命中(照原作)。
## 按钮统一用 UIButton3D.create() 构建;键盘备选 = 按钮 shortcut 键 / MessageBox 的 Enter/Esc。

const RAY_LENGTH := 100.0
const RAY_MASK := 0b100  # 只打 layer 3 Button

@export var camera_path: NodePath = ^".."

var camera: Camera3D

func _ready() -> void:
	camera = get_node_or_null(camera_path) as Camera3D
	if camera == null:
		camera = get_viewport().get_camera_3d()
	InputManager.fire_enabled = true
	InputManager.key1_right.connect(_on_trigger)
	InputManager.key1_left.connect(_on_trigger)
	_make_crosshair()

func _on_trigger() -> void:
	if GlobalObject.is_game_pause or camera == null:
		return
	_fire_ray(camera.global_position, _aim_dir())

## 瞄准方向 = 相机朝向 × 枪口旋转(4.2 节,与 FireSystem 同一解算)
func _aim_dir() -> Vector3:
	return camera.global_basis * (GlobalObject.get_ring_rotation() * Vector3.FORWARD)

func _unhandled_input(event: InputEvent) -> void:
	if GlobalObject.is_game_pause or camera == null:
		return
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_fire_ray(camera.project_ray_origin(event.position),
			camera.project_ray_normal(event.position))
	elif event is InputEventKey and event.pressed and not event.echo:
		for b in get_tree().get_nodes_in_group("ui_button_3d"):
			if b is UIButton3D and b.shortcut != KEY_NONE and b.shortcut == event.keycode:
				if MessageBox.is_open() and not String(b.name).begins_with("MessageButton"):
					continue
				b.trigger()
				return

func _fire_ray(from: Vector3, dir: Vector3) -> void:
	var query := PhysicsRayQueryParameters3D.create(from, from + dir * RAY_LENGTH, RAY_MASK)
	var hit := get_world_3d().direct_space_state.intersect_ray(query)
	if not hit.is_empty():
		handle_hit(hit.collider)

## MessageBox 打开时只响应 MessageButton(返回是否真触发;自检测试用)
func handle_hit(obj: Object) -> bool:
	if obj == null or not obj.has_method("on_shot"):
		return false
	if MessageBox.is_open() and not String(obj.name).begins_with("MessageButton"):
		return false
	obj.on_shot()
	return true

func _make_crosshair() -> void:
	var layer := CanvasLayer.new()
	layer.name = "CrosshairLayer"
	var label := Label.new()
	label.text = "+"
	label.add_theme_font_size_override("font_size", 32)
	label.set_anchors_preset(Control.PRESET_CENTER)
	layer.add_child(label)
	add_child(layer)
