extends StaticBody3D
## M1 测试靶:实现怪物 hit() 协议(M3 MonsterBase 同签名,见 6.1 Hit)。
## 变色反馈:HP 越低越暗,打空变黑。

@export var max_hp := 50.0

var hp: float

@onready var _mesh: MeshInstance3D = $MeshInstance3D
var _mat: StandardMaterial3D

func _ready() -> void:
	hp = max_hp
	_mat = StandardMaterial3D.new()
	_mat.albedo_color = Color(0.85, 0.2, 0.15)
	_mesh.material_override = _mat

## 返回特效 tag(敌人默认 Blood;骷髅/包头=Concrete、Level2Boss=Metal 等由 M3 怪物类定义)
func hit(attack: float, point: Vector3, hit_type: int, side: int) -> String:
	hp -= attack
	print("[TestEnemy] %s hit: attack=%.1f hp=%.1f hit_type=%d side=%d point=%s" % [
		name, attack, hp, hit_type, side, point])
	var t := clampf(hp / max_hp, 0.0, 1.0)
	_mat.albedo_color = Color(0.85, 0.2, 0.15).lerp(Color(0.1, 0.1, 0.1), 1.0 - t)
	if hp <= 0.0:
		print("[TestEnemy] %s destroyed" % name)
		_mat.albedo_color = Color(0.05, 0.05, 0.05)
	return "Blood"
