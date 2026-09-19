extends Node
## StartUp(1:1 移植 Unity Assets/UI/StartUp.unity + NewScene.cs):
## 场景只活 1 帧 → 直接 LoadScene("Menu");无 logo/视频/进度条。
## 另含原作中默认隐藏(m_IsActive=0)的调试 Canvas:FPS 数字 Text "100"
## + ReStart 按钮(NumberDisplay.ReStart:数字 100→500,3 秒 EasyOut 缓动)。
## --m5-selftest:挂 M5 自检驱动到 root(跨场景切换,见 scenes/test/m5_selftest.gd)。

const MENU := "res://scenes/ui/menu.tscn"

var _debug_number: Label

func _ready() -> void:
	GlobalObject.scene_state = GlobalObject.GameState.UI
	_build_debug_canvas()
	if OS.get_cmdline_user_args().has("--m5-selftest"):
		var runner := Node.new()
		runner.name = "M5SelfTest"
		runner.set_script(preload("res://scenes/test/m5_selftest.gd"))
		get_tree().root.add_child.call_deferred(runner)
		return  # runner 全权驱动后续场景切换
	await get_tree().process_frame
	get_tree().change_scene_to_file(MENU)

## 调试画布(原作隐藏,原样保留隐藏状态)
func _build_debug_canvas() -> void:
	var layer := CanvasLayer.new()
	layer.name = "DebugCanvas"
	layer.visible = false
	add_child(layer)
	_debug_number = Label.new()
	_debug_number.name = "Text"
	_debug_number.text = "100"
	_debug_number.add_theme_font_size_override("font_size", 60)
	_debug_number.set_anchors_preset(Control.PRESET_CENTER)
	_debug_number.offset_left = -150
	_debug_number.offset_right = 150
	_debug_number.offset_top = -50
	_debug_number.offset_bottom = 50
	layer.add_child(_debug_number)
	var btn := Button.new()
	btn.name = "Button"
	btn.text = "Button"
	btn.add_theme_font_size_override("font_size", 14)
	btn.add_theme_color_override("font_color", Color(0.19607843, 0.19607843, 0.19607843))
	btn.set_anchors_preset(Control.PRESET_CENTER)
	btn.offset_left = 41.4 - 80
	btn.offset_right = 41.4 + 80
	btn.offset_top = -129.2 - 15
	btn.offset_bottom = -129.2 + 15
	btn.pressed.connect(restart)
	layer.add_child(btn)

## NumberDisplay.ReStart():100 → 500,3 秒,EasyOut
func restart() -> void:
	var from := float(_debug_number.text)
	var tw := create_tween().set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
	tw.tween_method(func(v): _debug_number.text = str(roundi(v)), from, 500.0, 3.0)
