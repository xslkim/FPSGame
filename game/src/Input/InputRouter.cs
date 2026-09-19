using Godot;

namespace FPSGame;

/// <summary>
/// InputRouter [autoload]:统一输入路由。对应原作 InputManager.cs / UdpServer.cs(4.1/4.3/4.4)。
/// 输入源:UDP 体感枪(双路 R/L)、键盘回落(方向键瞄准/回车扳机)、鼠标模拟光枪(MouseGun)。
/// 消费方通过 GetRightAim()/GetLegAim() 拿统一瞄准状态,通过 Trigger*/SwitchGun* 信号接边沿事件。
/// </summary>
public partial class InputRouter : Node
{
    public enum InputMode { Menu, OnlyRight, OnlyLeft, ControllerOrRight, RightAndLeft }

    [Signal] public delegate void TriggerRightEventHandler();   // 右手扳机边沿(0→1)
    [Signal] public delegate void SwitchGunRightEventHandler(); // 右手换枪边沿
    [Signal] public delegate void TriggerLeftEventHandler();    // 左手扳机边沿
    [Signal] public delegate void SwitchGunLeftEventHandler();  // 左手换枪边沿

    public static readonly float AimSpeed = Mathf.DegToRad(30.0f); // 原 0.5°/帧(帧率相关 bug)→ 30°/s × delta
    public static readonly float AimLimit = Mathf.DegToRad(45.0f); // pitch/yaw 限 ±45°
    public const double DeviceTimeout = 2.0; // 2 秒无数据 → 该路标记断开(4.1)

    public static InputRouter Instance { get; private set; } = null!;

    /// <summary>鼠标模拟光枪(无实体枪时的瞄准/扳机源)</summary>
    public MouseGunSource MouseGun { get; } = new();

    public InputMode Mode = InputMode.RightAndLeft;

    /// <summary>开火/换枪输入总开关(关卡开场演出期间禁用,7.4 节)</summary>
    public bool FireEnabled = true;

    // 原始四元数(UDP 写入 / 键盘由欧拉角合成),GunMath 取走后做枪口旋转解算
    public Quaternion RawRingRotation = Quaternion.Identity;
    public Quaternion RawLegRotation = Quaternion.Identity;

    // ---- UDP 状态(4.1:双路完全分离)----
    public bool RingConnected;  // 逻辑连接(ControllerOrRight 模式恒 true,物理断开时回落键盘)
    public bool LegConnected;
    public string RingIp => _server?.RingIp ?? "";
    public string LegIp => _server?.LegIp ?? "";
    public int RingPressure;  // 压力值(原作未用,解析保留)
    public int LegPressure;
    public double RingLastPacketTime = -1.0;
    public double LegLastPacketTime = -1.0;

    private UdpDeviceServer _server = null!;

    private double _now;
    private bool _ringPhyConnected;   // 物理连接(原作 _PhyRingConnected)
    private bool _ringHadPacket;      // 本帧是否收到 R 包(衰减回中用)
    private Quaternion _prevRawRing = Quaternion.Identity; // 上一个 R 包四元数(衰减回中用)

    private float _aimYaw;
    private float _aimPitch;
    private bool _leftAltHeld;

    private bool _key1RightLevel, _key1LeftLevel, _key2RightLevel, _key2LeftLevel;
    private bool _prevKey1Right, _prevKey1Left, _prevKey2Right, _prevKey2Left;

    public override void _Ready()
    {
        Instance = this;
        _server = new UdpDeviceServer();
        _server.PacketReceived += OnUdpPacket;
        _server.StartBroadcast();
    }

    /// <summary>设模式时按模式创建/停用左右玩家(4.4 节)。
    /// 照原作 SetInputMode:ControllerOrRight 置 RingConnected=true(允许键盘回落),其余清 false。</summary>
    public void SetInputMode(InputMode mode)
    {
        Mode = mode;
        RingConnected = mode == InputMode.ControllerOrRight;
        PlayerState.Instance.SetupPlayers(mode);
    }

    public override void _Input(InputEvent e)
    {
        // LeftAlt = 右手换枪调试键(全局)
        if (e is InputEventKey k && k.Keycode == Key.Alt &&
            (k.Location == KeyLocation.Left || k.Location == KeyLocation.Unspecified))
            _leftAltHeld = k.Pressed;
        MouseGun.HandleInput(e);
    }

    public override void _Process(double delta)
    {
        _now += delta;
        _server.Poll(delta);
        UpdateDisconnects();
        UpdateBroadcast();
        UpdateRingDecay((float)delta);
        if (KeyboardFallbackActive())
        {
            UpdateKeyboardAim((float)delta);
            UpdateKeyboardButtons();
        }
        EmitEdges();
        _ringHadPacket = false;
    }

    /// <summary>
    /// 22/23 字节小端包(4.1):
    /// [0] 'R'/'L'(其他丢弃);[1..16] 4×float32 四元数 x,y,z,w(归一化);
    /// [17..20] int32 压力值(解析保留);[21] 扳机 k;[22] 换枪 k2(缺省 0)。
    /// </summary>
    private void OnUdpPacket(byte[] data, string _fromIp)
    {
        if (data.Length < 22)
            return;
        byte flag = data[0];
        bool isRing;
        if (flag == 0x52) // 'R'
        {
            if (Mode == InputMode.OnlyLeft)
                return;
            isRing = true;
        }
        else if (flag == 0x4C) // 'L'
        {
            if (Mode == InputMode.OnlyRight)
                return;
            isRing = false;
        }
        else
            return;
        var q = new Quaternion(
            System.BitConverter.ToSingle(data, 1), System.BitConverter.ToSingle(data, 5),
            System.BitConverter.ToSingle(data, 9), System.BitConverter.ToSingle(data, 13));
        if (q.LengthSquared() < 1e-8)
            return;
        q = q.Normalized();
        int pressure = System.BitConverter.ToInt32(data, 17);
        bool k = data[21] != 0;
        bool k2 = data.Length >= 23 && data[22] != 0;
        if (isRing)
        {
            _prevRawRing = RawRingRotation;
            RawRingRotation = q;
            _ringHadPacket = true;
            RingPressure = pressure;
            RingConnected = true;
            _ringPhyConnected = true;
            RingLastPacketTime = _now;
            _key1RightLevel = k;
            _key2RightLevel = k2;
        }
        else
        {
            RawLegRotation = q;
            LegPressure = pressure;
            LegConnected = true;
            LegLastPacketTime = _now;
            _key1LeftLevel = k;
            _key2LeftLevel = k2;
        }
    }

    /// <summary>2 秒无数据 → 该路断开;ControllerOrRight 保留逻辑连接(回落键盘),其余模式照原作清标志</summary>
    private void UpdateDisconnects()
    {
        if (_ringPhyConnected && _now - RingLastPacketTime > DeviceTimeout)
        {
            _ringPhyConnected = false;
            if (Mode != InputMode.ControllerOrRight)
                RingConnected = false;
            GD.Print($"[InputRouter] 戒指(R) {DeviceTimeout:0} 秒无数据:phy 断开,RingConnected={RingConnected}");
        }
        if (LegConnected && _now - LegLastPacketTime > DeviceTimeout)
        {
            LegConnected = false;
            GD.Print($"[InputRouter] 腿部(L) {DeviceTimeout:0} 秒无数据:LegConnected=false");
        }
    }

    /// <summary>所需设备连齐 → 停止广播;断开 → 重启广播(4.1,照原作 StopBroadCast 判定)</summary>
    private void UpdateBroadcast()
    {
        bool ready = Mode switch
        {
            InputMode.OnlyRight or InputMode.ControllerOrRight => RingConnected,
            InputMode.OnlyLeft => LegConnected,
            InputMode.RightAndLeft => RingConnected && LegConnected,
            _ => false, // 菜单(连接手机页)持续广播
        };
        if (ready && _server.IsBroadcasting())
            _server.StopBroadcast();
        else if (!ready && !_server.IsBroadcasting())
            _server.StartBroadcast();
    }

    /// <summary>原作 Update:R 路本帧无新包时,按上两包欧拉角差 × delta × 0.1 衰减回中</summary>
    private void UpdateRingDecay(float delta)
    {
        if (!_ringPhyConnected || _ringHadPacket)
            return;
        var cur = RawRingRotation.GetEuler();
        var diff = cur - _prevRawRing.GetEuler();
        RawRingRotation = Quaternion.FromEuler(cur - diff * delta * 0.1f);
    }

    /// <summary>原作仅 ControllerOrRight 且无物理设备时回落键盘;
    /// 移植版放宽到所有含右玩家的模式(无设备时方便调试),设备连上后 UDP 自动接管。</summary>
    private bool KeyboardFallbackActive()
    {
        if (_ringPhyConnected)
            return false;
        return Mode is InputMode.OnlyRight or InputMode.ControllerOrRight or InputMode.RightAndLeft;
    }

    /// <summary>键盘瞄准:方向键 30°/s × delta,±45° 限位。
    /// 键盘输出是 Godot 空间四元数,先过一遍镜像(GunMath.ApplyMirror 为对合变换,
    /// 应用两次即还原),使 RawRingRotation 与 UDP 数据同为 Unity 约定,下游转换统一。</summary>
    private void UpdateKeyboardAim(float delta)
    {
        bool moved = false;
        if (Input.IsKeyPressed(Key.Left)) { _aimYaw += AimSpeed * delta; moved = true; }
        if (Input.IsKeyPressed(Key.Right)) { _aimYaw -= AimSpeed * delta; moved = true; }
        if (Input.IsKeyPressed(Key.Up)) { _aimPitch += AimSpeed * delta; moved = true; }
        if (Input.IsKeyPressed(Key.Down)) { _aimPitch -= AimSpeed * delta; moved = true; }
        if (!moved)
            return;
        _aimYaw = Mathf.Clamp(_aimYaw, -AimLimit, AimLimit);
        _aimPitch = Mathf.Clamp(_aimPitch, -AimLimit, AimLimit);
        RawRingRotation = GunMath.ApplyMirror(
            Quaternion.FromEuler(new Vector3(_aimPitch, _aimYaw, 0.0f)));
    }

    /// <summary>开火 = Return / 小键盘 Enter;换枪 = Menu 键 或 LeftAlt(右手调试)。
    /// 仅键盘回落激活时写右路电平;左路电平始终由 UDP 写入。</summary>
    private void UpdateKeyboardButtons()
    {
        _key1RightLevel = Input.IsKeyPressed(Key.Enter) || Input.IsKeyPressed(Key.KpEnter);
        _key2RightLevel = Input.IsKeyPressed(Key.Menu) || _leftAltHeld;
    }

    private void EmitEdges()
    {
        if (FireEnabled)
        {
            if (_key1RightLevel && !_prevKey1Right) EmitSignal(SignalName.TriggerRight);
            if (_key1LeftLevel && !_prevKey1Left) EmitSignal(SignalName.TriggerLeft);
            if (_key2RightLevel && !_prevKey2Right) EmitSignal(SignalName.SwitchGunRight);
            if (_key2LeftLevel && !_prevKey2Left) EmitSignal(SignalName.SwitchGunLeft);
        }
        _prevKey1Right = _key1RightLevel;
        _prevKey1Left = _key1LeftLevel;
        _prevKey2Right = _key2RightLevel;
        _prevKey2Left = _key2LeftLevel;
    }

    // ------------------------------------------------ 查询接口

    /// <summary>统一右路瞄准:鼠标模拟优先(无实体枪时),否则体感枪/键盘旋转</summary>
    public AimState GetRightAim() =>
        MouseGun.IsActiveForRight(this)
            ? AimState.Screen(MouseGun.AimPos)
            : AimState.Rot(GunMath.PhoneToGunRotation(RawRingRotation));

    /// <summary>左手瞄准(仅体感枪四元数)</summary>
    public AimState GetLeftAim() => AimState.Rot(GunMath.PhoneToGunRotation(RawLegRotation));

    /// <summary>扳机电平查询</summary>
    public bool GetCurKeyRing() => _key1RightLevel && FireEnabled;
    public bool GetCurKeyLeg() => _key1LeftLevel && FireEnabled;

    /// <summary>换枪电平查询(原作 GetCurKey2Ring/Leg)</summary>
    public bool GetCurKey2Ring() => _key2RightLevel;
    public bool GetCurKey2Leg() => _key2LeftLevel;

    /// <summary>向两端发送 "Reset"(原作 ResetDevice)</summary>
    public void ResetDevice() => _server.ResetDevice();

    /// <summary>设备发现广播开关(断线/重建时由内部自动管理,也可手动调用)</summary>
    public void StartDeviceBroadcast() => _server.StartBroadcast();
    public void StopDeviceBroadcast() => _server.StopBroadcast();
    public bool IsBroadcasting() => _server.IsBroadcasting();

    /// <summary>物理连接(不含 ControllerOrRight 键盘回落)</summary>
    public bool IsRingPhyConnected() => _ringPhyConnected;

    /// <summary>socket 重建次数(5 秒无包看门狗,自检用)</summary>
    public int GetSocketRebuildCount() => _server.RebuildCount;
}
