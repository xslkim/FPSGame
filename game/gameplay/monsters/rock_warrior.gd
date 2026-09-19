extends MonsterBase
class_name RockWarriorMonster
## 树皮石头怪 RockWarrior(L3 Boss,6.3):
## 追击同 FatZombie 语义(标准追击,meta 带 0.5 速参 move_speed_level_rate),
## 进入 attack_radius(36) 且正面夹角 <15° 放 atk01;
## 动画事件 rock_attack():火球(复用 fireball.gd,localScale×10,定时命中 Both,Poison 类型);
## 硬化皮肤:非 atk01 动画状态被打只掉 1 血且不播受击动画/音效,atk01 状态中才全额掉血。

const ATTACK_FACING := deg_to_rad(15.0)
const HARDENED_DAMAGE := 1.0
const FIREBALL_SCALE := 10.0

var last_fireball: Fireball = null  # 自检/调试用

## M7 材质(RockWarrior.png),在 .tscn 配置
@export var body_material: Material = null

func _ready() -> void:
	super()
	if body_material != null:
		for mi in find_children("*", "MeshInstance3D", true, false):
			mi.set_surface_override_material(0, body_material)

## M7:真实 clip 已随 .tscn 挂载,跳过占位(基类 add_animation_library("") 会冲突报错)
func _build_placeholder_anims() -> void:
	if _anim != null and _anim.has_animation(damage_anim):
		_anims_built = true
		return
	super()

func get_attack_event_method() -> StringName:
	return &"rock_attack"

func _update_active(delta: float) -> void:
	if _is_playing_any(attack_anims) or (is_current_anim(damage_anim) and _anim.is_playing()):
		velocity.x = 0.0
		velocity.z = 0.0
		_apply_gravity(delta)
		move_and_slide()
		return
	var cam := get_viewport().get_camera_3d()
	if cam == null:
		return
	var target := cam.global_position
	target.y = global_position.y
	var to: Vector3 = target - global_position
	var d := Vector2(to.x, to.z).length()
	var desired_yaw := atan2(-to.x, -to.z) if d > 0.001 else rotation.y
	rotation.y = _yaw_towards(rotation.y, desired_yaw, turn_speed * delta)
	if d < attack_radius and _attack_ready():
		# 正面夹角 <15° 才放 atk01(未到角继续转身)
		if absf(wrapf(desired_yaw - rotation.y, -PI, PI)) <= ATTACK_FACING:
			do_attack()
		velocity.x = 0.0
		velocity.z = 0.0
		_apply_gravity(delta)
		move_and_slide()
		return
	# 追击(同 FatZombie 标准追击语义,3m 内速度压到 1)
	if d > 0.001:
		var dir := to / d
		var speed := get_move_speed()
		if d < 3.0:
			speed = minf(speed, 1.0)
		velocity.x = dir.x * speed
		velocity.z = dir.z * speed
	_apply_gravity(delta)
	move_and_slide()
	if not is_current_anim(LOCOMOTION_ANIM) and _anim.has_animation(LOCOMOTION_ANIM):
		_anim.play(LOCOMOTION_ANIM, 0.2)

## 动画事件 RockAttack:10 倍大火球,定时命中 Both,Poison 类型(meta attack_type=2)
func rock_attack() -> void:
	if _state == State.DEAD or _state == State.IDLE:
		return
	last_fireball = Fireball.spawn(get_tree().current_scene,
		global_position + Vector3(0.0, 1.5, 0.0), get_attack(), attack_type, true)
	last_fireball.scale = Vector3.ONE * FIREBALL_SCALE

## 硬化皮肤:非 atk01 状态只掉 1 血,不播受击动画/音效;atk01 状态中全额掉血
func hit(p_attack: float, point: Vector3, hit_type: int, side: int) -> String:
	if _state == State.DEAD or _state == State.IDLE:
		return impact_tag
	if not (is_current_anim(&"atk01") and _anim.is_playing()):
		hp -= HARDENED_DAMAGE
		MonsterHP.show_damage("- %d" % int(HARDENED_DAMAGE), point, self)
		_hp_bar.set_hp(maxf(hp, 0.0) / get_max_hp())
		if hp <= 0.0:
			_die()
		return impact_tag
	return super(p_attack, point, hit_type, side)
