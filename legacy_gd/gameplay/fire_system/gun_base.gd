extends Node3D
class_name GunBase
## 枪基类:CD / 耗弹 / 开火表现(5.1 / 5.4 节)。
## 数值从 DataMgr.get_gun_info(gun_type) 读;模型为占位盒体,后续换 FBX。

@export var gun_type := 2  # 0=AK47 / 1=M4 / 2=HandGun

var gun_name := ""
var fire_cd := 0.5
var attack := 10.0
var bullet_cost := 1
var last_fire_time := -99.0

var player  # PlayerSystem.Player(RefCounted,不设类型避免循环引用)
var is_left := false

## 枪口火光贴图(FPS Pack MuzzleFlash1-9,开火随机帧)
const MUZZLE_TEXTURES := [
	"res://assets/effects/textures/muzzle1.png",
	"res://assets/effects/textures/muzzle2.png",
	"res://assets/effects/textures/muzzle3.png",
	"res://assets/effects/textures/muzzle4.png",
	"res://assets/effects/textures/muzzle5.png",
	"res://assets/effects/textures/muzzle6.png",
	"res://assets/effects/textures/muzzle7.png",
	"res://assets/effects/textures/muzzle8.png",
	"res://assets/effects/textures/muzzle9.png",
]

@onready var muzzle: Node3D = $Muzzle
@onready var _flash: Sprite3D = $Muzzle/MuzzleFlash
@onready var _audio: AudioStreamPlayer3D = $AudioStreamPlayer3D

var _flash_tween: Tween
var _muzzle_streams: Array = []
var _flash_mat: StandardMaterial3D = null

func _ready() -> void:
	var info := DataMgr.get_gun_info(gun_type)
	gun_name = str(info.get("name", "Gun%d" % gun_type))
	fire_cd = float(info.get("fire_cd", 0.5))
	attack = float(info.get("attack", 10.0))
	bullet_cost = int(info.get("bullet", 1))
	var sound_path := str(info.get("fire_sound", ""))
	if not sound_path.is_empty() and ResourceLoader.exists(sound_path):
		_audio.stream = load(sound_path)
	for path in MUZZLE_TEXTURES:
		if ResourceLoader.exists(path):
			_muzzle_streams.append(load(path))
	_flash.double_sided = true
	# 加色混合:贴图黑底自动消隐(部分枪口贴图为 RGB 无 alpha)
	_flash_mat = StandardMaterial3D.new()
	_flash_mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	_flash_mat.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
	_flash_mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	_flash_mat.albedo_color = Color(1.0, 0.85, 0.6)
	_flash_mat.albedo_texture = _muzzle_streams[0] if not _muzzle_streams.is_empty() \
		else FxHelper.make_radial_texture(Color(1.0, 0.75, 0.25))
	_flash.material_override = _flash_mat
	_flash.visible = false

## 开火:CD 到 + 弹够才成功。返回 [success, bullet_enough](5.1 节)
func fire() -> Array:
	var now := Time.get_ticks_msec() / 1000.0
	if now - last_fire_time < fire_cd:
		return [false, true]
	if player == null or not player.active or player.bullet < bullet_cost:
		return [false, false]
	last_fire_time = now
	player.use_bullet(bullet_cost)
	_show_muzzle_flash()
	if _audio.stream != null:
		_audio.play()
	return [true, true]

## 火精灵闪断重激活:随机帧 / 随机旋转 / 随机镜像
func _show_muzzle_flash() -> void:
	if not _muzzle_streams.is_empty():
		var tex: Texture2D = _muzzle_streams[randi() % _muzzle_streams.size()]
		_flash.texture = tex
		_flash_mat.albedo_texture = tex  # material_override 下贴图走覆盖材质
	_flash.flip_h = randf() < 0.5
	_flash.visible = false
	_flash.visible = true
	_flash.rotation.z = randf() * TAU
	if _flash_tween != null and _flash_tween.is_valid():
		_flash_tween.kill()
	_flash_tween = create_tween()
	_flash_tween.tween_interval(0.05)
	_flash_tween.tween_callback(_flash.hide)
