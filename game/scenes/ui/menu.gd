extends Node
## Menu:1:1 移植 Unity Assets/UI/Menu.unity + MenuController.cs。
## 3D(SubViewport 透明叠加在背景 UI 之上、按钮之下,复现原作 WorldSpace Canvas
##   z=623.2 的深度序):相机 FOV60、平行光强度3、RockWarrior(×100,z=450,yaw190°)
##   循环 Idle02、相机下挂 M4+激光+枪口火光。
## UI:1280×720 参考(与 Unity CanvasScaler 等效,经 canvas_items 拉伸适配)。
##   背景 background4、科幻圆环组(±20°/s 反转)、标题"士兵打怪兵"、
##   4 个 SpriteSwap 主按钮(单人/双人/手机/退出,设置隐藏)、金币 HUD(PlayerSystem)。
## 交互:鼠标悬停/点击、方向键焦点导航(默认选中单人游戏)、手机体感枪瞄准+扳机。

const LEVEL_CHOOSE := "res://scenes/ui/level_choose.tscn"
const DEVICE_CONNECTION := "res://scenes/ui/device_connection.tscn"

const TEX := "res://assets/textures/ui/"
const TEXM := "res://assets/textures/ui/menu/"
const FONT_BTT := "res://assets/fonts/btt.ttf"
const CONFIG_URL := "http://pc.oceanfitness.xyz/data/FPSGameConfig.json"

var _btn_one: TextureButton
var _btn_two: TextureButton
var _btn_device: TextureButton
var _btn_setting: TextureButton
var _btn_exit: TextureButton

const VP := "ViewportLayer/SubViewportContainer/SubViewport/"

@onready var _subvp: SubViewport = $ViewportLayer/SubViewportContainer/SubViewport
@onready var _camera: Camera3D = get_node(VP + "Camera3D")
@onready var _gun: Node3D = get_node(VP + "Camera3D/M4View")
@onready var _lazer: MeshInstance3D = get_node(VP + "Camera3D/M4View/Lazer")
@onready var _muzzle: Node3D = get_node(VP + "Camera3D/M4View/MuzzleFlash")
@onready var _monster: Node3D = get_node(VP + "RockWarrior")
@onready var _ui_root: Control
var _button_root: Control

func _ready() -> void:
	GlobalObject.scene_state = GlobalObject.GameState.UI
	GlobalObject.play_menu_music()
	InputManager.set_input_mode(InputManager.InputMode.Menu)
	PlayerSystem.update_ui_mode("Menu")
	_sync_viewport_size()
	get_viewport().size_changed.connect(_sync_viewport_size)
	_build_ui()
	# 怪物 idle 循环(原作 MainnenuController.controller 唯一状态)
	var ap: AnimationPlayer = _monster.get_node("AnimationPlayer")
	var anim := ap.get_animation(&"Idle02")
	if anim != null:
		anim.loop_mode = Animation.LOOP_LINEAR
		ap.play(&"Idle02")
	# 怪物材质(原作 monster_086.mat)
	var mat := load("res://assets/models/monsters/rock_warrior/rock_warrior_mat.tres")
	for mi in _monster.get_node("Model").find_children("*", "MeshInstance3D", true, false):
		mi.material_override = mat
	# 默认选中"单人游戏"(原作 MenuController.Start)
	_btn_one.grab_focus.call_deferred()
	_get_config()
	InputManager.key1_right.connect(_on_gun_trigger)
	if OS.get_cmdline_user_args().has("--menu-selftest"):
		_menu_selftest.call_deferred()
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shot:"):
			_take_shot.call_deferred(a.trim_prefix("--shot:"))
		elif a.begins_with("--shot-box:"):
			# 打开单人弹框后再截屏(MessageBox 视觉验证)
			one_player()
			_take_shot.call_deferred(a.trim_prefix("--shot-box:"))

## 截图验证:--shot:<path>,约 30 帧后截屏退出(含圆环旋转/怪物动画帧)
func _take_shot(path: String) -> void:
	for i in range(30):
		await get_tree().process_frame
	await RenderingServer.frame_post_draw
	var img := get_viewport().get_texture().get_image()
	img.save_png(path)
	print("SHOT SAVED: ", path)
	get_tree().quit()

# ---------------------------------------------------------------- UI 构建

## Unity 中心锚点(尺寸 sizeDelta,Y 向上)+ 可选缩放 → Godot 中心锚点(Y 向下)
func _place(c: Control, x: float, y_up: float, w: float, h: float, sx := 1.0, sy := 1.0) -> void:
	c.set_anchors_preset(Control.PRESET_CENTER)
	c.offset_left = x - w / 2.0
	c.offset_right = x + w / 2.0
	c.offset_top = -y_up - h / 2.0
	c.offset_bottom = -y_up + h / 2.0
	c.pivot_offset = Vector2(w / 2.0, h / 2.0)
	c.scale = Vector2(sx, sy)

## 1280×720 参考层:居中 ×1.5 铺满 1920×1080(等效 Unity CanvasScaler 按宽匹配;
## 窗口 1280×720 时 canvas_items 缩回 1.0,显示与原作一致)
func _make_ref1280(parent: Node) -> Control:
	var outer := Control.new()
	outer.name = "Root"
	outer.set_anchors_preset(Control.PRESET_FULL_RECT)
	outer.mouse_filter = Control.MOUSE_FILTER_IGNORE
	parent.add_child(outer)
	var ref := Control.new()
	ref.name = "Ref1280"
	ref.set_anchors_preset(Control.PRESET_CENTER)
	ref.offset_left = -640
	ref.offset_right = 640
	ref.offset_top = -360
	ref.offset_bottom = 360
	ref.pivot_offset = Vector2(640, 360)
	ref.scale = Vector2(1.5, 1.5)
	outer.add_child(ref)
	return ref

func _tex_rect(name: String, tex_path: String) -> TextureRect:
	var t := TextureRect.new()
	t.name = name
	t.texture = load(tex_path)
	t.stretch_mode = TextureRect.STRETCH_SCALE
	t.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	t.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return t

func _label(text: String, font_size: int, color: Color, use_btt := true) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", font_size)
	l.add_theme_color_override("font_color", color)
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	if use_btt:
		l.add_theme_font_override("font", load(FONT_BTT))
	return l

func _sync_viewport_size() -> void:
	_subvp.size = get_viewport().get_visible_rect().size

func _build_ui() -> void:
	# 原作 Canvas 是 WorldSpace(z=623.2):怪物(z=450)挡在背景图之前、按钮之下。
	# Godot 分层复现:BGLayer(-1)<3D(SubViewport)<UILayer(1)<HUD(10)<MessageBox(20)。
	_ui_root = _make_ref1280($BGLayer)
	_button_root = _make_ref1280($UILayer)

	# BG:1920×1080 scale 0.6667 居中(等效铺满 1280×720)
	var bg := _tex_rect("BG", TEX + "background4.png")
	_place(bg, 0, 0, 1920, 1080, 0.6666667, 0.6666667)
	_ui_root.add_child(bg)

	# 科幻圆环组:@(-507,225) 915×455 scale 0.96586,底层星图
	var circle := _tex_rect("SciFiLargeCircle", TEXM + "scifi_circle_stars.png")
	_place(circle, -507, 225, 915, 455, 0.96586007, 0.96586007)
	bg.add_child(circle)
	# 四层圆盘:584×584 @(-32.096,30.025) scale 1.0353467;1/2 层 ±20°/s 互反转
	for i in range(1, 5):
		var ring: TextureRect
		if i == 1 or i == 2:
			ring = JustRotate.new()
			ring.speed = 20.0 if i == 1 else -20.0
			ring.texture = load(TEXM + "scifi_circle_%d.png" % i)
			ring.stretch_mode = TextureRect.STRETCH_SCALE
			ring.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
			ring.mouse_filter = Control.MOUSE_FILTER_IGNORE
			ring.name = "Ring%d" % i
		else:
			ring = _tex_rect("Ring%d" % i, TEXM + "scifi_circle_%d.png" % i)
		_place(ring, -32.09576, 30.025047, 584, 584, 1.0353467, 1.0353467)
		circle.add_child(ring)
	# 圆环下弧线:441×137 @(-9.318,-199.822) scale 1.0353467
	var line := _tex_rect("CircleLine", TEXM + "scifi_circle_line.png")
	_place(line, -9.318, -199.822, 441, 137, 1.0353467, 1.0353467)
	circle.add_child(line)

	# 隐藏装饰图(原作 BG>Image,bg_Title.png,默认 inactive)
	var deco := _tex_rect("Image", TEXM + "bg_Title.png")
	_place(deco, -513, 223, 960, 519)
	deco.visible = false
	bg.add_child(deco)

	# 标题 Title2:"士兵打怪兵" btt 80 号,色 (0.8676,0.9393,1)
	var title := _label("士兵打怪兵", 80, Color(0.8676, 0.9393, 1.0))
	title.name = "Title2"
	_place(title, -543, 264, 521, 109, 1.2041858, 0.9712007)
	bg.add_child(title)
	# 标题上划线 bg_Title2 / 下划线 bg_Title1(子级,scale 0.8304366/1.0296533)
	var under_top := _tex_rect("Image1", TEXM + "bg_Title2.png")
	_place(under_top, -0.83044434, 53.850906, 698, 24, 0.8304366, 1.0296533)
	title.add_child(under_top)
	var under_bottom := _tex_rect("Image2", TEXM + "bg_Title1.png")
	_place(under_bottom, -0.83044434, -53.541946, 664, 36, 0.8304366, 1.0296533)
	title.add_child(under_bottom)

	# 主按钮:505×102,常态 Btn_MainMenu_n,悬停/选中 _h
	_btn_one = _main_button("Btn_OnePlayer", "单人游戏", 620, 270, -103, TEX + "Btn_MainMenu_h.png")
	_btn_two = _main_button("Btn_TwoPlayer", "双人合作", 582, 166, -92, TEX + "Btn_MainMenu_h.png")
	_btn_device = _main_button("Btn_Device", "连接手机", 545, 59, -82, TEX + "Btn_button03_h.png")
	_btn_setting = _main_button("Btn_Setting", "游戏设置", 511, -54, -75, TEX + "Btn_button03_h.png")
	_btn_exit = _main_button("Btn_Exit", "退出游戏", 620, -315, -97, TEX + "Btn_button03_h.png")
	_btn_setting.visible = false  # 原作 inactive

	_btn_one.pressed.connect(one_player)
	_btn_two.pressed.connect(two_player)
	_btn_device.pressed.connect(device_connect)
	_btn_exit.pressed.connect(exit_game)
	# 原作:除退出外每个按钮 onClick 还绑 Utils.PlayMenuSound()
	for b in [_btn_one, _btn_two, _btn_device, _btn_setting]:
		b.pressed.connect(GlobalObject.play_ui_sound)

	# 焦点导航(原作 EventSystem Explicit)
	_link_focus(_btn_one, null, _btn_two)
	_link_focus(_btn_two, _btn_one, _btn_device)
	_link_focus(_btn_device, _btn_two, _btn_exit)
	_link_focus(_btn_exit, _btn_device, null)

func _main_button(node_name: String, text: String, x: float, y_up: float,
		text_dx: float, pressed_tex: String) -> TextureButton:
	var b := TextureButton.new()
	b.name = node_name
	b.texture_normal = load(TEX + "Btn_MainMenu_n.png")
	b.texture_hover = load(TEX + "Btn_MainMenu_h.png")
	b.texture_focused = load(TEX + "Btn_MainMenu_h.png")
	b.texture_pressed = load(pressed_tex)
	b.ignore_texture_size = true
	b.stretch_mode = TextureButton.STRETCH_SCALE
	b.focus_mode = Control.FOCUS_ALL
	b.add_to_group(MessageBox.GROUP)
	_place(b, x, y_up, 505, 102)
	_button_root.add_child(b)
	# 文字:填满按钮整体左移 text_dx,btt 40(原 BestFit 10~40),#D8EDFF
	var l := _label(text, 40, Color(0.847, 0.929, 1.0))
	l.set_anchors_preset(Control.PRESET_FULL_RECT)
	l.offset_left = text_dx
	l.offset_right = text_dx
	b.add_child(l)
	return b

func _link_focus(b: TextureButton, up: TextureButton, down: TextureButton) -> void:
	if up != null:
		b.focus_neighbor_top = b.get_path_to(up)
	if down != null:
		b.focus_neighbor_bottom = b.get_path_to(down)

# ---------------------------------------------------------------- 按钮行为(MenuController.cs)

func one_player() -> void:
	var ring := _ring_connected()
	var leg := _leg_connected()
	if not ring and not leg:
		_monster.hide()
		MessageBox.show_box(self, "选择控制方式", "可以选择用手机控制玩游戏哟！", "手机", "遥控器",
			func(phone):
				if phone:
					get_tree().change_scene_to_file(DEVICE_CONNECTION)
				else:
					InputManager.set_input_mode(InputManager.InputMode.ControllerOrRight)
					get_tree().change_scene_to_file(LEVEL_CHOOSE)
				_monster.show())
	elif ring and leg:
		MessageBox.show_box(self, "选择持枪方式", "单人游戏只能连接一个手机哟", "右手持枪", "左手持枪",
			func(right):
				InputManager.set_input_mode(
					InputManager.InputMode.OnlyRight if right else InputManager.InputMode.OnlyLeft)
				get_tree().change_scene_to_file(LEVEL_CHOOSE)
				_monster.show())
		# 原作 bug 保真:MenuController.cs:50 弹框外多跳一次 LevelChoose
		get_tree().change_scene_to_file(LEVEL_CHOOSE)
	elif ring:
		InputManager.set_input_mode(InputManager.InputMode.OnlyRight)
		get_tree().change_scene_to_file(LEVEL_CHOOSE)
	else:
		InputManager.set_input_mode(InputManager.InputMode.OnlyLeft)
		get_tree().change_scene_to_file(LEVEL_CHOOSE)

func two_player() -> void:
	if _ring_connected() and _leg_connected():
		InputManager.set_input_mode(InputManager.InputMode.RightAndLeft)
		get_tree().change_scene_to_file(LEVEL_CHOOSE)
	else:
		_monster.hide()
		MessageBox.show_box(self, "连接手机", "需要用手机控制才能双人游戏", "确定", "取消",
			func(confirm):
				if confirm:
					get_tree().change_scene_to_file(DEVICE_CONNECTION)
				_monster.show()
				_btn_two.grab_focus())

func device_connect() -> void:
	get_tree().change_scene_to_file(DEVICE_CONNECTION)

func exit_game() -> void:
	get_tree().quit()

## 原作 InputManager.isRingConnected/isLegConnected(物理连接;键盘回落模式视作右手已连)
func _ring_connected() -> bool:
	return InputManager.ring_connected or InputManager.ring_ip != ""

func _leg_connected() -> bool:
	return InputManager.leg_connected or InputManager.leg_ip != ""

# ---------------------------------------------------------------- 远程配置(离线容错)

func _get_config() -> void:
	var req := HTTPRequest.new()
	req.timeout = 5.0
	add_child(req)
	req.request_completed.connect(func(_r, code, _h, body):
		if code == 200:
			var j: Variant = JSON.parse_string(body.get_string_from_utf8())
			if j is Dictionary:
				DataMgr.remote_config = j
		req.queue_free())
	req.request(CONFIG_URL)

# ---------------------------------------------------------------- 体感枪瞄准 + 扳机

func _gun_active() -> bool:
	return _ring_connected()

func _process(_delta: float) -> void:
	var active := _gun_active()
	_lazer.visible = active
	if active:
		var q := GlobalObject.get_ring_rotation()
		_gun.quaternion = q
	elif _gun.quaternion != Quaternion.IDENTITY:
		_gun.quaternion = Quaternion.IDENTITY

func _on_gun_trigger() -> void:
	if not _gun_active():
		return
	_flash_muzzle()
	var b := _button_at_aim()
	if b != null:
		b.pressed.emit()

## 枪口指向 → 视口坐标 → 命中的按钮(MessageBox 打开时只认 MessageButton*)
func _button_at_aim() -> TextureButton:
	var dir: Vector3 = _camera.global_basis * (GlobalObject.get_ring_rotation() * Vector3.FORWARD)
	var point := _camera.unproject_position(_camera.global_position + dir * 100.0)
	var vp := get_viewport().get_visible_rect().size
	var ref := point * Vector2(1920.0 / vp.x, 1080.0 / vp.y)
	var box_open := MessageBox.is_open()
	for n in get_tree().get_nodes_in_group(MessageBox.GROUP):
		if n is TextureButton and n.visible and n.get_global_rect().has_point(ref):
			if box_open and not String(n.name).begins_with("MessageButton"):
				continue
			return n
	return null

## 枪口火光:FPSLightCurves 0.15s(峰 0.979),贴图随机 Z 转角(FPSRandomRotateAngle)
func _flash_muzzle() -> void:
	var flash: Sprite3D = _muzzle.get_node("Flash")
	var light: OmniLight3D = _muzzle.get_node("Light")
	flash.rotation.z = randf() * TAU
	_muzzle.show()
	light.light_energy = 2.0 * 0.979
	var tw := create_tween()
	tw.tween_property(light, "light_energy", 0.0, 0.145)
	await get_tree().create_timer(0.15).timeout
	_muzzle.hide()

# ---------------------------------------------------------------- 自检(--menu-selftest)

func _menu_selftest() -> void:
	var fails := 0
	var check := func(ok: bool, what: String):
		print(("PASS " if ok else "FAIL ") + what)
		if not ok: fails += 1
	await get_tree().process_frame
	await get_tree().process_frame
	check.call(get_viewport().gui_get_focus_owner() == _btn_one, "默认选中单人游戏")
	check.call(_btn_one.size == Vector2(505, 102), "按钮尺寸 505x102")
	check.call(PlayerSystem._coin_obj.visible, "菜单模式金币可见")
	var ring1 = find_child("Ring1", true, false)
	var r0: float = ring1.rotation
	await get_tree().process_frame
	check.call(not is_equal_approx(ring1.rotation, r0), "圆环在旋转")
	check.call(_monster.get_node("AnimationPlayer").is_playing(), "怪物 idle 播放中")
	# 焦点导航链
	for path in [["ui_down", "Btn_TwoPlayer"], ["ui_down", "Btn_Device"], ["ui_down", "Btn_Exit"], ["ui_up", "Btn_Device"]]:
		var ev := InputEventAction.new()
		ev.action = path[0]
		ev.pressed = true
		Input.parse_input_event(ev)
		await get_tree().process_frame
		var owner := get_viewport().gui_get_focus_owner()
		check.call(owner != null and owner.name == path[1], "导航 %s → %s" % [path[0], path[1]])
	# 双人弹框(无设备)
	two_player()
	await get_tree().process_frame
	check.call(MessageBox.is_open(), "双人弹框打开")
	check.call(not _monster.visible, "弹框时怪物隐藏")
	if MessageBox.is_open():
		check.call(MessageBox.current._title_label.text == "连接手机", "双人弹框标题")
		check.call(MessageBox.current._content_label.text == "需要用手机控制才能双人游戏", "双人弹框正文")
		check.call(MessageBox.current.ok_button.get_child(0).text == "确定", "双人弹框确定")
		MessageBox.current.press_cancel()
		await get_tree().process_frame
		check.call(_monster.visible, "取消后怪物恢复")
		check.call(get_viewport().gui_get_focus_owner() == _btn_two, "取消后回选双人按钮")
	# 单人弹框(无设备)
	one_player()
	await get_tree().process_frame
	check.call(MessageBox.is_open(), "单人弹框打开")
	if MessageBox.is_open():
		check.call(MessageBox.current._title_label.text == "选择控制方式", "单人弹框标题")
		check.call(MessageBox.current._content_label.text == "可以选择用手机控制玩游戏哟！", "单人弹框正文")
		check.call(MessageBox.current.ok_button.get_child(0).text == "手机", "单人弹框确定=手机")
		check.call(MessageBox.current.cancel_button.get_child(0).text == "遥控器", "单人弹框取消=遥控器")
		MessageBox.current.press_cancel()  # 遥控器 → ControllerOrRight → LevelChoose
		check.call(InputManager.input_mode == InputManager.InputMode.ControllerOrRight, "遥控器→ControllerOrRight")
	print("MENU SELFTEST %s (fails=%d)" % ["PASS" if fails == 0 else "FAIL", fails])
	# 回调里已跳场景,本节点可能已析构,走 main_loop 退出
	(Engine.get_main_loop() as SceneTree).quit(1 if fails > 0 else 0)
