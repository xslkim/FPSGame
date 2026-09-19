extends SceneTree
## M7a 工具:为学生演员模型烘焙"剪影级"K-POP 舞蹈循环 AnimationLibrary。
##
## 简化声明(方案 7.4):原作 K-POP Dance 1.anim 是 Unity humanoid muscle 曲线
## (m_FloatCurves + 数字肌肉 attribute,200.8s),映射到 Godot 骨架需完整 humanoid
## 重定向链,成本过高;按任务许可改为程序化舞蹈:在 rest 姿态上叠加节拍化的
## 摆臂/扭腰/点头/弹跳,4s 循环,远景剪影可读。
##
## 用法: godot --headless --path game -s tools/build_story_anims.gd

const OUT := "res://assets/models/actors"

# 每个演员:模型 FBX、贴图(运行时由 level1_story.gd 盖材质)、节拍相位错开
var ACTORS := {
	"f05_schoolwear": {
		"model": OUT + "/f05_schoolwear/f05_schoolwear_200_m.fbx",
		"out": OUT + "/f05_schoolwear/f05_schoolwear_anims.tres",
	},
	"casual_dressed_girl": {
		"model": OUT + "/casual_dressed_girl/casual_dressed_girl.FBX",
		"out": OUT + "/casual_dressed_girl/casual_dressed_girl_anims.tres",
	},
	"blade_girl": {
		"model": OUT + "/blade_girl/blade_girl.FBX",
		"out": OUT + "/blade_girl/blade_girl_anims.tres",
	},
}

var _fail := false

func _init() -> void:
	for key in ACTORS:
		_build(key, ACTORS[key])
	print("[STORY-BUILD] done, failed=%s" % _fail)
	quit(1 if _fail else 0)

func _build(key: String, cfg: Dictionary) -> void:
	var ps: PackedScene = load(cfg["model"])
	if ps == null:
		push_error("[STORY-BUILD] %s: model load failed" % key)
		_fail = true
		return
	var model := ps.instantiate()
	var sk: Skeleton3D = null
	for n in _find(model, "Skeleton3D"):
		sk = n
		break
	if sk == null:
		push_error("[STORY-BUILD] %s: no skeleton" % key)
		_fail = true
		model.free()
		return
	var p := str(model.get_path_to(sk))
	print("[STORY-BUILD] %s skeleton=%s bones=%d" % [key, p, sk.get_bone_count()])
	var lib := AnimationLibrary.new()
	lib.add_animation(&"dance", _make_dance(sk, p))
	var err := ResourceSaver.save(lib, cfg["out"])
	print("[STORY-BUILD] %s -> %s err=%d" % [key, cfg["out"], err])
	if err != OK:
		_fail = true
	model.free()

## 4s 八拍舞蹈循环:BPM≈120(每拍 0.5s)。所有旋转为 rest 姿态叠加欧拉偏移。
func _make_dance(sk: Skeleton3D, sk_path: String) -> Animation:
	var anim := Animation.new()
	anim.length = 4.0
	anim.loop_mode = Animation.LOOP_LINEAR
	var T := [0.0, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0]
	var mk := func(bone: String) -> String: return "%s:%s" % [sk_path, bone]

	# --- 匹配骨骼(大小写不敏感,命中第一组可用别名) ---
	var b_hips := _find_bone(sk, ["hips", "pelvis", "bip001", "root"])
	var b_spine := _find_bone(sk, ["spine", "spine1", "bip001 spine", "spine_01"])
	var b_chest := _find_bone(sk, ["chest", "spine2", "upperchest", "spine_02", "spine1"])
	var b_head := _find_bone(sk, ["head", "bip001 head"])
	var b_arm_l := _find_bone(sk, ["leftarm", "upperarm_l", "l upperarm", "bip001 l upperarm", "left upper arm", "upperarm.l"])
	var b_arm_r := _find_bone(sk, ["rightarm", "upperarm_r", "r upperarm", "bip001 r upperarm", "right upper arm", "upperarm.r"])
	var b_fore_l := _find_bone(sk, ["leftforearm", "lowerarm_l", "l forearm", "left fore arm", "forearm.l", "lowerarm.l"])
	var b_fore_r := _find_bone(sk, ["rightforearm", "lowerarm_r", "r forearm", "right fore arm", "forearm.r", "lowerarm.r"])
	var b_leg_l := _find_bone(sk, ["leftupleg", "upleg_l", "l thigh", "left thigh", "thigh_l", "upleg.l"])
	var b_leg_r := _find_bone(sk, ["rightupleg", "upleg_r", "r thigh", "right thigh", "thigh_r", "upleg.r"])

	# --- 髋部:每拍弹跳(位置在 rest 上叠加)+ 左右扭 ---
	if b_hips != "":
		_pos_bounce(anim, sk, mk.call(b_hips), T, 0.05)
		_rot(anim, sk, mk.call(b_hips), T,
			[0, 0.12, 0, -0.12, 0, 0.12, 0, -0.12, 0].map(func(v): return Vector3(0, v, 0)))
	# --- 脊柱/胸:反相位摆动 ---
	if b_spine != "":
		_rot(anim, sk, mk.call(b_spine), T,
			[0, -0.1, 0, 0.1, 0, -0.1, 0, 0.1, 0].map(func(v): return Vector3(0.06, v, 0)))
	if b_chest != "" and b_chest != b_spine:
		_rot(anim, sk, mk.call(b_chest), T,
			[0, 0.14, 0, -0.14, 0, 0.14, 0, -0.14, 0].map(func(v): return Vector3(0, v, 0.1)))
	# --- 头:打点式俯仰 ---
	if b_head != "":
		_rot(anim, sk, mk.call(b_head), T,
			[0, 0.15, 0, 0.15, 0, -0.08, 0, -0.08, 0].map(func(v): return Vector3(v, 0, 0)))
	# --- 手臂:前 2s 平举开合,后 2s 上举挥舞 ---
	if b_arm_l != "":
		_rot(anim, sk, mk.call(b_arm_l), T,
			[Vector3(0, 0, 0.9), Vector3(0, 0, 1.35), Vector3(0, 0, 0.9), Vector3(0, 0, 1.35), Vector3(0, 0, 2.4), Vector3(0, -0.5, 2.6), Vector3(0, 0, 2.4), Vector3(0, 0.5, 2.6), Vector3(0, 0, 0.9)])
	if b_arm_r != "":
		_rot(anim, sk, mk.call(b_arm_r), T,
			[Vector3(0, 0, -0.9), Vector3(0, 0, -1.35), Vector3(0, 0, -0.9), Vector3(0, 0, -1.35), Vector3(0, 0, -2.4), Vector3(0, 0.5, -2.6), Vector3(0, 0, -2.4), Vector3(0, -0.5, -2.6), Vector3(0, 0, -0.9)])
	# --- 前臂:随拍屈伸 ---
	if b_fore_l != "":
		_rot(anim, sk, mk.call(b_fore_l), T,
			[0.3, 0.5, 0.3, 0.5, 0.9, 0.4, 0.9, 0.4, 0.3].map(func(v): return Vector3(v, 0, 0)))
	if b_fore_r != "":
		_rot(anim, sk, mk.call(b_fore_r), T,
			[0.3, 0.5, 0.3, 0.5, 0.9, 0.4, 0.9, 0.4, 0.3].map(func(v): return Vector3(v, 0, 0)))
	# --- 大腿:小幅踏步 ---
	if b_leg_l != "":
		_rot(anim, sk, mk.call(b_leg_l), T,
			[0, -0.18, 0, 0, 0, -0.18, 0, 0, 0].map(func(v): return Vector3(v, 0, 0)))
	if b_leg_r != "":
		_rot(anim, sk, mk.call(b_leg_r), T,
			[0, 0, 0, -0.18, 0, 0, 0, -0.18, 0].map(func(v): return Vector3(v, 0, 0)))
	return anim

## 大小写不敏感骨骼名匹配;返回 "" 表示未找到。
func _find_bone(sk: Skeleton3D, aliases: Array) -> String:
	var names := []
	for i in sk.get_bone_count():
		names.append(sk.get_bone_name(i))
	var lower: Array[String] = []
	for n in names:
		lower.append(String(n).to_lower().replace(" ", "").replace("_", "").replace(".", "").replace(":", ""))
	for a in aliases:
		var key := String(a).to_lower().replace(" ", "").replace("_", "").replace(".", "").replace(":", "")
		var i := lower.find(key)
		if i >= 0:
			return String(names[i])
		# 包含匹配(如 "bip001spine1" 含 "spine1")
		if key.length() >= 4:
			for i2 in lower.size():
				if lower[i2].contains(key):
					return String(names[i2])
	return ""

func _rot(anim: Animation, sk: Skeleton3D, path: String, times: Array, eulers: Array) -> void:
	var bone := String(path).split(":")[-1]
	var idx := sk.find_bone(bone)
	if idx < 0:
		push_error("[STORY-BUILD] bone missing: " + bone)
		_fail = true
		return
	var rest_q := sk.get_bone_rest(idx).basis.get_rotation_quaternion()
	var t := anim.add_track(Animation.TYPE_ROTATION_3D)
	anim.track_set_path(t, NodePath(path))
	anim.track_set_interpolation_type(t, Animation.INTERPOLATION_CUBIC)
	for i in times.size():
		var q := rest_q * Quaternion.from_euler(eulers[i])
		anim.track_insert_key(t, times[i], q)

func _pos_bounce(anim: Animation, sk: Skeleton3D, path: String, times: Array, amp: float) -> void:
	var bone := String(path).split(":")[-1]
	var idx := sk.find_bone(bone)
	if idx < 0:
		return
	var rest_p: Vector3 = sk.get_bone_rest(idx).origin
	var t := anim.add_track(Animation.TYPE_POSITION_3D)
	anim.track_set_path(t, NodePath(path))
	anim.track_set_interpolation_type(t, Animation.INTERPOLATION_CUBIC)
	for i in times.size():
		var hop := 0.0 if i % 2 == 0 else amp
		anim.track_insert_key(t, times[i], rest_p + Vector3(0, hop, 0))

func _find(n: Node, cls: String) -> Array:
	var out := []
	if n.is_class(cls):
		out.append(n)
	for c in n.get_children():
		out.append_array(_find(c, cls))
	return out
