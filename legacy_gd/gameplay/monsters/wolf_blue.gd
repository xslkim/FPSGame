extends MonsterBase
class_name WolfBlueMonster
## WolfBlue(6.2):标准近战(基类 move_to_player_and_attack),
## 攻击动画 BiteAttack/ClawAttack,数值全走 meta(wolf_blue)。
## M7:Demon Wolf 真实模型;wolf_green.tscn 复用本脚本,仅换 body_material(绿皮)。

## M7 颜色变体材质(blue/green .tres),在 .tscn 配置
@export var body_material: Material = null

func _ready() -> void:
	super()
	if body_material != null:
		for mi in find_children("*", "MeshInstance3D", true, false):
			mi.set_surface_override_material(0, body_material)

## M7:真实 clip 已随 .tscn 挂载,跳过占位(基类 add_animation_library("") 会冲突报错)
func _build_placeholder_anims() -> void:
	if _anim != null and _anim.has_animation(damage_anim):
		_anims_built = true
		return
	super()
