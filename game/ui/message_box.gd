extends Node3D
class_name MessageBox
## 通用弹框(8.1:内容/标题/取消/确定/回调)。3D 面板,摆在相机前。
## 按钮名 MessageButtonOk/MessageButtonCancel:打开期间 GunUIController
## 只响应 "MessageButton" 前缀的命中(照原作 UIController.cs)。
## 键盘备选:Enter=确定 / Esc=取消。
## 用法:MessageBox.show_box(self, "标题", "内容", "确定", "取消", func(ok): ...)

signal closed(ok: bool)

static var current: MessageBox = null

var ok_button: UIButton3D
var cancel_button: UIButton3D

var _cb := Callable()

static func show_box(parent: Node, title: String, content: String,
		ok_text := "确定", cancel_text := "取消", cb := Callable()) -> MessageBox:
	close_current()
	var mb := MessageBox.new()
	mb.name = "MessageBox"
	parent.add_child(mb)
	mb._build(title, content, ok_text, cancel_text, cb)
	current = mb
	return mb

static func close_current() -> void:
	if current != null and is_instance_valid(current):
		current.queue_free()
	current = null

static func is_open() -> bool:
	return current != null and is_instance_valid(current)

func _build(title: String, content: String, ok_text: String, cancel_text: String, cb: Callable) -> void:
	_cb = cb
	var cam := get_viewport().get_camera_3d()
	if cam != null:
		global_transform = cam.global_transform * Transform3D(Basis.IDENTITY, Vector3(0, 0, -2.0))
	var panel := MeshInstance3D.new()
	panel.name = "Panel"
	var q := QuadMesh.new()
	q.size = Vector2(1.7, 1.0)
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.albedo_color = Color(0.08, 0.09, 0.12, 0.97)
	q.material = mat
	panel.mesh = q
	add_child(panel)
	var title_label := _make_label(title, 72, Vector3(0, 0.36, 0.01))
	add_child(title_label)
	var content_label := _make_label(content, 48, Vector3(0, 0.05, 0.01))
	add_child(content_label)
	ok_button = UIButton3D.create(ok_text, Vector2(0.55, 0.2), func(): _close(true))
	ok_button.name = "MessageButtonOk"
	ok_button.position = Vector3(0.4, -0.32, 0.02)
	add_child(ok_button)
	cancel_button = UIButton3D.create(cancel_text, Vector2(0.55, 0.2), func(): _close(false))
	cancel_button.name = "MessageButtonCancel"
	cancel_button.set_bg_color(Color(0.45, 0.2, 0.2, 0.95))
	cancel_button.position = Vector3(-0.4, -0.32, 0.02)
	add_child(cancel_button)

func _make_label(text: String, font_size: int, pos: Vector3) -> Label3D:
	var l := Label3D.new()
	l.text = text
	l.font_size = font_size
	l.pixel_size = 0.0022
	l.position = pos
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	l.width = 700.0
	UITheme.apply_label3d(l)
	return l

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_ENTER or event.keycode == KEY_KP_ENTER:
			_close(true)
		elif event.keycode == KEY_ESCAPE:
			_close(false)

func press_ok() -> void:
	_close(true)

func press_cancel() -> void:
	_close(false)

func _close(ok: bool) -> void:
	if current == self:
		current = null
	closed.emit(ok)
	if _cb.is_valid():
		_cb.call(ok)
	queue_free()
