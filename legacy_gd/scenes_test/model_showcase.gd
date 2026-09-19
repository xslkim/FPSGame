extends Node3D
## M7 模型陈列/turntable 截图:逐个实例化 Level1 怪 .tscn,播放指定动画连拍 2 帧。
## 用法(需 GPU,非 headless):
##   godot --path game scenes/test/model_showcase.tscn -- --showcase-shot <outdir>
## 输出:<outdir>/<monster>_idle_a.png / _idle_b.png / <monster>_attack_a/b.png / lineup.png

const MONSTERS := {
	"bull": {"scene": "res://gameplay/monsters/bull.tscn", "attack": "attack_01"},
	"axe_zombie": {"scene": "res://gameplay/monsters/axe_zombie.tscn", "attack": "Attack1"},
	"fly_axe_zombie": {"scene": "res://gameplay/monsters/fly_axe_zombie.tscn", "attack": "Skill"},
	"skeleton": {"scene": "res://gameplay/monsters/skeleton.tscn", "attack": "Attack1"},
	"baotou": {"scene": "res://gameplay/monsters/baotou.tscn", "attack": "anim_attack"},
	"box_monster": {"scene": "res://gameplay/monsters/box_monster.tscn", "attack": "BiteAttack"},
}

var _outdir := "user://showcase"
var _queue: Array = []
var _shot_idx := 0

func _ready() -> void:
	var args := OS.get_cmdline_user_args()
	var i := args.find("--showcase-shot")
	if i >= 0 and i + 1 < args.size():
		_outdir = args[i + 1]
	DirAccess.make_dir_recursive_absolute(_outdir)
	# 每个怪:idle 2 帧 + attack 2 帧(攻击动画应能看到动作变化)
	for key in MONSTERS:
		_queue.append({"key": key, "anim": "Idle02", "tag": "idle"})
		_queue.append({"key": key, "anim": MONSTERS[key]["attack"], "tag": "attack"})
	_next()

func _next() -> void:
	if _queue.is_empty():
		_lineup_shot()
		return
	var job: Dictionary = _queue.pop_front()
	for c in get_children():
		if c.is_in_group("showcase_monster"):
			c.queue_free()
	await get_tree().process_frame
	var ps: PackedScene = load(MONSTERS[job["key"]]["scene"])
	var m: Node3D = ps.instantiate()
	m.add_to_group("showcase_monster")
	add_child(m)
	m.visible = true
	m.set_process(false)
	m.set_physics_process(false)
	var ap: AnimationPlayer = m.get_node("AnimationPlayer")
	if ap.has_animation(job["anim"]):
		ap.play(job["anim"])
	else:
		print("[showcase] WARN %s 缺少动画 %s" % [job["key"], job["anim"]])
	# 相机:怪物 -Z 前方 3/4 视角(front = -Z,即相机放在 -Z 侧)
	var cam: Camera3D = $Camera3D
	cam.global_position = Vector3(1.6, 1.7, -3.2)
	cam.look_at(Vector3(0, 1.0, 0), Vector3.UP)
	_shot_idx = 0
	await get_tree().create_timer(0.5).timeout
	_shot("%s_%s_a" % [job["key"], job["tag"]])
	await get_tree().create_timer(0.4).timeout
	_shot("%s_%s_b" % [job["key"], job["tag"]])
	_next()

func _lineup_shot() -> void:
	for c in get_children():
		if c.is_in_group("showcase_monster"):
			c.queue_free()
	await get_tree().process_frame
	var x := 0.0
	for key in MONSTERS:
		var m: Node3D = load(MONSTERS[key]["scene"]).instantiate()
		m.add_to_group("showcase_monster")
		add_child(m)
		m.visible = true
		m.set_process(false)
		m.set_physics_process(false)
		m.position = Vector3(x, 0, 0)
		var ap: AnimationPlayer = m.get_node("AnimationPlayer")
		if ap.has_animation("Idle02"):
			ap.play("Idle02")
			ap.seek(randf() * 1.0, true)
		x += 1.8
	var cam: Camera3D = $Camera3D
	cam.global_position = Vector3(x * 0.5 - 0.9, 2.2, -7.5)
	cam.look_at(Vector3(x * 0.5 - 0.9, 0.9, 0), Vector3.UP)
	await get_tree().create_timer(0.5).timeout
	_shot("lineup")
	print("[showcase] done -> %s" % _outdir)
	get_tree().quit()

func _shot(name: String) -> void:
	var img := get_viewport().get_texture().get_image()
	var path := "%s/%s.png" % [_outdir, name]
	img.save_png(path)
	print("[showcase] shot %s" % path)
