extends MonsterBase
class_name MagmaDemonMonster
## MagmaDemonBlue(6.3 MagmaDemon):
## 出生自定义:相机前 10m,四方向(右/左/上/下,Right=2/Up=2 + OutOffset=6,合计 8)随机一边;
## 先飞到屏幕内随机点,到位 LookAt 相机进 idle → CD → attack01/02 循环;
## 受击动画半速播放,播完回 idle 恢复;死亡坠落 y<-3 回收。get_move_speed 恒 8。

const BORN_FORWARD := 10.0
const BORN_RIGHT := 2.0
const BORN_UP := 2.0
const BORN_OUT_OFFSET := 6.0
const FLY_ARRIVE := 0.5
const HURT_ANIM_SPEED := 0.5
const FALL_RECYCLE_Y := -3.0

enum Phase { FlyIn, Combat }

var _phase := Phase.FlyIn
var _screen_point := Vector3.ZERO
var born_axis := -1  # 出生方向(自检用):0 右 / 1 左 / 2 上 / 3 下

## M7 颜色变体材质(blue .tres),在 .tscn 配置
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

func get_move_speed() -> float:
	return 8.0  # 恒 8(不吃 level 加成)

## 出生自定义:忽略关卡传入点,相机前 10m + 四向随机一边(偏移 2+6=8)
func _on_born() -> void:
	var cam := get_viewport().get_camera_3d()
	if cam == null:
		return
	var p := cam.global_position + (-cam.global_basis.z) * BORN_FORWARD
	born_axis = randi() % 4
	var off := Vector3.ZERO
	match born_axis:
		0:
			off = cam.global_basis.x * (BORN_RIGHT + BORN_OUT_OFFSET)
		1:
			off = -cam.global_basis.x * (BORN_RIGHT + BORN_OUT_OFFSET)
		2:
			off = Vector3.UP * (BORN_UP + BORN_OUT_OFFSET)
		3:
			off = -Vector3.UP * (BORN_UP + BORN_OUT_OFFSET)
	global_position = p + off
	_born_pos = global_position

## 进入活跃:先飞到屏幕内随机点
func _enter_active() -> void:
	_phase = Phase.FlyIn
	var cam := get_viewport().get_camera_3d()
	if cam != null:
		_screen_point = cam.global_position + (-cam.global_basis.z) * randf_range(8.0, 12.0) \
			+ cam.global_basis.x * randf_range(-4.0, 4.0) \
			+ Vector3.UP * randf_range(-1.0, 3.0)

func _update_active(delta: float) -> void:
	if _phase == Phase.FlyIn:
		var to: Vector3 = _screen_point - global_position
		if to.length() < FLY_ARRIVE:
			_phase = Phase.Combat
			face_camera()  # 到位 LookAt 相机
			_play_idle()
			return
		# 飞行直接位移(可穿地面,演出弹道语义)
		global_position += to.normalized() * get_move_speed() * delta
		return
	# Combat:hover + idle → CD → attack01/02 循环
	velocity = Vector3.ZERO
	move_and_slide()
	if _is_playing_any(attack_anims):
		return
	if is_current_anim(damage_anim) and _anim.is_playing():
		return  # 受击动画(半速)播完回 idle 恢复
	if not is_current_anim(StringName(_meta.get("idle_anim", idle2_anim))):
		_play_idle()
	if _attack_ready():
		do_attack()

func _play_idle() -> void:
	var idle_name := StringName(_meta.get("idle_anim", idle2_anim))
	if _anim.has_animation(idle_name):
		_anim.play(idle_name, 0.3)

## 受击动画半速(基类 hit 已按原速播,这里重播为半速)
func _on_hurt(_point: Vector3, _hit_type: int, _side: int) -> void:
	if _anim.has_animation(damage_anim):
		_anim.play(damage_anim, 0.1, HURT_ANIM_SPEED)

## 死亡:坠落 y<-3 回收(不调基类 1.5s 定时回收)
func _die() -> void:
	_state = State.DEAD
	velocity = Vector3.ZERO
	_col.set_deferred("disabled", true)
	_play_sound("dead")
	_spawn_blood_flower()
	if _anim.has_animation(dead_anim):
		_anim.play(dead_anim, 0.1, dead_anim_speed)
	_on_death()

func _physics_process(delta: float) -> void:
	if _state == State.DEAD:
		velocity.y -= GRAVITY * delta
		global_position += velocity * delta
		if global_position.y < FALL_RECYCLE_Y:
			_recycle()
		return
	super(delta)

## 飞行怪等待期悬浮(不下坠),等待时长仍按难度语义
func _update_waiting(delta: float) -> void:
	_wait_time += delta
	velocity = Vector3.ZERO
	if _wait_time >= waitting_time:
		_state = State.ACTIVE
		_enter_active()
