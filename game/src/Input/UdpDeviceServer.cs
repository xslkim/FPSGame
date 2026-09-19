using Godot;

namespace FPSGame;

/// <summary>
/// UdpDeviceServer:绑定 8281/udp 收包;向手机 8282/udp 发 Heart/Reset;
/// 向 255.255.255.255:8282 广播 "Fortune" 做设备发现;socket 级 5 秒无包重建。
/// 对应原作 UdpServer.cs + UdpBroadCast.cs,由 InputRouter 持有并每帧 Poll()。
/// </summary>
public sealed class UdpDeviceServer
{
    public const int ListenPort = 8281;
    public const int PhonePort = 8282;
    public const string BroadcastAddr = "255.255.255.255";
    public const double HeartInterval = 1.0;
    public const double BroadcastInterval = 1.0;
    public const double SocketTimeout = 5.0; // 5 秒无任何包 → 重建 socket 并重启广播

    /// <summary>收到 UDP 包(数据, 来源 IP)</summary>
    public event System.Action<byte[], string>? PacketReceived;

    public string RingIp = "";
    public string LegIp = "";
    public int RebuildCount; // socket 重建次数(自检/调试用)

    private PacketPeerUdp? _recv;
    private PacketPeerUdp? _send;
    private bool _broadcasting;
    private double _heartTimer;
    private double _broadcastTimer;
    private double _noPacketTime;
    private bool _everReceived;
    private double _rebindTimer;
    private bool _bound;

    public UdpDeviceServer() => OpenSocket();

    public void Poll(double delta)
    {
        if (_recv == null || !_bound)
        {
            _rebindTimer += delta;
            if (_rebindTimer >= 1.0)
            {
                _rebindTimer = 0.0;
                OpenSocket();
            }
            return;
        }
        while (_recv.GetAvailablePacketCount() > 0)
        {
            var pkt = _recv.GetPacket();
            string ip = _recv.GetPacketIP();
            _noPacketTime = 0.0;
            _everReceived = true;
            // 来源 IP 在模式过滤前记录(照原作 UdpServer.RingIp/LegIp 语义)
            if (pkt.Length >= 1)
            {
                if (pkt[0] == 0x52) // 'R'
                    RingIp = ip;
                else if (pkt[0] == 0x4C) // 'L'
                    LegIp = ip;
            }
            PacketReceived?.Invoke(pkt, ip);
        }
        _noPacketTime += delta;
        if (_everReceived && _noPacketTime > SocketTimeout)
        {
            _noPacketTime = 0.0;
            RebuildCount += 1;
            GD.Print($"[UdpDeviceServer] {(int)SocketTimeout} 秒无任何包,重建 socket 并重启广播");
            OpenSocket();
            StartBroadcast();
        }
        // 心跳:每 1 秒向已知 R/L IP 的 8282 发 ASCII "Heart"(原作只看 IP 是否已知)
        _heartTimer += delta;
        if (_heartTimer >= HeartInterval)
        {
            _heartTimer = 0.0;
            if (RingIp != "") SendText("Heart", RingIp);
            if (LegIp != "") SendText("Heart", LegIp);
        }
        // 设备发现广播:每 1 秒向 255.255.255.255:8282 发 UTF-8 "Fortune"
        if (_broadcasting)
        {
            _broadcastTimer += delta;
            if (_broadcastTimer >= BroadcastInterval)
            {
                _broadcastTimer = 0.0;
                SendText("Fortune", BroadcastAddr);
            }
        }
    }

    public void StartBroadcast()
    {
        if (!_broadcasting)
            GD.Print($"[UdpDeviceServer] 开始设备发现广播 Fortune → {BroadcastAddr}:{PhonePort}");
        _broadcasting = true;
        _broadcastTimer = BroadcastInterval; // 下一帧立即发一次
    }

    public void StopBroadcast()
    {
        if (_broadcasting)
            GD.Print("[UdpDeviceServer] 所需设备已连齐,停止广播");
        _broadcasting = false;
    }

    public bool IsBroadcasting() => _broadcasting;

    /// <summary>向两端发送 "Reset"(原作 InputManager.ResetDevice)</summary>
    public void ResetDevice()
    {
        if (RingIp != "") SendText("Reset", RingIp);
        if (LegIp != "") SendText("Reset", LegIp);
    }

    private void OpenSocket()
    {
        _recv?.Close();
        _recv = new PacketPeerUdp();
        _bound = _recv.Bind(ListenPort) == Error.Ok;
        if (!_bound)
            GD.PushError($"[UdpDeviceServer] 绑定 {ListenPort}/udp 失败");
        if (_send == null)
        {
            _send = new PacketPeerUdp();
            _send.SetBroadcastEnabled(true);
        }
    }

    private void SendText(string text, string ip)
    {
        if (_send == null)
            return;
        _send.SetDestAddress(ip, PhonePort);
        var err = _send.PutPacket(text.ToUtf8Buffer());
        if (err != Error.Ok)
            GD.PushWarning($"[UdpDeviceServer] 发送 \"{text}\" → {ip} 失败: {err}");
    }
}
