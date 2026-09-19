extends CharacterBody3D
class_name MonsterBase
## 怪物基类:6.1 节生命周期全量实现。
## 动画:各怪 .tscn 的 AnimationPlayer 若已挂真实 FBX AnimationLibrary(clip 名同
## monster_meta.json),_build_placeholder_anims 会跳过同名 clip;缺失的 clip 才由
## 程序生成占位(机制不变,attack clip 的 Call Method Track 由真实 clip 自带)。

signal died(monster)

enum State { IDLE, WAITING, ACTIVE, DEAD }

const MAX_LIFE_TIME := 25.0        # 非 Boss 超时自毁(原 ResetMaxLifeTimeDeath 恒 25s,保留行为)
const FALL_SPEED := 3.0            # 等待期持续下坠速度
const IDLE2_INTERVAL := 5.0
const RECYCLE_DELAY := 1.5         # 死亡 1.5s 后回收
const GRAVITY := 9.8
const BORN_RAY_MASK := 0xFFFFFFF5  # 排除 layer2 Enemy / layer4 CameraWall
const LOCOMOTION_ANIM := &"locomotion"  # 占位移动动画名;FBX 导入后换真实走跑 clip 名

@export var meta_key := ""   # data/monster_meta.json 的 monsters 键名
@export var is_boss := false

var level := 0
var waitting_time := 0.0       # 由关卡按难度设置:Easy rand(3,8)/Hard rand(0,2)/Hell 0
var last_attack_time := -99.0  # 出生即可攻
var max_distance := 2000.0

# ---- meta 字段(born 时读 defaults + 个体覆盖)----
var attack := 15.0
var attack_level_rate := 0.0
var attack_level_bonus := 0.0
var attack_cd := 5.0
var attack_cd_level_rate := 0.0
var attack_radius := 2.0
var max_hp := 30.0
var hp_level_rate := 0.0
var turn_speed := 2.0
var move_speed := 1.0
var move_speed_level_rate := 0.0
var hit_rate := 0.15
var attack_type := GlobalObject.AttackType.Phy
var idle2_anim := &"Idle02"
var attack_anims: Array = [&"Attack1"]
var damage_anim := &"Damage"
var dead_anim := &"Dead"
var dead_anim_speed := 1.0
var impact_tag := "Blood"
var life_active_time := 0.0  # >0 覆盖 MAX_LIFE_TIME(box=20s)

var hp := 0.0
var _state := State.IDLE
var _born_pos := Vector3.ZERO
var _life_time := 0.0
var _wait_time := 0.0
var _idle2_time := 0.0
var _meta: Dictionary = {}
var _anims_built := false

# 真实模型场景里有 "Model" 子节点时,特效目标(如 baotou 瞬移闪缩)作用于模型,否则退回占位 Body
@onready var _body: Node3D = get_node_or_null(^"Model") if has_node(^"Model") else $Body
@onready var _col: CollisionShape3D = $CollisionShape3D
@onready var _anim: AnimationPlayer = $AnimationPlayer
@onready var _audio: AudioStreamPlayer3D = $AudioStreamPlayer3D
@onready var _hp_bar: MonsterHP = $HpAnchor

func _ready() -> void:
	_deactivate()

func is_active() -> bool:
	return _state != State.IDLE

func is_dead() -> bool:
	return _state == State.DEAD

## 出生:读 meta → HP 满 → 计数+1 → 碰撞启用 → 进入等待期
func born(pos: Vector3, p_level := 0, p_waitting_time := 0.0) -> void:
	_load_meta()
	_build_placeholder_anims()
	level = p_level
	waitting_time = p_waitting_time
	last_attack_time = -99.0
	hp = get_max_hp()
	_born_pos = pos
	global_position = pos
	rotation = Vector3.ZERO
	velocity = Vector3.ZERO
	_life_time = 0.0
	_wait_time = 0.0
	_idle2_time = 0.0
	_state = State.WAITING
	visible = true
	set_process(true)
	set_physics_process(true)
	_col.set_deferred("disabled", false)
	_hp_bar.set_hp(1.0)
	PlayerSystem.cur_alive_monster += 1
	_on_born()
	if _anim.has_animation(idle2_anim):
		_anim.play(idle2_anim)

func _load_meta() -> void:
	var all := DataMgr.get_monster_meta()
	_meta = all.get("defaults", {}).merged(all.get("monsters", {}).get(meta_key, {}), true)
	attack = float(_meta.get("attack", 15.0))
	attack_level_rate = float(_meta.get("attack_level_rate", 0.0))
	attack_level_bonus = float(_meta.get("attack_level_bonus", 0.0))
	attack_cd = float(_meta.get("attack_cd", 5.0))
	attack_cd_level_rate = float(_meta.get("attack_cd_level_rate", 0.0))
	attack_radius = float(_meta.get("attack_radius", 2.0))
	max_hp = float(_meta.get("hp", 30.0))
	hp_level_rate = float(_meta.get("hp_level_rate", 0.0))
	turn_speed = float(_meta.get("turn_speed", 2.0))
	move_speed = float(_meta.get("move_speed", 1.0))
	move_speed_level_rate = float(_meta.get("move_speed_level_rate", 0.0))
	hit_rate = float(_meta.get("hit_rate", 0.15))
	attack_type = int(_meta.get("attack_type", 0))
	idle2_anim = StringName(_meta.get("idle2_anim", "Idle02"))
	attack_anims = []
	for a in _meta.get("attack_anims", ["Attack1"]):
		attack_anims.append(StringName(a))
	damage_anim = StringName(_meta.get("damage_anim", "Damage"))
	dead_anim = StringName(_meta.get("dead_anim", "Dead"))
	dead_anim_speed = float(_meta.get("dead_anim_speed", 1.0))
	impact_tag = str(_meta.get("impact_tag", "Blood"))
	life_active_time = float(_meta.get("life_active_time", 0.0))

# ---- 等级公式(6.1.6)----
func get_max_hp() -> float:
	return max_hp + (hp_level_rate * level if level > 0 else 0.0)

func get_move_speed() -> float:
	return move_speed + (move_speed_level_rate * level if level > 0 else 0.0)

func get_attack_cd() -> float:
	return attack_cd - (attack_cd_level_rate * level if level > 0 else 0.0)

func get_attack() -> float:
	return attack + (attack_level_bonus * level if level > 0 and attack_level_bonus > 0.0 else 0.0)

func _physics_process(delta: float) -> void:
	if GlobalObject.is_game_pause or _state == State.IDLE:
		return
	_life_time += delta
	var limit := life_active_time if life_active_time > 0.0 else MAX_LIFE_TIME
	if not is_boss and _state != State.DEAD and _life_time >= limit:
		_recycle()  # 超时自毁(无死亡演出,也会推进波次)
		return
	match _state:
		State.WAITING:
			_update_waiting(delta)
		State.ACTIVE:
			_update_active(delta)

## 等待期:持续下坠 3/s;每 5s 不在 idle2 则淡入(0.3s);超过 waitting_time 进追击
func _update_waiting(delta: float) -> void:
	_wait_time += delta
	_idle2_time += delta
	velocity.x = 0.0
	velocity.z = 0.0
	velocity.y = 0.0 if is_on_floor() else -FALL_SPEED
	move_and_slide()
	if _idle2_time >= IDLE2_INTERVAL:
		_idle2_time = 0.0
		if not is_current_anim(idle2_anim) and _anim.has_animation(idle2_anim):
			_anim.play(idle2_anim, 0.3)
	if _wait_time >= waitting_time:
		_state = State.ACTIVE
		_enter_active()

## 追击 + 攻击(6.1.3):目标=相机位置(y 压平)
func move_to_player_and_attack(delta: float, locomotion_name: StringName, p_max_distance: float) -> void:
	var cam := get_viewport().get_camera_3d()
	if cam == null:
		return
	if _is_playing_any(attack_anims) or (is_current_anim(damage_anim) and _anim.is_playing()):
		velocity.x = 0.0
		velocity.z = 0.0
		_apply_gravity(delta)
		move_and_slide()
		return
	var target := cam.global_position
	target.y = global_position.y
	var to_target := target - global_position
	var flat_dist := Vector2(to_target.x, to_target.z).length()
	var real_dist := global_position.distance_to(cam.global_position)
	if flat_dist < attack_radius and _attack_ready() and is_current_anim(locomotion_name) \
			and real_dist < p_max_distance:
		do_attack()
		return
	var dir := Vector3(to_target.x, 0.0, to_target.z)
	if dir.length_squared() > 0.0001:
		dir = dir.normalized()
		rotation.y = _yaw_towards(rotation.y, atan2(-dir.x, -dir.z), turn_speed * delta)
	var speed := get_move_speed()
	if flat_dist < 3.0:
		speed = minf(speed, 1.0)  # 3m 内速度压到 1
	velocity.x = dir.x * speed
	velocity.z = dir.z * speed
	_apply_gravity(delta)
	move_and_slide()
	if not is_current_anim(locomotion_name) and _anim.has_animation(locomotion_name):
		_anim.play(locomotion_name, 0.2)

## 随机播一个攻击动画(动画事件 event_attack 在 clip 0.3s 处由 Call Method Track 触发)
func do_attack() -> void:
	last_attack_time = Time.get_ticks_msec() / 1000.0
	_anim.play(attack_anims[randi() % attack_anims.size()], 0.1)

func _attack_ready() -> bool:
	return Time.get_ticks_msec() / 1000.0 - last_attack_time >= get_attack_cd()

## 动画事件(6.1.4):怪投到屏幕,x > 屏宽/2 → 右玩家,否则左玩家(转嫁在 PlayerSystem)
func event_attack() -> void:
	if _state == State.DEAD or _state == State.IDLE:
		return
	PlayerSystem.hit_player(get_attack(), attack_type, _pick_target_side())

func _pick_target_side() -> int:
	var cam := get_viewport().get_camera_3d()
	if cam == null or cam.is_position_behind(global_position):
		return PlayerSystem.Side.Right
	var sx := cam.unproject_position(global_position).x
	var w := get_viewport().get_visible_rect().size.x
	return PlayerSystem.Side.Right if sx > w * 0.5 else PlayerSystem.Side.Left

## 受击(与 M1 FireSystem 约定签名)。M3:Body 全伤;≤0 → 血花+dead+死亡音+关碰撞→1.5s 回收
func hit(p_attack: float, point: Vector3, hit_type: int, side: int) -> String:
	if _state == State.DEAD or _state == State.IDLE:
		return impact_tag
	hp -= p_attack
	MonsterHP.show_damage("- %d" % int(p_attack), point, self)
	_hp_bar.set_hp(maxf(hp, 0.0) / get_max_hp())
	if hp <= 0.0:
		_die()
	else:
		_play_sound("hurt")
		if _anim.has_animation(damage_anim):
			_anim.play(damage_anim, 0.1)
		_on_hurt(point, hit_type, side)
	return impact_tag

func _die() -> void:
	_state = State.DEAD
	velocity = Vector3.ZERO
	_col.set_deferred("disabled", true)
	_play_sound("dead")
	_spawn_blood_flower()
	if _anim.has_animation(dead_anim):
		_anim.play(dead_anim, 0.1, dead_anim_speed)
	_on_death()
	await get_tree().create_timer(RECYCLE_DELAY).timeout
	_recycle()

## 回收:隐藏 + 停 process + 计数-1,回对象池
func _recycle() -> void:
	if _state == State.IDLE:
		return
	PlayerSystem.cur_alive_monster -= 1
	_deactivate()
	died.emit(self)

func _deactivate() -> void:
	_state = State.IDLE
	visible = false
	velocity = Vector3.ZERO
	set_process(false)
	set_physics_process(false)
	_col.set_deferred("disabled", true)
	if _anim != null:
		_anim.stop()

## 刷怪点:相机 forward 绕 up 随机偏 ±max_fov,射线命中退 0.5m,未命中取最大距离点
func get_born_position(p_max_fov: float, p_max_length: float) -> Vector3:
	var cam := get_viewport().get_camera_3d()
	if cam == null:
		return global_position
	var forward := -cam.global_basis.z
	var flat := Vector3(forward.x, 0.0, forward.z)
	if flat.length_squared() < 0.001:
		flat = Vector3.FORWARD
	var dir := flat.normalized().rotated(Vector3.UP, deg_to_rad(randf_range(-p_max_fov, p_max_fov)))
	var from := cam.global_position
	var to := from + dir * p_max_length
	var query := PhysicsRayQueryParameters3D.create(from, to, BORN_RAY_MASK)
	var result := get_world_3d().direct_space_state.intersect_ray(query)
	var pos: Vector3 = to if result.is_empty() else result.position - dir * 0.5
	# 落点 y:真实地面探测(M4)——起点只抬 0.5m,避免命中吊灯/吊顶等悬挂碰撞;
	# 命中环境取命中 y,否则退回 0
	var ground_query := PhysicsRayQueryParameters3D.create(
		pos + Vector3(0.0, 0.5, 0.0), pos + Vector3(0.0, -2.5, 0.0), BORN_RAY_MASK)
	var ground := get_world_3d().direct_space_state.intersect_ray(ground_query)
	pos.y = ground.position.y if not ground.is_empty() else 0.0
	return pos

## 出生参数:meta born_override 优先(fly_axe=15/12、fat_zombie=30/12),否则用关卡默认
func get_born_params(default_fov: float, default_len: float) -> Vector2:
	if _meta.is_empty():
		_load_meta()
	var o: Dictionary = _meta.get("born_override", {})
	return Vector2(float(o.get("max_fov", default_fov)), float(o.get("max_length", default_len)))

## 面向相机(fly_axe 出生 / baotou 瞬移后)
func face_camera() -> void:
	var cam := get_viewport().get_camera_3d()
	if cam == null:
		return
	var dir := cam.global_position - global_position
	dir.y = 0.0
	if dir.length_squared() > 0.001:
		dir = dir.normalized()
		rotation.y = atan2(-dir.x, -dir.z)

# ---- 动画辅助(等价 Unity Animator.IsName)----
func is_current_anim(anim_name: StringName) -> bool:
	return _anim.current_animation == anim_name

func _is_playing_any(names: Array) -> bool:
	return _anim.is_playing() and names.has(StringName(_anim.current_animation))

## 攻击 clip 的动画事件方法;baotou 覆盖为 baotou_skill
func get_attack_event_method() -> StringName:
	return &"event_attack"

## 按 clip 名分派动画事件方法(默认全部走 get_attack_event_method;
## level2_boss 覆盖:Skill1/Skill2 分别走 hert_player_skill1/2)
func get_attack_event_method_for(_anim_name: StringName) -> StringName:
	return get_attack_event_method()

# ---- 派生钩子 ----
func _on_born() -> void:
	pass

func _enter_active() -> void:
	pass

func _update_active(delta: float) -> void:
	move_to_player_and_attack(delta, LOCOMOTION_ANIM, max_distance)

func _on_hurt(_point: Vector3, _hit_type: int, _side: int) -> void:
	pass

func _on_death() -> void:
	pass

func _apply_gravity(delta: float) -> void:
	velocity.y = 0.0 if is_on_floor() else velocity.y - GRAVITY * delta

## 转向(RotateTowards 语义,转身速度 turn_speed)
func _yaw_towards(current: float, target: float, max_delta: float) -> float:
	var diff := wrapf(target - current, -PI, PI)
	return current + clampf(diff, -max_delta, max_delta)

func _play_sound(sound_name: String) -> void:
	# 每怪独立目录优先 assets/audio/monsters/<meta_key>/<name>.{wav,ogg,mp3},
	# 再回退共享 assets/audio/monsters/<name>.{wav,ogg,mp3};全缺失静默
	for ext in ["wav", "ogg", "mp3"]:
		var own := "res://assets/audio/monsters/%s/%s.%s" % [meta_key, sound_name, ext]
		if ResourceLoader.exists(own):
			_audio.stream = load(own)
			_audio.play()
			return
	for ext in ["wav", "ogg", "mp3"]:
		var shared := "res://assets/audio/monsters/%s.%s" % [sound_name, ext]
		if ResourceLoader.exists(shared):
			_audio.stream = load(shared)
			_audio.play()
			return

## 血花:FireSystem 线性池,取第一个未激活挂怪身上(5.2 节)
func _spawn_blood_flower() -> void:
	var fs := get_tree().get_first_node_in_group("fire_system")
	if fs != null and fs.has_method("spawn_blood_flower"):
		fs.spawn_blood_flower(self, Vector3(0.0, 1.0, 0.0))

# ---- M3 占位动画:程序生成极简 clip,只补 AnimationPlayer 里不存在的 clip ----
# 真实 FBX 动画(tscn 挂的 AnimationLibrary)同名 clip 优先,占位不覆盖
func _build_placeholder_anims() -> void:
	if _anims_built:
		return
	_anims_built = true
	var lib := AnimationLibrary.new()
	var cfgs := {}
	cfgs[idle2_anim] = {"len": 1.2, "loop": true}
	cfgs[LOCOMOTION_ANIM] = {"len": 0.8, "loop": true}
	for a in attack_anims:
		cfgs[a] = {"len": 0.8, "call": get_attack_event_method_for(a), "call_time": 0.3}
	cfgs[damage_anim] = {"len": 0.4}
	cfgs[dead_anim] = {"len": 1.0}
	var extra_idle := StringName(_meta.get("idle_anim", ""))
	if extra_idle != &"":
		cfgs[extra_idle] = {"len": 1.0, "loop": true}
	var created := 0
	for anim_name in cfgs:
		if _anim.has_animation(anim_name):
			continue
		lib.add_animation(anim_name, _make_clip(cfgs[anim_name]))
		created += 1
	if created == 0:
		return
	if _anim.has_animation_library(""):
		# tscn 已挂真实动画库:占位 clip 合并进去(只含缺失项)
		var existing := _anim.get_animation_library("")
		for a in lib.get_animation_list():
			existing.add_animation(a, lib.get_animation(a))
	else:
		_anim.add_animation_library("", lib)

func _make_clip(cfg: Dictionary) -> Animation:
	var a := Animation.new()
	a.length = cfg["len"]
	if cfg.get("loop", false):
		a.loop_mode = Animation.LOOP_LINEAR
	var track := a.add_track(Animation.TYPE_VALUE)
	a.track_set_path(track, NodePath("Body:scale"))
	a.track_insert_key(track, 0.0, Vector3.ONE)
	a.track_insert_key(track, cfg["len"] * 0.5, Vector3(1.05, 0.95, 1.05))
	a.track_insert_key(track, cfg["len"], Vector3.ONE)
	if cfg.has("call"):
		var mt := a.add_track(Animation.TYPE_METHOD)
		a.track_set_path(mt, NodePath("."))
		a.track_insert_key(mt, cfg["call_time"], {"method": cfg["call"], "args": []})
	return a
