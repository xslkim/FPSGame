extends CanvasLayer
class_name MessageBox
## 通用弹框,1:1 移植原作 Assets/UI/MessageBox.prefab(800×500 居中):
## 九宫格底 messagebox.png(border 65)、标题条 bg_Title_LargePanel03.png、
## 左取消(46 号)/右确定(50 号)两个 300×100 按钮(Btn_button03_n/h)。
## 按钮名保留 MessageButtonOk/MessageButtonCancel:打开期间枪的射线/扳机
## 只响应 MessageButton*(照原作 UIController.cs);键盘由焦点导航承担
## (打开时默认选中"确定",方向键左右切换,Enter 触发)。
## 用法:MessageBox.show_box(self, "标题", "内容", "确定", "取消", func(ok): ...)

signal closed(ok: bool)

static var current: MessageBox = null

## 枪瞄准/扳机交互时视作可命中对象(menu.gd 遍历此分组)
const GROUP := "gun_button"

var ok_button: TextureButton
var cancel_button: TextureButton

var _cb := Callable()
var _title_label: Label
var _content_label: Label

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

func _center(ctrl: Control, x: float, y: float, w: float, h: float) -> void:
	# Unity 中心锚点(800×500 弹框局部坐标,Y 向上)→ Godot(Y 向下)
	ctrl.set_anchors_preset(Control.PRESET_CENTER)
	ctrl.offset_left = x - w / 2.0
	ctrl.offset_right = x + w / 2.0
	ctrl.offset_top = -y - h / 2.0
	ctrl.offset_bottom = -y + h / 2.0

func _make_label(text: String, font_size: int, color: Color, x: float, y: float, w: float, h: float) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", font_size)
	l.add_theme_color_override("font_color", color)
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	var f: Font = load("res://assets/fonts/btt.ttf")
	if f != null:
		l.add_theme_font_override("font", f)
	return l

func _make_button(node_name: String, x: float, y: float, w: float, h: float) -> TextureButton:
	var b := TextureButton.new()
	b.name = node_name
	b.texture_normal = load("res://assets/textures/ui/Btn_button03_n.png")
	b.texture_hover = load("res://assets/textures/ui/Btn_button03_h.png")
	b.texture_pressed = load("res://assets/textures/ui/Btn_button03_h.png")
	b.texture_focused = load("res://assets/textures/ui/Btn_button03_h.png")
	b.ignore_texture_size = true
	b.stretch_mode = TextureButton.STRETCH_SCALE
	b.focus_mode = Control.FOCUS_ALL
	b.custom_minimum_size = Vector2(w, h)
	b.add_to_group(GROUP)
	return b

func _build(title: String, content: String, ok_text: String, cancel_text: String, cb: Callable) -> void:
	_cb = cb
	layer = 20
	# 1280×720 参考层(同 menu.gd,等效 CanvasScaler 按宽匹配)
	var ref := Control.new()
	ref.name = "Ref1280"
	ref.set_anchors_preset(Control.PRESET_CENTER)
	ref.offset_left = -640
	ref.offset_right = 640
	ref.offset_top = -360
	ref.offset_bottom = 360
	ref.pivot_offset = Vector2(640, 360)
	ref.scale = Vector2(1.5, 1.5)
	add_child(ref)
	# 根:800×500 居中九宫格面板
	var root := NinePatchRect.new()
	root.name = "Panel"
	root.texture = load("res://assets/textures/ui/messagebox.png")
	root.patch_margin_left = 65
	root.patch_margin_top = 65
	root.patch_margin_right = 65
	root.patch_margin_bottom = 65
	root.set_anchors_preset(Control.PRESET_CENTER)
	root.offset_left = -400
	root.offset_right = 400
	root.offset_top = -250
	root.offset_bottom = 250
	ref.add_child(root)
	# 标题条底图:600×50 @(0, 207)
	var bar := TextureRect.new()
	bar.name = "TitleBar"
	bar.texture = load("res://assets/textures/ui/bg_Title_LargePanel03.png")
	bar.stretch_mode = TextureRect.STRETCH_SCALE
	bar.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_center(bar, 0.0, 207.0, 600.0, 50.0)
	root.add_child(bar)
	# 标题:505×102 @(0, 208.07),btt 40(原 BestFit 10~40),#D8EDFF
	_title_label = _make_label(title, 40, Color(0.847, 0.929, 1.0), 0.0, 208.07, 505.0, 102.0)
	_title_label.name = "Title"
	_center(_title_label, 0.0, 208.07, 505.0, 102.0)
	root.add_child(_title_label)
	# 正文:600×160.91 @(0, 77.8),40 号(原作内置 Arial),色 (0.7338,0.8314,0.9151)
	_content_label = _make_label(content, 40, Color(0.7338, 0.8314, 0.9151), 0.0, 77.8, 600.0, 160.91)
	_content_label.name = "Content"
	_content_label.add_theme_font_override("font", UITheme.cjk_font())
	_center(_content_label, 0.0, 77.8, 600.0, 160.91)
	root.add_child(_content_label)
	# 取消(左):300×100 @(-192, -132),"取消" 46 号白
	cancel_button = _make_button("MessageButtonCancel", -192.0, -132.0, 300.0, 100.0)
	_center(cancel_button, -192.0, -132.0, 300.0, 100.0)
	cancel_button.pressed.connect(func(): _close(false))
	root.add_child(cancel_button)
	var cancel_label := _make_label(cancel_text, 46, Color.WHITE, 0, 0, 300, 100)
	cancel_label.set_anchors_preset(Control.PRESET_FULL_RECT)
	cancel_button.add_child(cancel_label)
	# 确定(右):300×100 @(180.62, -132),"确定" 50 号白
	ok_button = _make_button("MessageButtonOk", 180.62, -132.0, 300.0, 100.0)
	_center(ok_button, 180.62, -132.0, 300.0, 100.0)
	ok_button.pressed.connect(func(): _close(true))
	root.add_child(ok_button)
	var ok_label := _make_label(ok_text, 50, Color.WHITE, 0, 0, 300, 100)
	ok_label.set_anchors_preset(Control.PRESET_FULL_RECT)
	ok_button.add_child(ok_label)
	# 焦点导航:取消 ↔ 确定(原作 EventSystem Explicit)
	cancel_button.focus_neighbor_right = cancel_button.get_path_to(ok_button)
	ok_button.focus_neighbor_left = ok_button.get_path_to(cancel_button)
	# 打开即选中"确定"(原作 MessageBox.Show 里 SetSelectedGameObject)
	ok_button.grab_focus.call_deferred()

func press_ok() -> void:
	_close(true)

func press_cancel() -> void:
	_close(false)

func _close(ok: bool) -> void:
	if current == self:
		current = null
	GlobalObject.play_ui_sound()
	closed.emit(ok)
	if _cb.is_valid():
		_cb.call(ok)
	queue_free()
