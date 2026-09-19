extends MonsterBase
class_name DragonMonster
## Dragon 红/蓝/绿(6.3 Dragon,type 0/1/2 由 meta attack_type 区分):
## 四点巡回飞行 FarWay→InCamera→Attack→CamOffset→重随机循环
## (参数 Forward150/Right160/Up20/CamUp0.1/CamRight20/RightOffset15,
##  Pos2=InCamera 带 rand(-10,10) 横向与 rand(-20,20) 竖向抖动);
## 非 Attack 段速度×2;Attack 段与相机距离<attack_radius(75) 时播吐息
## (FireBreathOnce,0.75 倍速)+ 激活吐息特效(占位粒子),伤害走基类 event_attack 半屏判定;
## TakeDamage 状态中无敌(hit 返回空);死亡坠落 y<-3 回收。

enum FlySeg { FarWay, InCamera, Attack, CamOffset }

const P_FORWARD := 150.0
const P_RIGHT := 160.0
const P_UP := 20.0
const P_CAM_UP := 0.1
const P_CAM_RIGHT := 20.0
const P_RIGHT_OFFSET := 15.0
const POS2_JITTER_X := 10.0   # Pos2 横向 rand(-10,10)
const POS2_JITTER_Y := 20.0   # Pos2 竖向 rand(-20,20)
const ARRIVE_DIST := 2.0
const BREATH_ANIM_SPEED := 0.75
const FALL_RECYCLE_Y := -3.0

const FIRE_BREATH_SCENE := preload("res://assets/effects/fire_breath.tscn")
const SND_BREATH_FIRE := "res://assets/audio/effects/breath_fire.wav"
const SND_BREATH_ICE := "res://assets/audio/effects/breath_ice.wav"

var _segment := FlySeg.FarWay
var _target := Vector3.ZERO
var _breath_fx: GPUParticles3D = null
var _breath_root: Node3D = null
var _breath_time := 0.0
var _breath_audio: AudioStreamPlayer3D = null

## M7 颜色变体材质(red/blue/green .tres),在 .tscn 配置
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

func _on_born() -> void:
	_segment = FlySeg.FarWay
	_breath_time = 0.0
	_target = _make_point(_segment)
	_ensure_breath_fx()

## 入场点恒取右侧(+right*160):原作左右随机分支为死代码恒右(方案 §10),保留语义并标注。
func _make_point(seg: int) -> Vector3:
	var cam := get_viewport().get_camera_3d()
	if cam == null:
		return global_position
	var p := cam.global_position
	var fwd := -cam.global_basis.z
	var right := cam.global_basis.x
	match seg:
		FlySeg.FarWay:
			return p + fwd * P_FORWARD + right * P_RIGHT + Vector3.UP * P_UP
		FlySeg.InCamera:
			# Pos2:rand(-10,10) 横向 / rand(-20,20) 竖向抖动
			return p + fwd * P_CAM_RIGHT + Vector3.UP * P_CAM_UP \
				+ right * randf_range(-POS2_JITTER_X, POS2_JITTER_X) \
				+ Vector3.UP * randf_range(-POS2_JITTER_Y, POS2_JITTER_Y)
		FlySeg.Attack:
			# 攻击点取 attack_radius 内(75×0.8=60<75),到位即进入吐息判定
			return p + fwd * attack_radius * 0.8 + right * P_RIGHT_OFFSET
		FlySeg.CamOffset:
			return p + fwd * P_CAM_RIGHT + right * P_RIGHT_OFFSET + Vector3.UP * P_CAM_UP
	return p

func _update_active(delta: float) -> void:
	var cam := get_viewport().get_camera_3d()
	if cam == null:
		return
	if _breath_time > 0.0:
		_breath_time -= delta
		if _breath_time <= 0.0 and _breath_fx != null:
			_breath_fx.emitting = false
	# 非 Attack 段速度×2
	var speed := get_move_speed() * (1.0 if _segment == FlySeg.Attack else 2.0)
	var to: Vector3 = _target - global_position
	if to.length() < ARRIVE_DIST:
		_segment = (_segment + 1) % 4  # CamOffset 后回 FarWay,重随机(抖动在 _make_point)
		_target = _make_point(_segment)
		return
	var dir := to.normalized()
	rotation.y = _yaw_towards(rotation.y, atan2(-dir.x, -dir.z), turn_speed * delta)
	# 飞行直接位移(目标点可在地面以下,不用碰撞移动)
	global_position += dir * speed * delta
	if not is_current_anim(LOCOMOTION_ANIM) and not _is_playing_any(attack_anims) \
			and _anim.has_animation(LOCOMOTION_ANIM):
		_anim.play(LOCOMOTION_ANIM, 0.2)
	if _segment == FlySeg.Attack:
		_update_breath(cam)

## Attack 段:与相机距离 < attack_radius(75) → 吐息(0.75 倍速)+ 火焰锥粒子;
## 伤害由 clip 的 Call Method Track → event_attack(基类半屏判定)
func _update_breath(cam: Camera3D) -> void:
	if _is_playing_any(attack_anims):
		return
	if global_position.distance_to(cam.global_position) >= attack_radius:
		return
	_anim.play(&"FireBreathOnce", 0.1, BREATH_ANIM_SPEED)
	if _breath_fx != null:
		_breath_fx.emitting = true
		_breath_time = 0.8 / BREATH_ANIM_SPEED  # clip 0.8s ÷ 0.75 倍速
		if _breath_audio != null:
			_breath_audio.play()

## 吐息特效:fire_breath.tscn 火焰锥挂在龙口(0,1,-3);
## 蓝龙(冰)/绿龙(毒)改火焰色调(逐实例复制 draw_pass 材质,避免三色龙互相污染);
## 音效文件缺失静默
func _ensure_breath_fx() -> void:
	if _breath_root != null:
		_breath_fx.emitting = false
		return
	_breath_root = FIRE_BREATH_SCENE.instantiate()
	_breath_root.position = Vector3(0.0, 1.0, -3.0)  # 真实模型:龙口在 -Z 前方约 3m
	add_child(_breath_root)
	_breath_fx = _breath_root.get_node("Flames")
	# 逐实例复制网格与材质再改色(场景 sub_resource 为实例间共享)
	var mesh: QuadMesh = _breath_fx.draw_pass_1.duplicate()
	var flame_mat: StandardMaterial3D = mesh.material.duplicate()
	match attack_type:
		GlobalObject.AttackType.Ice:
			flame_mat.albedo_color = Color(0.45, 0.8, 1.0)
		GlobalObject.AttackType.Poison:
			flame_mat.albedo_color = Color(0.55, 1.0, 0.4)
		_:
			flame_mat.albedo_color = Color(1.0, 0.75, 0.4)
	mesh.material = flame_mat
	_breath_fx.draw_pass_1 = mesh
	var snd_path := SND_BREATH_ICE if attack_type == GlobalObject.AttackType.Ice else SND_BREATH_FIRE
	if ResourceLoader.exists(snd_path):
		_breath_audio = AudioStreamPlayer3D.new()
		_breath_audio.stream = load(snd_path)
		_breath_audio.unit_size = 10.0
		add_child(_breath_audio)

## TakeDamage 状态中无敌:hit 返回空,不掉血
func hit(p_attack: float, point: Vector3, hit_type: int, side: int) -> String:
	if is_current_anim(damage_anim) and _anim.is_playing():
		return ""
	return super(p_attack, point, hit_type, side)

## 死亡:坠落 y<-3 回收(不调基类 1.5s 定时回收)
func _die() -> void:
	_state = State.DEAD
	velocity = Vector3.ZERO
	_col.set_deferred("disabled", true)
	_play_sound("dead")
	_spawn_blood_flower()
	if _breath_fx != null:
		_breath_fx.emitting = false
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
