extends MonsterBase
class_name Level2BossMonster
## Level2Boss(6.3):
## 动画速度体系 Normal0.5/Attack0.3/Slow0.05,难度倍率由关卡设置(easy×1/hard×2.25/hell×4);
## 出场 0.8 秒震屏信号(entrance_shake,关卡侧接相机震动);
## 移动目标=相机位置+forward*12、y=-8、速度 4(直接 translate 不用碰撞移动),
## 朝目标以 5*dt 插值转身;到位后按 CD 攻击:
##   50% Skill1(物理单体,50% 选边,目标不活跃由 hit_player 转嫁换边)
##   50% Skill2(冰,雷柱特效激活 0.5s 后 Both,伤害×0.5);
## 只 HitType.Head 掉血;HitType.Armour → 播随机金属音效返回 "Metal" 不掉血;
## 受击罚 3 秒 CD(last_attack_time+=3);Damage02 状态中无敌;死亡不调回收(关卡处理)。

## 出场震屏信号(0.8s),关卡侧连接做相机震动
signal entrance_shake(duration: float)

const SPEED_NORMAL := 0.5
const SPEED_ATTACK := 0.3
const SPEED_SLOW := 0.05
const MOVE_SPEED := 4.0
const TURN_LERP := 5.0
const TARGET_FORWARD := 12.0
const TARGET_Y := -8.0
const ARRIVE_DIST := 0.5
const SKILL2_DELAY := 0.5
const SKILL2_DMG_RATE := 0.5
const HURT_CD_PENALTY := 3.0
const ENTRANCE_SHAKE_TIME := 0.8

const LIGHTNING_SCENE := preload("res://assets/effects/lightning_pillar.tscn")

## 难度动画倍率,由关卡设置(easy 1 / hard 2.25 / hell 4)
var anim_speed_rate := 1.0

var _arrived := false
var _lightning_fx: Node3D = null
var _lightning_time := 0.0

## M7:真实 clip 已随 .tscn 挂载,跳过占位(基类 add_animation_library("") 会冲突报错)
func _build_placeholder_anims() -> void:
	if _anim != null and _anim.has_animation(damage_anim):
		_anims_built = true
		return
	super()

func set_difficulty_anim_rate(rate: float) -> void:
	anim_speed_rate = rate

func _on_born() -> void:
	_arrived = false
	entrance_shake.emit(ENTRANCE_SHAKE_TIME)
	print("[L2Boss] entrance shake %.1fs" % ENTRANCE_SHAKE_TIME)
	_play_idle()

## Skill1/Skill2 各自的动画事件方法(基类占位 clip 按 clip 名分派)
func get_attack_event_method_for(anim_name: StringName) -> StringName:
	match anim_name:
		&"Skill1":
			return &"hert_player_skill1"
		&"Skill2":
			return &"hert_player_skill2"
	return super(anim_name)

func _update_active(delta: float) -> void:
	var cam := get_viewport().get_camera_3d()
	if cam == null:
		return
	if _lightning_time > 0.0:
		_lightning_time -= delta
		if _lightning_time <= 0.0 and _lightning_fx != null:
			_lightning_fx.visible = false
	if _is_playing_any(attack_anims) or (is_current_anim(damage_anim) and _anim.is_playing()):
		return
	var target := cam.global_position + (-cam.global_basis.z) * TARGET_FORWARD
	target.y = TARGET_Y
	if not _arrived:
		var to: Vector3 = target - global_position
		if to.length() < ARRIVE_DIST:
			_arrived = true
		else:
			# 速度 4 直接 translate(不用碰撞移动);朝目标 5*dt 插值转身
			global_position += to.normalized() * MOVE_SPEED * delta
			_lerp_face(to, delta)
			return
	# 到位:面向相机(5*dt 插值),按 CD 攻击
	var to_cam: Vector3 = cam.global_position - global_position
	_lerp_face(to_cam, delta)
	if not _anim.is_playing():
		_play_idle()
	if _attack_ready():
		last_attack_time = Time.get_ticks_msec() / 1000.0
		var skill := &"Skill1" if randf() < 0.5 else &"Skill2"  # 50% Skill1 / 50% Skill2
		_anim.play(skill, 0.1, SPEED_ATTACK * anim_speed_rate)

func _lerp_face(to: Vector3, delta: float) -> void:
	var flat := Vector2(to.x, to.z)
	if flat.length() < 0.001:
		return
	rotation.y = lerp_angle(rotation.y, atan2(-to.x, -to.z), TURN_LERP * delta)

func _play_idle() -> void:
	var idle_name := StringName(_meta.get("idle_anim", idle2_anim))
	if _anim.has_animation(idle_name):
		_anim.play(idle_name, 0.3, SPEED_NORMAL * anim_speed_rate)

## 动画事件 HertPlayerSkill1:物理单体,50% 选边(目标不活跃由 hit_player 转嫁换边)
func hert_player_skill1() -> void:
	if _state == State.DEAD or _state == State.IDLE:
		return
	var side: int = PlayerSystem.Side.Right if randf() < 0.5 else PlayerSystem.Side.Left
	PlayerSystem.hit_player(get_attack(), GlobalObject.AttackType.Phy, side)

## 动画事件 HertPlayerSkill2:冰,闪电特效占位激活,0.5s 后 Both,伤害×0.5
func hert_player_skill2() -> void:
	if _state == State.DEAD or _state == State.IDLE:
		return
	_activate_lightning()
	var atk := get_attack() * SKILL2_DMG_RATE
	get_tree().create_timer(SKILL2_DELAY).timeout.connect(func():
		if _state == State.DEAD or _state == State.IDLE:
			return
		PlayerSystem.hit_player(atk, GlobalObject.AttackType.Ice, PlayerSystem.Side.Both))

## 雷柱特效:lightning_pillar.tscn(3 帧随机闪电 + 底部光晕 + 闪白灯 + 雷声),
## 激活 0.5s 后隐藏(EffectBase 自隐,这里再兜底)
func _activate_lightning() -> void:
	if _lightning_fx == null:
		_lightning_fx = LIGHTNING_SCENE.instantiate()
		_lightning_fx.visible = false
		get_tree().current_scene.add_child(_lightning_fx)
	var cam := get_viewport().get_camera_3d()
	if cam != null:
		_lightning_fx.global_position = cam.global_position + (-cam.global_basis.z) * 4.0
	if _lightning_fx.has_method("activate"):
		_lightning_fx.activate(SKILL2_DELAY)
	else:
		_lightning_fx.visible = true
	_lightning_time = SKILL2_DELAY

## 只 HitType.Head 掉血;Armour → 随机金属音效 + 返回 "Metal" 不掉血;
## Damage02 状态中无敌;受击罚 3 秒 CD
func hit(p_attack: float, point: Vector3, hit_type: int, side: int) -> String:
	if _state == State.DEAD or _state == State.IDLE:
		return impact_tag
	if is_current_anim(damage_anim) and _anim.is_playing():
		return ""  # Damage02 状态中无敌
	if hit_type != GlobalObject.HitType.Head:
		if hit_type == GlobalObject.HitType.Armour:
			_play_sound("metal_%d" % (randi() % 3 + 1))  # 随机金属音效(资源缺失时静默)
		return "Metal"
	hp -= p_attack
	MonsterHP.show_damage("- %d" % int(p_attack), point, self)
	_hp_bar.set_hp(maxf(hp, 0.0) / get_max_hp())
	last_attack_time += HURT_CD_PENALTY  # 受击罚 3 秒 CD
	if hp <= 0.0:
		_die()
	else:
		_play_sound("hurt")
		if _anim.has_animation(damage_anim):
			_anim.play(damage_anim, 0.1, SPEED_NORMAL * anim_speed_rate)
		_on_hurt(point, hit_type, side)
	return impact_tag

## 死亡:播 dead(Slow 0.05 慢速体系),发 died 信号,不调回收(关卡处理胜利与清理)
func _die() -> void:
	_state = State.DEAD
	velocity = Vector3.ZERO
	_col.set_deferred("disabled", true)
	_play_sound("dead")
	_spawn_blood_flower()
	if _anim.has_animation(dead_anim):
		_anim.play(dead_anim, 0.1, SPEED_SLOW * anim_speed_rate)
	_on_death()
	died.emit(self)
