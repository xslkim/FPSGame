using Godot;

namespace FPSGame;

/// <summary>
/// FireSystem:挂在战斗相机下,管理左右枪节点(原作 FireSystem.cs)。
/// 每帧(LateUpdate 语义):枪口旋转 = 输入瞄准 → 枪口射线 2000 →
/// 激光瞄准器(LaserSight,右红/左绿,照原作 Lazer.mat)从枪口伸到命中点、红点贴面,
/// 未命中沿瞄准方向伸 200m(照原作线长)→ 命中点摆光标火光(距离衰减公式照 5.3) →
/// 扳机 → Button 触发 / 开枪(CD+耗弹) →
/// 敌人 hit() 或环境弹着特效(池化)。换枪:key2 边沿 → 旧枪下沉 → 新枪升起(~1s)。
/// 弹尽且 Battle 态 → OpenContinue(true, side)(只发一次,补弹复位)。
/// </summary>
public partial class FireSystem : Node3D
{
    [Signal] public delegate void OpenContinueEventHandler(bool isOpen, int side);

    public const float RayLength = 2000.0f;
    public const uint RayMask = 0xFFFFFFF7; // 除 layer4(CameraWall)外全部
    public const int PoolSize = 3;
    public const int BloodFlowerPoolSize = 5;
    public const float SwitchSink = 0.10f;   // 换枪下沉量(+Z,相机看 -Z)
    public const float SwitchDownTime = 0.5f;
    public const float SwitchUpTime = 0.55f;

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

    private Camera3D _camera = null!;
    private readonly System.Collections.Generic.Dictionary<PlayerState.Side, Node3D> _anchors = new();
    private readonly System.Collections.Generic.Dictionary<PlayerState.Side, System.Collections.Generic.Dictionary<int, GunBase>> _guns = new();
    private readonly System.Collections.Generic.Dictionary<PlayerState.Side, GunBase?> _currentGun = new();
    private readonly System.Collections.Generic.Dictionary<PlayerState.Side, MeshInstance3D> _flash = new();
    private readonly System.Collections.Generic.Dictionary<PlayerState.Side, LaserSight> _lazer = new();
    private readonly System.Collections.Generic.Dictionary<PlayerState.Side, bool> _switching = new();
    private readonly System.Collections.Generic.Dictionary<PlayerState.Side, bool> _emptySignaled = new();
    private readonly System.Collections.Generic.Dictionary<string, EffectBase[]> _effects = new();
    private readonly System.Collections.Generic.Dictionary<string, int> _effectIdx = new();
    private EffectBase[] _bloodFlowers = System.Array.Empty<EffectBase>();
    private Label3D[] _damageLabels = System.Array.Empty<Label3D>();
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
            _switching[side] = false;
            _emptySignaled[side] = false;
            // 命中光标火光(flash_point19 为加色贴图:alpha 全 255、光存 RGB;
            // 普通 Sprite3D 是 alpha 混合会带黑底 → 用 billboard quad + BlendMode.Add)
            var flashMat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(1.0f, 0.85f, 0.35f, 0.45f), // 加色模式 α=强度;过亮会触发泛光大光晕
                AlbedoTexture = LoadFlashCursor(),
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            };
            var flash = new MeshInstance3D
            {
                Name = "Flash" + sideName,
                TopLevel = true,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
                Mesh = new QuadMesh { Size = new Vector2(0.12f, 0.12f) }, // 加色下亮核全显,取比原 sprite 小的等效光点
                MaterialOverride = flashMat,
            };
            AddChild(flash);
            _flash[side] = flash;
            // 激光瞄准器(原作 Lazer.prefab:右红/左绿,线宽 0.03);每帧 UpdateSide 拉伸到命中点
            var lazer = LaserSight.Create(
                side == PlayerState.Side.Right ? LaserSight.RightRed : LaserSight.LeftGreen,
                0.015f, withDot: true);
            lazer.Visible = false;
            AddChild(lazer);
            _lazer[side] = lazer;
        }
        foreach (var kv in EffectScenes)
        {
            var pool = new EffectBase[PoolSize];
            for (int i = 0; i < PoolSize; i++)
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
        // 掉血飘字池:每只怪命中共用(原 HpReduceText 复用池)
        _damageLabels = new Label3D[8];
        for (int i = 0; i < _damageLabels.Length; i++)
        {
            var lb = new Label3D
            {
                Name = $"DamageLabel_{i}",
                PixelSize = 0.0025f,
                FontSize = 96,
                Modulate = new Color(1.0f, 0.3f, 0.2f),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Visible = false,
            };
            UiTheme.ApplyLabel3D(lb);
            AddChild(lb);
            _damageLabels[i] = lb;
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
        InputRouter.Instance.TriggerRight += () => { }; // 扳机用电平查询,事件仅保留接口
        InputRouter.Instance.SwitchGunRight += () => OnSwitchKey(PlayerState.Side.Right);
        InputRouter.Instance.SwitchGunLeft += () => OnSwitchKey(PlayerState.Side.Left);
    }

    public override void _ExitTree() => Current = null;

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
            // 原作枪口标记 Sphere @(0,0,0.088)(Unity +Z 前)→ Godot -Z
            muzzle.Position = new Vector3(0, 0, -0.088f);
            gun.AddChild(muzzle);
            gun.Setup(type, side == PlayerState.Side.Left);
            // 真实枪模型(FBX 导入场景根):ak47 枪管沿 -X(rotY-90→-Z,scale 0.4);
            // m4/handgun 枪管已沿 -Z 且自带缩放。贴图手动接线(FBX 未内嵌)
            var model = GD.Load<PackedScene>(GunModelPaths[type]).Instantiate<Node3D>();
            model.Name = "Model";
            if (type == 0)
            {
                model.Rotation = new Vector3(0, -Mathf.Pi / 2.0f, 0);
                model.Scale = Vector3.One * 0.4f;
            }
            WireGunMaterial(model, type);
            gun.AddChild(model);
            anchor.AddChild(gun);
            gun.Position = GunBasePos(side, type);
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

    private static Vector3 GunBasePos(PlayerState.Side side, int type)
    {        var info = SaveService.Instance.GetGunInfo(type);
        bool wide = false; // FOV>50 用 pos60;战斗相机 FOV 恒 45,取 pos
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
        // 暂停期只放行 Button 命中(枪打面板按钮),其余冻结
        UpdateSide(PlayerState.Side.Right);
        UpdateSide(PlayerState.Side.Left);
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
        // 1. 冰冻且未暂停 → 本帧跳过(不转枪不开火)
        if (player.Status == Player.HurtState.Frozen)
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
        // 2. 枪口旋转 + 射线(鼠标模式 = 相机过屏幕点的射线)
        AimState aim = side == PlayerState.Side.Right
            ? InputRouter.Instance.GetRightAim()
            : InputRouter.Instance.GetLeftAim();
        Vector3 from, dir;
        if (aim.IsScreenPoint && _camera != null)
        {
            from = _camera.ProjectRayOrigin(aim.ScreenPos);
            dir = _camera.ProjectRayNormal(aim.ScreenPos);
            gun.Quaternion = (_camera.GlobalTransform.Basis.Inverse() * Basis.LookingAt(dir, Vector3.Up)).GetRotationQuaternion();
        }
        else
        {
            gun.Quaternion = aim.Rotation;
            from = gun.Muzzle.GlobalPosition;
            dir = -gun.Muzzle.GlobalBasis.Z;
        }
        var space = GetWorld3D().DirectSpaceState;
        var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + dir * RayLength, RayMask));
        // 激光指引:枪口 → 命中点(未命中沿瞄准方向伸 200m,照原作线长);红点贴命中表面
        var muzzlePos = gun.Muzzle.GlobalPosition;
        if (hit.Count == 0)
        {
            flash.Visible = false;
            _lazer[side].SetBeam(muzzlePos, from + dir * 200.0f);
            _lazer[side].ShowDot(false);
            return;
        }
        var point = (Vector3)hit["position"];
        var normal = (Vector3)hit["normal"];
        var collider = (GodotObject)hit["collider"];
        _lazer[side].SetBeam(muzzlePos, point);
        _lazer[side].SetDotPosition(point - dir * 0.02f);
        _lazer[side].ShowDot(true);
        // 3. 命中 → Flash 光标(距离衰减公式照原作)
        float dist = _camera != null ? _camera.GlobalPosition.DistanceTo(point) : from.DistanceTo(point);
        float flashScale = 1.0f - Mathf.Clamp(3.0f / Mathf.Max(dist, 0.001f), 0.0f, 1.0f) * 0.8f;
        float backOff = 0.5f - Mathf.Clamp(1.0f / Mathf.Max(dist, 0.001f), 0.0f, 1.0f) * 0.4f;
        flash.Visible = true;
        flash.GlobalPosition = point - dir * backOff;
        flash.Scale = Vector3.One * flashScale;
        bool trigger = side == PlayerState.Side.Right
            ? InputRouter.Instance.GetCurKeyRing()
            : InputRouter.Instance.GetCurKeyLeg();
        if (!trigger || _switching[side])
            return;
        // 4. 命中 Button(layer 3)→ 触发回调,不耗弹不开火(暂停期唯一放行路径)
        if (collider is CollisionObject3D co && (co.CollisionLayer & 0b100) != 0)
        {
            if (collider.HasMethod("on_shot"))
                collider.Call("on_shot");
            _uiShotPlayer?.Play();
            return;
        }
        if (Game.Instance.IsGamePause)
            return;
        // 5. 开枪
        var (success, bulletEnough) = gun.Fire();
        if (!success)
        {
            if (!bulletEnough && Game.Instance.SceneState == Game.GameState.Battle
                && !_emptySignaled[side])
            {
                _emptySignaled[side] = true;
                EmitSignal(SignalName.OpenContinue, true, (int)side);
            }
            return;
        }
        _emptySignaled[side] = false;
        // 6. 命中敌人(layer 2)→ hit();否则环境 tag
        bool isEnemy = collider is CollisionObject3D c2 && (c2.CollisionLayer & 0b010) != 0;
        string tag = "Dust";
        if (isEnemy)
        {
            if (collider.HasMethod("hit"))
            {
                var ret = collider.Call("hit", gun.Attack, point, (int)Game.HitType.Body, (int)side);
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

    // ------------------------------------------------ 换枪(5.5)

    private void OnSwitchKey(PlayerState.Side side)
    {
        if (_switching[side])
            return;
        var player = PlayerState.Instance.GetPlayer(side);
        if (!player.Active)
            return;
        var old = _currentGun[side];
        if (old == null || !player.NextGun())
            return;
        var newGun = _guns[side][player.GunType];
        _switching[side] = true;
        Vector3 baseNew = GunBasePos(side, newGun.GunType);
        var tw = CreateTween();
        tw.TweenProperty(old, "position:z", old.Position.Z + SwitchSink, SwitchDownTime);
        tw.TweenCallback(Callable.From(() =>
        {
            old.Hide();
            newGun.Position = baseNew + new Vector3(0, 0, SwitchSink);
            newGun.Show();
            _currentGun[side] = newGun;
            var tw2 = CreateTween();
            tw2.TweenProperty(newGun, "position", baseNew, SwitchUpTime);
            tw2.TweenCallback(Callable.From(() => _switching[side] = false));
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

    /// <summary>掉血飘字(静态入口,怪物 hit() 调用):池取第一个未激活,上飘渐隐</summary>
    public static void ShowDamage(string text, Vector3 point, Node3D parent)
    {
        if (Current == null)
            return;
        foreach (var lb in Current._damageLabels)
        {
            if (lb.Visible)
                continue;
            lb.Text = text;
            lb.GlobalPosition = point + new Vector3(0, 0.3f, 0);
            lb.Modulate = new Color(1.0f, 0.3f, 0.2f, 1.0f);
            lb.Visible = true;
            var tw = Current.CreateTween();
            tw.TweenProperty(lb, "global_position:y", lb.GlobalPosition.Y + 0.5f, 1.2f);
            tw.Parallel().TweenProperty(lb, "modulate:a", 0.0f, 1.2f);
            tw.TweenCallback(Callable.From(() => lb.Visible = false));
            return;
        }
    }

    /// <summary>血花线性池(5.2):取第一个未激活挂怪身上;自隐后回池</summary>
    public static void SpawnBloodFlower(Node3D parent, Vector3 localPos)
    {
        if (Current == null)
            return;
        foreach (var bf in Current._bloodFlowers)
        {
            if (bf.Visible)
                continue;
            if (bf.GetParent() != parent)
                bf.Reparent(parent, false);
            bf.Transform = new Transform3D(Basis.Identity, localPos);
            bf.Activate(1.2f);
            return;
        }
    }

    private static Texture2D LoadFlashCursor()
    {
        if (ResourceLoader.Exists(FlashCursorPath))
            return GD.Load<Texture2D>(FlashCursorPath);
        // 程序化径向光点兜底
        var img = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            float d = new Vector2(x - 31.5f, y - 31.5f).Length() / 32.0f;
            float a = Mathf.Clamp(1.0f - d, 0.0f, 1.0f);
            img.SetPixel(x, y, new Color(1.0f, 0.9f, 0.4f, a * a));
        }
        return ImageTexture.CreateFromImage(img);
    }
}
