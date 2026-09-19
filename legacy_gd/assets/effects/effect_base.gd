extends Node3D
class_name EffectBase
## 可复用特效场景基类(M7b):activate() 重启全部粒子、随机化弹孔/闪电帧、
## 播放自带音效,并按 lifetime 延迟自隐。fire_system 与各怪技能特效统一走这个接口。

@export var lifetime := 0.5

var _hide_tween: Tween


## 激活一次特效;p_lifetime>0 可覆盖自隐时长(血花用 1.2s)
func activate(p_lifetime := -1.0) -> void:
	visible = true
	var hole := get_node_or_null(^"Hole")
	if hole != null:
		hole.rotation.z = randf() * TAU  # 弹孔随机朝向
	# 多帧闪电/闪光片(Bolt1/Bolt2/...):随机选一帧显示
	var bolts: Array = []
	for c in get_children():
		if c.name.begins_with("Bolt"):
			bolts.append(c)
	if not bolts.is_empty():
		var pick: int = randi() % bolts.size()
		for i in bolts.size():
			bolts[i].visible = i == pick
	for p in find_children("*", "GPUParticles3D", true, false):
		p.restart()
	for s in find_children("*", "AudioStreamPlayer3D", true, false):
		s.play()
	if _hide_tween != null and _hide_tween.is_valid():
		_hide_tween.kill()
	_hide_tween = create_tween()
	_hide_tween.tween_interval(lifetime if p_lifetime < 0.0 else p_lifetime)
	_hide_tween.tween_callback(hide)
