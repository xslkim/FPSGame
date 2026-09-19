extends Node3D
## Menu(8.1):单人游戏/双人合作/连接手机/游戏设置(占位)/退出游戏。
## 模式选择照 4.4 切换点;修原作"弹框外多跳一次 LevelChoose"——只在弹框回调里显式跳转。

const LEVEL_CHOOSE := "res://scenes/ui/level_choose.tscn"
const DEVICE_CONNECTION := "res://scenes/ui/device_connection.tscn"

@export var button_root_path: NodePath = ^"ButtonRoot"

var buttons := {}

func _ready() -> void:
	GlobalObject.scene_state = GlobalObject.GameState.UI
	MessageBox.close_current()
	_build_buttons()

func _build_buttons() -> void:
	var root := get_node(button_root_path)
	_add_button(root, "BtnTitle", "士兵打怪兼", Vector3(0, 0.85, 0), Vector2(1.7, 0.32), Callable())
	_add_button(root, "BtnSingle", "单人游戏", Vector3(0, 0.42, 0), Vector2(0.95, 0.24), on_single_player)
	_add_button(root, "BtnDouble", "双人合作", Vector3(0, 0.12, 0), Vector2(0.95, 0.24), on_two_player)
	_add_button(root, "BtnPhone", "连接手机", Vector3(0, -0.18, 0), Vector2(0.95, 0.24), on_connect_phone)
	_add_button(root, "BtnSettings", "游戏设置", Vector3(0, -0.48, 0), Vector2(0.95, 0.24), on_settings)
	_add_button(root, "BtnQuit", "退出游戏", Vector3(0, -0.78, 0), Vector2(0.95, 0.24), on_quit)
	buttons["BtnTitle"].set_enabled(false)
	buttons["BtnTitle"].set_bg_color(Color(0.1, 0.12, 0.16, 0.0))

func _add_button(root: Node, key: String, text: String, pos: Vector3, size: Vector2, cb: Callable) -> void:
	var b := UIButton3D.create(text, size, cb)
	b.name = key
	b.position = pos
	root.add_child(b)
	buttons[key] = b

## 单人:无设备 → 选遥控器/手机;双设备 → 选持枪手;单设备 → 对应模式(4.4)
func on_single_player() -> void:
	var ring := InputManager.ring_connected
	var leg := InputManager.leg_connected
	if ring and leg:
		MessageBox.show_box(self, "单人游戏", "检测到两台设备,请选择持枪手:", "右手", "左手",
			func(ok): _goto_level_choose(
				InputManager.InputMode.OnlyRight if ok else InputManager.InputMode.OnlyLeft))
	elif ring or leg:
		_goto_level_choose(InputManager.InputMode.OnlyRight if ring else InputManager.InputMode.OnlyLeft)
	else:
		MessageBox.show_box(self, "单人游戏", "未检测到手机设备,请选择控制方式:", "遥控器", "连接手机",
			func(ok):
				if ok:
					_goto_level_choose(InputManager.InputMode.ControllerOrRight)
				else:
					on_connect_phone())

## 双人:双设备未连 → 引导去 DeviceConnection;已连 → RightAndLeft
func on_two_player() -> void:
	if InputManager.ring_connected and InputManager.leg_connected:
		_goto_level_choose(InputManager.InputMode.RightAndLeft)
	else:
		MessageBox.show_box(self, "双人合作",
			"双人游戏需要两部手机分别连接。\n请先在「连接手机」页完成配对。",
			"去连接", "返回", func(ok):
				if ok:
					on_connect_phone())

func on_connect_phone() -> void:
	get_tree().change_scene_to_file(DEVICE_CONNECTION)

func on_settings() -> void:
	MessageBox.show_box(self, "游戏设置", "设置项开发中,敬请期待。", "确定", "返回")

func on_quit() -> void:
	get_tree().quit()

## 选完模式:setup_players 在 set_input_mode 内完成(4.4)
func _goto_level_choose(mode: InputManager.InputMode) -> void:
	InputManager.set_input_mode(mode)
	get_tree().change_scene_to_file(LEVEL_CHOOSE)
