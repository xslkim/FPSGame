extends MonsterBase
class_name FatZombieMonster
## FatZombie(6.2):标准近战(基类 move_to_player_and_attack)。
## 出生固定 get_born_position(30, 12):由 meta born_override {max_fov:30, max_length:12}
## 经基类 get_born_params 提供,关卡刷怪路径统一调用。

## M7:真实 clip 已随 .tscn 挂载,跳过占位(基类 add_animation_library("") 会冲突报错)
func _build_placeholder_anims() -> void:
	if _anim != null and _anim.has_animation(damage_anim):
		_anims_built = true
		return
	super()
