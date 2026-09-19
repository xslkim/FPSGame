extends StaticBody3D
## M1 测试 3D 按钮:实现 on_shot() 协议(M5 枪打 UI 按钮同约定,难点 #6)。
## 被射线命中且扳机按下时由 FireSystem 调用(不耗弹)。

func on_shot() -> void:
	print("[TestButton] on_shot: %s" % name)
	var tw := create_tween()
	tw.tween_property(self, "scale", Vector3.ONE * 0.85, 0.05)
	tw.tween_property(self, "scale", Vector3.ONE, 0.1)
