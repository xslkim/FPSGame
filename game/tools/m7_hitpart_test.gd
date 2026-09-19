extends Node3D
## M7 部位碰撞盒转发功能测试(无 GPU,headless 可跑)。
func _ready() -> void:
	var fail := false
	var b: MonsterBase = load("res://gameplay/monsters/level2_boss.tscn").instantiate()
	add_child(b)
	b.born(Vector3(0, 0, 20), 0, 99.0)
	var hp0: float = b.hp
	var r2: String = b.get_node("BoxArmour").hit(10.0, b.global_position, GlobalObject.HitType.Body, 0)
	print("[HITPART] %s: boss BoxArmour 转发 Armour → hp %.0f→%.0f, ret=%s(期待不变, Metal)" % [
		"PASS" if b.hp == hp0 and r2 == "Metal" else "FAIL", hp0, b.hp, r2])
	fail = fail or not (b.hp == hp0 and r2 == "Metal")
	var r1: String = b.get_node("BoxHead").hit(10.0, b.global_position, GlobalObject.HitType.Body, 0)
	print("[HITPART] %s: boss BoxHead 转发 Head → hp %.0f→%.0f, ret=%s(期待 -10, Metal)" % [
		"PASS" if b.hp == hp0 - 10.0 and r1 == "Metal" else "FAIL", hp0, b.hp, r1])
	fail = fail or not (b.hp == hp0 - 10.0 and r1 == "Metal")
	var w: MonsterBase = load("res://gameplay/monsters/wolf_blue.tscn").instantiate()
	add_child(w)
	w.born(Vector3(0, 0, -14), 0, 99.0)
	var whp: float = w.hp
	var r3: String = w.get_node("BoxHead").hit(5.0, w.global_position, GlobalObject.HitType.Body, 0)
	print("[HITPART] %s: wolf_blue BoxHead 转发 Head → hp %.0f→%.0f, ret=%s(期待 -5, Blood)" % [
		"PASS" if w.hp == whp - 5.0 and r3 == "Blood" else "FAIL", whp, w.hp, r3])
	fail = fail or not (w.hp == whp - 5.0 and r3 == "Blood")
	print("[HITPART] done, failed=%s" % fail)
	get_tree().quit(1 if fail else 0)
