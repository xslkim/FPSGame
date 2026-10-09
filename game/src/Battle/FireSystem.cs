using Godot;

namespace FPSGame;

/// <summary>
/// FireSystem:挂在战斗相机下,管理左右枪节点(原作 FireSystem.cs)。
/// 每帧(LateUpdate 语义):枪口旋转 = 输入瞄准 → 枪口射线 2000 →
/// 激光瞄准器从枪口到实际射线命中点(未命中时延伸 200m) →
/// 命中点摆光标火光(右手红 Flash.prefab/左手绿 FlashGreen.prefab,距离衰减公式照 5.3) →
/// 扳机 → Button 触发 / 开枪(CD+耗弹) →
/// 敌人 hit() 或环境弹着特效(池化)。换枪:key2 边沿 → 旧枪 0.5s 下沉收起 →
/// 新枪直接出现在最终位置(照原作字面行为:伸出循环作用于已隐藏旧枪,新枪瞬现;换枪期不挡扳机)。
/// 弹尽且 Battle 态 → OpenContinue(true, side)(只发一次,补弹复位)。
/// </summary>
public partial class FireSystem : Node3D, IDebugInspectable
{
    private sealed class FireRaySnapshot
    {
        public Vector3 Origin;
        public Vector3 Muzzle;
        public Vector3 Direction;
        public Vector3 HitPoint;
        public Node? HitNode;
        public bool HasHit;
    }

    private readonly System.Collections.Generic.Dictionary<PlayerState.Side, FireRaySnapshot> _rayDebug = new();

    public string DebugSummary => $"R={GunName(PlayerState.Side.Right)} L={GunName(PlayerState.Side.Left)}";

    private string GunName(PlayerState.Side side) =>
        _currentGun.TryGetValue(side, out var gun) && gun != null ? gun.GunType.ToString() : "none";

    public System.Collections.Generic.Dictionary<string, object?> CaptureDebugState()
    {
        var rays = new System.Collections.Generic.Dictionary<string, object?>();
        foreach (var side in new[] { PlayerState.Side.Right, PlayerState.Side.Left })
        {
            _rayDebug.TryGetValue(side, out var ray);
            rays[side.ToString()] = new System.Collections.Generic.Dictionary<string, object?>
            {
                ["gun_type"] = GunName(side),
                ["laser_visible"] = _lazer.TryGetValue(side, out var laser) && laser.Visible,
                ["ray_origin"] = ray?.Origin.ToString(),
                ["muzzle_world"] = ray?.Muzzle.ToString(),
                ["visual_muzzle_world"] = _currentGun.TryGetValue(side, out var current) && current != null ? current.VisualMuzzle.ToString() : null,
                ["muzzle_error_meters"] = current != null ? current.Muzzle.GlobalPosition.DistanceTo(current.VisualMuzzle) : null,
                ["ray_direction"] = ray?.Direction.ToString(),
                ["hit_id"] = ray?.HitNode != null && GodotObject.IsInstanceValid(ray.HitNode)
                    ? DebugIdentity.ObjectId(GetTree().CurrentScene, ray.HitNode) : "(none)",
                ["hit_world"] = ray?.HasHit == true ? ray.HitPoint.ToString() : null,
                ["beam_end_world"] = laser?.EndPoint.ToString(),
                ["impact_screen"] = _flash.TryGetValue(side, out var flash) && flash.Visible
                    ? _camera.UnprojectPosition(flash.GlobalPosition).ToString() : null,
                ["impact_alignment_error_pixels"] = ray?.HasHit == true && flash != null && flash.Visible
                    ? _camera.UnprojectPosition(flash.GlobalPosition).DistanceTo(_camera.UnprojectPosition(ray.HitPoint)) : null,
            };
        }
        return new System.Collections.Generic.Dictionary<string, object?> { ["sides"] = rays };
    }
    [Signal] public delegate void OpenContinueEventHandler(bool isOpen, int side);

    public const float RayLength = 2000.0f;
    public const uint RayMask = 0xFFFFFFF7; // 除 layer4(CameraWall)外全部
    public const int PoolSize = 3;
    public const int ConcretePoolSize = 4;    // 原作 FireSystem.prefab ConcreteEffect×4(其余×3)
    public const int BloodFlowerPoolSize = 3; // 原作 _BloodEffects×3
    public const float SwitchSink = 0.10f;   // 换枪下沉量(+Z,相机看 -Z)
    public const float SwitchDownTime = 0.5f;
    /// <summary>手枪/M4 的屏幕占比校准值。</summary>
    public const float GunModelScale = 0.725f;
    // AK FBX 的本体比例与手枪不同；按用户截图和枪口投影单独校准。
    public const float AkModelScale = 0.20f;

    public static readonly string[] GunModelPaths =
    {
        "res://assets/models/guns/ak47/ak47.fbx",
        "res://assets/models/guns/m4/m4.fbx",
        "res://assets/models/guns/handgun/handgun.fbx",
    };

    public static readonly System.Collections.Generic.Dictionary<string, string> EffectScenes = new()
    {
        ["Wood"] = "res://assets/effects/impact_wood.tscn",
        ["Metal"] = "res://assets/effects/impact_metal.tscn",
        ["Blood"] = "res://assets/effects/impact_blood.tscn",
        ["Concrete"] = "res://assets/effects/impact_concrete.tscn",
        ["Dust"] = "res://assets/effects/impact_dust.tscn",
    };

    public const string FlashCursorPath = "res://assets/effects/textures/flash_point19.png";
    public const string UiShotSoundPath = "res://assets/audio/ui/shot.wav";

    /// <summary>当前战斗的 FireSystem 实例(怪物血花/飘字按此访问)</summary>
    public static FireSystem? Current { get; private set; }

    /// <summary>--fire-debug:每帧打印右路射线命中(诊断激光/红点落点)</summary>
    public static bool DebugFire
    {
        get
        {
            if (!_debugSearched)
            {
                _debugSearched = true;
                _debugFire = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--fire-debug") >= 0;
            }
            return _debugFire;
        }
    }
    private static bool _debugSearched;
    private static bool _debugFire;

    /// <summary>自检:所有已绑枪的 Player 引用非空(GunBase.Fire 依赖;缺失=开火 NRE)</summary>
    public bool AllGunsBound
    {
        get
        {
            foreach (var side in _guns.Values)
            foreach (var g in side.Values)
                if (g.Player == null)
                    return false;
            return true;
        }
    }

    private Camera3D _camera = null!;
    private readonly System.Collections.Generic.Dictionary<PlayerState.Side, Node3D> _anchors = new();
    private readonly System.Collections.Generic.Dictionary<PlayerState.Side, System.Collections.Generic.Dictionary<int, GunBase>> _guns = new();
    private readonly System.Collections.Generic.Dictionary<PlayerState.Side, GunBase?> _currentGun = new();
    private readonly System.Collections.Generic.Dictionary<PlayerState.Side, MeshInstance3D> _flash = new();
    private readonly System.Collections.Generic.Dictionary<PlayerState.Side, LaserSight> _lazer = new();
    private readonly System.Collections.Generic.Dictionary<PlayerState.Side, bool> _emptySignaled = new();
    private readonly System.Collections.Generic.Dictionary<string, EffectBase[]> _effects = new();
    private readonly System.Collections.Generic.Dictionary<string, int> _effectIdx = new();
    private EffectBase[] _bloodFlowers = System.Array.Empty<EffectBase>();
    /// <summary>掉血飘字池(原作 MonstHp HpReduceText:每怪 3 个复用池)</summary>
    private readonly System.Collections.Generic.Dictionary<Node3D, Label3D[]> _damagePools = new();
    private AudioStreamPlayer? _uiShotPlayer;

    public override void _Ready()
    {
        Current = this;
        ProcessMode = ProcessModeEnum.Always; // 暂停期仍要响应面板按钮射击
        _camera = GetParent() as Camera3D ?? GetViewport().GetCamera3D();
        foreach (var side in new[] { PlayerState.Side.Right, PlayerState.Side.Left })
        {
            string sideName = side == PlayerState.Side.Right ? "Right" : "Left";
            var anchor = new Node3D { Name = sideName + "GunAnchor" };
            AddChild(anchor);
            _anchors[side] = anchor;
            _guns[side] = new System.Collections.Generic.Dictionary<int, GunBase>();
            _currentGun[side] = null;
            _rayDebug[side] = new FireRaySnapshot();
            _emptySignaled[side] = false;
            // 命中光标火光(原作 Effect/Flash.prefab 右手红 (1,0,0) / FlashGreen.prefab 左手绿 (0,1,0.034):
            // 根 scale 0.5(运行时被 flashScale 公式覆盖)、Darkness 粒子 startSize 0.5、
            // Blend_CenterGlow alpha 混合;flash_point19.png 与原作 Point19.png 同源但 alpha 全 255
            // → LoadFlashCursor 取亮度作 alpha,颜色交给 AlbedoColor 按手染色)
            var flashMat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BlendMode = BaseMaterial3D.BlendModeEnum.Mix,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = side == PlayerState.Side.Right
                    ? new Color(1.0f, 0.0f, 0.0f)
                    : new Color(0.0f, 1.0f, 0.0345f),
                AlbedoTexture = LoadFlashCursor(),
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                BillboardKeepScale = true,
                DisableFog = true,
                RenderPriority = 121,
            };
            var flash = new MeshInstance3D
            {
                Name = "Flash" + sideName,
                TopLevel = true,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
                Mesh = new QuadMesh { Size = new Vector2(0.5f, 0.5f) }, // = 原作粒子 startSize,软边贴图亮核约 1/3
                MaterialOverride = flashMat,
            };
            AddChild(flash);
            _flash[side] = flash;
            // 激光瞄准器(原作 Lazer.prefab:右红/左绿;宽度曲线 0.0089→0.03 近细远粗);
            // Clip at the hit surface; the separate Flash marks that same endpoint.
            var lazer = LaserSight.Create(
                side == PlayerState.Side.Right ? LaserSight.RightRed : LaserSight.LeftGreen,
                0.015f, withDot: false, nearRadius: 0.00894f * 0.5f);
            lazer.Visible = false;
            lazer.FullLength = 200;
            AddChild(lazer);
            _lazer[side] = lazer;
        }
        foreach (var kv in EffectScenes)
        {
            int n = kv.Key == "Concrete" ? ConcretePoolSize : PoolSize;
            var pool = new EffectBase[n];
            for (int i = 0; i < n; i++)
            {
                var e = GD.Load<PackedScene>(kv.Value).Instantiate<EffectBase>();
                e.Name = $"Fx_{kv.Key}_{i}";
                e.Visible = false;
                AddChild(e);
                pool[i] = e;
            }
            _effects[kv.Key] = pool;
            _effectIdx[kv.Key] = 0;
        }
        _bloodFlowers = new EffectBase[BloodFlowerPoolSize];
        for (int i = 0; i < BloodFlowerPoolSize; i++)
        {
            var bf = GD.Load<PackedScene>("res://assets/effects/blood_flower.tscn").Instantiate<EffectBase>();
            bf.Name = $"BloodFlower_{i}";
            bf.Visible = false;
            AddChild(bf);
            _bloodFlowers[i] = bf;
        }
        if (ResourceLoader.Exists(UiShotSoundPath))
        {
            _uiShotPlayer = new AudioStreamPlayer
            {
                Name = "UIShotSound",
                Stream = GD.Load<AudioStream>(UiShotSoundPath),
            };
            AddChild(_uiShotPlayer);
        }
        InputRouter.Instance.SwitchGunRight += OnSwitchRight;
        InputRouter.Instance.SwitchGunLeft += OnSwitchLeft;
        RenderingServer.FramePreDraw += SyncVisualBeams;
        // --autofire:测试挂接,每 0.4s 强制右路开火(截图验证枪口火光/烟雾位置)
        _autoFire = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--autofire") >= 0;
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--combat-presentation-selftest") >= 0)
            CallDeferred(nameof(PresentationSelfTest));
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--combat-presentation-shot:"))
                CallDeferred(nameof(PresentationShot), arg["--combat-presentation-shot:".Length..]);
    }

    private bool _autoFire;
    private float _autoFireTimer;
    private readonly System.Collections.Generic.HashSet<PlayerState.Side> _switchingSides = new();
    private bool _rightUiPullConsumed, _leftUiPullConsumed;

    private void OnSwitchRight() => OnSwitchKey(PlayerState.Side.Right);
    private void OnSwitchLeft() => OnSwitchKey(PlayerState.Side.Left);

    public override void _ExitTree()
    {
        RenderingServer.FramePreDraw -= SyncVisualBeams;
        if (Current == this) Current = null;
        if (InputRouter.Instance == null) return;
        InputRouter.Instance.SwitchGunRight -= OnSwitchRight;
        InputRouter.Instance.SwitchGunLeft -= OnSwitchLeft;
    }

    private void SyncVisualBeams()
    {
        // Camera tweens and model animation can advance after _Process. Resolve
        // the visible beam from the final barrel pose immediately before drawing.
        foreach (var side in new[] { PlayerState.Side.Right, PlayerState.Side.Left })
            if (_currentGun.TryGetValue(side, out var gun) && gun != null
                && _lazer.TryGetValue(side, out var laser) && laser.Visible)
            {
                gun.RefreshMuzzle();
                var from = gun.Muzzle.GlobalPosition;
                var hit = GetWorld3D().DirectSpaceState.IntersectRay(
                    PhysicsRayQueryParameters3D.Create(from, from + gun.BarrelDirection * RayLength, RayMask));
                PresentRayHit(side, gun, hit);
            }
    }

    private async void PresentationSelfTest()
    {
        GetTree().Root.Size = new Vector2I(1280, 720); // headless defaults to a square viewport
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        SetProcess(false);
        bool failed = false;
        void Check(bool condition, string label)
        {
            GD.Print($"[COMBAT-PRESENTATION] {(condition ? "PASS" : "FAIL")}: {label}");
            failed |= !condition;
        }
        var player = PlayerState.Instance.PlayerRight;
        player.Born();
        player.Hp = 10000;
        BindPlayers();
        var gunList = _guns[PlayerState.Side.Right];
        var expected = new[] { new Vector3(0, 0.014569f, -0.119348f),
            new Vector3(0, 0.004887f, -0.308574f), new Vector3(0, 0.0014615f, -0.052324f) };
        foreach (var gun in gunList.Values)
            Check(gun.ToLocal(gun.VisualMuzzle).DistanceTo(expected[gun.GunType]) < 0.0001f,
                $"{gun.GunName} true barrel front measured from FBX");
        float originalFov = _camera.Fov;
        foreach (float fov in new[] { 45f, 52f })
        {
            _camera.Fov = fov;
            foreach (var gun in gunList.Values)
            {
                gun.Position = GunBasePos(PlayerState.Side.Right, gun.GunType);
                gun.AimRotation(Quaternion.Identity);
                var screen = _camera.UnprojectPosition(gun.VisualMuzzle) / GetViewport().GetVisibleRect().Size;
                Check(screen.X > 0.65f && screen.X < 1.0f && screen.Y > 0.6f && screen.Y < 0.97f,
                    $"{gun.GunName} visible muzzle in lower right at FOV {fov}: {screen}");
            }
        }
        _camera.Fov = originalFov;
        foreach (var gun in gunList.Values)
            gun.Position = GunBasePos(PlayerState.Side.Right, gun.GunType);
        foreach (float yaw in new[] { -25.203205f, 155.59274f, 40.0f })
        {
            _camera.Rotation = new Vector3(0.04f, Mathf.DegToRad(yaw), 0);
            foreach (var gun in gunList.Values)
            foreach (var point in new[] { new Vector2(0.1f, 0.2f), new Vector2(0.5f, 0.5f), new Vector2(0.85f, 0.75f) })
            {
                var screen = point * GetViewport().GetVisibleRect().Size;
                var target = _camera.ProjectPosition(screen, 20);
                gun.AimAt(target);
                Check(gun.Muzzle.GlobalPosition.DistanceTo(gun.VisualMuzzle) < 0.00001f
                    && gun.BarrelDirection.Cross((target - gun.VisualMuzzle).Normalized()).Length() < 0.0001f,
                    $"{gun.GunName} muzzle/ray collinear at camera yaw={yaw}, aim={point}");
            }
        }
        foreach (var gun in gunList.Values)
        {
            _currentGun[PlayerState.Side.Right] = gun;
            gun.LastFireTime = -99;
            Check(gun.Fire().Success, $"{gun.GunName} fires");
            var laser = _lazer[PlayerState.Side.Right];
            laser.SetBeam(gun.VisualMuzzle, gun.VisualMuzzle + gun.BarrelDirection * 200);
            for (int i = 0; i < 8; i++)
            {
                _camera.RotateY(0.02f); // camera motion in the same frame as recoil
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                SyncVisualBeams();
                Check(gun.Muzzle.GlobalPosition.DistanceTo(gun.VisualMuzzle) < 0.00001f,
                    $"{gun.GunName} muzzle follows animated recoil frame {i}");
                Check(laser.GlobalPosition.DistanceTo(gun.VisualMuzzle) < 0.00001f
                    && (-laser.GlobalBasis.Z).Dot(gun.BarrelDirection) > 0.99999f,
                    $"{gun.GunName} visible beam follows camera/recoil frame {i}");
            }
        }
        player.GunType = 2;
        _currentGun[PlayerState.Side.Right] = gunList[2];
        for (int i = 0; i < 3; i++)
        {
            int expectedType = (player.GunType + 1) % 3;
            OnSwitchRight();
            await ToSignal(GetTree().CreateTimer(0.55), SceneTreeTimer.SignalName.Timeout);
            var gun = _currentGun[PlayerState.Side.Right]!;
            gun.AimRotation(Quaternion.Identity);
            Check(gun.GunType == expectedType && gun.Muzzle.GlobalPosition.DistanceTo(gun.VisualMuzzle) < 0.00001f,
                $"switch to {expectedType} keeps calibrated muzzle");
        }
        int rapidExpected = (player.GunType + 1) % 3;
        OnSwitchRight();
        OnSwitchRight();
        OnSwitchRight();
        await ToSignal(GetTree().CreateTimer(0.55), SceneTreeTimer.SignalName.Timeout);
        int visibleGuns = 0;
        foreach (var gun in gunList.Values) if (gun.Visible) visibleGuns++;
        Check(player.GunType == rapidExpected && _currentGun[PlayerState.Side.Right]!.GunType == rapidExpected
            && visibleGuns == 1, "rapid swap presses cannot overlap weapons or desynchronize selected gun");
        player.Hp = 37;
        PlayerState.Instance.NotifyUiChanged();
        var state = PlayerState.Instance.CaptureDebugState();
        Check((string?)state["right_health_text"] == "生命 37 / 100"
            && state["right_health_visible"] is true, "visible live health value 37/100");
        GD.Print($"[COMBAT-PRESENTATION] {(failed ? "FAILED" : "ALL PASS")}");
        GetTree().Quit(failed ? 1 : 0);
    }

    private async void PresentationShot(string specification)
    {
        var fields = specification.Split('|'); // path|camera marker index|gun type|HP
        if (fields.Length < 4 || fields.Length > 5) throw new System.ArgumentException("Expected path|camera|gun|HP[|controller]");
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var level = GetTree().CurrentScene;
        level.ProcessMode = ProcessModeEnum.Disabled;
        _camera.GlobalTransform = level.GetNode<Node3D>($"CamPositions/cam_pos_{fields[1]}").GlobalTransform;
        var player = PlayerState.Instance.PlayerRight;
        player.Born();
        player.GunType = int.Parse(fields[2]);
        player.Hp = float.Parse(fields[3]);
        InputRouter.Instance.Mode = fields.Length == 5 ? InputRouter.InputMode.ControllerOrRight : InputRouter.InputMode.Mouse;
        InputRouter.Instance.MouseGun.Enabled = fields.Length == 4;
        InputRouter.Instance.MouseGun.SimulateMove(new Vector2(555, 479));
        BindPlayers();
        PlayerState.Instance.NotifyUiChanged();
        UpdateSide(PlayerState.Side.Right);
        var shotGun = _currentGun[PlayerState.Side.Right]!;
        GD.Print($"[COMBAT-SHOT] before draw muzzle={_camera.UnprojectPosition(shotGun.VisualMuzzle)} laser={_camera.UnprojectPosition(_lazer[PlayerState.Side.Right].GlobalPosition)} camera={_camera.GlobalPosition}");
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GD.Print($"[COMBAT-SHOT] after draw muzzle={_camera.UnprojectPosition(shotGun.VisualMuzzle)} laser={_camera.UnprojectPosition(_lazer[PlayerState.Side.Right].GlobalPosition)} camera={_camera.GlobalPosition}");
        GetViewport().GetTexture().GetImage().SavePng(fields[0]);
        GD.Print($"[COMBAT-PRESENTATION] shot saved: {fields[0]}");
        GetTree().Quit();
    }

    // ------------------------------------------------ 绑定玩家(战斗开始时)

    public void BindPlayers()
    {
        BindSide(PlayerState.Side.Right, PlayerState.Instance.PlayerRight);
        BindSide(PlayerState.Side.Left, PlayerState.Instance.PlayerLeft);
    }

    private void BindSide(PlayerState.Side side, Player player)
    {
        foreach (var g in _guns[side].Values)
            g.QueueFree();
        _guns[side].Clear();
        _currentGun[side] = null;
        _lazer[side].HideBeam();
        var anchor = _anchors[side];
        if (!player.Active)
        {
            anchor.Visible = false;
            return;
        }
        anchor.Visible = true;
        for (int type = 0; type < SaveService.Instance.GunTypeNum && type < GunModelPaths.Length; type++)
        {
            if ((player.Guns & (1 << type)) == 0)
                continue;
            var gun = new GunBase();
            // 先挂枪口节点,Setup 会往 Muzzle 下挂火光
            var muzzle = new Node3D { Name = "Muzzle" };
            gun.AddChild(muzzle);
            gun.Setup(type, side == PlayerState.Side.Left);
            gun.Player = player; // Fire() 耗弹/激活判定依赖(缺失会在开火时 NRE)
            // 真实枪模型(FBX 导入场景根):ak47 枪管沿 -X(rotY-90→-Z,scale 0.4);
            // m4/handgun 枪管沿 +Z(实测:枪口/准星朝 +Z)→ rotY 180° 转向 -Z。
            // 统一 ×GunModelScale(0.725) 对真值截图枪占屏比。
            // 贴图手动接线(FBX 未内嵌)
            var model = GD.Load<PackedScene>(GunModelPaths[type]).Instantiate<Node3D>();
            model.Name = "Model";
            model.Rotation = type == 0
                ? new Vector3(0, -Mathf.Pi / 2.0f, 0)
                : new Vector3(0, Mathf.Pi, 0);
            model.Scale = Vector3.One * (type == 0 ? 0.4f * AkModelScale : GunModelScale);
            WireGunMaterial(model, type);
            gun.AddChild(model);
            anchor.AddChild(gun);
            gun.Position = GunBasePos(side, type);
            gun.CalibrateMuzzle();
            gun.Visible = type == player.GunType;
            _guns[side][type] = gun;
            if (gun.Visible)
                _currentGun[side] = gun;
        }
    }

    /// <summary>枪身材质:FBX 未内嵌贴图,手动接 <name>_tex.png</summary>
    private static void WireGunMaterial(Node3D model, int type)
    {
        string dir = type switch { 0 => "ak47", 1 => "m4", _ => "handgun" };
        string texPath = $"res://assets/models/guns/{dir}/{dir}_tex.png";
        if (!ResourceLoader.Exists(texPath))
            return;
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = GD.Load<Texture2D>(texPath),
            Roughness = 0.8f,
        };
        foreach (var mi in model.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (mi is MeshInstance3D m)
                m.MaterialOverride = mat;
        }
    }

    private Vector3 GunBasePos(PlayerState.Side side, int type)
    {
        var info = SaveService.Instance.GetGunInfo(type);
        bool wide = _camera.Fov > 50;
        var p = SaveService.Get(info, wide ? "pos60" : "pos",
            new Godot.Collections.Dictionary { ["x"] = 0.1, ["y"] = -0.04, ["z"] = 0.16 }).AsGodotDictionary();
        var v = new Vector3((float)p["x"].AsDouble(), (float)p["y"].AsDouble(), -(float)p["z"].AsDouble());
        if (side == PlayerState.Side.Left)
            v.X = -v.X;
        return v;
    }

    // ------------------------------------------------ 每帧流程

    public override void _Process(double delta)
    {
        if (!InputRouter.Instance.GetCurKeyRing()) _rightUiPullConsumed = false;
        if (!InputRouter.Instance.GetCurKeyLeg()) _leftUiPullConsumed = false;
        // 暂停期只放行 Button 命中(枪打面板按钮),其余冻结
        UpdateSide(PlayerState.Side.Right);
        UpdateSide(PlayerState.Side.Left);
        if (_autoFire && !Game.Instance.IsGamePause)
        {
            _autoFireTimer -= (float)delta;
            if (_autoFireTimer <= 0.0f)
            {
                _autoFireTimer = 0.4f;
                _currentGun[PlayerState.Side.Right]?.Fire(); // 火光/烟雾/后座,不走射线命中
            }
        }
    }

    private void UpdateSide(PlayerState.Side side)
    {
        var player = PlayerState.Instance.GetPlayer(side);
        var anchor = _anchors[side];
        var flash = _flash[side];
        if (!player.Active)
        {
            anchor.Visible = false;
            flash.Visible = false;
            _lazer[side].HideBeam();
            return;
        }
        anchor.Visible = true;
        // 1. 冰冻且未暂停 → 本帧跳过(不转枪不开火);暂停期冰冻手仍可触发面板按钮(照原作)
        if (player.Status == Player.HurtState.Frozen && !Game.Instance.IsGamePause)
        {
            _lazer[side].HideBeam();
            return;
        }
        var gun = _currentGun[side];
        if (gun == null)
        {
            _lazer[side].HideBeam();
            return;
        }
        // 2. Use the screen ray to choose a target, then solve the actual barrel
        // pose. Collision, laser and muzzle flash all use that same muzzle/axis.
        AimState aim = side == PlayerState.Side.Right
            ? InputRouter.Instance.GetRightAim()
            : InputRouter.Instance.GetLeftAim();
        Vector3 from, dir;
        var space = GetWorld3D().DirectSpaceState;
        if (aim.IsScreenPoint && _camera != null)
        {
            var rayOrigin = _camera.ProjectRayOrigin(aim.ScreenPos);
            var rayDir = _camera.ProjectRayNormal(aim.ScreenPos);
            var chit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(rayOrigin, rayOrigin + rayDir * RayLength, RayMask));
            Vector3 target = chit.Count > 0 ? (Vector3)chit["position"] : rayOrigin + rayDir * 200.0f;
            gun.AimAt(target);
        }
        else
        {
            gun.AimRotation(aim.Rotation);
        }
        from = gun.Muzzle.GlobalPosition;
        dir = gun.BarrelDirection;
        var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + dir * RayLength, RayMask));
        PresentRayHit(side, gun, hit);
        bool trigger = side == PlayerState.Side.Right
            ? InputRouter.Instance.GetCurKeyRing()
            : InputRouter.Instance.GetCurKeyLeg();
        if (hit.Count == 0)
        {
            flash.Visible = false;
            if (trigger && !Game.Instance.IsGamePause && player.Hp > 0) TryFire(side, gun);
            return;
        }
        var point = (Vector3)hit["position"];
        var normal = (Vector3)hit["normal"];
        var collider = (GodotObject)hit["collider"];
        if (DebugFire && side == PlayerState.Side.Right)
        {
            string cpath = collider is Node cn ? cn.GetPath().ToString() : "?";
            GD.Print($"[FIREDBG] from={from} dir={dir} hit={cpath} point={point} dist={from.DistanceTo(point):0.00}");
        }
        // 3. Distance controls the glow size, never its screen position.
        float dist = _camera != null ? _camera.GlobalPosition.DistanceTo(point) : from.DistanceTo(point);
        float flashScale = 1.0f - Mathf.Clamp(3.0f / Mathf.Max(dist, 0.001f), 0.0f, 1.0f) * 0.8f;
        if (!trigger)
            return;
        // 4. 命中 Button(layer 3)→ 触发回调,不耗弹不开火(暂停期唯一放行路径)
        if (collider is CollisionObject3D co && (co.CollisionLayer & 0b100) != 0)
        {
            if (collider is UiButton3D button && (!button.Enabled || !button.IsVisibleInTree())) return;
            bool consumed = side == PlayerState.Side.Right ? _rightUiPullConsumed : _leftUiPullConsumed;
            if (consumed) return; // one UI action per pull; holding cannot click through consecutive panels
            if (side == PlayerState.Side.Right) _rightUiPullConsumed = true; else _leftUiPullConsumed = true;
            if (collider.HasMethod("OnShot")) // UiButton3D.OnShot(C# 方法注册为原名,大小写敏感)
                collider.Call("OnShot");
            _uiShotPlayer?.Play();
            return;
        }
        if (Game.Instance.IsGamePause || player.Hp <= 0)
            return;
        // 5. 开枪
        if (!TryFire(side, gun)) return;
        // 6. 命中敌人(layer 2)→ hit();否则环境 tag
        bool isEnemy = collider is CollisionObject3D c2 && (c2.CollisionLayer & 0b010) != 0;
        string tag = "Dust";
        if (isEnemy)
        {
            if (collider.HasMethod("Hit")) // Monster.Hit / BossHeart.Hit(原名大写)
            {
                var ret = collider.Call("Hit", gun.Attack, point, (int)Game.HitType.Body, (int)side);
                if (ret.VariantType == Variant.Type.String && EffectScenes.ContainsKey(ret.AsString()))
                    tag = ret.AsString();
            }
        }
        else
        {
            tag = EffectTagOf(collider);
        }
        SpawnEffect(tag, point, normal, flashScale, isEnemy);
    }

    private bool TryFire(PlayerState.Side side, GunBase gun)
    {
        var (success, bulletEnough) = gun.Fire();
        if (success)
        {
            _emptySignaled[side] = false;
            // A pull used to fire in battle cannot also activate a menu when
            // the held aim crosses its button. Release before a deliberate UI shot.
            if (side == PlayerState.Side.Right) _rightUiPullConsumed = true;
            else _leftUiPullConsumed = true;
        }
        else if (!bulletEnough && Game.Instance.SceneState == Game.GameState.Battle && !_emptySignaled[side])
        {
            _emptySignaled[side] = true;
            EmitSignal(SignalName.OpenContinue, true, (int)side);
        }
        return success;
    }

    private void PresentRayHit(PlayerState.Side side, GunBase gun, Godot.Collections.Dictionary hit)
    {
        var from = gun.Muzzle.GlobalPosition;
        var dir = gun.BarrelDirection;
        var ray = _rayDebug[side];
        ray.Origin = ray.Muzzle = from;
        ray.Direction = dir;
        ray.HasHit = hit.Count > 0;
        ray.HitNode = ray.HasHit ? hit["collider"].AsGodotObject() as Node : null;
        ray.HitPoint = ray.HasHit ? hit["position"].AsVector3() : from + dir * 200;
        var laser = _lazer[side];
        laser.SetOverlay(Game.Instance.IsGamePause);
        laser.SetBeam(from, ray.HitPoint);
        var flash = _flash[side];
        flash.Visible = ray.HasHit;
        if (!ray.HasHit) return;
        // A small offset along the camera ray avoids z fighting while preserving
        // the exact projected hit point. Retreating along the barrel ray shifts
        // the glow sideways, especially on the nearby pause/continue panel.
        flash.GlobalPosition = ray.HitPoint + (_camera.GlobalPosition - ray.HitPoint).Normalized() * 0.001f;
        float dist = _camera.GlobalPosition.DistanceTo(ray.HitPoint);
        float scale = 1 - Mathf.Clamp(3 / Mathf.Max(dist, 0.001f), 0, 1) * 0.8f;
        flash.Scale = Vector3.One * scale;
        ((StandardMaterial3D)flash.MaterialOverride).NoDepthTest = Game.Instance.IsGamePause;
    }

    // ------------------------------------------------ 换枪(5.5)

    private void OnSwitchKey(PlayerState.Side side)
    {
        var player = PlayerState.Instance.GetPlayer(side);
        if (!player.Active || _switchingSides.Contains(side))
            return;
        var old = _currentGun[side];
        if (old == null || !player.NextGun())
            return;
        var newGun = _guns[side][player.GunType];
        _switchingSides.Add(side);
        // 照原作字面行为(Unity FireSystem.ChangeGun):旧枪 0.5s 下沉 0.1 后 SetActive(false),
        // 新枪在 CreateGun 中直接出现在最终位置——原作随后的"伸出"循环作用于已隐藏旧枪,
        // 视觉上新枪瞬现,无升起动画;原作换枪全程不挡扳机(新枪立即可射,收起中旧枪也可射)。
        var tw = CreateTween();
        tw.TweenProperty(old, "position:z", old.Position.Z + SwitchSink, SwitchDownTime);
        tw.TweenCallback(Callable.From(() =>
        {
            old.Hide();
            old.Position = GunBasePos(side, old.GunType); // 复位,下次切出位置正确
            newGun.Position = GunBasePos(side, newGun.GunType);
            newGun.Show();
            _currentGun[side] = newGun;
            _switchingSides.Remove(side);
        }));
    }

    // ------------------------------------------------ 特效池

    private string EffectTagOf(GodotObject collider)
    {
        if (collider is Node n)
        {
            if (n.HasMeta("effect_tag"))
            {
                string tag = n.GetMeta("effect_tag").AsString();
                if (EffectScenes.ContainsKey(tag))
                    return tag;
            }
            foreach (var kv in EffectScenes)
            {
                if (n.IsInGroup(kv.Key))
                    return kv.Key;
            }
        }
        return "Dust";
    }

    private void SpawnEffect(string tag, Vector3 point, Vector3 normal, float flashScale, bool isEnemy)
    {
        var pool = _effects[tag];
        int idx = _effectIdx[tag];
        _effectIdx[tag] = (idx + 1) % pool.Length;
        var e = pool[idx];
        Vector3 up = Mathf.Abs(normal.Dot(Vector3.Up)) < 0.99f ? Vector3.Up : Vector3.Right;
        e.GlobalTransform = new Transform3D(Basis.Identity, point).LookingAt(point - normal, up);
        e.Scale = Vector3.One * flashScale * 3.0f;
        var hole = e.GetNodeOrNull<Sprite3D>("Hole");
        if (hole != null)
            hole.Visible = !isEnemy;
        e.Activate();
    }

    /// <summary>掉血飘字(静态入口,怪物 hit() 调用)。
    /// 照原作 MonstHp.showHpNumber:"- N" 纯红 (1,0,0) 14 号(根缩放 0.01 → 世界字高 ≈0.14m)、
    /// 上升 5px/s(≈0.05m/s)、alpha 1→0.3(≈0.7s)后消失、每怪 3 个复用池。</summary>
    public static void ShowDamage(string text, Vector3 point, Node3D parent)
    {
        Current?.ShowDamageInternal(text, point, parent);
    }

    private void ShowDamageInternal(string text, Vector3 point, Node3D parent)
    {
        if (!_damagePools.TryGetValue(parent, out var pool))
        {
            // 清理由已释放怪物的池(Label3D 挂在 FireSystem 下,随池释放)
            var dead = new System.Collections.Generic.List<Node3D>();
            foreach (var kv in _damagePools)
            {
                if (!GodotObject.IsInstanceValid(kv.Key))
                    dead.Add(kv.Key);
            }
            foreach (var d in dead)
            {
                foreach (var lb in _damagePools[d])
                    lb.QueueFree();
                _damagePools.Remove(d);
            }
            pool = new Label3D[3];
            for (int i = 0; i < pool.Length; i++)
            {
                var lb = new Label3D
                {
                    Name = "DamageLabel",
                    PixelSize = 0.0025f,
                    FontSize = 56, // 56×0.0025 ≈ 0.14m 字高(原作 14 号×0.01 缩放)
                    Modulate = new Color(1.0f, 0.0f, 0.0f),
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    Visible = false,
                };
                UiTheme.ApplyLabel3D(lb);
                AddChild(lb);
                pool[i] = lb;
            }
            _damagePools[parent] = pool;
        }
        foreach (var lb in pool)
        {
            if (lb.Visible)
                continue;
            lb.Text = text;
            lb.GlobalPosition = point + new Vector3(0, 0.3f, 0);
            lb.Modulate = new Color(1.0f, 0.0f, 0.0f, 1.0f);
            lb.Visible = true;
            var tw = lb.CreateTween();
            tw.TweenProperty(lb, "global_position:y", lb.GlobalPosition.Y + 0.035f, 0.7); // 5px/s×0.7s×0.01
            tw.Parallel().TweenProperty(lb, "modulate:a", 0.3, 0.7);
            tw.TweenCallback(Callable.From(() => lb.Visible = false));
            return;
        }
    }

    /// <summary>血花线性池:跟随怪物但保留特效池所有权；怪物释放不能销毁池条目。</summary>
    public static void SpawnBloodFlower(Node3D parent, Vector3 localPos)
    {
        if (Current == null)
            return;
        foreach (var bf in Current._bloodFlowers)
        {
            if (bf.Visible)
                continue;
            bf.Follow(parent, localPos);
            bf.Activate(1.2f);
            return;
        }
    }

    private static Texture2D? _flashCursorTex;

    /// <summary>命中光标贴图:flash_point19.png 与原作 Point19.png 同源,但 alpha 通道全 255、
    /// 光存 RGB → 取 R 亮度重写为 alpha(RGB 留白),供 alpha 混合材质按手染色(红/绿)。</summary>
    private static Texture2D LoadFlashCursor()
    {
        if (_flashCursorTex != null)
            return _flashCursorTex;
        Image img;
        if (ResourceLoader.Exists(FlashCursorPath))
        {
            img = GD.Load<Texture2D>(FlashCursorPath).GetImage();
            if (img.IsCompressed())
                img.Decompress();
            img.Convert(Image.Format.Rgba8);
            int w = img.GetWidth(), h = img.GetHeight();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = img.GetPixel(x, y);
                // 原作 Blend_CenterGlow 是软光斑:贴图转换丢了 alpha,仅凭 R 亮度会留大面积实心核,
                // 叠乘径向平方衰减还原"亮点+光晕"观感
                float d = new Vector2(x - (w - 1) * 0.5f, y - (h - 1) * 0.5f).Length() / (w * 0.5f);
                float falloff = Mathf.Clamp(1.0f - d, 0.0f, 1.0f);
                img.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, c.R * falloff * falloff));
            }
        }
        else
        {
            // 程序化径向光点兜底
            img = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float d = new Vector2(x - 31.5f, y - 31.5f).Length() / 32.0f;
                float a = Mathf.Clamp(1.0f - d, 0.0f, 1.0f);
                img.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, a * a));
            }
        }
        _flashCursorTex = ImageTexture.CreateFromImage(img);
        return _flashCursorTex;
    }
}
