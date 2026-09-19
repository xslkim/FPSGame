extends Node3D
class_name FireWindow
## L2 FireWindow(7.2):Toon 怪的射击窗口节点。
## fire_monster = 当前占用窗口的怪(null/已回收 = 空窗);src_position = 翻窗爬入出生点。
## 关卡侧分配逻辑(7.2,Level2 脚本实现):每刷怪 tick 收集空窗口,均匀随机选一个,
## 分配后 w.fire_monster = toon;toon.fire_window = w;toon.born(w.get_src_position(), ...)。
## 窗口释放由 ToonMonster 在死亡/回收时自行完成。

var fire_monster: MonsterBase = null  # 占用字段

## 翻窗爬入出生点子节点(可选;缺省用窗口自身位置)
@export var src_position_path: NodePath = ^"SrcPosition"

func get_src_position() -> Vector3:
	var n := get_node_or_null(src_position_path) as Node3D
	return n.global_position if n != null else global_position

## 窗口是否空闲(占用怪死亡/回收后引用失效也算空)
func is_free() -> bool:
	return fire_monster == null or not is_instance_valid(fire_monster) \
		or not fire_monster.is_active()

## 释放占用(ToonMonster 死亡/回收时调用)
func release(p_monster: MonsterBase) -> void:
	if fire_monster == p_monster:
		fire_monster = null

## 7.2 分配策略:收集空窗口,均匀随机选一个;无空窗返回 null(关卡下 tick 重试)
static func pick_free(windows: Array) -> FireWindow:
	var free: Array = []
	for w in windows:
		if w is FireWindow and w.is_free():
			free.append(w)
	if free.is_empty():
		return null
	return free[randi() % free.size()]
