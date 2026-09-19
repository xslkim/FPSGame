extends CanvasLayer
## HUD 雏形(8.2 节):金币 / 左右 HP 条 / 子弹数 / 受击泛红。
## 数据源 PlayerSystem;受击颜色 Phy 黄 / Ice 青 / Poison 绿,2 秒淡出(每 0.1s 减 0.05)。
## 冰/毒的血量结算在 PlayerSystem,这里只放泛红动效。

@onready var _coin_label: Label = $CoinLabel
@onready var _left_panel: VBoxContainer = $LeftPanel
@onready var _right_panel: VBoxContainer = $RightPanel
@onready var _hp_left: ProgressBar = $LeftPanel/HPBar
@onready var _hp_right: ProgressBar = $RightPanel/HPBar
@onready var _bullet_left: Label = $LeftPanel/BulletLabel
@onready var _bullet_right: Label = $RightPanel/BulletLabel
@onready var _flash_left: ColorRect = $HurtFlashLeft
@onready var _flash_right: ColorRect = $HurtFlashRight
@onready var _debug_label: Label = $DebugLabel

var _flash_tweens := {}

func _ready() -> void:
	PlayerSystem.ui_changed.connect(_refresh)
	PlayerSystem.player_hurt.connect(_on_player_hurt)
	_debug_label.visible = GlobalObject.is_debug
	_refresh()

func _refresh() -> void:
	_coin_label.text = "Coin: %d/%d" % [DataMgr.coin, DataMgr.max_coin]
	_refresh_side(PlayerSystem.player_left, _left_panel, _hp_left, _bullet_left)
	_refresh_side(PlayerSystem.player_right, _right_panel, _hp_right, _bullet_right)

## 单人时隐藏另一侧
func _refresh_side(player, panel: VBoxContainer, hp_bar: ProgressBar, bullet_label: Label) -> void:
	var on: bool = player != null and player.active
	panel.visible = on
	if on:
		hp_bar.value = player.hp
		bullet_label.text = "Bullet: %d" % player.bullet

func _on_player_hurt(side: int, attack_type: int) -> void:
	var flash: ColorRect = _flash_left if side == PlayerSystem.Side.Left else _flash_right
	# Phy 黄 / Ice 青 / Poison 绿(重复受击时颜色以最新一次为准)
	var col := Color(1.0, 1.0, 0.0)
	if attack_type == GlobalObject.AttackType.Ice:
		col = Color(0.0, 1.0, 1.0)
	elif attack_type == GlobalObject.AttackType.Poison:
		col = Color(0.0, 1.0, 0.0)
	flash.color = col
	flash.modulate.a = 1.0
	flash.visible = true
	if _flash_tweens.has(side) and _flash_tweens[side].is_valid():
		_flash_tweens[side].kill()
	var tw := create_tween()
	_flash_tweens[side] = tw
	tw.tween_property(flash, "modulate:a", 0.0, 2.0)  # alpha 1→0,即每 0.1s 减 0.05
	tw.tween_callback(flash.hide)
