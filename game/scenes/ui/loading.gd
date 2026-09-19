extends CanvasLayer
## Loading(8.1):最少 4 秒 + 随机 Tips + 真实进度条(修原作只显示 100% 的 bug)。
## 激活瞬间:scene_state=Battle、cur_alive_monster=0、重置玩家。

const MIN_TIME := 4.0

var _path := ""
var _elapsed := 0.0
var _done := false

@onready var _bar: ProgressBar = $Center/ProgressBar
@onready var _tip_label: Label = $Center/TipLabel
@onready var _info_label: Label = $Center/InfoLabel

func _ready() -> void:
	_path = GlobalObject.next_scene_path
	if _path.is_empty():
		_path = "res://scenes/levels/level1_battle.tscn"
	var tips := DataMgr.get_tips()
	if not tips.is_empty():
		_tip_label.text = "Tips: " + str(tips[randi() % tips.size()])
	_info_label.text = "Loading: " + _path
	var err := ResourceLoader.load_threaded_request(_path)
	if err != OK:
		push_error("[Loading] threaded request failed: %s (err %d)" % [_path, err])

func _process(delta: float) -> void:
	if _done:
		return
	_elapsed += delta
	var progress := [0.0]
	var status := ResourceLoader.load_threaded_get_status(_path, progress)
	# 真实进度:加载进度与最小耗时取小(修原作恒 100%)
	_bar.value = 100.0 * minf(progress[0], _elapsed / MIN_TIME)
	if status == ResourceLoader.THREAD_LOAD_LOADED and _elapsed >= MIN_TIME:
		_done = true
		var packed: PackedScene = ResourceLoader.load_threaded_get(_path)
		if packed == null:
			push_error("[Loading] threaded get failed: " + _path)
			return
		# 激活瞬间重置战斗状态(8.1)
		GlobalObject.scene_state = GlobalObject.GameState.Battle
		PlayerSystem.cur_alive_monster = 0
		PlayerSystem.setup_players(InputManager.input_mode)
		get_tree().change_scene_to_packed(packed)
