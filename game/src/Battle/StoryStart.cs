using Godot;

namespace FPSGame;

/// <summary>
/// 剧情过场(1:1 Level1.unity + StoryStart.cs + StoryStartTimeline.playable):
/// 学校走廊傍晚,3 学生跳 K-POP(3~54.8s,实际仅 casual/f05 可见,blade_girl 原作即 inactive),
/// 10 机位按切镜表 blend 切换,0~2.967s 字幕 + dance.mp3(3.1s),50.5s RockWarrior 冲出(run),
/// 54.35s(时长-0.5)或"跳过"按钮 → level1_battle.tscn。
/// 舞蹈动画:Unity BakeKpopDance 把 humanoid 肌肉剪辑逐帧烘成全骨骼局部 TRS(.kdance.bin),
/// KDancePlayer 全局链镜像 X 回放(数值与 Unity 逐位一致),剧情时钟驱动;相位错落照原作。
/// 启动参数:--story-selftest(加速断言,抑制切场景)/ --story-shot:&lt;path&gt;[:delay]
/// </summary>
public partial class StoryStart : Node3D
{
    public const double Duration = 54.85;
    public const double NextSceneTime = Duration - 0.5; // StoryStart:_pd.time >= duration-0.5
    public const double RockShowTime = 50.5;
    public const double SubtitleEnd = 2.967;
    public const double CamBlend = 1.5; // Cinemachine 默认 blend 近似

    public const string NextScenePath = "res://scenes/levels/level1_battle.tscn";

    private struct Cut { public double Time; public string Marker; public NodePath? LookAt; }
    private Cut[] _cuts = System.Array.Empty<Cut>();
    private int _cutIdx;

    private Camera3D _camera = null!;
    private Node3D _rock = null!;
    private AnimationPlayer _rockAnim = null!;
    private Node3D _casual = null!;
    private Node3D _f05 = null!;
    private Label _subtitle = null!;
    private AudioStreamPlayer _dancePlayer = null!;
    private double _clock;
    private bool _ended;
    private bool _suppressSwitch;
    private Tween? _camTween;
    private Node3D? _lookTarget;
    private bool _testFailed;
    private readonly System.Collections.Generic.List<KDancePlayer> _dancePlayers = new();

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _suppressSwitch = System.Array.IndexOf(args, "--story-selftest") >= 0;
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
        var students = new Node3D { Name = "Students" };
        AddChild(students);
        _casual = BuildDancer(students, "casual_dressed_girl",
            "res://assets/models/actors/casual_dressed_girl/casual_dressed_girl.FBX",
            new Vector3(0.271f, 0.033f, 21.799f), 0.0);
        _f05 = BuildDancer(students, "f05_schoolwear",
            "res://assets/models/actors/f05_schoolwear/f05_schoolwear_200_m.fbx",
            new Vector3(1.142f, 0.012f, 20.519f), 0.0667); // 原作相位错落 3.0333-2.9667
        // blade_girl:原作 inactive(Animator 被清空),不参与演出,不创建
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

    private Node3D BuildDancer(Node parent, string name, string fbx, Vector3 pos, double delay)
    {
        var root = new Node3D { Name = name, Position = pos };
        parent.AddChild(root);
        var model = GD.Load<PackedScene>(fbx).Instantiate<Node3D>();
        model.Name = "Model";
        model.Rotation = new Vector3(0, Mathf.Pi, 0);
        root.AddChild(model);
        // 演员材质:FBX 未内嵌贴图,按目录同名 png 接线(f05 另有脸部贴图,先整体身体贴图)
        string texRel = fbx.GetBaseDir().TrimPrefix("res://assets/models/actors/");
        string texName = texRel.GetFile() == "f05_schoolwear"
            ? "f05_schoolwear_200_m.png" : $"{texRel}.png";
        string texPath = $"res://assets/models/actors/{texRel}/{texName}";
        if (ResourceLoader.Exists(texPath))
        {
            var mat = new StandardMaterial3D
            {
                AlbedoTexture = GD.Load<Texture2D>(texPath),
                Roughness = 0.9f,
            };
            foreach (var mi in model.FindChildren("*", "MeshInstance3D", true, false))
            {
                if (mi is MeshInstance3D m)
                    m.MaterialOverride = mat;
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
            kd.Phase = delay;
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
        NodePath f05Path = "Students/f05_schoolwear";
        _cuts = new[]
        {
            new Cut { Time = 2.983, Marker = "Markers/vcam1" },
            new Cut { Time = 4.633, Marker = "Markers/vcam2" },
            new Cut { Time = 9.233, Marker = "Markers/vcam3", LookAt = casualPath },
            new Cut { Time = 12.667, Marker = "Markers/vcam3_1" },
            new Cut { Time = 16.267, Marker = "Markers/vcam4" },
            new Cut { Time = 21.733, Marker = "Markers/vcam3_2", LookAt = f05Path },
            new Cut { Time = 25.967, Marker = "Markers/vcam6" },
            new Cut { Time = 30.083, Marker = "Markers/vcam7" },
            new Cut { Time = 34.267, Marker = "Markers/vcam8" },
            // vcam10(53.467)在原场景缺失 → 保持 vcam8 到结束
        };
    }

    // ------------------------------------------------ UI(字幕 + 跳过)

    private void BuildUi()
    {
        var layer = new CanvasLayer { Name = "StoryUI", Layer = 10 };
        AddChild(layer);
        _subtitle = new Label
        {
            Name = "Subtitle",
            Text = "维欧高中的同学正在排练舞蹈",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _subtitle.AddThemeFontSizeOverride("font_size", 90);
        _subtitle.SetAnchorsPreset(Control.LayoutPreset.Center);
        _subtitle.Position = new Vector2(-400, -60);
        _subtitle.Size = new Vector2(800, 120);
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
        // 跟踪机位(GroupComposer 近似:位置 blend 后每帧 LookAt)
        if (_lookTarget != null && (_camTween == null || !_camTween.IsRunning()))
            _camera.LookAt(_lookTarget.GlobalPosition + new Vector3(0, 1.0f, 0), Vector3.Up);
        // 字幕
        _subtitle.Visible = _clock < SubtitleEnd;
        // 完整舞蹈回放(烘焙数据存在时;剧情时钟驱动,与音乐/切镜同步)
        foreach (var kd in _dancePlayers)
            kd.SetStoryTime(_clock);
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
        var marker = GetNode<Node3D>(cut.Marker);
        _camTween?.Kill();
        _camTween = CreateTween();
        _camTween.TweenProperty(_camera, "global_transform", marker.GlobalTransform,
            CamBlend).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _lookTarget = cut.LookAt != null ? GetNode<Node3D>(cut.LookAt) : null;
        GD.Print($"[Story] cut → {cut.Marker} @{_clock:0.00}s");
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
        Check(_cuts.Length == 9, $"cut table = 9 (got {_cuts.Length})");
        Check(_casual != null && _f05 != null, "dancers exist");
        Check(_dancePlayers.Count == 2, $"full K-POP dance baked players = 2 (got {_dancePlayers.Count})");
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
        Check(_subtitle.Visible == false, "subtitle hidden after 2.967s");
        GD.Print($"[STORY-TEST] done, failed={_testFailed}");
        GetTree().Quit(_testFailed ? 1 : 0);
    }
}
