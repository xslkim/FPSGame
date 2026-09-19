extends TextureRect
class_name JustRotate
## 移植 SCI-FI 包 JustRotate.cs:绕 Z 轴匀速旋转(度/秒),无限循环,无缓动。
## 用法:pivot_offset 需设为控件中心(size/2)后挂此脚本。

@export var speed := 20.0

func _ready() -> void:
	pivot_offset = size / 2.0

func _process(delta: float) -> void:
	rotation += deg_to_rad(speed) * delta
