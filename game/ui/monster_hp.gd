extends Node3D
class_name MonsterHP
## 怪物头顶血条(公告板双 Sprite3D,8.4 节)+ 全场景共享飘伤害数字池。
## 挂在怪物的 HpAnchor 节点上;伤害数字用静态池,alpha 1→0.3(0.7s)后停用复用。

const BAR_PIXEL_SIZE := 0.016  # 64px × 0.016 ≈ 1m 宽
const DMG_POOL_MAX := 16

static var _white_tex: ImageTexture
static var _dmg_pool: Array = []
static var _dmg_parent: Node = null

var _fg: Sprite3D

func _ready() -> void:
	var bg := _make_bar(Color(0.08, 0.08, 0.08, 0.85))
	add_child(bg)
	_fg = _make_bar(Color(0.2, 0.9, 0.25, 0.95))
	_fg.position.y = 0.001  # 与底条错开(公告板旋转下 z 偏移不可靠)
	add_child(_fg)
	set_hp(1.0)

func set_hp(frac: float) -> void:
	frac = clampf(frac, 0.0, 1.0)
	if _fg == null:
		return
	_fg.visible = frac > 0.0
	_fg.region_rect = Rect2(0, 0, 64.0 * frac, 8)
	_fg.offset.x = -32.0 * (1.0 - frac)  # 左端固定,向右缩短

func _make_bar(color: Color) -> Sprite3D:
	var s := Sprite3D.new()
	s.texture = _get_white_tex()
	s.modulate = color
	s.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	s.pixel_size = BAR_PIXEL_SIZE
	s.region_enabled = true
	s.region_rect = Rect2(0, 0, 64, 8)
	return s

static func _get_white_tex() -> ImageTexture:
	if _white_tex == null:
		var img := Image.create(64, 8, false, Image.FORMAT_RGBA8)
		img.fill(Color.WHITE)
		_white_tex = ImageTexture.create_from_image(img)
	return _white_tex

# ---- 飘伤害数字池:"- N",0.7s 上浮淡出(y += 5*dt、alpha -= 1*dt、alpha<=0.3 停用)----

static func show_damage(text: String, world_pos: Vector3, context: Node) -> void:
	if _dmg_parent == null or not is_instance_valid(_dmg_parent):
		_dmg_parent = context.get_tree().current_scene
		_dmg_pool.clear()
	var label: DamageLabel
	for l in _dmg_pool:
		if not l.active:
			label = l
			break
	if label == null:
		if _dmg_pool.size() >= DMG_POOL_MAX:
			return
		label = DamageLabel.new()
		label.billboard = BaseMaterial3D.BILLBOARD_ENABLED
		label.font_size = 48
		label.pixel_size = 0.004
		label.modulate = Color(1.0, 0.35, 0.2)
		label.visible = false
		_dmg_parent.add_child(label)
		_dmg_pool.append(label)
	label.text = text
	label.global_position = world_pos
	label.modulate.a = 1.0
	label.visible = true
	label.active = true

class DamageLabel extends Label3D:
	var active := false

	func _process(delta: float) -> void:
		if not active:
			return
		global_position.y += 5.0 * delta
		modulate.a -= 1.0 * delta
		if modulate.a <= 0.3:
			active = false
			visible = false
