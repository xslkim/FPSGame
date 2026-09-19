extends CanvasLayer
class_name InGamePanel
## 关卡内面板(8.1/7.1):暂停 / 续币(弹尽"兑换子弹" + 死亡"继续游戏")/ 胜利。
## 打开一律 Engine.time_scale=0 + GlobalObject.is_game_pause=true。
## 续币:确认扣 1 币 → 对应侧 relife()(HP=100 且 Bullet+=120);币不足按钮显示"币不足";
## 打开 1 秒内禁止"返回主菜单"防误触;续币面板显示回币倒计时 mm:ss。
## 挂进关卡场景,自动连 LevelBase(group "level")的 level_victory/open_continue 信号。

const MENU := "res://scenes/ui/menu.tscn"

var _level: Node = null
var _open_time := -99.0
var _continue_side := PlayerSystem.Side.Right
var _continue_is_bullet := false
var _click_player: AudioStreamPlayer = null
var _win_player: AudioStreamPlayer = null

const UI_CLICK := "res://assets/audio/ui/click.mp3"
const WIN_JINGLE := "res://assets/audio/ui/win.mp3"

@onready var _pause_btn: Button = $PauseButton
@onready var _pause_panel: PanelContainer = $PausePanel
@onready var _continue_panel: PanelContainer = $ContinuePanel
@onready var _victory_panel: PanelContainer = $VictoryPanel
@onready var _continue_title: Label = $ContinuePanel/VBox/TitleLabel
@onready var _continue_info: Label = $ContinuePanel/VBox/InfoLabel
@onready var _continue_coin: Label = $ContinuePanel/VBox/CoinLabel
@onready var _continue_countdown: Label = $ContinuePanel/VBox/CountdownLabel
@onready var _continue_confirm: Button = $ContinuePanel/VBox/ConfirmButton
@onready var _victory_stars: Label = $VictoryPanel/VBox/StarsLabel
@onready var _menu_buttons: Array = [
	$PausePanel/VBox/MenuButton, $ContinuePanel/VBox/MenuButton, $VictoryPanel/VBox/MenuButton]

func _ready() -> void:
	call_deferred("_connect_level")  # 子节点 _ready 先于关卡根节点,延迟一帧等其入组
	_hide_all()
	_pause_btn.visible = true
	_pause_btn.pressed.connect(open_pause)
	$PausePanel/VBox/ResumeButton.pressed.connect(close_all)
	$PausePanel/VBox/MenuButton.pressed.connect(back_to_menu)
	_continue_confirm.pressed.connect(confirm_continue)
	$ContinuePanel/VBox/MenuButton.pressed.connect(back_to_menu)
	$VictoryPanel/VBox/MenuButton.pressed.connect(back_to_menu)
	# UI 音:面板按钮点击(文件缺失静默)
	if ResourceLoader.exists(UI_CLICK):
		_click_player = AudioStreamPlayer.new()
		_click_player.name = "UIClick"
		_click_player.stream = load(UI_CLICK)
		add_child(_click_player)
		for b in [$PauseButton, $PausePanel/VBox/ResumeButton, $PausePanel/VBox/MenuButton,
				$ContinuePanel/VBox/ConfirmButton, $ContinuePanel/VBox/MenuButton,
				$VictoryPanel/VBox/MenuButton]:
			b.pressed.connect(_play_click)
	if ResourceLoader.exists(WIN_JINGLE):
		_win_player = AudioStreamPlayer.new()
		_win_player.name = "WinJingle"
		_win_player.stream = load(WIN_JINGLE)
		add_child(_win_player)

func _play_click() -> void:
	if _click_player != null:
		_click_player.play()

func _connect_level() -> void:
	_level = get_tree().get_first_node_in_group("level")
	if _level != null:
		_level.level_victory.connect(_on_level_victory)
		_level.open_continue.connect(_on_open_continue)

func _process(_delta: float) -> void:
	# 打开 1 秒内禁止返回主菜单(防误触)
	var lock: bool = Time.get_ticks_msec() / 1000.0 - _open_time < 1.0
	for b in _menu_buttons:
		b.disabled = lock
	if _continue_panel.visible:
		var t := DataMgr.time_to_next_coin()
		_continue_coin.text = "游戏币: %d/%d" % [DataMgr.coin, DataMgr.max_coin]
		_continue_countdown.text = "回币倒计时 %02d:%02d" % [int(t) / 60, int(t) % 60]
		if DataMgr.coin < 1:
			_continue_confirm.text = "币不足"
			_continue_confirm.disabled = true
		else:
			_continue_confirm.text = "确定(消耗 1 币)"
			_continue_confirm.disabled = false

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo \
			and event.keycode == KEY_ESCAPE:
		if _victory_panel.visible:
			return
		if _any_open():
			close_all()
		else:
			open_pause()

func _any_open() -> bool:
	return _pause_panel.visible or _continue_panel.visible or _victory_panel.visible

func open_pause() -> void:
	_open_panel(_pause_panel)

func _on_open_continue(is_open: bool, side: int) -> void:
	open_continue(is_open, side)

## 续币:is_open=true 弹尽("兑换子弹")/ false 死亡("继续游戏")
func open_continue(is_open: bool, side: int) -> void:
	_continue_is_bullet = is_open
	_continue_side = side
	_continue_title.text = "兑换子弹" if is_open else "继续游戏"
	_continue_info.text = ("子弹耗尽,消耗 1 枚游戏币兑换子弹?" if is_open
		else "玩家倒下,消耗 1 枚游戏币继续战斗?")
	_open_panel(_continue_panel)

func _on_level_victory() -> void:
	var stars := ""
	for i in 3:
		stars += "*" if i < int(_level.last_stars) else "-"
	_victory_stars.text = "星级: %s (%d/3)" % [stars, int(_level.last_stars)]
	_open_panel(_victory_panel)
	if _win_player != null:
		_win_player.play()

func _open_panel(p: PanelContainer) -> void:
	_hide_all()
	p.visible = true
	_open_time = Time.get_ticks_msec() / 1000.0
	Engine.time_scale = 0.0
	GlobalObject.is_game_pause = true
	_pause_btn.visible = false

func close_all() -> void:
	_hide_all()
	Engine.time_scale = 1.0
	GlobalObject.is_game_pause = false
	_pause_btn.visible = true

func _hide_all() -> void:
	_pause_panel.visible = false
	_continue_panel.visible = false
	_victory_panel.visible = false

## 续币确认:扣 1 币 → 对应侧 relife → 关面板
func confirm_continue() -> void:
	if DataMgr.coin < 1:
		return
	if DataMgr.spend_coin(1):
		PlayerSystem.get_player(_continue_side).relife()
		print("[Panel] continue: side=%d relife (coin=%d)" % [_continue_side, DataMgr.coin])
		close_all()

func back_to_menu() -> void:
	if Time.get_ticks_msec() / 1000.0 - _open_time < 1.0:
		return  # 防误触
	Engine.time_scale = 1.0
	GlobalObject.is_game_pause = false
	GlobalObject.scene_state = GlobalObject.GameState.UI
	get_tree().change_scene_to_file(MENU)
