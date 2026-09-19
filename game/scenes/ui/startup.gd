extends Node
## StartUp(8.1):首帧 scene_state=UI → 自动跳 Menu。
## --m5-selftest:挂 M5 自检驱动到 root(跨场景切换,见 scenes/test/m5_selftest.gd)。

const MENU := "res://scenes/ui/menu.tscn"

func _ready() -> void:
	GlobalObject.scene_state = GlobalObject.GameState.UI
	if OS.get_cmdline_user_args().has("--m5-selftest"):
		var runner := Node.new()
		runner.name = "M5SelfTest"
		runner.set_script(preload("res://scenes/test/m5_selftest.gd"))
		get_tree().root.add_child.call_deferred(runner)
		return  # runner 全权驱动后续场景切换
	await get_tree().process_frame
	get_tree().change_scene_to_file(MENU)
