class_name UdpDeviceServer
extends RefCounted
## UdpDeviceServer:绑定 8281/udp 收包;向手机 8282/udp 发 Heart/Reset;
## 向 255.255.255.255:8282 广播 "Fortune" 做设备发现;socket 级 5 秒无包重建。
## 对应原作 UdpServer.cs + UdpBroadCast.cs(方案 4.1),由 InputManager 持有并每帧 poll()。

signal packet_received(data: PackedByteArray, from_ip: String)

const LISTEN_PORT := 8281
const PHONE_PORT := 8282
const BROADCAST_ADDR := "255.255.255.255"
const HEART_INTERVAL := 1.0
const BROADCAST_INTERVAL := 1.0
const SOCKET_TIMEOUT := 5.0  # 5 秒无任何包 → 重建 socket 并重启广播

var ring_ip := ""
var leg_ip := ""
var rebuild_count := 0  # socket 重建次数(自检/调试用)

var _recv: PacketPeerUDP
var _send: PacketPeerUDP
var _broadcasting := false
var _heart_timer := 0.0
var _broadcast_timer := 0.0
var _no_packet_time := 0.0
var _ever_received := false
var _rebind_timer := 0.0
var _bound := false

func _init() -> void:
	_open_socket()

func poll(delta: float) -> void:
	if _recv == null or not _bound:
		_rebind_timer += delta
		if _rebind_timer >= 1.0:
			_rebind_timer = 0.0
			_open_socket()
		return
	while _recv.get_available_packet_count() > 0:
		var pkt := _recv.get_packet()
		var ip := _recv.get_packet_ip()
		_no_packet_time = 0.0
		_ever_received = true
		# 来源 IP 在模式过滤前记录(照原作 UdpServer.RingIp/LegIp 语义)
		if pkt.size() >= 1:
			if pkt[0] == 0x52:  # 'R'
				ring_ip = ip
			elif pkt[0] == 0x4C:  # 'L'
				leg_ip = ip
		packet_received.emit(pkt, ip)
	_no_packet_time += delta
	if _ever_received and _no_packet_time > SOCKET_TIMEOUT:
		_no_packet_time = 0.0
		rebuild_count += 1
		print("[UdpDeviceServer] %d 秒无任何包,重建 socket 并重启广播" % int(SOCKET_TIMEOUT))
		_open_socket()
		start_broadcast()
	# 心跳:每 1 秒向已知 R/L IP 的 8282 发 ASCII "Heart"(原作只看 IP 是否已知)
	_heart_timer += delta
	if _heart_timer >= HEART_INTERVAL:
		_heart_timer = 0.0
		if ring_ip != "":
			_send_text("Heart", ring_ip)
		if leg_ip != "":
			_send_text("Heart", leg_ip)
	# 设备发现广播:每 1 秒向 255.255.255.255:8282 发 UTF-8 "Fortune"
	if _broadcasting:
		_broadcast_timer += delta
		if _broadcast_timer >= BROADCAST_INTERVAL:
			_broadcast_timer = 0.0
			_send_text("Fortune", BROADCAST_ADDR)

func start_broadcast() -> void:
	if not _broadcasting:
		print("[UdpDeviceServer] 开始设备发现广播 Fortune → %s:%d" % [BROADCAST_ADDR, PHONE_PORT])
	_broadcasting = true
	_broadcast_timer = BROADCAST_INTERVAL  # 下一帧立即发一次

func stop_broadcast() -> void:
	if _broadcasting:
		print("[UdpDeviceServer] 所需设备已连齐,停止广播")
	_broadcasting = false

func is_broadcasting() -> bool:
	return _broadcasting

## 向两端发送 "Reset"(原作 InputManager.ResetDevice)
func reset_device() -> void:
	if ring_ip != "":
		_send_text("Reset", ring_ip)
	if leg_ip != "":
		_send_text("Reset", leg_ip)

func _open_socket() -> void:
	if _recv != null:
		_recv.close()
	_recv = PacketPeerUDP.new()
	_bound = _recv.bind(LISTEN_PORT) == OK
	if not _bound:
		push_error("[UdpDeviceServer] 绑定 %d/udp 失败" % LISTEN_PORT)
	if _send == null:
		_send = PacketPeerUDP.new()
		_send.set_broadcast_enabled(true)

func _send_text(text: String, ip: String) -> void:
	if _send == null:
		return
	_send.set_dest_address(ip, PHONE_PORT)
	var err := _send.put_packet(text.to_utf8_buffer())
	if err != OK:
		push_warning("[UdpDeviceServer] 发送 \"%s\" → %s 失败: %s" % [text, ip, error_string(err)])
