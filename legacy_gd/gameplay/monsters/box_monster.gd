extends MonsterBase
class_name BoxMonster
## 牙齿宝箱(6.2/6.3 BoxMonster):HP1、移速 3、20 秒自毁(meta life_active_time)。
## 逃跑跳:每 2 秒反向跳一次。死亡掉落按箱类型:
## Bullet → +60 弹(DataMgr.box_bullet);GunAK/GunM4 → add_gun 解锁。

enum BoxKind { Bullet, GunAK, GunM4 }

const HOP_INTERVAL := 2.0
const HOP_VY := 4.0

const PICKUP_SCENE := preload("res://assets/effects/pickup_drop.tscn")

@export var box_kind := BoxKind.Bullet

var _move_dir := Vector3.FORWARD
var _hop_time := 0.0

func _on_born() -> void:
	# 初始逃跑方向:背向相机
	var cam := get_viewport().get_camera_3d()
	if cam != null:
		var d := global_position - cam.global_position
		d.y = 0.0
		if d.length_squared() > 0.01:
			_move_dir = d.normalized()
	_hop_time = 0.0

func _update_active(delta: float) -> void:
	_hop_time += delta
	if _hop_time >= HOP_INTERVAL:
		_hop_time = 0.0
		_move_dir = -_move_dir  # 每 2 秒反向
		if is_on_floor():
			velocity.y = HOP_VY  # 跳
	velocity.x = _move_dir.x * get_move_speed()
	velocity.z = _move_dir.z * get_move_speed()
	_apply_gravity(delta)
	move_and_slide()
	if _move_dir.length_squared() > 0.01:
		rotation.y = _yaw_towards(rotation.y, atan2(-_move_dir.x, -_move_dir.z),
			turn_speed * delta)

## 掉落(6.3):对所有活跃玩家生效;死亡点放 FX_Pickup_Heart 风格掉落光效
func _on_death() -> void:
	var fx: Node3D = PICKUP_SCENE.instantiate()
	get_tree().current_scene.add_child(fx)
	fx.global_position = global_position + Vector3(0.0, 0.5, 0.0)
	if fx.has_method("activate"):
		fx.activate()
	for side in [PlayerSystem.Side.Left, PlayerSystem.Side.Right]:
		var p := PlayerSystem.get_player(side)
		if p == null or not p.active:
			continue
		match box_kind:
			BoxKind.Bullet:
				p.bullet += DataMgr.box_bullet
			BoxKind.GunAK:
				p.add_gun(0)
			BoxKind.GunM4:
				p.add_gun(1)
	PlayerSystem.notify_ui_changed()
	print("[BoxMonster] drop kind=%d applied" % box_kind)
