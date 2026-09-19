extends MonsterBase
class_name FlyAxeZombieMonster
## 飞斧头僵尸(6.2):攻击半径 = 7+rand(0,3)(born 时定),
## 出生固定 get_born_position(15, 12)(meta born_override),面向相机出生。

func _on_born() -> void:
	attack_radius = float(_meta.get("attack_radius_base", 7.0)) \
		+ randf() * float(_meta.get("attack_radius_rand", 3.0))
	face_camera()
