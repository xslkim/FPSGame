extends Node
## M2 UDP 联调测试:屏幕实时显示双路连接状态/来源 IP/原始四元数/解算欧拉角/按键状态;
## headless 下每 0.5 秒 print 同样信息。
## 自检(配合 tools/udp_device_sim.py --selftest):
##   godot --headless --path game scenes/test/udp_test.tscn -- --m2-selftest

const MARKER_QUAT := Quaternion(0.1, -0.2, 0.3, 0.9)  # 模拟器自检标记(未归一化)

var _label: Label
var _print_timer := 0.0
var _edge_counts := {"key1_right": 0, "key2_right": 0, "key1_left": 0, "key2_left": 0}
var _failed := false

func _ready() -> void:
	var mode := InputManager.InputMode.RightAndLeft
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--m2-mode=") and arg.trim_prefix("--m2-mode=") in InputManager.InputMode:
			mode = InputManager.InputMode[arg.trim_prefix("--m2-mode=")]
	InputManager.set_input_mode(mode)
	for sig_name in _edge_counts:
		InputManager[sig_name].connect(_on_key_edge.bind(sig_name))
	_build_ui()
	if OS.get_cmdline_user_args().has("--m2-selftest"):
		_self_test()

func _on_key_edge(sig_name: String) -> void:
	_edge_counts[sig_name] += 1
	print("[UdpTest] 按键边沿: %s (累计 %d)" % [sig_name, _edge_counts[sig_name]])

func _build_ui() -> void:
	var layer := CanvasLayer.new()
	add_child(layer)
	_label = Label.new()
	_label.position = Vector2(16, 16)
	_label.add_theme_font_size_override("font_size", 18)
	layer.add_child(_label)

func _side_text(title: String, connected: bool, ip: String, last_time: float,
		raw: Quaternion, gun: Quaternion, k1: bool, k2: bool, pressure: int) -> String:
	var e := gun.get_euler()
	return "%s connected=%s ip=%s idle=%.1fs\n  raw=(%.4f, %.4f, %.4f, %.4f)\n  gun_euler=(%.1f, %.1f, %.1f)° 扳机=%s 换枪=%s 压力=%d" % [
		title, connected, ip if ip != "" else "-",
		maxf(0.0, Time.get_ticks_msec() / 1000.0 - last_time) if last_time >= 0.0 else -1.0,
		raw.x, raw.y, raw.z, raw.w,
		rad_to_deg(e.x), rad_to_deg(e.y), rad_to_deg(e.z), k1, k2, pressure]

func _status_text() -> String:
	var lines := ["mode=%s broadcasting=%s rebuilds=%d mirror=%d" % [
		InputManager.InputMode.keys()[InputManager.input_mode],
		InputManager.is_broadcasting(), InputManager.get_socket_rebuild_count(),
		GlobalObject.get_quat_mirror_mode()]]
	lines.append(_side_text("[Ring/R]", InputManager.ring_connected, InputManager.ring_ip,
		InputManager.ring_last_packet_time, InputManager.raw_ring_rotation,
		GlobalObject.get_ring_rotation(),
		InputManager.get_cur_key_ring(), InputManager.get_cur_key2_ring(),
		InputManager.ring_pressure))
	lines.append(_side_text("[Leg/L]", InputManager.leg_connected, InputManager.leg_ip,
		InputManager.leg_last_packet_time, InputManager.raw_leg_rotation,
		GlobalObject.get_leg_rotation(),
		InputManager.get_cur_key_leg(), InputManager.get_cur_key2_leg(),
		InputManager.leg_pressure))
	lines.append("edges: %s" % _edge_counts)
	return "\n".join(lines)

func _process(delta: float) -> void:
	var text := _status_text()
	if _label != null:
		_label.text = text
	if DisplayServer.get_name() == "headless":
		_print_timer += delta
		if _print_timer >= 0.5:
			_print_timer = 0.0
			print("[UdpTest] " + text.replace("\n", " | "))

# ---- 自检(模拟器时序:标记 0-5s → 摆动+按键 5-10s → 静默 10-16s → 标记 16-20s)----

func _check(cond: bool, label: String) -> void:
	print(("[M2-TEST] PASS: " if cond else "[M2-TEST] FAIL: ") + label)
	if not cond:
		_failed = true

## 在 timeout 秒内等待 cond 为真(每帧轮询),返回是否等到
func _wait_for(cond: Callable, timeout: float) -> bool:
	var t := 0.0
	while t < timeout:
		if cond.call():
			return true
		await get_tree().process_frame
		t += get_tree().root.get_process_delta_time()
	return cond.call()

func _marker_ok() -> bool:
	return absf(InputManager.raw_ring_rotation.dot(MARKER_QUAT.normalized())) > 0.999

func _self_test() -> void:
	await get_tree().process_frame
	# 1. 连接建立(R+L 双路)
	_check(await _wait_for(func():
		return InputManager.ring_connected and InputManager.leg_connected, 10.0),
		"ring+leg connected")
	_check(InputManager.ring_ip == "127.0.0.1" and InputManager.leg_ip == "127.0.0.1",
		"source ip recorded: ring=%s leg=%s" % [InputManager.ring_ip, InputManager.leg_ip])
	# 2. 四元数解析(模拟器前 5 秒发固定标记,验证小端 float32 解码 + 归一化)
	_check(await _wait_for(_marker_ok, 3.0),
		"marker quat parsed: raw=%s expect≈%s" % [InputManager.raw_ring_rotation, MARKER_QUAT.normalized()])
	# 3. 设备连齐 → 广播自动停止
	_check(await _wait_for(func(): return not InputManager.is_broadcasting(), 4.0),
		"broadcast stops when all devices connected")
	# 4. 按键边沿 + 电平(模拟器 5-10 秒周期脉冲 k/k2)
	var saw_level := {"ring": false, "leg": false}
	var t := 0.0
	while t < 10.0:
		saw_level["ring"] = saw_level["ring"] or InputManager.get_cur_key_ring()
		saw_level["leg"] = saw_level["leg"] or InputManager.get_cur_key_leg()
		if saw_level.ring and saw_level.leg and not _edge_counts.values().any(func(c): return c == 0):
			break
		await get_tree().process_frame
		t += get_tree().root.get_process_delta_time()
	_check(_edge_counts.key1_right > 0 and _edge_counts.key2_right > 0
		and _edge_counts.key1_left > 0 and _edge_counts.key2_left > 0,
		"key edges all fired: %s" % _edge_counts)
	_check(saw_level.ring and saw_level.leg, "key levels readable via get_cur_key_*")
	# 5. 2 秒断线(模拟器 10 秒起静默)→ 广播重启
	_check(await _wait_for(func():
		return not InputManager.ring_connected and not InputManager.leg_connected, 12.0),
		"2s silence -> both sides disconnected")
	_check(await _wait_for(func(): return InputManager.is_broadcasting(), 3.0),
		"broadcast restarts after disconnect")
	# 6. 5 秒 socket 看门狗 → 重建
	_check(await _wait_for(func(): return InputManager.get_socket_rebuild_count() >= 1, 10.0),
		"5s no packet -> socket rebuilt (count=%d)" % InputManager.get_socket_rebuild_count())
	# 7. 恢复发包 → 重连 + 解析仍正确 + 广播再停
	_check(await _wait_for(func():
		return InputManager.ring_connected and InputManager.leg_connected, 12.0),
		"reconnect after silence")
	_check(await _wait_for(_marker_ok, 3.0), "marker quat parsed after reconnect")
	_check(await _wait_for(func(): return not InputManager.is_broadcasting(), 4.0),
		"broadcast stops again after reconnect")
	print("[M2-TEST] done, failed=%s" % _failed)
	get_tree().quit(1 if _failed else 0)
