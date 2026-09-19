extends Node3D
## DeviceConnection(8.1):二维码(占位)+ 说明,UDP 配对,射"返回"回 Menu。
## 进场 InputManager 持续广播(Menu 模式);双路连接状态实时显示(M2 已通)。

const MENU := "res://scenes/ui/menu.tscn"

@export var button_root_path: NodePath = ^"ButtonRoot"

var _status_label: Label3D

func _ready() -> void:
	GlobalObject.scene_state = GlobalObject.GameState.UI
	MessageBox.close_current()
	InputManager.set_input_mode(InputManager.InputMode.Menu)  # Menu 模式持续广播(4.1)
	var root := get_node(button_root_path)
	_add_label(root, "连接手机", 88, Vector3(0, 0.85, 0))
	_add_label(root, "手机与电脑连接同一 Wi-Fi,\n打开手机 App 后自动搜索设备(UDP 8281/8282)。\n连接成功后下方状态变为「已连接」。",
		44, Vector3(0, 0.42, 0))
	# 二维码占位图
	var qr := MeshInstance3D.new()
	qr.name = "QRPlaceholder"
	var q := QuadMesh.new()
	q.size = Vector2(0.5, 0.5)
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.albedo_color = Color(0.9, 0.9, 0.9)
	q.material = mat
	qr.mesh = q
	qr.position = Vector3(0, -0.12, 0)
	root.add_child(qr)
	_add_label(qr, "QR", 96, Vector3(0, 0, 0.01))
	_status_label = _add_label(root, "", 44, Vector3(0, -0.58, 0))
	var back := UIButton3D.create("返回", Vector2(0.7, 0.24), func():
		get_tree().change_scene_to_file(MENU))
	back.name = "BtnBack"
	back.position = Vector3(0, -0.9, 0)
	root.add_child(back)

func _process(_delta: float) -> void:
	if _status_label == null:
		return
	var ring := "已连接(%s)" % InputManager.ring_ip if InputManager.ring_connected else "未连接"
	var leg := "已连接(%s)" % InputManager.leg_ip if InputManager.leg_connected else "未连接"
	_status_label.text = "右手(R): %s    左手(L): %s" % [ring, leg]

func _add_label(parent: Node, text: String, font_size: int, pos: Vector3) -> Label3D:
	var l := Label3D.new()
	l.text = text
	l.font_size = font_size
	l.pixel_size = 0.0022
	l.position = pos
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	UITheme.apply_label3d(l)
	parent.add_child(l)
	return l
