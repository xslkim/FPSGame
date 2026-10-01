using Godot;

namespace FPSGame;

/// <summary>
/// 剧情过场(参考 Level1.unity + StoryStart.cs + StoryStartTimeline.playable):
/// 学校走廊傍晚,3 学生按 Unity Timeline 的激活与动画轨道跳 K-POP(约 3~54.8s),
/// 10 机位按切镜表 blend 切换,0~2.967s 字幕 + dance.mp3(3.1s),50.5s RockWarrior 冲出(run),
/// 54.35s(时长-0.5)或"跳过"按钮 → level1_battle.tscn。
/// 舞蹈动画:Unity BakeKpopDance 把 humanoid 肌肉剪辑逐帧烘成全骨骼局部 TRS(.kdance.bin),
/// KDancePlayer 全局链镜像 X 回放,剧情时钟驱动;三条轨道的开始时间照原作。
/// 启动参数:--story-selftest(加速断言,抑制切场景)/ --story-shot:&lt;path&gt;[:delay]
/// </summary>
public partial class StoryStart : Node3D
{
    public const double Duration = 54.85;
    public const double NextSceneTime = Duration - 0.5; // StoryStart:_pd.time >= duration-0.5
    public const double RockShowTime = 50.5;
    public const double SubtitleEnd = 2.967;
    public const string NextScenePath = "res://scenes/levels/level1_battle.tscn";

    private struct Cut
    {
        public double Time;
        public double Blend;
        public string Marker;
        public NodePath? HeadActor;
        public string HeadBone;
        public bool OrbitFollow;
        public float ZoomWidth;
    }
    private Cut[] _cuts = System.Array.Empty<Cut>();
    private int _cutIdx;

    private Camera3D _camera = null!;
    private Node3D _rock = null!;
    private AnimationPlayer _rockAnim = null!;
    private Node3D _casual = null!;
    private Node3D _f05 = null!;
    private Node3D _blade = null!;
    private ColorRect _introBlack = null!;
    private Label _subtitle = null!;
    private AudioStreamPlayer _dancePlayer = null!;
    private double _clock;
    private bool _ended;
    private bool _suppressSwitch;
    private bool _testFailed;
    private readonly System.Collections.Generic.List<KDancePlayer> _dancePlayers = new();
    private readonly System.Collections.Generic.List<FootContact> _footContacts = new();
    private readonly System.Collections.Generic.List<DancerGrounding> _grounding = new();

    private sealed class DancerGrounding
    {
        public Node3D Actor = null!;
        public Skeleton3D Skeleton = null!;
        public int RootBone;
        public int LeftSole;
        public int RightSole;
        public Vector3 RootPose;
    }

    private sealed class FootContact
    {
        public Node3D Actor = null!;
        public Skeleton3D Skeleton = null!;
        public int Bone;
        public MeshInstance3D Shadow = null!;
        public StandardMaterial3D Material = null!;
    }

    public override void _Ready()
    {
        PlayerState.Instance.UpdateUiMode("Level1Story");
        var args = OS.GetCmdlineUserArgs();
        _suppressSwitch = System.Array.IndexOf(args, "--story-selftest") >= 0;
        // Unity 走廊的点灯全部是 Baked(m_Lightmapping=2)。移植网格没有光照贴图，
        // 保留实时近似，但压低强度以免与环境光叠加后过曝。
        foreach (var node in GetNode("Environment").FindChildren("*", "Light3D", true, false))
            ((Light3D)node).LightEnergy *= 0.12f;
        BuildActors();
        BuildCuts();
        _camera = GetNode<Camera3D>("Camera3D");
        _camera.GlobalTransform = GetNode<Node3D>("Markers/vcam8").GlobalTransform;
        _dancePlayer = GetNode<AudioStreamPlayer>("DanceMusic");
        BuildUi();
        _dancePlayer.Play();
        GD.Print("[Story] start, duration 54.85s");
        foreach (var a in args)
        {
            if (a.StartsWith("--story-shot:"))
            {
                var rest = a["--story-shot:".Length..];
                float delay = 6.0f;
                string path = rest;
                int ci = rest.LastIndexOf(':');
                if (ci > 1 && float.TryParse(rest[(ci + 1)..], out var d))
                {
                    delay = d;
                    path = rest[..ci];
                }
                TakeShotDelayed(path, delay);
            }
        }
        if (_suppressSwitch)
            SelfTest();
    }

    // ------------------------------------------------ 演员

    private void BuildActors()
    {
        // FBX 导入后的几何中心偏右，整体校正到 Unity 第 2 镜头的三人构图。
        var students = new Node3D { Name = "Students", Position = new Vector3(-0.42f, 0, 0) };
        AddChild(students);
        _casual = BuildDancer(students, "casual_dressed_girl",
            "res://assets/models/actors/casual_dressed_girl/casual_dressed_girl.FBX",
            new Vector3(-0.271f, -0.012f, 21.799f), 0.0666667);
        _f05 = BuildDancer(students, "f05_schoolwear",
            "res://assets/models/actors/f05_schoolwear/f05_schoolwear_200_m.fbx",
            new Vector3(-1.142f, -0.008f, 20.519f), 0.1333333);
        // blade_girl 的场景初始状态为 inactive,但 Timeline Activation Track (4)
        // 在 2.9833s 激活她;舞蹈 Animation Track 从 2.9667s 开始。
        _blade = BuildDancer(students, "blade_girl",
            "res://assets/models/actors/blade_girl/blade_girl.FBX",
            new Vector3(0.75f, 0.012f, 20.519f), 0.0);
        _blade.Scale = new Vector3(1.0f, 0.95f, 1.0f);
        _blade.Visible = false;
        // Unity 的烘焙地面阴影在此场景未随网格导入；用脚趾骨投射软接触影，
        // 跟随脚步但不移动演员或模型根节点。
        AddFootContacts(_casual, "Bip01 L Toe0", "Bip01 R Toe0");
        AddFootContacts(_f05, "LeftToes", "RightToes");
        AddFootContacts(_blade, "Bip001 L Toe0", "Bip001 R Toe0");
        AddGrounding(_casual, "Bip01 L Toe0Nub", "Bip01 R Toe0Nub");
        AddGrounding(_f05, "LeftToesNull", "RightToesNull");
        AddGrounding(_blade, "Bip001 L Toe0Nub", "Bip001 R Toe0Nub");
        // RockWarrior:50.5s 激活,倾倒出场姿态 → run
        _rock = new Node3D { Name = "RockWarrior", Position = new Vector3(-0.2686f, 0.0102f, -0.1627f) };
        _rock.Rotation = new Vector3(0, Mathf.DegToRad(-61.6f), 0);
        _rock.Visible = false;
        AddChild(_rock);
        var model = GD.Load<PackedScene>("res://assets/models/monsters/rock_warrior/RockWarrior.FBX")
            .Instantiate<Node3D>();
        model.Name = "Model";
        model.Rotation = new Vector3(0, Mathf.Pi, 0);
        _rock.AddChild(model);
        var lib = GD.Load<AnimationLibrary>("res://assets/models/monsters/rock_warrior/rock_warrior_anims.tres");
        _rockAnim = new AnimationPlayer { Name = "AnimationPlayer" };
        _rock.AddChild(_rockAnim);
        _rockAnim.AddAnimationLibrary("", lib);
        OverrideRockMaterial(model);
    }

    private static void OverrideRockMaterial(Node3D model)
    {
        string matPath = "res://assets/models/monsters/rock_warrior/rock_warrior_mat.tres";
        if (!ResourceLoader.Exists(matPath))
            return;
        var mat = GD.Load<Material>(matPath);
        foreach (var mi in model.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (mi is MeshInstance3D m && m.Visible)
                m.MaterialOverride = mat;
        }
    }

    private void AddFootContacts(Node3D actor, string left, string right)
    {
        var skeleton = actor.FindChild("Skeleton3D", true, false) as Skeleton3D;
        if (skeleton == null)
            return;
        foreach (var boneName in new[] { left, right })
        {
            int bone = skeleton.FindBone(boneName);
            if (bone < 0)
                continue;
            var img = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float dx = (x + 0.5f - 32.0f) / 32.0f;
                    float dy = (y + 0.5f - 32.0f) / 32.0f;
                    float falloff = Mathf.Pow(Mathf.Max(0, 1.0f - dx * dx - dy * dy), 2.0f);
                    img.SetPixel(x, y, new Color(0.02f, 0.02f, 0.02f, falloff));
                }
            var mat = new StandardMaterial3D
            {
                AlbedoTexture = ImageTexture.CreateFromImage(img),
                AlbedoColor = new Color(1, 1, 1, 0.22f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            var shadow = new MeshInstance3D
            {
                Name = $"Contact_{actor.Name}_{boneName.Replace(' ', '_')}",
                Mesh = new QuadMesh { Size = new Vector2(0.28f, 0.18f) },
                MaterialOverride = mat,
                Rotation = new Vector3(-Mathf.Pi / 2, 0, 0),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            AddChild(shadow);
            _footContacts.Add(new FootContact
            {
                Actor = actor, Skeleton = skeleton, Bone = bone, Shadow = shadow, Material = mat,
            });
        }
    }

    private void UpdateFootContacts()
    {
        foreach (var contact in _footContacts)
        {
            contact.Shadow.Visible = contact.Actor.Visible;
            if (!contact.Shadow.Visible)
                continue;
            var foot = (contact.Skeleton.GlobalTransform *
                contact.Skeleton.GetBoneGlobalPose(contact.Bone)).Origin;
            contact.Shadow.GlobalPosition = new Vector3(foot.X, 0.006f, foot.Z);
            // 脚抬高时自然淡出，落地时加深；实际脚骨姿态与演员根保持独立。
            float strength = Mathf.Lerp(0.30f, 0.04f, Mathf.Clamp(foot.Y / 0.24f, 0, 1));
            contact.Material.AlbedoColor = new Color(1, 1, 1, strength);
        }
    }

    private void AddGrounding(Node3D actor, string leftSole, string rightSole)
    {
        var sk = actor.FindChild("Skeleton3D", true, false) as Skeleton3D;
        if (sk == null)
            return;
        int left = sk.FindBone(leftSole), right = sk.FindBone(rightSole);
        int rootBone = -1;
        for (int i = 0; i < sk.GetBoneCount(); i++)
            if (sk.GetBoneParent(i) < 0)
            {
                rootBone = i;
                break;
            }
        if (rootBone < 0 || left < 0 || right < 0)
        {
            GD.PushWarning($"[Story] {actor.Name}: missing sole/root bones for grounding");
            return;
        }
        _grounding.Add(new DancerGrounding
        {
            Actor = actor, Skeleton = sk, RootBone = rootBone,
            LeftSole = left, RightSole = right, RootPose = sk.GetBonePosePosition(rootBone),
        });
    }

    private void GroundDancers()
    {
        foreach (var dancer in _grounding)
        {
            var sk = dancer.Skeleton;
            // 每帧先恢复绑定姿态，避免校正量累积。演员和 Model 的变换始终固定。
            sk.SetBonePosePosition(dancer.RootBone, dancer.RootPose);
            if (!dancer.Actor.Visible)
                continue;
            float leftY = (sk.GlobalTransform * sk.GetBoneGlobalPose(dancer.LeftSole)).Origin.Y;
            float rightY = (sk.GlobalTransform * sk.GetBoneGlobalPose(dancer.RightSole)).Origin.Y;
            float lowestSole = Mathf.Min(leftY, rightY);
            // 双脚均明显离地时保留舞步中的跳跃，过渡段平滑衰减以免姿态突跳。
            if (lowestSole <= 0 || lowestSole >= 0.18f)
                continue;
            float weight = Mathf.Clamp((0.18f - lowestSole) / 0.10f, 0, 1);
            weight = weight * weight * (3 - 2 * weight);
            Vector3 localDown = sk.GlobalBasis.Inverse() * (Vector3.Down * lowestSole * weight);
            sk.SetBonePosePosition(dancer.RootBone, dancer.RootPose + localDown);
        }
    }

    private Node3D BuildDancer(Node parent, string name, string fbx, Vector3 pos, double delay)
    {
        var root = new Node3D { Name = name, Position = pos };
        parent.AddChild(root);
        var model = GD.Load<PackedScene>(fbx).Instantiate<Node3D>();
        model.Name = "Model";
        model.Rotation = Vector3.Zero;
        bool isBlade = name == "blade_girl";
        if (isBlade)
            model.Scale = Vector3.One * 0.01f; // 原 FBX 网格为厘米单位
        else if (name == "casual_dressed_girl")
            model.Scale = Vector3.One * 120.0f; // 原 prefab ×12, FBX 导入单位差 ×10
        root.AddChild(model);
        // 演员材质:FBX 未内嵌贴图,按原材质槽接回身体/面部贴图。
        string texRel = fbx.GetBaseDir().TrimPrefix("res://assets/models/actors/");
        string texName = texRel.GetFile() == "f05_schoolwear"
            ? "f05_schoolwear_200_m.png" : $"{texRel}.png";
        string texPath = $"res://assets/models/actors/{texRel}/{texName}";
        if (isBlade)
        {
            var bladeMat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoTexture = GD.Load<Texture2D>("res://assets/models/actors/blade_girl/blade_girl_base.png"),
            };
            foreach (var mi in model.FindChildren("*", "MeshInstance3D", true, false))
            {
                if (mi is not MeshInstance3D mesh)
                    continue;
                mesh.Visible = mesh.Name == "headusOBJexport008";
                if (mesh.Visible)
                    mesh.MaterialOverride = bladeMat;
            }
        }
        else if (ResourceLoader.Exists(texPath))
        {
            var mat = new StandardMaterial3D
            {
                AlbedoTexture = GD.Load<Texture2D>(texPath),
                Roughness = 0.9f,
                ShadingMode = name == "casual_dressed_girl"
                    ? BaseMaterial3D.ShadingModeEnum.Unshaded
                    : BaseMaterial3D.ShadingModeEnum.PerPixel,
                DiffuseMode = name == "f05_schoolwear"
                    ? BaseMaterial3D.DiffuseModeEnum.Toon
                    : BaseMaterial3D.DiffuseModeEnum.Burley,
                Transparency = name == "f05_schoolwear"
                    ? BaseMaterial3D.TransparencyEnum.AlphaScissor
                    : BaseMaterial3D.TransparencyEnum.Disabled,
                AlphaScissorThreshold = 0.9f,
                CullMode = name == "f05_schoolwear"
                    ? BaseMaterial3D.CullModeEnum.Disabled
                    : BaseMaterial3D.CullModeEnum.Back,
            };
            StandardMaterial3D? faceMat = name == "f05_schoolwear"
                ? new StandardMaterial3D
                {
                    AlbedoTexture = GD.Load<Texture2D>(
                        "res://assets/models/actors/f05_schoolwear/f05_face_00_m.png"),
                    Roughness = 0.9f,
                    DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Toon,
                    Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
                    AlphaScissorThreshold = 0.9f,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                }
                : null;
            foreach (var mi in model.FindChildren("*", "MeshInstance3D", true, false))
            {
                if (mi is MeshInstance3D m)
                {
                    if (faceMat == null || m.Mesh == null)
                    {
                        m.MaterialOverride = mat;
                        continue;
                    }
                    for (int s = 0; s < m.Mesh.GetSurfaceCount(); s++)
                    {
                        var sourceName = m.Mesh.SurfaceGetMaterial(s)?.ResourceName ?? "";
                        m.SetSurfaceOverrideMaterial(s,
                            sourceName.Contains("f05_face") || sourceName.Contains("f05_hair")
                                ? faceMat : mat);
                    }
                }
            }
        }
        var ap = new AnimationPlayer { Name = "AnimationPlayer" };
        // 优先:K-POP 完整舞蹈烘焙(Unity BakeKpopDance 落盘的 200.8s 全骨架 TRS,KDancePlayer 回放);
        // 缺失时回退 4s dance 循环(legacy 占位)
        string danceBin = $"res://assets/models/actors/dance/{fbx.GetFile().GetBaseName()}.kdance.bin";
        var kd = KDancePlayer.TryCreate(model, danceBin,
            new Transform3D(new Basis(new Quaternion(Vector3.Up, Mathf.Pi)), Vector3.Zero));
        if (kd != null)
        {
            // Unity school_day.unity 的演员 Animator 关闭了 Apply Root Motion。
            // 保持演员的场景站位，舞步只驱动骨架。
            kd.ApplyRootMotion = false;
            if (isBlade)
                kd.BonePositionScale = 100.0f;
            else if (name == "casual_dressed_girl")
                kd.BonePositionScale = 0.1f;
            kd.Phase = -delay;
            _dancePlayers.Add(kd);
            return root;
        }
        model.AddChild(ap);
        // 演员动画库轨道路径为 "Skeleton3D:<骨>"(无 Model 前缀)→ AnimationPlayer 必须挂在
        // Model 实例根(与 Skeleton3D 同级);怪物库是 "Model/Skeleton3D:..." 挂外层
        string dir = fbx.GetBaseDir();
        string baseName = fbx.GetFile().GetBaseName();
        string animPath = $"{dir}/{baseName}_anims.tres";
        if (!ResourceLoader.Exists(animPath))
            animPath = $"{dir}/{dir.GetFile()}_anims.tres";
        ap.AddAnimationLibrary("", GD.Load<AnimationLibrary>(animPath));
        ap.Play("dance");
        if (delay > 0.0)
            ap.Seek(delay, true);
        return root;
    }

    // ------------------------------------------------ 切镜表(StoryStartTimeline Cinemachine Track)

    private void BuildCuts()
    {
        NodePath casualPath = "Students/casual_dressed_girl";
        NodePath bladePath = "Students/blade_girl";
        NodePath f05Path = "Students/f05_schoolwear";
        _cuts = new[]
        {
            // Unity StoryStartTimeline.playable 的 Cinemachine 轨道。首镜头没有 BlendIn，直接切。
            // 后续 Blend 为相邻 clip 的实际重叠时长，远非统一的 1.5 秒。
            new Cut { Time = 2.983333, Blend = 0, Marker = "Markers/vcam1" },
            new Cut { Time = 4.633333, Blend = 1.333333, Marker = "Markers/vcam2" },
            new Cut { Time = 9.233333, Blend = 1.7, Marker = "Markers/vcam3",
                HeadActor = casualPath, HeadBone = "Bip01 Head", OrbitFollow = true, ZoomWidth = 0.43f },
            new Cut { Time = 12.666667, Blend = 1.566667, Marker = "Markers/vcam3_1",
                HeadActor = bladePath, HeadBone = "Bip001 Head", OrbitFollow = true, ZoomWidth = 0.43f },
            new Cut { Time = 16.266667, Blend = 1.4, Marker = "Markers/vcam4" },
            new Cut { Time = 21.733333, Blend = 2.083333, Marker = "Markers/vcam3_2",
                HeadActor = f05Path, HeadBone = "Head", ZoomWidth = 4.7f },
            new Cut { Time = 25.966667, Blend = 1.333333, Marker = "Markers/vcam6" },
            new Cut { Time = 30.083333, Blend = 1.233333, Marker = "Markers/vcam7" },
            new Cut { Time = 34.266667, Blend = 0.883333, Marker = "Markers/vcam8" },
            new Cut { Time = 53.466667, Blend = 1.383333, Marker = "Markers/vcam10" },
        };
    }

    // ------------------------------------------------ UI(字幕 + 跳过)

    private void BuildUi()
    {
        var layer = new CanvasLayer { Name = "StoryUI", Layer = 10 };
        AddChild(layer);
        _introBlack = new ColorRect
        {
            Name = "IntroBlack",
            Color = Colors.Black,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _introBlack.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_introBlack);
        _subtitle = new Label
        {
            Name = "Subtitle",
            Text = "维欧高中的同学正在排练舞蹈",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _subtitle.AddThemeFontSizeOverride("font_size", 62);
        _subtitle.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_subtitle);
        // 跳过按钮 200×60 右上(Btn_button03_n,文字 32 白)
        var skip = new Button
        {
            Name = "SkipBtn",
            Text = "跳过",
            AnchorLeft = 1.0f, AnchorRight = 1.0f, AnchorTop = 0.0f, AnchorBottom = 0.0f,
            OffsetLeft = -200.0f, OffsetRight = 0.0f, OffsetTop = 0.0f, OffsetBottom = 60.0f,
        };
        skip.AddThemeFontSizeOverride("font_size", 32);
        var texN = GD.Load<Texture2D>("res://assets/textures/ui/Btn_button03_n.png");
        var texH = GD.Load<Texture2D>("res://assets/textures/ui/Btn_button03_h.png");
        var styleN = new StyleBoxTexture { Texture = texN };
        var styleH = new StyleBoxTexture { Texture = texH };
        skip.AddThemeStyleboxOverride("normal", styleN);
        skip.AddThemeStyleboxOverride("hover", styleH);
        skip.AddThemeStyleboxOverride("pressed", styleH);
        skip.AddThemeColorOverride("font_color", Colors.White);
        skip.Pressed += NextScene;
        layer.AddChild(skip);
    }

    // ------------------------------------------------ 主时钟

    public override void _Process(double delta)
    {
        if (_ended)
            return;
        _clock += delta;
        // 切镜
        while (_cutIdx < _cuts.Length && _clock >= _cuts[_cutIdx].Time)
        {
            ApplyCut(_cuts[_cutIdx]);
            _cutIdx += 1;
        }
        UpdateCamera();
        // 字幕
        _subtitle.Visible = _clock < SubtitleEnd;
        _introBlack.Visible = _subtitle.Visible;
        _blade.Visible = _clock >= 2.98333333333333;
        // 完整舞蹈回放(烘焙数据存在时;剧情时钟驱动,与音乐/切镜同步)
        foreach (var kd in _dancePlayers)
            kd.SetStoryTime(_clock);
        GroundDancers();
        UpdateFootContacts();
        // RockWarrior 出场
        if (_clock >= RockShowTime && !_rock.Visible)
        {
            _rock.Visible = true;
            _rockAnim.Play("locomotion", 0.2);
            GD.Print("[Story] RockWarrior run @50.5s");
        }
        // 结束
        if (_clock >= NextSceneTime)
        {
            _ended = true;
            GD.Print("[Story] done → level1_battle");
            if (!_suppressSwitch)
                Game.Instance.ChangeScene(NextScenePath);
            else
                EmitSignal(SignalName.StoryEnded);
        }
    }

    [Signal] public delegate void StoryEndedEventHandler();

    private void ApplyCut(Cut cut)
    {
        GD.Print($"[Story] cut → {cut.Marker} @{_clock:0.00}s");
    }

    private Transform3D? HeadTransform(Cut cut)
    {
        if (cut.HeadActor == null)
            return null;
        var actor = GetNode<Node3D>(cut.HeadActor);
        var skeleton = actor.FindChild("Skeleton3D", true, false) as Skeleton3D;
        if (skeleton == null)
            return null;
        int bone = skeleton.FindBone(cut.HeadBone);
        if (bone < 0)
            return null;
        var pose = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(bone);
        // 导入的厘米单位 SkinnedMesh 与 Unity Head Transform 存在网格顶点偏移。
        // 用可见头部中心校正跟随点，避免 Cinemachine 近景把脸裁到画面上沿。
        if (cut.OrbitFollow)
            pose.Origin += Vector3.Up * (cut.HeadBone == "Bip01 Head" ? 0.12f : 0.05f);
        return pose;
    }

    private Transform3D CutTransform(Cut cut)
    {
        var pose = GetNode<Node3D>(cut.Marker).GlobalTransform;
        if (cut.OrbitFollow && HeadTransform(cut) is Transform3D head)
        {
            // CinemachineOrbitalTransposer: LockToTargetWithWorldUp, FollowOffset=(1,0,0).
            // Unity→Godot 的 X 镜像使局部 +X 变为 −X；只跟随目标头部的水平朝向。
            var side = -head.Basis.X;
            side.Y = 0;
            if (side.LengthSquared() > 0.001f)
                pose.Origin = head.Origin + side.Normalized();
            // 子级 CinemachineHardLookAt 直接盯头骨原点，没有额外的 +1m 高度。
            pose = pose.LookingAt(head.Origin, Vector3.Up);
        }
        return pose;
    }

    private float CutFov(Cut cut, Transform3D pose)
    {
        if (cut.ZoomWidth <= 0 || HeadTransform(cut) is not Transform3D head)
            return 45.0f;
        // CinemachineFollowZoom: 目标宽度 / 相机到 LookAt 目标的距离，限制 3°..60°。
        float distance = pose.Origin.DistanceTo(head.Origin);
        return Mathf.Clamp(Mathf.RadToDeg(2.0f * Mathf.Atan(cut.ZoomWidth / (2.0f * distance))), 3.0f, 60.0f);
    }

    private void UpdateCamera()
    {
        if (_cutIdx == 0)
            return;
        var current = _cuts[_cutIdx - 1];
        var target = CutTransform(current);
        float targetFov = CutFov(current, target);
        if (current.Blend <= 0 || _clock >= current.Time + current.Blend)
        {
            _camera.GlobalTransform = target;
            _camera.Fov = targetFov;
            return;
        }
        var from = _cutIdx == 1
            ? GetNode<Node3D>("Markers/vcam8").GlobalTransform
            : CutTransform(_cuts[_cutIdx - 2]);
        float fromFov = _cutIdx == 1 ? 45.0f : CutFov(_cuts[_cutIdx - 2], from);
        float weight = Mathf.Clamp((float)((_clock - current.Time) / current.Blend), 0, 1);
        _camera.GlobalTransform = from.InterpolateWith(target, weight);
        _camera.Fov = Mathf.Lerp(fromFov, targetFov, weight);
    }

    private void NextScene()
    {
        if (_ended)
            return;
        _ended = true;
        GD.Print("[Story] skip → level1_battle");
        Game.Instance.ChangeScene(NextScenePath);
    }

    private async void TakeShotDelayed(string path, float delay)
    {
        await ToSignal(GetTree().CreateTimer(delay, true, true), SceneTreeTimer.SignalName.Timeout);
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(path);
        GD.Print($"[Story] shot saved: {path}");
        GetTree().Quit();
    }

    // ------------------------------------------------ 自检

    private void Check(bool cond, string label)
    {
        GD.Print((cond ? "[STORY-TEST] PASS: " : "[STORY-TEST] FAIL: ") + label);
        if (!cond)
            _testFailed = true;
    }

    private async void SelfTest()
    {
        var actorStarts = new[] { _casual.GlobalTransform, _f05.GlobalTransform, _blade.GlobalTransform };
        var modelStarts = new[]
        {
            _casual.GetNode<Node3D>("Model").GlobalTransform,
            _f05.GetNode<Node3D>("Model").GlobalTransform,
            _blade.GetNode<Node3D>("Model").GlobalTransform,
        };
        Check(_cuts.Length == 10, $"cut table = 10 (got {_cuts.Length})");
        Check(_casual != null && _f05 != null && _blade != null, "three dancers exist");
        Check(!_blade.Visible, "blade dancer initially hidden");
        Check(_dancePlayers.Count == 3, $"full K-POP dance baked players = 3 (got {_dancePlayers.Count})");
        Check(_grounding.Count == 3, $"three sole-grounded dancers (got {_grounding.Count})");
        Check(_rock != null && !_rock.Visible, "rock hidden before 50.5s");
        const double accel = 20.0; // 20 倍速跑完 54.85s ≈ 2.7s
        bool ended = false;
        StoryEnded += () => ended = true;
        double t0 = Time.GetTicksMsec() / 1000.0;
        while (!ended && Time.GetTicksMsec() / 1000.0 - t0 < 60.0)
        {
            // 加速时钟:_Process 正常走,每帧额外推进 (accel-1)/60
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _clock += (accel - 1.0) / 60.0;
        }
        Check(ended, "story ended (54.35s reached, switch suppressed)");
        Check(_cutIdx == _cuts.Length, $"all cuts fired ({_cutIdx}/{_cuts.Length})");
        Check(_rock.Visible, "rock shown after 50.5s");
        Check(_blade.Visible, "blade dancer activated by Timeline");
        Check(_subtitle.Visible == false, "subtitle hidden after 2.967s");
        Check(!_introBlack.Visible, "black subtitle background hidden after 2.967s");
        var actors = new[] { _casual, _f05, _blade };
        for (int i = 0; i < actors.Length; i++)
        {
            Check(actors[i].GlobalTransform.IsEqualApprox(actorStarts[i]), $"{actors[i].Name} actor root fixed");
            Check(actors[i].GetNode<Node3D>("Model").GlobalTransform.IsEqualApprox(modelStarts[i]),
                $"{actors[i].Name} model root fixed");
        }
        GD.Print($"[STORY-TEST] done, failed={_testFailed}");
        GetTree().Quit(_testFailed ? 1 : 0);
    }
}
