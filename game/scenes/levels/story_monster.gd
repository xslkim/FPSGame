extends Node3D
## M7a 剧情怪容器:rock_warrior_anims.tres 的 atk01 带 Call Method Track(path "."),
## 解析到 AnimationPlayer root_node(= 本节点),battle 里由 monster_base 实现;
## 剧情场景不做伤害,空实现(对齐 battle 语义,避免运行时 method not found)。

func rock_attack() -> void:
	pass
