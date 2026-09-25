using Godot;

namespace FPSGame;

/// <summary>
/// FireSystem:挂在战斗相机下,管理左右枪节点(原作 FireSystem.cs)。
/// 每帧(LateUpdate 语义):枪口旋转 = 输入瞄准 → 枪口射线 2000 →
/// 激光瞄准器(LaserSight,右红/左绿,照原作 Lazer.mat)从枪口恒伸 200m
/// (原作 LineRenderer (0,0,0)→(0,0,200):打怪穿透,墙体遮挡段由深度剔除) →
/// 命中点摆光标火光(右手红 Flash.prefab/左手绿 FlashGreen.prefab,距离衰减公式照 5.3) →
/// 扳机 → Button 触发 / 开枪(CD+耗弹) →
/// 敌人 hit() 或环境弹着特效(池化)。换枪:key2 边沿 → 旧枪 0.5s 下沉收起 →
/// 新枪直接出现在最终位置(照原作字面行为:伸出循环作用于已隐藏旧枪,新枪瞬现;换枪期不挡扳机)。
/// 弹尽且 Battle 态 → OpenContinue(true, side)(只发一次,补弹复位)。
/// </summary>
public partial class FireSystem : Node3D
{
    [Signal] public delegate void OpenContinueEventHandler(bool isOpen, int side);

    public const float RayLength = 2000.0f;
    public const uint RayMask = 0xFFFFFFF7; // 除 layer4(CameraWall)外全部
    public const int PoolSize = 3;
    public const int ConcretePoolSize = 4;    // 原作 FireSystem.prefab ConcreteEffect×4(其余×3)
    public const int BloodFlowerPoolSize = 3; // 原作 _BloodEffects×3
    public const float SwitchSink = 0.10f;   // 换枪下沉量(+Z,相机看 -Z)
    public const float SwitchDownTime = 0.5f;
    /// <summary>枪模型统一缩放系数:真值截图手枪 ~320×180px vs 旧值 ~440×250px(同 1472×668 FOV45)
    /// → 0.725;三枪同包(Weapons Pack LOW POLY)同导入管线,AK/M4 无真值截图,按同系数推断</summary>
    public const float GunModelScale = 0.725f;

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
            // 每帧 UpdateSide 恒伸 200m;原作无终点红点(命中指示全靠 Flash),不带 dot
            var lazer = LaserSight.Create(
                side == PlayerState.Side.Right ? LaserSight.RightRed : LaserSight.LeftGreen,
                0.015f, withDot: false, nearRadius: 0.00894f * 0.5f);
            lazer.Visible = false;
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
            // 原作枪口标记 Sphere(Unity +Z 前):AK47 z=0.088 / M4 z=0.162 / HandGun z=0.042 → Godot -Z
            float muzzleZ = type == 1 ? 0.162f : type == 2 ? 0.042f : 0.088f;
            muzzle.Position = new Vector3(0, 0, -muzzleZ);
            gun.AddChild(muzzle);
            gun.Setup(type, side == PlayerState.Side.Left);
            // 真实枪模型(FBX 导入场景根):ak47 枪管沿 -X(rotY-90→-Z,scale 0.4);
            // m4/handgun 枪管已沿 -Z。统一 ×GunModelScale(0.725) 对真值截图枪占屏比。
            // 贴图手动接线(FBX 未内嵌)
            var model = GD.Load<PackedScene>(GunModelPaths[type]).Instantiate<Node3D>();
            model.Name = "Model";
            if (type == 0)
                model.Rotation = new Vector3(0, -Mathf.Pi / 2.0f, 0);
            model.Scale = Vector3.One * (type == 0 ? 0.4f : 1.0f) * GunModelScale;
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
        // 激光:枪口起恒伸 200m(原作 LineRenderer (0,0,0)→(0,0,200):打怪穿透,被墙遮挡段由深度剔除)
        var muzzlePos = gun.Muzzle.GlobalPosition;
        _lazer[side].SetBeam(muzzlePos, muzzlePos + dir * 200.0f);
        if (hit.Count == 0)
        {
            flash.Visible = false;
            return;
        }
        var point = (Vector3)hit["position"];
        var normal = (Vector3)hit["normal"];
        var collider = (GodotObject)hit["collider"];
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
        if (!trigger)
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
        var player = PlayerState.Instance.GetPlayer(side);
        if (!player.Active)
            return;
        var old = _currentGun[side];
        if (old == null || !player.NextGun())
            return;
        var newGun = _guns[side][player.GunType];
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
            var tw = CreateTween();
            tw.TweenProperty(lb, "global_position:y", lb.GlobalPosition.Y + 0.035f, 0.7); // 5px/s×0.7s×0.01
            tw.Parallel().TweenProperty(lb, "modulate:a", 0.3, 0.7);
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
            for (int y = 0; y < img.GetHeight(); y++)
            for (int x = 0; x < img.GetWidth(); x++)
            {
                var c = img.GetPixel(x, y);
                img.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, c.R));
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
