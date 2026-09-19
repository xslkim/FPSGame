extends SceneTree
## M7 烘焙工具:把 @ 动画 FBX 的 clip 合并成每怪一个 AnimationLibrary(.tres),
## clip 重命名为 monster_meta.json 要求的名字,track 路径加 "Model/" 前缀
## (对应怪 .tscn 里模型实例节点名),attack clip 按比例挂 Call Method Track。
## 同时生成每怪材质 .tres(albedo 贴图手动指定)。
## 用法:godot --headless --path game -s res://tools/bake_monster_anims.gd

const BASE := "res://assets/models/monsters/"

# 每怪:模型 FBX、贴图、动画列表 [fbx相对路径, 目标clip名, 选项]
# 选项: loop=循环, call=Call Method Track 方法名, ratio=挂点时间比例
const MONSTERS := {
	"bull": {
		"model": "bull_king.FBX",
		"texture": "bull_king_c.png",
		"anims": [
			["anims/bull_king_idle.FBX", "Idle02", {"loop": true}],
			["anims/bull_king_walk.FBX", "locomotion", {"loop": true}],
			["anims/bull_king_attack_01.FBX", "attack_01", {"call": &"event_attack", "ratio": 0.6}],
			["anims/bull_king_attack_02.FBX", "attack_02", {"call": &"event_attack", "ratio": 0.6}],
			["anims/bull_king_attack_03.FBX", "attack_03", {"call": &"event_attack", "ratio": 0.6}],
			["anims/bull_king_damage.FBX", "damage", {}],
			["anims/bull_king_die.FBX", "die", {}],
		],
	},
	"axe_zombie": {  # fly_axe_zombie 复用本库(Skill 挂 event_attack)
		"model": "MonsterAxeZombie.FBX",
		"texture": "MonsterAxeZombi.tga",
		"anims": [
			["anims/MonsterAZ_Idle01.FBX", "Idle01", {"loop": true}],
			["anims/MonsterAZ_Idle02.FBX", "Idle02", {"loop": true}],
			["anims/MonsterAZ_Walk.FBX", "locomotion", {"loop": true}],
			["anims/MonsterAZ_Attack01.FBX", "Attack1", {"call": &"event_attack", "ratio": 0.6}],
			["anims/MonsterAZ_Attack02.FBX", "Attack2", {"call": &"event_attack", "ratio": 0.6}],
			["anims/MonsterAZ_AttackL.FBX", "AttackL", {"call": &"event_attack", "ratio": 0.6}],
			["anims/MonsterAZ_AttackR.FBX", "AttackR", {"call": &"event_attack", "ratio": 0.6}],
			["anims/MonsterAZ_Skill.FBX", "Skill", {"call": &"event_attack", "ratio": 0.6}],
			["anims/MonsterAZ_Damage01.FBX", "Damage1", {}],
			["anims/MonsterAZ_Damage02.FBX", "Damage2", {}],
			["anims/MonsterAZ_Damage03.FBX", "Damage3", {}],
			["anims/MonsterAZ_Die.FBX", "Die", {}],
		],
	},
	"skeleton": {
		"model": "MonsterSkeleton.FBX",
		"texture": "",  # 导入器已自动链接 MonsterSkeleton.png
		"anims": [
			["anims/MonsterS_Idle01.FBX", "Idle01", {"loop": true}],
			["anims/MonsterS_Idle02.FBX", "Idle02", {"loop": true}],
			["anims/MonsterS_Walk.FBX", "locomotion", {"loop": true}],
			["anims/MonsterS_Attack01.FBX", "Attack1", {"call": &"event_attack", "ratio": 0.6}],
			["anims/MonsterS_Attack02.FBX", "Attack2", {"call": &"event_attack", "ratio": 0.6}],
			["anims/MonsterS_Damage01.FBX", "Damage1", {}],
			["anims/MonsterS_Damage02.FBX", "Damage2", {}],
			["anims/MonsterS_Damage03.FBX", "Damage3", {}],
			["anims/MonsterS_Damage04.FBX", "Damage4", {}],
			["anims/MonsterS_Die.FBX", "Die", {}],
		],
	},
	"baotou": {
		"model": "Chr_Zcharacter_01.FBX",
		"texture": "tex_chr_albedo.png",
		"anims": [
			["anims/Anim_IDLE_01.FBX", "anim_idle", {"loop": true}],
			["anims/Anim_IDLE_01.FBX", "Idle02", {"loop": true}],
			["anims/Anim_WALK_01.FBX", "locomotion", {"loop": true}],
			["anims/Anim_ATTACK_01.FBX", "anim_attack", {"call": &"baotou_skill", "ratio": 0.6}],
			["anims/Anim_DEATH_01.FBX", "anim_death", {}],
		],
	},
	"box_monster": {
		"model": "treasure_chest_monster.FBX",
		"texture": "tcm_Blue.tga",
		"anims": [
			["anims/tcm_Idle.FBX", "Idle02", {"loop": true}],
			["anims/tcm_HopForward.FBX", "locomotion", {"loop": true}],
			["anims/tcm_TakeDamage.FBX", "TakeDamage", {}],
			["anims/tcm_Die.FBX", "Die", {}],
			["anims/tcm_BiteAttack.FBX", "BiteAttack", {}],
			["anims/tcm_LickAttack.FBX", "LickAttack", {}],
		],
	},
}

var _failed := false

func _init() -> void:
	for key in MONSTERS:
		_bake(key, MONSTERS[key])
	print("[bake] done, failed=%s" % _failed)
	quit(1 if _failed else 0)

func _err(msg: String) -> void:
	_failed = true
	printerr("[bake] ERROR: " + msg)

func _bake(key: String, cfg: Dictionary) -> void:
	var dir := BASE + key + "/"
	print("\n===== %s" % key)
	# 模型实例:校验 track 路径可解析
	var model: Node = ResourceLoader.load(dir + cfg["model"]).instantiate()
	if model == null:
		_err(key + ": model load failed")
		return
	var lib := AnimationLibrary.new()
	for entry in cfg["anims"]:
		var anim := _load_clip(dir + entry[0], key)
		if anim == null:
			continue
		var target: String = entry[1]
		var opts: Dictionary = entry[2]
		_retarget(anim, model, key, target)
		anim.resource_name = target
		if opts.get("loop", false):
			anim.loop_mode = Animation.LOOP_LINEAR
		if opts.has("call"):
			var mt := anim.add_track(Animation.TYPE_METHOD)
			anim.track_set_path(mt, NodePath("."))
			anim.track_insert_key(mt, anim.length * float(opts.get("ratio", 0.6)),
				{"method": opts["call"], "args": []})
			print("  clip %-12s len=%.2f call=%s@%.2fs" % [target, anim.length,
				opts["call"], anim.length * float(opts.get("ratio", 0.6))])
		else:
			print("  clip %-12s len=%.2f loop=%s" % [target, anim.length, opts.get("loop", false)])
		lib.add_animation(target, anim)
	model.free()
	var err := ResourceSaver.save(lib, dir + key + "_anims.tres")
	if err != OK:
		_err("%s: save anims failed %d" % [key, err])
	# 材质
	var tex_path: String = cfg.get("texture", "")
	if tex_path != "":
		var mat := StandardMaterial3D.new()
		mat.albedo_texture = ResourceLoader.load(dir + tex_path)
		mat.resource_name = key + "_mat"
		err = ResourceSaver.save(mat, dir + key + "_mat.tres")
		if err != OK:
			_err("%s: save material failed %d" % [key, err])

func _load_clip(path: String, key: String) -> Animation:
	var root: Node = ResourceLoader.load(path).instantiate()
	if root == null:
		_err(key + ": anim fbx load failed " + path)
		return null
	var ap := _find_anim_player(root)
	if ap == null:
		_err(key + ": no AnimationPlayer in " + path)
		root.free()
		return null
	var names := ap.get_animation_list()
	if names.is_empty():
		_err(key + ": no animations in " + path)
		root.free()
		return null
	var clip: Animation = ap.get_animation(names[0]).duplicate(true)
	root.free()
	return clip

func _find_anim_player(n: Node) -> AnimationPlayer:
	if n is AnimationPlayer:
		return n
	for c in n.get_children():
		var r := _find_anim_player(c)
		if r != null:
			return r
	return null

## track 路径加 Model/ 前缀;模型里不存在的节点 track 直接删除(如 Footsteps 标记)
func _retarget(anim: Animation, model: Node, key: String, clip_name: String) -> void:
	for ti in range(anim.get_track_count() - 1, -1, -1):
		var p := str(anim.track_get_path(ti))
		var node_part := p.get_slice(":", 0)
		if not model.has_node(node_part):
			print("  [info] %s/%s: 删除不可解析 track %s" % [key, clip_name, node_part])
			anim.remove_track(ti)
			continue
		anim.track_set_path(ti, NodePath("Model/" + p))
