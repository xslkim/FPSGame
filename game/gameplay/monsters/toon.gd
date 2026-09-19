extends MonsterBase
class_name ToonMonster
## ToonShoot / ToonShootAlien(6.3 ToonMonster):
## born 时动画速度 = 1 + level*0.5;先走向分配的 FireWindow(距离>0.1 时速度 3),
## 到位后面向相机,CD 到播 reload 动画;动画事件 toon_shoot():
##   视觉子弹(速度 50,1s 自隐)+ 0.5 秒后定时命中(50% 选边,目标不活跃由 hit_player 转嫁另一侧)。
## 死亡/回收时释放窗口占用。换装:6 组 Appearance 子节点随机显隐(ChangeApperance 占位)。

const WALK_SPEED := 3.0
const ARRIVE_DIST := 0.1
const RELOAD_ANIM := &"reload"       # meta attack_anims=["reload"],事件方法 toon_shoot
const BULLET_SPEED := 50.0
const BULLET_LIFE := 1.0
const HIT_DELAY := 0.5

# M7 材质:militia 全身件同图集,武器独立图集;alien 拼装(宇航服/头盔/步枪)
const TOON_MAT := preload("res://assets/models/monsters/toon/toon_mat.tres")
const WEAPON_MAT := preload("res://assets/models/monsters/toon/toon_weapon_mat.tres")
const ALIEN_SUIT_MAT := preload("res://assets/models/monsters/toon_alien/alien_suit_mat.tres")
const ALIEN_HELMET_MAT := preload("res://assets/models/monsters/toon_alien/alien_helmet_mat.tres")
const ALIEN_RIFLE_MAT := preload("res://assets/models/monsters/toon_alien/alien_rifle_mat.tres")

## 由 Level2 在 born 前分配(FireWindow.pick_free)
var fire_window: FireWindow = null

func _ready() -> void:
	super()
	_apply_materials()
	_change_appearance()  # 未 born 前也给一个完整形态(陈列/编辑器预览)

func _on_born() -> void:
	_anim.speed_scale = 1.0 + 0.5 * level  # ToonShootAlien 动画 1+0.5Lv 倍速
	_change_appearance()

## M7:真实 clip 已随 .tscn 挂载,跳过占位(基类 add_animation_library("") 会冲突报错)
func _build_placeholder_anims() -> void:
	if _anim != null and _anim.has_animation(damage_anim):
		_anims_built = true
		return
	super()

## M7 材质指派:按网格名分派(宇航服/头盔/步枪/ militia 武器/ militia 身体件)
func _apply_materials() -> void:
	var model := get_node_or_null("Model")
	if model == null:
		return
	for mi in model.find_children("*", "MeshInstance3D", true, false):
		var n := String(mi.name).to_lower()
		if n.contains("spacesuit"):
			mi.set_surface_override_material(0, ALIEN_SUIT_MAT)
		elif n.contains("helmet"):
			mi.set_surface_override_material(0, ALIEN_HELMET_MAT)
		elif n.contains("battlerifle"):
			mi.set_surface_override_material(0, ALIEN_RIFLE_MAT)
		elif n.contains("weapon"):
			mi.set_surface_override_material(0, WEAPON_MAT)
		else:
			mi.set_surface_override_material(0, TOON_MAT)

## 换装(ChangeApperance):真实模型按部件组随机;militia 头/身/腿各随机一件,
## extra 配件(围巾/装备/背包)逐件随机显隐;无模型时退回 6 组占位显隐
func _change_appearance() -> void:
	var ap := get_node_or_null("Appearance")
	if ap == null:
		return
	var sk := get_node_or_null("Model/Skeleton3D")
	if sk == null:
		for c in ap.get_children():
			c.visible = randf() < 0.5
		return
	var heads := []
	var bodys := []
	var legs := []
	var extras := []
	for mi in sk.get_children():
		if not (mi is MeshInstance3D):
			continue
		var n := String(mi.name)
		if n.begins_with("head_") or n.begins_with("Head_"):
			heads.append(mi)
		elif n.begins_with("Body_"):
			bodys.append(mi)
		elif n.begins_with("Legs_"):
			legs.append(mi)
		elif n.begins_with("extra_"):
			extras.append(mi)
	for arr in [heads, bodys, legs]:
		for mi in arr:
			mi.visible = false
		if not arr.is_empty():
			arr[randi() % arr.size()].visible = true
	for mi in extras:
		mi.visible = randf() < 0.5

func get_attack_event_method() -> StringName:
	return &"toon_shoot"

func _update_active(delta: float) -> void:
	if _is_playing_any(attack_anims) or (is_current_anim(damage_anim) and _anim.is_playing()):
		velocity.x = 0.0
		velocity.z = 0.0
		_apply_gravity(delta)
		move_and_slide()
		return
	if fire_window != null and is_instance_valid(fire_window) \
			and global_position.distance_to(fire_window.global_position) > ARRIVE_DIST:
		# 走向窗口(距离>0.1 时速度 3)
		var dir: Vector3 = fire_window.global_position - global_position
		dir.y = 0.0
		if dir.length_squared() > 0.0001:
			dir = dir.normalized()
			rotation.y = _yaw_towards(rotation.y, atan2(-dir.x, -dir.z), turn_speed * delta)
		velocity.x = dir.x * WALK_SPEED
		velocity.z = dir.z * WALK_SPEED
		_apply_gravity(delta)
		move_and_slide()
		if not is_current_anim(LOCOMOTION_ANIM) and _anim.has_animation(LOCOMOTION_ANIM):
			_anim.play(LOCOMOTION_ANIM, 0.2)
		return
	# 到位:面向相机,CD 到播 reload
	velocity.x = 0.0
	velocity.z = 0.0
	_apply_gravity(delta)
	move_and_slide()
	face_camera()
	if _attack_ready():
		last_attack_time = Time.get_ticks_msec() / 1000.0
		_anim.play(RELOAD_ANIM, 0.1)
	elif not _anim.is_playing() and _anim.has_animation(idle2_anim):
		_anim.play(idle2_anim, 0.3)

## 动画事件 ToonShoot:视觉子弹 + 0.5s 后定时命中(50% 选边)
func toon_shoot() -> void:
	if _state == State.DEAD or _state == State.IDLE:
		return
	ToonBullet.spawn(get_tree().current_scene, global_position + Vector3(0.0, 1.2, 0.0),
		BULLET_SPEED, BULLET_LIFE)
	var atk := get_attack()
	var atype := attack_type
	get_tree().create_timer(HIT_DELAY).timeout.connect(func():
		if _state == State.DEAD or _state == State.IDLE:
			return
		# 50% 选边;目标侧不活跃由 PlayerSystem.hit_player 转嫁另一侧
		var side: int = PlayerSystem.Side.Right if randf() < 0.5 else PlayerSystem.Side.Left
		PlayerSystem.hit_player(atk, atype, side))

## 死亡/回收释放窗口占用
func _on_death() -> void:
	_release_window()

func _recycle() -> void:
	_release_window()
	super()

func _release_window() -> void:
	if fire_window != null and is_instance_valid(fire_window):
		fire_window.release(self)
	fire_window = null

## Toon 视觉子弹(EnemyBullet 思路,5.2):直线飞向相机,速度 50,1s 自隐,无伤害
class ToonBullet extends Node3D:
	var _dir := Vector3.FORWARD
	var _speed := 50.0
	var _life := 1.0
	var _t := 0.0

	static func spawn(parent: Node, from: Vector3, p_speed: float, p_life: float) -> ToonBullet:
		var b := ToonBullet.new()
		b._speed = p_speed
		b._life = p_life
		parent.add_child(b)
		b.global_position = from
		return b

	func _ready() -> void:
		var mesh := MeshInstance3D.new()
		var sphere := SphereMesh.new()
		sphere.radius = 0.08
		sphere.height = 0.16
		var mat := StandardMaterial3D.new()
		mat.albedo_color = Color(1.0, 0.9, 0.2)
		mat.emission_enabled = true
		mat.emission = Color(1.0, 0.85, 0.15)
		sphere.material = mat
		mesh.mesh = sphere
		add_child(mesh)
		var cam := get_viewport().get_camera_3d()
		if cam != null:
			_dir = (cam.global_position - global_position).normalized()

	func _process(delta: float) -> void:
		_t += delta
		global_position += _dir * _speed * delta
		if _t >= _life:
			queue_free()
