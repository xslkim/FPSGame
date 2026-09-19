extends Node3D
class_name Fireball
## Baotou 火球(6.3 BaotouFireball):直线飞向相机的演出弹道,
## 发射 1 秒后定时命中 Both 玩家(伤害=怪攻击,Poison 怪附加中毒由 hit_player 处理)。
## 保持原作"演出弹道 + 定时结算"原则,不做真实碰撞。
## M7b:KriptoFX Fireball1 风格贴图核心 + 火焰拖尾粒子,命中爆炸 + 音效;
## rock_warrior 大火球(×10)用 big=true(石核音效)。

const HIT_DELAY := 1.0
const SPEED := 20.0
const LIFE_AFTER_HIT := 0.35

const TEX_CORE := "res://assets/effects/textures/energy_ball.png"
const TEX_TRAIL := "res://assets/effects/textures/flame_4frm.png"
const TEX_EXPLOSION := "res://assets/effects/textures/fireball_explosion.png"
const SND_LAUNCH := "res://assets/audio/effects/fireball_launch.wav"
const SND_LAUNCH_BIG := "res://assets/audio/effects/fireball_rock.wav"
const SND_HIT := "res://assets/audio/effects/fireball_hit.wav"

var _attack := 15.0
var _attack_type := GlobalObject.AttackType.Phy
var _dir := Vector3.FORWARD
var _time := 0.0
var _resolved := false

var _core: Sprite3D = null
var _trail: GPUParticles3D = null
var _explosion: Sprite3D = null
var _audio: AudioStreamPlayer3D = null


static func spawn(parent: Node, from: Vector3, p_attack: float, p_attack_type: int,
		big := false) -> Fireball:
	var fb := Fireball.new()
	fb._attack = p_attack
	fb._attack_type = p_attack_type
	parent.add_child(fb)
	fb.global_position = from
	fb._build_visuals(big)
	return fb


func _build_visuals(big: bool) -> void:
	# 核心:能量球贴图(包头火球橙色 / rock_warrior 大火球保持同风格,由 ×10 缩放体现)
	_core = Sprite3D.new()
	_core.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	_core.double_sided = true
	_core.pixel_size = 0.01
	if ResourceLoader.exists(TEX_CORE):
		var mat := StandardMaterial3D.new()
		mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		mat.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
		mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		mat.albedo_color = Color(1.0, 0.55, 0.15) if not big else Color(1.0, 0.45, 0.2)
		mat.albedo_texture = load(TEX_CORE)
		_core.material_override = mat
		_core.texture = load(TEX_CORE)
	add_child(_core)
	# 火焰拖尾:flame_4frm 2×2 翻页粒子
	_trail = GPUParticles3D.new()
	_trail.amount = 24
	_trail.lifetime = 0.35
	_trail.preprocess = 0.2
	var pm := ParticleProcessMaterial.new()
	pm.direction = Vector3(0, 0, 1)
	pm.spread = 35.0
	pm.gravity = Vector3.ZERO
	pm.initial_velocity_min = 0.5
	pm.initial_velocity_max = 1.5
	pm.scale_min = 0.6
	pm.scale_max = 1.1
	pm.anim_speed_min = 2.0
	pm.anim_speed_max = 3.0
	_trail.process_material = pm
	var qm := QuadMesh.new()
	qm.size = Vector2(0.5, 0.5)
	var tm := StandardMaterial3D.new()
	tm.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	tm.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
	tm.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	tm.albedo_color = Color(1.0, 0.6, 0.2)
	if ResourceLoader.exists(TEX_TRAIL):
		tm.albedo_texture = load(TEX_TRAIL)
	tm.billboard_mode = BaseMaterial3D.BILLBOARD_PARTICLES
	tm.particles_anim_h_frames = 2
	tm.particles_anim_v_frames = 2
	tm.particles_anim_loop = true
	qm.material = tm
	_trail.draw_pass_1 = qm
	add_child(_trail)
	# 命中爆炸贴图(初始隐藏)
	_explosion = Sprite3D.new()
	_explosion.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	_explosion.double_sided = true
	_explosion.pixel_size = 0.02
	_explosion.visible = false
	if ResourceLoader.exists(TEX_EXPLOSION):
		var em := StandardMaterial3D.new()
		em.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		em.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
		em.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		em.albedo_texture = load(TEX_EXPLOSION)
		_explosion.material_override = em
		_explosion.texture = load(TEX_EXPLOSION)
	add_child(_explosion)
	# 发射音效(缺失静默)
	var snd_path := SND_LAUNCH_BIG if big else SND_LAUNCH
	if ResourceLoader.exists(snd_path):
		_audio = AudioStreamPlayer3D.new()
		_audio.stream = load(snd_path)
		_audio.unit_size = 8.0
		add_child(_audio)
		_audio.play()
	var cam := get_viewport().get_camera_3d()
	if cam != null:
		_dir = (cam.global_position - global_position).normalized()


func _process(delta: float) -> void:
	_time += delta
	global_position += _dir * SPEED * delta
	if _core != null:
		_core.rotation.z += delta * 6.0
	if not _resolved and _time >= HIT_DELAY:
		_resolved = true
		_explode()
		PlayerSystem.hit_player(_attack, _attack_type, PlayerSystem.Side.Both)
	if _time >= HIT_DELAY + LIFE_AFTER_HIT:
		queue_free()


## 命中演出:隐藏核心/拖尾,炸开贴图 + 命中音
func _explode() -> void:
	if _core != null:
		_core.visible = false
	if _trail != null:
		_trail.emitting = false
	if _explosion != null:
		_explosion.visible = true
		var tw := create_tween()
		tw.set_parallel(true)
		tw.tween_property(_explosion, "scale", Vector3.ONE * 3.0, LIFE_AFTER_HIT)
		tw.tween_property(_explosion, "modulate:a", 0.0, LIFE_AFTER_HIT)
	if _audio != null and ResourceLoader.exists(SND_HIT):
		_audio.stream = load(SND_HIT)
		_audio.play()
