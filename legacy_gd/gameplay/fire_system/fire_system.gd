extends Node3D
class_name FireSystem
## FireSystem:挂在战斗相机下,管理左右枪节点(5.1 每帧流程 / 5.3 Flash / 5.5 换枪)。
## 由场景(如 fire_range)在玩家 born 后调用 bind_players() 绑定。

signal open_continue(is_open: bool, side: int)  # 弹尽开续币面板(M5 实现 UI)

const RAY_LENGTH := 2000.0
const RAY_MASK := 0xFFFFFFF7  # 除 layer 4 (CameraWall) 外全部
const POOL_SIZE := 3          # 每组特效池轮询数量(占位)
const BLOOD_FLOWER_POOL_SIZE := 5  # 血花线性池(5.2):取第一个未激活挂怪身上
const SWITCH_SINK := 0.10     # 换枪下沉量(Godot 中"后退"为 +Z,相机看 -Z)
const SWITCH_DOWN_TIME := 0.5
const SWITCH_UP_TIME := 0.55

const GUN_SCENES := {
	0: preload("res://gameplay/fire_system/ak47.tscn"),
	1: preload("res://gameplay/fire_system/m4.tscn"),
	2: preload("res://gameplay/fire_system/handgun.tscn"),
}
## 特效 tag → 可复用特效场景(M7b:GPUParticles3D 喷射 + 弹孔贴花,5.2 节)
const EFFECT_SCENES := {
	"Wood": preload("res://assets/effects/impact_wood.tscn"),
	"Metal": preload("res://assets/effects/impact_metal.tscn"),
	"Blood": preload("res://assets/effects/impact_blood.tscn"),
	"Concrete": preload("res://assets/effects/impact_concrete.tscn"),
	"Dust": preload("res://assets/effects/impact_dust.tscn"),
}
const BLOOD_FLOWER_SCENE := preload("res://assets/effects/blood_flower.tscn")
## 命中光标贴图(RPG VFX Point19)与射击 UI 音;文件缺失时静默回退
const FLASH_CURSOR := "res://assets/effects/textures/flash_point19.png"
const UI_SHOT_SOUND := "res://assets/audio/ui/shot.wav"

var _camera: Camera3D
var _anchors := {}       # side -> Node3D
var _guns := {}          # side -> { gun_type: GunBase }
var _current_gun := {}   # side -> GunBase
var _flash := {}         # side -> Sprite3D
var _lazer := {}         # side -> Node3D(细长红色半透明柱体)
var _switching := {}     # side -> bool(换枪期间锁开火)
var _empty_signaled := {}  # side -> bool(弹尽信号只发一次,补弹后复位)
var _effects := {}       # tag -> Array[Node3D]
var _effect_idx := {}    # tag -> int
var _blood_flowers: Array = []  # 血花线性池(M3 怪物死亡用)
var _ui_shot_player: AudioStreamPlayer = null  # 枪打 UI 按钮音(Zapper)

func _ready() -> void:
	_camera = get_parent() as Camera3D
	for side in [PlayerSystem.Side.Right, PlayerSystem.Side.Left]:
		var side_name := "Right" if side == PlayerSystem.Side.Right else "Left"
		var anchor := Node3D.new()
		anchor.name = side_name + "GunAnchor"
		add_child(anchor)
		_anchors[side] = anchor
		_guns[side] = {}
		_current_gun[side] = null
		_switching[side] = false
		_empty_signaled[side] = false
		var flash := Sprite3D.new()
		flash.name = "Flash" + side_name
		flash.top_level = true
		flash.billboard = BaseMaterial3D.BILLBOARD_ENABLED
		flash.texture = _load_flash_cursor()
		flash.modulate = Color(1.0, 0.85, 0.35)
		flash.pixel_size = 0.002
		flash.visible = false
		add_child(flash)
		_flash[side] = flash
		var lazer := _make_lazer()
		lazer.visible = false
		add_child(lazer)
		_lazer[side] = lazer
	for tag in EFFECT_SCENES:
		_effects[tag] = []
		_effect_idx[tag] = 0
		for i in POOL_SIZE:
			var e: Node3D = EFFECT_SCENES[tag].instantiate()
			e.name = "Fx_%s_%d" % [tag, i]
			e.visible = false
			add_child(e)
			_effects[tag].append(e)
	for i in BLOOD_FLOWER_POOL_SIZE:
		var bf: Node3D = BLOOD_FLOWER_SCENE.instantiate()
		bf.name = "BloodFlower_%d" % i
		bf.visible = false
		add_child(bf)
		_blood_flowers.append(bf)
	if ResourceLoader.exists(UI_SHOT_SOUND):
		_ui_shot_player = AudioStreamPlayer.new()
		_ui_shot_player.name = "UIShotSound"
		_ui_shot_player.stream = load(UI_SHOT_SOUND)
		add_child(_ui_shot_player)
	add_to_group("fire_system")  # 怪物血花等跨系统接口按组查找
	InputManager.key2_right.connect(_on_switch_key.bind(PlayerSystem.Side.Right))
	InputManager.key2_left.connect(_on_switch_key.bind(PlayerSystem.Side.Left))

## 玩家 born 后调用:按玩家已解锁枪实例化左右枪节点
func bind_players() -> void:
	_bind_side(PlayerSystem.Side.Right, PlayerSystem.player_right)
	_bind_side(PlayerSystem.Side.Left, PlayerSystem.player_left)

func _bind_side(side: int, player) -> void:
	for g in _guns[side].values():
		g.queue_free()
	_guns[side].clear()
	_current_gun[side] = null
	_lazer[side].visible = false
	var anchor: Node3D = _anchors[side]
	if player == null or not player.active:
		anchor.visible = false
		return
	anchor.visible = true
	for type in GUN_SCENES:
		if player.guns & (1 << type):
			var gun: GunBase = GUN_SCENES[type].instantiate()
			gun.player = player
			gun.is_left = side == PlayerSystem.Side.Left
			anchor.add_child(gun)
			gun.position = _gun_base_pos(side, type)
			gun.visible = type == player.gun_type
			_guns[side][type] = gun
			if gun.visible:
				_current_gun[side] = gun
	if _current_gun[side] != null:
		_remount_lazer(side, _current_gun[side])

func _process(_delta: float) -> void:
	if GlobalObject.is_game_pause:
		return
	_update_side(PlayerSystem.Side.Right)
	_update_side(PlayerSystem.Side.Left)

## 5.1 节每帧流程(LateUpdate 等价)
func _update_side(side: int) -> void:
	var player = PlayerSystem.get_player(side)
	var anchor: Node3D = _anchors[side]
	var flash: Sprite3D = _flash[side]
	if player == null or not player.active:
		anchor.visible = false
		flash.visible = false
		return
	anchor.visible = true
	# 1. 冰冻且未暂停 → 本帧跳过(不转枪不开火)
	if player.hurt_state == PlayerSystem.Player.HurtState.Frozen:
		return
	var gun: GunBase = _current_gun[side]
	if gun == null:
		return
	# 2. 枪口 localRotation = 4.2 节旋转;读扳机电平
	gun.quaternion = (GlobalObject.get_ring_rotation() if side == PlayerSystem.Side.Right
		else GlobalObject.get_leg_rotation())
	var trigger: bool = (InputManager.get_cur_key_ring() if side == PlayerSystem.Side.Right
		else InputManager.get_cur_key_leg())
	# 3. 射线:枪口沿 -Z 前向 2000,掩码除 CameraWall 外全部
	var from := gun.muzzle.global_position
	var dir := -gun.muzzle.global_basis.z
	var query := PhysicsRayQueryParameters3D.create(from, from + dir * RAY_LENGTH, RAY_MASK)
	var hit := get_world_3d().direct_space_state.intersect_ray(query)
	# 5. 未命中 → 隐藏 Flash
	if hit.is_empty():
		flash.visible = false
		return
	var point: Vector3 = hit.position
	var normal: Vector3 = hit.normal
	var collider: Object = hit.collider
	# 4. 命中 → Flash 光标(5.3 公式)
	var d: float = (_camera.global_position.distance_to(point) if _camera != null
		else from.distance_to(point))
	var flash_scale := 1.0 - clampf(3.0 / maxf(d, 0.001), 0.0, 1.0) * 0.8  # 近 0.2 → 远 1
	var back_off := 0.5 - clampf(1.0 / maxf(d, 0.001), 0.0, 1.0) * 0.4     # 近 0.1 → 远 0.5
	flash.visible = true
	flash.global_position = point - dir * back_off
	flash.scale = Vector3.ONE * flash_scale
	if not trigger or _switching[side]:
		return
	# 命中 Button(layer 3)→ 触发回调,不耗弹不开火;播射击 UI 音(枪打按钮反馈)
	if collider is CollisionObject3D and (collider.collision_layer & 0b100) != 0:
		if collider.has_method("on_shot"):
			collider.on_shot()
		if _ui_shot_player != null:
			_ui_shot_player.play()
		return
	var result: Array = gun.fire()
	if not result[0]:
		# 弹尽且战斗状态 → 开续币面板(只发一次,补弹后复位)
		if not result[1] and GlobalObject.scene_state == GlobalObject.GameState.Battle \
				and not _empty_signaled[side]:
			_empty_signaled[side] = true
			open_continue.emit(true, side)
		return
	_empty_signaled[side] = false
	# 命中敌人(layer 2)→ hit() 取特效 tag;否则用环境 tag;未匹配 → Dust
	var is_enemy: bool = collider is CollisionObject3D and (collider.collision_layer & 0b010) != 0
	var tag := "Dust"
	if is_enemy:
		if collider.has_method("hit"):
			# BoxHead 部件约定:同样实现 hit(),内部转发本体并带 Head/Body/Armour 部位(M3)
			var ret = collider.hit(gun.attack, point, GlobalObject.HitType.Body, side)
			if ret is String and EFFECT_SCENES.has(ret):
				tag = ret
	else:
		tag = _effect_tag_of(collider)
	_spawn_effect(tag, point, normal, flash_scale, is_enemy)

## 5.5 换枪:key2 边沿 / LeftAlt → next_gun → 旧枪下沉 → 隐藏 → 新枪升起
func _on_switch_key(side: int) -> void:
	if _switching[side]:
		return
	var player = PlayerSystem.get_player(side)
	if player == null or not player.active:
		return
	var old: GunBase = _current_gun[side]
	if old == null or not player.next_gun():
		return
	var new_gun: GunBase = _guns[side][player.gun_type]
	_switching[side] = true  # 换枪期间锁开火
	var base_new := _gun_base_pos(side, new_gun.gun_type)
	var tw := create_tween()
	tw.tween_property(old, "position:z", old.position.z + SWITCH_SINK, SWITCH_DOWN_TIME)
	tw.tween_callback(func():
		old.hide()
		new_gun.position = base_new + Vector3(0, 0, SWITCH_SINK)
		new_gun.show()
		_current_gun[side] = new_gun
		_remount_lazer(side, new_gun)
		var tw2 := create_tween()
		tw2.tween_property(new_gun, "position", base_new, SWITCH_UP_TIME)
		tw2.tween_callback(func(): _switching[side] = false))

## 挂载位:FOV>50 用 pos60;左手 pos.x 镜像;Unity +Z 前 → Godot -Z 前(5.4/5.5)
func _gun_base_pos(side: int, type: int) -> Vector3:
	var info := DataMgr.get_gun_info(type)
	var key := "pos60" if (_camera != null and _camera.fov > 50.0) else "pos"
	var p: Dictionary = info.get(key, {"x": 0.1, "y": -0.04, "z": 0.16})
	var v := Vector3(float(p.x), float(p.y), -float(p.z))
	if side == PlayerSystem.Side.Left:
		v.x = -v.x
	return v

func _effect_tag_of(collider: Object) -> String:
	if collider is Node:
		if collider.has_meta("effect_tag"):
			var tag := str(collider.get_meta("effect_tag"))
			if EFFECT_SCENES.has(tag):
				return tag
		for tag in EFFECT_SCENES:
			if collider.is_in_group(tag):
				return tag
	return "Dust"  # 未匹配或 Untagged → Dust(5.2 节)

## 从池取特效摆到命中点:面向法线,缩放 flashScale×3,打敌人不显示弹孔子节点;
## EffectBase.activate() 会重启粒子并按时自隐
func _spawn_effect(tag: String, point: Vector3, normal: Vector3, flash_scale: float, is_enemy: bool) -> void:
	var pool: Array = _effects[tag]
	var idx: int = _effect_idx[tag]
	_effect_idx[tag] = (idx + 1) % pool.size()
	var e: Node3D = pool[idx]
	var up := Vector3.UP if absf(normal.dot(Vector3.UP)) < 0.99 else Vector3.RIGHT
	e.global_transform = Transform3D(Basis.IDENTITY, point).looking_at(point - normal, up)
	e.scale = Vector3.ONE * flash_scale * 3.0
	e.get_node("Hole").visible = not is_enemy
	if e.has_method("activate"):
		e.activate()

# ---- 特效资源 ----

## 命中光标贴图:优先 RPG VFX Point19,缺失回退程序化径向
func _load_flash_cursor() -> Texture2D:
	if ResourceLoader.exists(FLASH_CURSOR):
		return load(FLASH_CURSOR)
	return FxHelper.make_radial_texture(Color(1.0, 0.9, 0.4))

## 血花线性池(5.2):取第一个未激活,挂怪身上;EffectBase 自隐后回池
func spawn_blood_flower(parent: Node3D, local_pos := Vector3.ZERO) -> void:
	for bf in _blood_flowers:
		if not bf.visible:
			bf.reparent(parent, false)
			bf.transform = Transform3D(Basis.IDENTITY, local_pos)
			if bf.has_method("activate"):
				bf.activate(1.2)
			else:
				bf.visible = true
			return

## Lazer 柱体:wrapper 内圆柱绕 X 转 90° 使长轴沿 -Z;
## _remount_lazer 每帧重置 wrapper transform,故旋转只能放在子节点上
func _make_lazer() -> Node3D:
	var root := Node3D.new()
	root.name = "Lazer"
	var m := MeshInstance3D.new()
	m.rotation_degrees = Vector3(90, 0, 0)
	var cyl := CylinderMesh.new()
	cyl.top_radius = 0.002
	cyl.bottom_radius = 0.002
	cyl.height = 1.5
	cyl.radial_segments = 8
	var mat := StandardMaterial3D.new()
	mat.albedo_color = Color(1.0, 0.1, 0.1, 0.6)
	if ResourceLoader.exists("res://assets/effects/textures/lazer.png"):
		mat.albedo_texture = load("res://assets/effects/textures/lazer.png")
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	cyl.material = mat
	m.mesh = cyl
	m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	root.add_child(m)
	return root

## Lazer:挂当前枪下的固定长度模型,换枪时重新挂载(5.3 节,无逻辑)
## 起点对齐真实枪口(z≈-0.05,覆盖三把枪 muzzle -0.039~-0.108),向 -Z 延伸 1.5m
func _remount_lazer(side: int, gun: GunBase) -> void:
	var lazer: Node3D = _lazer[side]
	lazer.reparent(gun, false)
	lazer.transform = Transform3D(Basis.IDENTITY, Vector3(0, -0.02, -0.80))
	lazer.visible = true

