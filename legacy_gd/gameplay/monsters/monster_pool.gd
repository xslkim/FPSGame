extends Node3D
class_name MonsterPool
## 怪物对象池(5.2 语义的怪物版):按类型预实例化 N 个,
## 取用 = 随机起点环形扫描第一个未激活实例;回收即复用(怪自行 _recycle 回 IDLE)。

var _pools := {}  # meta_key -> Array[MonsterBase]

func register_type(key: String, scene: PackedScene, size: int) -> void:
	if not _pools.has(key):
		_pools[key] = []
	var arr: Array = _pools[key]
	while arr.size() < size:
		var m: MonsterBase = scene.instantiate()
		m.name = "%s_%d" % [key, arr.size()]
		add_child(m)
		arr.append(m)

## 随机起点环形扫描;池满(全部活跃)返回 null,调用方下一 tick 重试
func get_monster(key: String) -> MonsterBase:
	var arr: Array = _pools.get(key, [])
	if arr.is_empty():
		return null
	var start := randi() % arr.size()
	for i in arr.size():
		var m: MonsterBase = arr[(start + i) % arr.size()]
		if not m.is_active():
			return m
	return null

## 是否有未激活实例(补给箱"箱未激活才出"判定用,不消耗)
func has_inactive(key: String) -> bool:
	return get_monster(key) != null

func count_active(key: String) -> int:
	var n := 0
	for m in _pools.get(key, []):
		if m.is_active():
			n += 1
	return n
