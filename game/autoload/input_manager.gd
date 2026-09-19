extends Node
## InputManager:UDP 体感输入(M2)+ 键盘调试输入。
## 对应原作 InputManager.cs / UdpServer.cs / UdpBroadCast.cs(4.1 / 4.3 / 4.4 节)。

signal key1_right  # 右手扳机边沿(0→1)
signal key2_right  # 右手换枪边沿
signal key1_left   # 左手扳机边沿
signal key2_left   # 左手换枪边沿

enum InputMode { Menu, OnlyRight, OnlyLeft, ControllerOrRight, RightAndLeft }

const AIM_SPEED := deg_to_rad(30.0)  # 原 0.5°/帧(帧率相关 bug)→ 30°/s × delta
const AIM_LIMIT := deg_to_rad(45.0)  # pitch/yaw 限 ±45°
const DEVICE_TIMEOUT := 2.0  # 2 秒无数据 → 该路标记断开(4.1)

var input_mode: InputMode = InputMode.RightAndLeft

## 开火/换枪输入总开关(关卡开场演出期间禁用,7.4 节)
var fire_enabled := true

# 原始四元数(UDP 写入 / 键盘由欧拉角合成),GlobalObject 取走后做枪口旋转解算
var raw_ring_rotation := Quaternion.IDENTITY
var raw_leg_rotation := Quaternion.IDENTITY

# ---- UDP 状态(4.1:双路完全分离)----
var ring_connected := false  # 逻辑连接(ControllerOrRight 模式恒 true,物理断开时回落键盘)
var leg_connected := false
var ring_ip: String:
	get:
		return _server.ring_ip if _server != null else ""
var leg_ip: String:
	get:
		return _server.leg_ip if _server != null else ""
var ring_pressure := 0  # 压力值(原作未用,解析保留)
var leg_pressure := 0
var ring_last_packet_time := -1.0
var leg_last_packet_time := -1.0

var _server: UdpDeviceServer

var _now := 0.0
var _ring_phy_connected := false  # 物理连接(原作 _PhyRingConnected)
var _ring_had_packet := false     # 本帧是否收到 R 包(衰减回中用)
var _prev_raw_ring := Quaternion.IDENTITY  # 上一个 R 包四元数(衰减回中用)

var _aim_yaw := 0.0
var _aim_pitch := 0.0
var _left_alt_held := false

var _key1_right_level := false
var _key1_left_level := false
var _key2_right_level := false
var _key2_left_level := false
var _prev_key1_right := false
var _prev_key1_left := false
var _prev_key2_right := false
var _prev_key2_left := false

func _ready() -> void:
	_server = UdpDeviceServer.new()
	_server.packet_received.connect(_on_udp_packet)
	_server.start_broadcast()

## 设模式时按模式创建/停用左右玩家(4.4 节)。
## 照原作 SetInputMode:ControllerOrRight 置 ring_connected=true(允许键盘回落),其余清 false。
func set_input_mode(mode: InputMode) -> void:
	input_mode = mode
	ring_connected = mode == InputMode.ControllerOrRight
	PlayerSystem.setup_players(mode)

func _input(event: InputEvent) -> void:
	# LeftAlt = 右手换枪调试键(全局)
	if event is InputEventKey and event.keycode == KEY_ALT:
		if event.location == KEY_LOCATION_LEFT or event.location == KEY_LOCATION_UNSPECIFIED:
			_left_alt_held = event.pressed

func _process(delta: float) -> void:
	_now += delta
	_server.poll(delta)
	_update_disconnects()
	_update_broadcast()
	_update_ring_decay(delta)
	if _keyboard_fallback_active():
		_update_keyboard_aim(delta)
		_update_keyboard_buttons()
	_emit_edges()
	_ring_had_packet = false

## 22/23 字节小端包(4.1):
## [0] 'R'/'L'(其他丢弃);[1..16] 4×float32 四元数 x,y,z,w(归一化);
## [17..20] int32 压力值(解析保留);[21] 扳机 k;[22] 换枪 k2。
## 注:原作 UdpServer.cs 实际连续读 23 字节(k2 在第 23 字节),方案表"22 字节"为 1 基偏移笔误;
## 此处兼容两种长度(<22 丢弃,缺 k2 视为 0)。
func _on_udp_packet(data: PackedByteArray, _from_ip: String) -> void:
	if data.size() < 22:
		return
	var flag := data[0]
	var is_ring: bool
	if flag == 0x52:  # 'R'
		if input_mode == InputMode.OnlyLeft:
			return
		is_ring = true
	elif flag == 0x4C:  # 'L'
		if input_mode == InputMode.OnlyRight:
			return
		is_ring = false
	else:
		return
	var q := Quaternion(
		data.decode_float(1), data.decode_float(5),
		data.decode_float(9), data.decode_float(13))
	if q.length_squared() < 1e-8:
		return
	q = q.normalized()
	var pressure := data.decode_s32(17)
	var k := data[21] != 0
	var k2 := data.size() >= 23 and data[22] != 0
	if is_ring:
		_prev_raw_ring = raw_ring_rotation
		raw_ring_rotation = q
		_ring_had_packet = true
		ring_pressure = pressure
		ring_connected = true
		_ring_phy_connected = true
		ring_last_packet_time = _now
		_key1_right_level = k
		_key2_right_level = k2
	else:
		raw_leg_rotation = q
		leg_pressure = pressure
		leg_connected = true
		leg_last_packet_time = _now
		_key1_left_level = k
		_key2_left_level = k2

## 2 秒无数据 → 该路断开;ControllerOrRight 保留逻辑连接(回落键盘),其余模式照原作清标志
func _update_disconnects() -> void:
	if _ring_phy_connected and _now - ring_last_packet_time > DEVICE_TIMEOUT:
		_ring_phy_connected = false
		if input_mode != InputMode.ControllerOrRight:
			ring_connected = false
		print("[InputManager] 戒指(R) %.0f 秒无数据:phy 断开,ring_connected=%s" % [DEVICE_TIMEOUT, ring_connected])
	if leg_connected and _now - leg_last_packet_time > DEVICE_TIMEOUT:
		leg_connected = false
		print("[InputManager] 腿部(L) %.0f 秒无数据:leg_connected=false" % DEVICE_TIMEOUT)

## 所需设备连齐 → 停止广播;断开 → 重启广播(4.1,照原作 StopBroadCast 判定)
func _update_broadcast() -> void:
	var ready := false
	match input_mode:
		InputMode.OnlyRight, InputMode.ControllerOrRight:
			ready = ring_connected
		InputMode.OnlyLeft:
			ready = leg_connected
		InputMode.RightAndLeft:
			ready = ring_connected and leg_connected
		InputMode.Menu:
			ready = false  # 菜单(连接手机页)持续广播
	if ready and _server.is_broadcasting():
		_server.stop_broadcast()
	elif not ready and not _server.is_broadcasting():
		_server.start_broadcast()

## 原作 Update:R 路本帧无新包时,按上两包欧拉角差 × delta × 0.1 衰减回中
func _update_ring_decay(delta: float) -> void:
	if not _ring_phy_connected or _ring_had_packet:
		return
	var cur := raw_ring_rotation.get_euler()
	var diff := cur - _prev_raw_ring.get_euler()
	raw_ring_rotation = Quaternion.from_euler(cur - diff * delta * 0.1)

## 原作仅 ControllerOrRight 且无物理设备时回落键盘;
## 移植版放宽到所有含右玩家的模式(无设备时方便试验场调试),设备连上后 UDP 自动接管。
func _keyboard_fallback_active() -> bool:
	if _ring_phy_connected:
		return false
	return input_mode in [
		InputMode.OnlyRight, InputMode.ControllerOrRight, InputMode.RightAndLeft]

## 键盘瞄准:方向键 30°/s × delta,±45° 限位。
## 键盘输出是 Godot 空间四元数,先过一遍镜像(GlobalObject.apply_quat_mirror 为对合变换,
## 应用两次即还原),使 raw_ring_rotation 与 UDP 数据同为 Unity 约定,下游转换统一。
func _update_keyboard_aim(delta: float) -> void:
	var moved := false
	if Input.is_key_pressed(KEY_LEFT):
		_aim_yaw += AIM_SPEED * delta
		moved = true
	if Input.is_key_pressed(KEY_RIGHT):
		_aim_yaw -= AIM_SPEED * delta
		moved = true
	if Input.is_key_pressed(KEY_UP):
		_aim_pitch += AIM_SPEED * delta
		moved = true
	if Input.is_key_pressed(KEY_DOWN):
		_aim_pitch -= AIM_SPEED * delta
		moved = true
	if not moved:
		return
	_aim_yaw = clampf(_aim_yaw, -AIM_LIMIT, AIM_LIMIT)
	_aim_pitch = clampf(_aim_pitch, -AIM_LIMIT, AIM_LIMIT)
	raw_ring_rotation = GlobalObject.apply_quat_mirror(
		Quaternion.from_euler(Vector3(_aim_pitch, _aim_yaw, 0.0)))

## 开火 = Return / 小键盘 Enter;换枪 = Menu 键 或 LeftAlt(右手调试)。
## 仅键盘回落激活时写右路电平;左路电平始终由 UDP 写入。
func _update_keyboard_buttons() -> void:
	_key1_right_level = Input.is_key_pressed(KEY_ENTER) or Input.is_key_pressed(KEY_KP_ENTER)
	_key2_right_level = Input.is_key_pressed(KEY_MENU) or _left_alt_held

func _emit_edges() -> void:
	if fire_enabled:
		if _key1_right_level and not _prev_key1_right:
			key1_right.emit()
		if _key1_left_level and not _prev_key1_left:
			key1_left.emit()
		if _key2_right_level and not _prev_key2_right:
			key2_right.emit()
		if _key2_left_level and not _prev_key2_left:
			key2_left.emit()
	_prev_key1_right = _key1_right_level
	_prev_key1_left = _key1_left_level
	_prev_key2_right = _key2_right_level
	_prev_key2_left = _key2_left_level

## 扳机电平查询
func get_cur_key_ring() -> bool:
	return _key1_right_level and fire_enabled

func get_cur_key_leg() -> bool:
	return _key1_left_level and fire_enabled

## 换枪电平查询(原作 GetCurKey2Ring/Leg,M2 联调用)
func get_cur_key2_ring() -> bool:
	return _key2_right_level

func get_cur_key2_leg() -> bool:
	return _key2_left_level

## 向两端发送 "Reset"(原作 ResetDevice)
func reset_device() -> void:
	_server.reset_device()

## 设备发现广播开关(断线/重建时由内部自动管理,也可手动调用)
func start_device_broadcast() -> void:
	_server.start_broadcast()

func stop_device_broadcast() -> void:
	_server.stop_broadcast()

func is_broadcasting() -> bool:
	return _server.is_broadcasting()

## 物理连接(不含 ControllerOrRight 键盘回落)
func is_ring_phy_connected() -> bool:
	return _ring_phy_connected

## socket 重建次数(5 秒无包看门狗,自检用)
func get_socket_rebuild_count() -> int:
	return _server.rebuild_count
