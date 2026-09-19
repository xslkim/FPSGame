# M7b FX screenshots (one-off verification tool). Run (windowed, GPU):
#   godot tools/m7b_fx_shots.tscn --quit-after 240
# 1) muzzle flash 连拍 2 帧(真实 gun.fire() 路径)
# 2) 怪物中弹血花(hit + Blood 弹着特效)
# 3) dragon 吐息(fire_breath.tscn 激活)
extends Node3D

const SHOT_DIR := "G:/FPSGame/tools/screenshots"
const FIRE_RANGE := "res://scenes/test/fire_range.tscn"
const AXE_ZOMBIE := "res://gameplay/monsters/axe_zombie.tscn"
const DRAGON := "res://gameplay/monsters/dragon_red.tscn"

var _frame := 0
var _fs: FireSystem = null
var _cam: Camera3D = null
var _monster: MonsterBase = null
var _dragon: DragonMonster = null


func _ready() -> void:
	DirAccess.make_dir_recursive_absolute(SHOT_DIR)
	var range: Node = load(FIRE_RANGE).instantiate()
	add_child(range)
	_fs = range.get_node("Camera3D/FireSystem")
	_cam = range.get_node("Camera3D")
	# 怪物放在相机正前方 6m(血花目标)
	_monster = load(AXE_ZOMBIE).instantiate()
	range.add_child(_monster)
	_monster.born(_cam.global_position + (-_cam.global_basis.z) * 6.0, 0, 99.0)
	# 龙放在相机前方 9m,面向相机(吐息特效驱动)
	_dragon = load(DRAGON).instantiate()
	range.add_child(_dragon)
	_dragon.born(_cam.global_position + (-_cam.global_basis.z) * 9.0 + Vector3.UP * 2.0, 0, 99.0)
	_dragon.face_camera()


func _process(_delta: float) -> void:
	_frame += 1
	match _frame:
		60:
			# 怪物中弹:非致死 hit + Blood 弹着特效(等价 fire_system 命中流程)
			var point: Vector3 = _monster.global_position + Vector3(0, 1.2, 0)
			_monster.hit(10.0, point, GlobalObject.HitType.Body, PlayerSystem.Side.Right)
			_fs._spawn_effect("Blood", point, _cam.global_basis.z, 1.0, true)
		63:
			_capture("m7b_blood_hit")
		80:
			# 直接调用 fire() 触发枪口火光(0.05s 闪断,连拍两帧)
			var gun: GunBase = _fs._current_gun[PlayerSystem.Side.Right]
			gun.last_fire_time = -99.0
			gun.fire()
		81:
			_capture("m7b_muzzle_fire_f1")  # 连拍第 1 帧
		82:
			_capture("m7b_muzzle_fire_f2")  # 连拍第 2 帧
		100:
			# 龙吐息:激活 fire_breath(特效场景 + 音效路径)
			_dragon._ensure_breath_fx()
			_dragon._breath_fx.emitting = true
			_dragon._breath_time = 3.0
		112:
			_capture("m7b_dragon_breath")
		130:
			# 四组环境弹着(墙面):Wood/Metal/Concrete/Dust
			var wall_z := -8.0
			var base := Vector3(_cam.global_position.x - 1.5, _cam.global_position.y, wall_z)
			var n := Vector3(0, 0, 1)
			_fs._spawn_effect("Wood", base + Vector3(0, 0.8, 0), n, 1.0, false)
			_fs._spawn_effect("Metal", base + Vector3(1.0, 0.8, 0), n, 1.0, false)
			_fs._spawn_effect("Concrete", base + Vector3(2.0, 0.8, 0), n, 1.0, false)
			_fs._spawn_effect("Dust", base + Vector3(3.0, 0.8, 0), n, 1.0, false)
		136:
			_capture("m7b_impacts")
		150:
			# 雷柱特效(相机前 4m)
			var pillar: Node3D = load("res://assets/effects/lightning_pillar.tscn").instantiate()
			add_child(pillar)
			pillar.global_position = _cam.global_position + (-_cam.global_basis.z) * 4.0
			pillar.activate(0.5)
		156:
			_capture("m7b_lightning")


func _capture(tag: String) -> void:
	var img := get_viewport().get_texture().get_image()
	var path := "%s/%s.png" % [SHOT_DIR, tag]
	img.save_png(path)
	print("[M7B-SHOT] %s" % path)
