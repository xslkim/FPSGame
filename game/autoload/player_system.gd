extends Node
## PlayerSystem:左右双玩家状态 / 受击结算 / 存活怪物计数。
## 对应原作 PlayerSystem.cs(5.5 换枪、6.4 受击状态、8.2 HUD 数据源)。

signal player_hurt(side: int, attack_type: int)
signal player_died(side: int)
signal ui_changed

enum Side { Left, Right, Both }

static var cur_alive_monster := 0

## 受击音效(按侧随机,文件缺失静默):右手 Male / 左手 Female
const HURT_SOUNDS := {
	Side.Right: [
		"res://assets/audio/player/Male_Hurt_01_Edwyn.ogg",
		"res://assets/audio/player/Male_Hurt_02_Edwyn.ogg",
		"res://assets/audio/player/Male_Hurt_03_Edwyn.ogg",
		"res://assets/audio/player/Male_Hurt_04_Edwyn.ogg",
		"res://assets/audio/player/Male_Hurt_05_Edwyn.ogg",
		"res://assets/audio/player/Male_Hurt_06_Edwyn.ogg",
	],
	Side.Left: [
		"res://assets/audio/player/Female_Hurt_03_Rina.ogg",
		"res://assets/audio/player/Female_Hurt_04_Rina.ogg",
	],
}

var player_left: Player
var player_right: Player
var _hurt_streams := {}   # side -> Array[AudioStream]
var _hurt_players := {}   # side -> AudioStreamPlayer
var _freeze_player: AudioStreamPlayer = null   # 冰冻循环(KriptoFX FreezeLoop)

const FREEZE_LOOP := "res://assets/audio/effects/freeze_loop.wav"

func _ready() -> void:
	player_left = Player.new()
	player_right = Player.new()
	for side in [Side.Right, Side.Left]:
		var streams: Array[AudioStream] = []
		for path in HURT_SOUNDS[side]:
			if ResourceLoader.exists(path):
				streams.append(load(path))
		_hurt_streams[side] = streams
		var asp := AudioStreamPlayer.new()
		asp.name = "HurtSoundRight" if side == Side.Right else "HurtSoundLeft"
		add_child(asp)
		_hurt_players[side] = asp
	if ResourceLoader.exists(FREEZE_LOOP):
		_freeze_player = AudioStreamPlayer.new()
		_freeze_player.name = "FreezeLoop"
		_freeze_player.stream = load(FREEZE_LOOP)
		add_child(_freeze_player)
	_build_hud()
	ui_changed.connect(refresh_hud)

## 常驻 HUD(对应原作 GlobalObject 下 PlayerSystem.prefab,跨场景 DontDestroyOnLoad)。
## 当前仅移植 CoinObj(菜单/选关/战斗显示);弹药/头像/血条仍在 ui/hud.tscn。
var _hud_layer: CanvasLayer = null
var _coin_obj: Control = null
var _coin_text: Label = null

func _build_hud() -> void:
	_hud_layer = CanvasLayer.new()
	_hud_layer.name = "HUD"
	_hud_layer.layer = 10
	add_child(_hud_layer)
	# 1280×720 参考层(同 menu,等效 CanvasScaler)
	var ref := Control.new()
	ref.name = "Ref1280"
	ref.set_anchors_preset(Control.PRESET_CENTER)
	ref.offset_left = -640
	ref.offset_right = 640
	ref.offset_top = -360
	ref.offset_bottom = 360
	ref.pivot_offset = Vector2(640, 360)
	ref.scale = Vector2(1.5, 1.5)
	ref.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_hud_layer.add_child(ref)
	# CoinObj:anchor 底中,pivot 底中,pos (-26.02, 0),64×64(原作 PlayerSystem.prefab)
	_coin_obj = Control.new()
	_coin_obj.name = "CoinObj"
	_coin_obj.set_anchors_preset(Control.PRESET_CENTER_BOTTOM)
	_coin_obj.offset_left = -26.02 - 32.0
	_coin_obj.offset_right = -26.02 + 32.0
	_coin_obj.offset_top = -64.0
	_coin_obj.offset_bottom = 0.0
	_coin_obj.mouse_filter = Control.MOUSE_FILTER_IGNORE
	ref.add_child(_coin_obj)
	var icon := TextureRect.new()
	icon.name = "CoinIcon"
	icon.texture = load("res://assets/textures/ui/coin.png")
	icon.set_anchors_preset(Control.PRESET_FULL_RECT)
	icon.stretch_mode = TextureRect.STRETCH_SCALE
	icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_coin_obj.add_child(icon)
	# "X":60×60,中心相对金币中心 (+49.31, +5.32),字号 32,暗金黄
	var x_label := Label.new()
	x_label.name = "X"
	x_label.text = "X"
	x_label.add_theme_font_size_override("font_size", 32)
	x_label.add_theme_color_override("font_color", Color(0.5188679, 0.4779218, 0.0))
	x_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	x_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	x_label.set_anchors_preset(Control.PRESET_CENTER)
	x_label.offset_left = 32.0 + 49.31 - 30.0
	x_label.offset_right = 32.0 + 49.31 + 30.0
	x_label.offset_top = 32.0 + 5.32 - 30.0
	x_label.offset_bottom = 32.0 + 5.32 + 30.0
	_coin_obj.add_child(x_label)
	# CoinText:60×60,中心 (+81.1, +4.15),字号 52 白,显示金币数
	_coin_text = Label.new()
	_coin_text.name = "CoinText"
	_coin_text.add_theme_font_size_override("font_size", 52)
	_coin_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_coin_text.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_coin_text.set_anchors_preset(Control.PRESET_CENTER)
	_coin_text.offset_left = 32.0 + 81.1 - 30.0
	_coin_text.offset_right = 32.0 + 81.1 + 30.0
	_coin_text.offset_top = 32.0 + 4.15 - 30.0
	_coin_text.offset_bottom = 32.0 + 4.15 + 30.0
	_coin_obj.add_child(_coin_text)
	_coin_obj.visible = false
	refresh_hud()

func refresh_hud() -> void:
	if _coin_text != null:
		_coin_text.text = str(DataMgr.coin)

## 对应原作 PlayerSystem.UpdateUIMode():按场景切 HUD 元素与 SceneState。
## Menu/LevelChoose=只显示金币;DeviceConnection/LoadingScene=全隐藏;其余=战斗。
func update_ui_mode(scene_name: String) -> void:
	if scene_name == "StartUp":
		return
	if _coin_obj == null:
		return
	if scene_name in ["Menu", "LevelChoose"]:
		_coin_obj.visible = true
		GlobalObject.scene_state = GlobalObject.GameState.UI
	elif scene_name in ["DeviceConnection", "LoadingScene"]:
		_coin_obj.visible = false
		GlobalObject.scene_state = GlobalObject.GameState.UI
	else:
		_coin_obj.visible = true
		GlobalObject.scene_state = GlobalObject.GameState.Battle
	refresh_hud()

func get_player(side: int) -> Player:
	return player_right if side == Side.Right else player_left

## 按输入模式创建/停用左右玩家(4.4 节)
func setup_players(mode: int) -> void:
	var right_on: bool = mode in [
		InputManager.InputMode.OnlyRight,
		InputManager.InputMode.ControllerOrRight,
		InputManager.InputMode.RightAndLeft,
	]
	var left_on: bool = mode in [
		InputManager.InputMode.OnlyLeft,
		InputManager.InputMode.RightAndLeft,
	]
	if right_on:
		player_right.born()
	else:
		player_right.active = false
	if left_on:
		player_left.born()
	else:
		player_left.active = false
	ui_changed.emit()

## 怪物攻击结算。side=Both 双打;目标侧不活跃则转嫁另一侧(6.1 EventAttack 规则)
func hit_player(monster_attack: float, attack_type: int, side: int) -> void:
	if side == Side.Both:
		_apply_hit(player_right, monster_attack, attack_type, Side.Right)
		_apply_hit(player_left, monster_attack, attack_type, Side.Left)
		return
	var p := get_player(side)
	var real_side := side
	if p == null or not p.active:
		real_side = Side.Left if side == Side.Right else Side.Right
		p = get_player(real_side)
		if p == null or not p.active:
			return
	_apply_hit(p, monster_attack, attack_type, real_side)

## 6.4:立即扣血 + 泛红(HUD 做);Ice 置 Frozen 2 秒;Poison 2 秒后再扣一次等额血
func _apply_hit(p: Player, monster_attack: float, attack_type: int, side: int) -> void:
	if not p.active:
		return
	p.hp -= monster_attack
	p.status_seq += 1
	var seq: int = p.status_seq
	player_hurt.emit(side, attack_type)
	_play_hurt_sound(side)
	if attack_type == GlobalObject.AttackType.Ice:
		p.hurt_state = Player.HurtState.Frozen
		if _freeze_player != null:
			_freeze_player.play()
		get_tree().create_timer(2.0).timeout.connect(func():
			if p.status_seq == seq and p.hurt_state == Player.HurtState.Frozen:
				p.hurt_state = Player.HurtState.Normal
				if _freeze_player != null:
					_freeze_player.stop()
				ui_changed.emit())
	elif attack_type == GlobalObject.AttackType.Poison:
		get_tree().create_timer(2.0).timeout.connect(func():
			if p.active and p.hp > 0.0:
				p.hp -= monster_attack
				ui_changed.emit()
				_check_dead(p, side))
	ui_changed.emit()
	# CheckDead 延迟 0.5s 检查
	get_tree().create_timer(0.5).timeout.connect(func(): _check_dead(p, side))

func _check_dead(p: Player, side: int) -> void:
	if p.hp <= 0.0:
		p.hp = 0.0
		ui_changed.emit()
		player_died.emit(side)

## 按侧随机播放受击音效;该侧无可用音频文件时静默
func _play_hurt_sound(side: int) -> void:
	var streams: Array = _hurt_streams.get(side, [])
	if streams.is_empty():
		return
	var asp: AudioStreamPlayer = _hurt_players[side]
	asp.stream = streams[randi() % streams.size()]
	asp.play()

func notify_ui_changed() -> void:
	ui_changed.emit()

## 玩家数据(5.5 换枪 / 8.3 经济)
class Player extends RefCounted:
	enum HurtState { Normal, Frozen, Poison }

	const MAX_HP := 100.0

	var hp := MAX_HP
	var bullet := 0
	var guns := 0        # 已解锁枪位掩码 1 << gun_type
	var gun_type := 2    # 当前枪,起始手枪 HandGun
	var hurt_state := HurtState.Normal
	var active := false
	var status_seq := 0  # 受击序号,防止过期冰冻定时器误清状态

	func born() -> void:
		hp = MAX_HP
		guns = (1 << 0) | (1 << 1) | (1 << 2)  # AK47 | M4 | HandGun
		gun_type = 2
		bullet = DataMgr.max_bullet
		hurt_state = HurtState.Normal
		active = true
		status_seq += 1

	## 向后找已解锁枪,回绕;只一把返回 false 不动画(5.5 节)
	func next_gun() -> bool:
		var count := 0
		for t in DataMgr.gun_type_num:
			if guns & (1 << t):
				count += 1
		if count <= 1:
			return false
		for i in range(1, DataMgr.gun_type_num + 1):
			var cand: int = (gun_type + i) % DataMgr.gun_type_num
			if guns & (1 << cand):
				gun_type = cand
				PlayerSystem.notify_ui_changed()
				return true
		return false

	func use_bullet(n: int) -> void:
		bullet = maxi(0, bullet - n)
		PlayerSystem.notify_ui_changed()

	## 续命:HP=100 且 Bullet += MaxBullet(8.3 节)
	func relife() -> void:
		hp = MAX_HP
		bullet += DataMgr.max_bullet
		hurt_state = HurtState.Normal
		PlayerSystem.notify_ui_changed()

	## 补给箱解锁枪(AddGun)
	func add_gun(type: int) -> void:
		guns |= 1 << type
