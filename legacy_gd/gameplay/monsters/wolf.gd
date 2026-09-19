extends MonsterBase
class_name WolfMonster
## Wolf(6.3):蛇形走位。
## born 时生成路径:到相机方向距离减 4 后按段长 6 分段,
## 横向按 8*(0.2+(1-i/segment)*0.8) 交替 ± 偏移(起始方向 50% 随机),每点超时 3s 跳下一点;
## 走完进入面向相机 ±15° 且 attack_radius 内的攻击阶段。
## 多段距离降速保真原作 if/else 覆盖顺序(方案 §10,逐行保真):
##   if d<4 speed=4; if d<3 speed=3; if d<2 speed=2; if d<1 speed=1(顺序执行,后者覆盖前者)。

const SEGMENT_LEN := 6.0
const APPROACH_TRIM := 4.0       # 到相机方向距离减 4
const LATERAL_BASE := 8.0
const POINT_TIMEOUT := 3.0
const POINT_ARRIVE := 0.3
const ATTACK_FACING := deg_to_rad(15.0)

var _path: Array = []        # Array[Vector3]
var _path_idx := 0
var _point_time := 0.0

## M7:真实 clip 已随 .tscn 挂载,跳过占位(基类 add_animation_library("") 会冲突报错)
func _build_placeholder_anims() -> void:
	if _anim != null and _anim.has_animation(damage_anim):
		_anims_built = true
		return
	super()

func _on_born() -> void:
	_build_path()

func _build_path() -> void:
	_path.clear()
	_path_idx = 0
	_point_time = 0.0
	var cam := get_viewport().get_camera_3d()
	if cam == null:
		return
	var to_cam: Vector3 = cam.global_position - global_position
	to_cam.y = 0.0
	var dist := to_cam.length()
	if dist < 0.001:
		return
	var dir := to_cam / dist
	var total := maxf(dist - APPROACH_TRIM, 0.0)
	var seg := maxi(1, int(total / SEGMENT_LEN))  # 按段长 6 分段
	var lateral := Vector3.UP.cross(dir).normalized()
	var side := 1.0 if randf() < 0.5 else -1.0    # 起始方向 50% 随机
	for i in range(1, seg + 1):
		var fwd_len := minf(i * SEGMENT_LEN, total)
		# 横向 8*(0.2+(1-i/segment)*0.8),交替 ±
		var amp := LATERAL_BASE * (0.2 + (1.0 - float(i) / seg) * 0.8)
		amp *= side * (1.0 if i % 2 == 1 else -1.0)
		var p := _born_pos + dir * fwd_len + lateral * amp
		p.y = _born_pos.y
		_path.append(p)

## 多段距离降速(逐行保真原作语义:独立 if 顺序执行,近距覆盖远距)
func _speed_for_dist(d: float) -> float:
	var speed := get_move_speed()
	if d < 4.0:
		speed = 4.0
	if d < 3.0:
		speed = 3.0
	if d < 2.0:
		speed = 2.0
	if d < 1.0:
		speed = 1.0
	return speed

func _update_active(delta: float) -> void:
	if _is_playing_any(attack_anims) or (is_current_anim(damage_anim) and _anim.is_playing()):
		velocity.x = 0.0
		velocity.z = 0.0
		_apply_gravity(delta)
		move_and_slide()
		return
	if _path_idx < _path.size():
		_update_serpentine(delta)
	else:
		_update_attack_phase(delta)

## 蛇形走位:朝当前路径点移动(按到点距离降速),到点或超时 3s 跳下一点
func _update_serpentine(delta: float) -> void:
	_point_time += delta
	var target: Vector3 = _path[_path_idx]
	var to: Vector3 = target - global_position
	to.y = 0.0
	var d := to.length()
	if d < POINT_ARRIVE or _point_time >= POINT_TIMEOUT:
		_path_idx += 1
		_point_time = 0.0
		return
	var dir := to / d
	rotation.y = _yaw_towards(rotation.y, atan2(-dir.x, -dir.z), turn_speed * delta)
	var speed := _speed_for_dist(d)
	velocity.x = dir.x * speed
	velocity.z = dir.z * speed
	_apply_gravity(delta)
	move_and_slide()
	if not is_current_anim(LOCOMOTION_ANIM) and _anim.has_animation(LOCOMOTION_ANIM):
		_anim.play(LOCOMOTION_ANIM, 0.2)

## 攻击阶段:面向相机 ±15° 且 attack_radius 内放攻击;否则继续逼近
func _update_attack_phase(delta: float) -> void:
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
		if absf(wrapf(desired_yaw - rotation.y, -PI, PI)) <= ATTACK_FACING:
			do_attack()
			return
	if d > 0.001:
		var dir := to / maxf(d, 0.001)
		var speed := _speed_for_dist(d)
		velocity.x = dir.x * speed
		velocity.z = dir.z * speed
	else:
		velocity.x = 0.0
		velocity.z = 0.0
	_apply_gravity(delta)
	move_and_slide()
	if not is_current_anim(LOCOMOTION_ANIM) and _anim.has_animation(LOCOMOTION_ANIM):
		_anim.play(LOCOMOTION_ANIM, 0.2)
