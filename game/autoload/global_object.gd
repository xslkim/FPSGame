extends Node
## GlobalObject:场景状态 / 难度 / 全局配置 / 体感四元数→枪口旋转。
## 对应原作 GlobalObject.cs。

enum GameState { UI, Battle }
enum Difficulty { Easy, Hard, Hell }
## 怪物攻击类型(6.4 受击状态用)
enum AttackType { Phy, Ice, Poison }
## 命中部位(BoxHead 转发时携带,M3 用)
enum HitType { Head, Body, Armour }

## Unity(左手系,+Z 向前)→ Godot(右手系,-Z 向前)四元数镜像候选(难点 #1,4.1 实测校准)。
## UDP 数据由手机 App 按 Unity 约定发出;Unity 与 Godot 的 Quaternion 都是 (x,y,z,w),
## 区别在坐标系手性。注意 q 与 -q 表示同一旋转,故"取反 x,w"≡"取反 y,z"(镜像 X 轴)。
enum QuatMirror {
	NONE,    # 0 不转换
	NEG_XW,  # 1 取反 x,w(镜像 X 轴:x→-x)
	NEG_YZ,  # 2 取反 y,z(与 NEG_XW 同旋转,冗余候选)
	NEG_ZW,  # 3 取反 z,w(镜像 Z 轴:z→-z)
}

## 默认 NEG_ZW:标准坐标映射 (x,y,z)_unity → (x,y,-z)_godot 的四元数形式为
## (-x,-y,z,w),整体取反(同旋转)即 (x,y,-z,-w) = 取反 z,w。
## 已按旋转向量验证方向直觉一致:Unity 正 pitch = 低头(forward +Z 向 -Y),
## 经 NEG_ZW 后 Godot 负 pitch = 低头(forward -Z 向 -Y);yaw 同理右转对右转。
## 可用 project setting fpsgame/input/quat_mirror(int)在真机校准时切换候选。
const QUAT_MIRROR_DEFAULT := QuatMirror.NEG_ZW

const PHONE_MOVE_RATE := 0.5

var scene_state: GameState = GameState.UI
var difficulty: Difficulty = Difficulty.Easy
var is_game_pause := false
var is_debug := true  # 调试期默认开

## Loading 场景目标(LevelChoose 选定后写入,8.1)
var next_scene_path := ""

func get_ring_rotation() -> Quaternion:
	return _phone_to_gun_rotation(InputManager.raw_ring_rotation)

func get_leg_rotation() -> Quaternion:
	return _phone_to_gun_rotation(InputManager.raw_leg_rotation)

## 当前镜像方案(project setting 可覆盖常量默认值)
func get_quat_mirror_mode() -> int:
	return int(ProjectSettings.get_setting("fpsgame/input/quat_mirror", QUAT_MIRROR_DEFAULT))

## 坐标系镜像修正。该变换是对合(应用两次即还原),
## InputManager 键盘回落路径也经它把 Godot 空间四元数转成 Unity 约定存储。
func apply_quat_mirror(raw: Quaternion) -> Quaternion:
	match get_quat_mirror_mode():
		QuatMirror.NEG_XW:
			return Quaternion(-raw.x, raw.y, raw.z, -raw.w)
		QuatMirror.NEG_YZ:
			return Quaternion(raw.x, -raw.y, -raw.z, raw.w)
		QuatMirror.NEG_ZW:
			return Quaternion(raw.x, raw.y, -raw.z, -raw.w)
	return raw

func _phone_to_gun_rotation(raw: Quaternion) -> Quaternion:
	# 原作: v = q * Vector3.forward; v.z = PhoneMoveRate; FromToRotation(forward, v)
	# Godot FORWARD 为 -Z(Unity 为 +Z),v.z 取 -PHONE_MOVE_RATE 保持"朝前";
	# q 先做手性镜像(难点 #1),v = q' * FORWARD 即标准映射 M(q·v)=(MqM)(Mv)。
	var q := apply_quat_mirror(raw)
	var v := q * Vector3.FORWARD
	v.z = -PHONE_MOVE_RATE
	return Quaternion(Vector3.FORWARD, v)
