extends Node3D
## M7a Level1 剧情开场场景:学校走廊学生跳 K-POP 舞 → RockWarrior 冲入 → 转战斗。
## 对应原作 Level1.unity StoryStartTimeline(~18.3s:11×Cinemachine vcam + Timeline)。
##
## 简化声明(方案 7.4 / 任务书许可):
##  - 舞蹈:原作 K-POP Dance 1.anim 是 Unity humanoid muscle 曲线(m_FloatCurves,
##    数字 attribute ID,200.8s),迁到 Godot 需完整 humanoid 重定向链,成本过高;
##    改为 tools/build_story_anims.gd 程序化烘焙的 4s 八拍"剪影级"舞蹈循环。
##  - 镜头:原作 11 个 vcam 简化为 AnimationPlayer 6 个关键帧 4 个镜头
##    (开场全景 → 推近舞者 → 怪冲入侧跟 → 越肩定格)。
##  - 学生反应(原作逃离/倒地)未做,怪冲入后舞者减速为"僵住"处理。
##
## 调试:user args:
##   --m5-selftest / --m7-story-skip  直接跳战斗(自检/手动)
##   --story-ts=N                     Engine.time_scale=N(加速验证)
##   --m7-story-shots                 GPU 截图模式(tools/screenshots/story_*.png,结束 quit)

const LOADING := "res://scenes/ui/loading.tscn"
const LEVEL1_BATTLE := "res://scenes/levels/level1_battle.tscn"
const SHOT_DIR := "res://tools/screenshots/"

const STORY_DURATION := 18.5
const MONSTER_ENTER_T := 7.5   # RockWarrior 冲入时刻(尖叫/呼救同步)
const MONSTER_STOP_T := 11.0   # 冲到学生面前
const FADE_T := 17.3           # 淡出开始

# 相机镜头关键帧:[time, pos, look_at](4 镜头:全景 / 推近 / 侧跟 / 越肩定格)
const SHOTS := [
	[0.0, Vector3(0.0, 2.2, -6.0), Vector3(0.0, 1.2, 20.5)],
	[5.0, Vector3(0.4, 1.7, 12.0), Vector3(-0.2, 1.1, 20.5)],
	[7.5, Vector3(0.3, 2.6, -1.0), Vector3(0.0, 1.3, 7.0)],
	[11.0, Vector3(0.3, 2.6, 10.5), Vector3(0.0, 1.3, 18.0)],
	[11.5, Vector3(0.1, 1.6, 24.5), Vector3(0.0, 1.25, 17.2)],
	[17.3, Vector3(0.1, 1.7, 23.4), Vector3(0.0, 1.3, 17.0)],
]

# 学生演员:原作 Level1.unity 摆位(走廊中段 z≈20.5),面向 -z(镜头方向)
const DANCERS := [
	{"actor": "blade_girl", "pos": Vector3(-0.75, 0.0, 20.5), "rot_y": 180.0},
	{"actor": "casual_dressed_girl", "pos": Vector3(0.27, 0.0, 21.8), "rot_y": 180.0},
	{"actor": "f05_schoolwear", "pos": Vector3(1.14, 0.0, 20.5), "rot_y": 180.0},
]

# 每演员:模型/动画库/经验缩放(FBX 单位不一,运行时再打印世界高度校准)
const ACTORS := {
	"blade_girl": {
		"model": "res://assets/models/actors/blade_girl/blade_girl.FBX",
		"anims": "res://assets/models/actors/blade_girl/blade_girl_anims.tres",
		"scale": 0.01,
	},
	"casual_dressed_girl": {
		"model": "res://assets/models/actors/casual_dressed_girl/casual_dressed_girl.FBX",
		"anims": "res://assets/models/actors/casual_dressed_girl/casual_dressed_girl_anims.tres",
		"scale": 120.0,
	},
	"f05_schoolwear": {
		"model": "res://assets/models/actors/f05_schoolwear/f05_schoolwear_200_m.fbx",
		"anims": "res://assets/models/actors/f05_schoolwear/f05_schoolwear_anims.tres",
		"scale": 1.0,
	},
}

const TEX_FACE := "res://assets/models/actors/f05_schoolwear/f05_face_00_m.png"
const TEX_F05 := "res://assets/models/actors/f05_schoolwear/f05_schoolwear_200_m.png"
const TEX_CASUAL := "res://assets/models/actors/casual_dressed_girl/casual_dressed_girl.png"
const TEX_BLADE := "res://assets/models/actors/blade_girl/blade_girl_base.png"

const ROCK_MODEL := "res://assets/models/monsters/rock_warrior/RockWarrior.FBX"
const ROCK_ANIMS := "res://assets/models/monsters/rock_warrior/rock_warrior_anims.tres"
const ROCK_MAT := "res://assets/models/monsters/rock_warrior/rock_warrior_mat.tres"
const MONSTER_SCRIPT := "res://scenes/levels/story_monster.gd"

var _args: Array = OS.get_cmdline_user_args()
var _skipped := false
var _shots_mode := false
var _dancer_players: Array[AnimationPlayer] = []
var _monster: Node3D
var _monster_ap: AnimationPlayer
var _music: AudioStreamPlayer
var _fade: ColorRect

func _ready() -> void:
	GlobalObject.scene_state = GlobalObject.GameState.UI  # 剧情:禁射击,不挂 FireSystem
	_shots_mode = _args.has("--m7-story-shots")
	var ti := _args.find("--story-ts")
	if ti >= 0 and ti + 1 < _args.size():
		Engine.time_scale = float(_args[ti + 1])
	_music = get_node("Music")
	_fade = get_node("FadeLayer/FadeRect")
	_build_camera_anim()
	_build_dancers()
	_build_monster()
	var ci := _args.find("--story-cam")
	if ci >= 0 and ci + 1 < _args.size():
		_test_cam(_args[ci + 1])
		return
	if _args.has("--m5-selftest") or _args.has("--m7-story-skip"):
		_skip_to_battle()
		return
	_run_story()

## 一次性机位校准:--story-cam "x,y,z,lx,ly,lz" 单拍退出
func _test_cam(spec: String) -> void:
	_shots_mode = true
	get_node("CamPlayer").stop()
	var v := spec.split(",")
	var pos := Vector3(float(v[0]), float(v[1]), float(v[2]))
	var look := Vector3(float(v[3]), float(v[4]), float(v[5]))
	var cam: Camera3D = get_node("Camera3D")
	cam.global_transform = Transform3D(Transform3D().looking_at(look - pos, Vector3.UP).basis, pos)
	# 怪放到冲锋中段便于取景
	_monster.visible = true
	_monster.position.z = 12.0
	_monster_ap.play(&"locomotion")
	await _shot("story_test")
	get_tree().quit(0)

## 相机镜头:AnimationPlayer 关键帧。3D 轨 cubic 在本构建上关键帧间姿态发散,
## 改为 0.2s 密集线性采样(镜头间 pos/look 自行 lerp,平滑且确定)。
func _build_camera_anim() -> void:
	var cam_player: AnimationPlayer = get_node("CamPlayer")
	var anim := Animation.new()
	anim.length = STORY_DURATION
	var tp := anim.add_track(Animation.TYPE_POSITION_3D)
	var tr := anim.add_track(Animation.TYPE_ROTATION_3D)
	anim.track_set_path(tp, NodePath("Camera3D"))
	anim.track_set_path(tr, NodePath("Camera3D"))
	var step := 0.2
	var t := 0.0
	while t <= STORY_DURATION + 0.001:
		var pose := _sample_shots(t)
		anim.track_insert_key(tp, t, pose[0])
		anim.track_insert_key(tr, t, pose[1])
		t += step
	var lib := AnimationLibrary.new()
	lib.add_animation(&"cam", anim)
	cam_player.add_animation_library(&"story", lib)
	cam_player.play(&"story/cam")

## 在 SHOTS 关键帧间线性插值 pose:[pos, quat](look_at 逐帧重算,转向连续)
func _sample_shots(t: float) -> Array:
	var i := 0
	while i < SHOTS.size() - 2 and t > SHOTS[i + 1][0]:
		i += 1
	var a: Array = SHOTS[i]
	var b: Array = SHOTS[i + 1]
	var u := 0.0 if b[0] <= a[0] else clampf((t - a[0]) / (b[0] - a[0]), 0.0, 1.0)
	var pos: Vector3 = a[1].lerp(b[1], u)
	var look: Vector3 = a[2].lerp(b[2], u)
	var basis := Transform3D().looking_at(look - pos, Vector3.UP).basis
	return [pos, basis.get_rotation_quaternion()]

func _build_dancers() -> void:
	var root: Node3D = get_node("Dancers")
	for d in DANCERS:
		var cfg: Dictionary = ACTORS[d["actor"]]
		var holder := Node3D.new()
		holder.name = d["actor"]
		holder.position = d["pos"]
		holder.rotation_degrees.y = d["rot_y"]
		root.add_child(holder)
		var model_ps: PackedScene = load(cfg["model"])
		var model := model_ps.instantiate()
		model.scale = Vector3.ONE * float(cfg["scale"])
		holder.add_child(model)
		_apply_actor_materials(String(d["actor"]), model)
		var ap := AnimationPlayer.new()
		ap.name = "AnimationPlayer"
		model.add_child(ap)  # 挂模型下:root_node ".." = FBX 根,"Skeleton3D:骨" 轨道路径可解析
		var lib: AnimationLibrary = load(cfg["anims"])
		ap.add_animation_library(&"", lib)
		ap.play(&"dance")
		_dancer_players.append(ap)
		_log_height(String(d["actor"]), holder)

## FBX 导入材质无贴图:按原 Unity .mat 对应关系盖 albedo
func _apply_actor_materials(actor: String, model: Node) -> void:
	var tex_default: Texture2D = null
	match actor:
		"blade_girl":
			tex_default = load(TEX_BLADE)
		"casual_dressed_girl":
			tex_default = load(TEX_CASUAL)
		"f05_schoolwear":
			tex_default = load(TEX_F05)
	for mi in _find(model, "MeshInstance3D"):
		for surf in range(mi.mesh.get_surface_count()):
			var mat := StandardMaterial3D.new()
			mat.albedo_texture = tex_default
			mat.roughness = 0.9
			var old: Material = mi.mesh.surface_get_material(surf)
			if actor == "f05_schoolwear" and old != null \
					and String(old.resource_name).to_lower().contains("face"):
				mat.albedo_texture = load(TEX_FACE)
			mi.set_surface_override_material(surf, mat)

func _build_monster() -> void:
	_monster = Node3D.new()
	_monster.name = "Monster"
	_monster.position = Vector3(0, 0, 0.5)
	_monster.visible = false
	_monster.set_script(load(MONSTER_SCRIPT))
	add_child(_monster)
	var model_ps: PackedScene = load(ROCK_MODEL)
	var model := model_ps.instantiate()
	model.name = "Model"  # 动画库轨道路径 "Model/..." 依赖此名(同 rock_warrior.tscn)
	model.scale.x = -1.0  # 与 gameplay/monsters/rock_warrior.tscn 同镜像
	_monster.add_child(model)
	var rock_mat: Material = load(ROCK_MAT)  # 与 battle 同款树皮石头材质
	for mi in _find(model, "MeshInstance3D"):
		for surf in range(mi.mesh.get_surface_count()):
			mi.set_surface_override_material(surf, rock_mat)
	_monster_ap = AnimationPlayer.new()
	_monster_ap.name = "AnimationPlayer"
	_monster.add_child(_monster_ap)
	var lib: AnimationLibrary = load(ROCK_ANIMS)
	_monster_ap.add_animation_library(&"", lib)
	_monster_ap.animation_finished.connect(_on_monster_anim_finished)

func _on_monster_anim_finished(anim_name: StringName) -> void:
	if anim_name == &"atk01" and not _skipped:
		_monster_ap.play(&"Idle02")

func _run_story() -> void:
	_music.play()  # 高中排练舞蹈.mp3(3.42s,finished 里重播,全程伴舞)
	await _wait(2.0)
	if _skipped:
		return
	await _shot("story_1_wide")
	await _wait(4.0)  # t=6:推近舞者
	if _skipped:
		return
	await _shot("story_2_dancers")
	await _wait(MONSTER_ENTER_T - 6.0)  # t=7.5:怪冲入
	if _skipped:
		return
	_monster.visible = true
	_monster_ap.play(&"locomotion")
	get_node("SfxScream").play()
	get_node("SfxHelp").play()
	var tw := create_tween()
	tw.tween_property(_monster, "position:z", 16.8, MONSTER_STOP_T - MONSTER_ENTER_T)
	await _wait(MONSTER_STOP_T - MONSTER_ENTER_T - 1.6)  # t≈9.4:跟拍怪
	if _skipped:
		return
	await _shot("story_3_charge")
	await _wait(MONSTER_STOP_T - 9.4)  # t=11:停下威胁
	if _skipped:
		return
	_monster_ap.play(&"atk01")
	for ap in _dancer_players:
		ap.speed_scale = 0.25  # 僵住(简化:无逃离动画)
	await _wait(0.8)  # t≈11.8:越肩定格
	if _skipped:
		return
	await _shot("story_4_faceoff")
	await _wait(FADE_T - 11.8)  # t=17.3:淡出
	if _skipped:
		return
	var fade_tw := create_tween()
	fade_tw.tween_property(_fade, "modulate:a", 1.0, STORY_DURATION - FADE_T)
	await _wait(STORY_DURATION - FADE_T)
	if _skipped:
		return
	if _shots_mode:
		get_tree().quit(0)
		return
	_skip_to_battle()

func _wait(t: float) -> void:
	await get_tree().create_timer(t).timeout

func _shot(name: String) -> void:
	if not _shots_mode:
		return
	await get_tree().process_frame
	await get_tree().process_frame
	var cam: Camera3D = get_node("Camera3D")
	print("[STORY] %s cam pos=%s rot=%s anim_t=%.2f" % [
		name, cam.global_position, cam.global_rotation,
		get_node("CamPlayer").current_animation_position])
	var img := get_viewport().get_texture().get_image()
	var err := img.save_png(SHOT_DIR + name + ".png")
	print("[STORY] shot %s err=%d" % [name, err])

func _skip_to_battle() -> void:
	if _skipped:
		return
	_skipped = true
	Engine.time_scale = 1.0
	GlobalObject.next_scene_path = LEVEL1_BATTLE
	get_tree().change_scene_to_file(LOADING)

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo \
			and event.keycode == KEY_ESCAPE:
		_skip_to_battle()

## 音乐 3.42s 短循环:finished 重播(剧情期间不停)
func _on_music_finished() -> void:
	if not _skipped and _music != null:
		_music.play()

func _log_height(actor: String, holder: Node3D) -> void:
	# 骨架 rest 全局包围盒最大轴 ≈ 身高(FBX 朝向不一),打印供缩放校准
	var sk: Skeleton3D = null
	for n in _find(holder, "Skeleton3D"):
		sk = n
		break
	if sk == null:
		return
	var lo := Vector3(1e9, 1e9, 1e9)
	var hi := Vector3(-1e9, -1e9, -1e9)
	for i in sk.get_bone_count():
		var p: Vector3 = (sk.global_transform * sk.get_bone_global_rest(i)).origin
		lo = Vector3(minf(lo.x, p.x), minf(lo.y, p.y), minf(lo.z, p.z))
		hi = Vector3(maxf(hi.x, p.x), maxf(hi.y, p.y), maxf(hi.z, p.z))
	print("[STORY] %s world size ≈ %s" % [actor, hi - lo])

func _find(n: Node, cls: String) -> Array:
	var out := []
	if n.is_class(cls):
		out.append(n)
	for c in n.get_children():
		out.append_array(_find(c, cls))
	return out
