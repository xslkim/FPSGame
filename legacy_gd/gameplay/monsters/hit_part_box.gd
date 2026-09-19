extends StaticBody3D
class_name MonsterHitPartBox
## 部位碰撞盒(M7):挂在怪身上的 StaticBody3D(layer 2),
## FireSystem 命中后转发本体 hit(),携带自身部位 HitType(Head/Armour)。
## 本体死亡/未激活时基类 hit() 直接返回 tag,天然安全。

@export var hit_type: GlobalObject.HitType = GlobalObject.HitType.Head

func hit(p_attack: float, point: Vector3, _hit_type: int, side: int) -> String:
	var m := get_parent()
	if m != null and m.has_method("hit"):
		return m.hit(p_attack, point, hit_type, side)
	return ""
