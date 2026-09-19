extends MonsterBase
class_name BaotouMonster
## 包头僵尸(L1 Boss,6.3):不移动(缓降 0.5/s);CD 到且处于 idle → 播 attack;
## 动画事件 baotou_skill 发火球(演出弹道 + 1s 定时命中 Both);
## 被打 0.5s 后在出生点 x±1.8 / z±2 随机瞬移并面向相机。

const SINK_SPEED := 0.5
const TELEPORT_DELAY := 0.5
const TELEPORT_MIN_X := 0.5
const TELEPORT_MAX_X := 1.8
const TELEPORT_MIN_Z := 0.5
const TELEPORT_MAX_Z := 2.0

const TELEPORT_SCENE := preload("res://assets/effects/teleport_flash.tscn")

var _teleport_tween: Tween


## 瞬移闪现:原位置 + 新位置各放一发 teleport_flash
func _flash_at(pos: Vector3) -> void:
	var fx: Node3D = TELEPORT_SCENE.instantiate()
	get_tree().current_scene.add_child(fx)
	fx.global_position = pos + Vector3(0.0, 1.0, 0.0)
	if fx.has_method("activate"):
		fx.activate()

func _on_born() -> void:
	is_boss = true  # Boss 不超时自毁

func get_attack_event_method() -> StringName:
	return &"baotou_skill"

func _update_active(delta: float) -> void:
	velocity.x = 0.0
	velocity.z = 0.0
	velocity.y = 0.0 if is_on_floor() else -SINK_SPEED
	move_and_slide()
	if _is_playing_any(attack_anims):
		return
	var idle_name := StringName(_meta.get("idle_anim", idle2_anim))
	if not is_current_anim(idle_name) and _anim.has_animation(idle_name):
		_anim.play(idle_name, 0.3)
	if _attack_ready():
		do_attack()

## 动画事件 BaotouSkill:发火球(BaotouFireball,6.3)
func baotou_skill() -> void:
	if _state == State.DEAD or _state == State.IDLE:
		return
	Fireball.spawn(get_tree().current_scene, global_position + Vector3(0.0, 1.2, 0.0),
		get_attack(), attack_type)

func _on_hurt(_point: Vector3, _hit_type: int, _side: int) -> void:
	if _teleport_tween != null and _teleport_tween.is_valid():
		_teleport_tween.kill()
	_teleport_tween = create_tween()
	_teleport_tween.tween_interval(TELEPORT_DELAY)
	_teleport_tween.tween_callback(_teleport)

func _teleport() -> void:
	if _state == State.DEAD or _state == State.IDLE:
		return
	_flash_at(global_position)  # 消失点闪现
	# 闪缩一下(保留原占位表现的体量反馈)
	var blink := create_tween()
	blink.tween_property(_body, "scale", Vector3.ONE * 1.3, 0.08)
	blink.tween_property(_body, "scale", Vector3.ONE, 0.12)
	var ox := randf_range(TELEPORT_MIN_X, TELEPORT_MAX_X) * (1.0 if randf() > 0.5 else -1.0)
	var oz := randf_range(TELEPORT_MIN_Z, TELEPORT_MAX_Z) * (1.0 if randf() > 0.5 else -1.0)
	global_position = _born_pos + Vector3(ox, 0.0, oz)
	face_camera()
	_flash_at(global_position)  # 出现点闪现
