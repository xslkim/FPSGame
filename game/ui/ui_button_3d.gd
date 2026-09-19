extends StaticBody3D
class_name UIButton3D
## 3D 空间 UI 按钮(统一方案):StaticBody3D(layer 3 Button)+ 占位 quad + Label3D 文字。
## 被 GunUIController 射线命中 → on_shot();也可用鼠标点击(同射线)或 shortcut 键触发。
## 用 UIButton3D.create(text, size, cb) 代码构建,add_child 后摆 position 即可。

signal pressed

var text := ""
var on_pressed := Callable()
var shortcut: Key = KEY_NONE
var enabled := true

var _size := Vector2(0.6, 0.22)
var _bg_color := Color(0.16, 0.35, 0.6, 0.95)
var _mesh: MeshInstance3D
var _label: Label3D
var _mat: StandardMaterial3D

static func create(p_text: String, p_size := Vector2(0.6, 0.22), cb := Callable()) -> UIButton3D:
	var b := UIButton3D.new()
	b.text = p_text
	b._size = p_size
	b.on_pressed = cb
	return b

func _ready() -> void:
	collision_layer = 0b100  # layer 3 Button
	collision_mask = 0
	add_to_group("ui_button_3d")
	_mat = StandardMaterial3D.new()
	_mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	_mat.albedo_color = _bg_color
	_mesh = MeshInstance3D.new()
	_mesh.name = "Bg"
	var q := QuadMesh.new()
	q.size = _size
	q.material = _mat
	_mesh.mesh = q
	add_child(_mesh)
	var col := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(_size.x, _size.y, 0.05)
	col.shape = box
	add_child(col)
	_label = Label3D.new()
	_label.name = "Text"
	_label.text = text
	_label.pixel_size = 0.0025
	_label.font_size = 64
	_label.modulate = Color.WHITE
	_label.position = Vector3(0, 0, 0.01)
	_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	UITheme.apply_label3d(_label)
	add_child(_label)
	_update_visual()

## GunUIController 命中回调(与鼠标/键盘同路径)
func on_shot() -> void:
	trigger()

func trigger() -> void:
	if not enabled:
		return
	pressed.emit()
	if on_pressed.is_valid():
		on_pressed.call()
	var tw := create_tween()
	tw.tween_property(self, "scale", Vector3.ONE * 0.92, 0.05)
	tw.tween_property(self, "scale", Vector3.ONE, 0.1)

func set_text(t: String) -> void:
	text = t
	if _label != null:
		_label.text = t

func set_enabled(v: bool) -> void:
	enabled = v
	_update_visual()

func set_bg_color(c: Color) -> void:
	_bg_color = c
	_update_visual()

func _update_visual() -> void:
	if _mat != null:
		_mat.albedo_color = _bg_color if enabled else Color(0.3, 0.3, 0.3, 0.7)
