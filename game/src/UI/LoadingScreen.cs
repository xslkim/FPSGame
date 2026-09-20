using Godot;

namespace FPSGame;

/// <summary>
/// Loading:1:1 移植 Unity Assets/UI/LoadingScene.unity + LoadingScene.cs。
/// 布局:原作生效的是 Loading 节点上的嵌套 Canvas(RectTransform 不被根驱动,保持 1920×1080、
///   scale 2/3、锚左下 (640,360))→ 视觉即 1280×720;此处按真值反解以 1280×720 重建(720p 像素一致。
///   原作超宽屏时 UI 贴左下、右侧露相机底色,属嵌套 Canvas 未驱动的 bug,移植版居中,见 README)。
///   BG background4 全屏;Title1_bg 654×142.67 @(0,119.33),"士兵打怪兽" btt 53 淡蓝
///   (0.883,0.947,0.991) 文字 scale (1.3875,0.95);进度条容器 799.27×13.33 @(4.4,-258.09)
///   scale (1.5,2):轨道 bg_Progress(竖向 scale 0.4)、填充 Progress1(值驱动宽度)、星芒 Progress2
///   (跟随值位),"60%" A001ff-Bold 25 淡蓝白 @(382.8,-18.8),Tips btt 19 淡黄绿 @(0,-19.79)。
/// 行为照原作 LoadingScene.cs:进场 UpdateUIMode 全隐藏 HUD;Tips 取服务器配置
///   (原作唯一来源 pc.oceanfitness.xyz 已失效,离线保持场景默认"你知道吗？");
///   线程加载 NextScenePath,加载完成→UpdateProgress(1)(原作 op.progress>=0.9 即显 100%),
///   且满 4s(LoadingMinTime)→SceneState=Battle、CurAliveMonster=0、重置玩家、
///   菜单音乐由 AudioService 在战斗场景加载后自动停(=原作 MenuUIAS.Stop)、放行切场景;
///   加载失败(目标场景缺失)停在原地不动(同原作协程死掉的状态)。
/// 验证:--shot(无目标→停留 "60%"+空条+星芒在左,对照真值);--loading-selftest(目标 menu.tscn
///   成功路径)/ --loading-selftest-stuck(无目标停滞路径)。
/// </summary>
public partial class LoadingScreen : Node
{
    private const double MinTime = 4.0;
    private const string LdDir = "res://assets/textures/ui/loading/";
    private const float SliderW = 799.267f;   // 进度条容器宽(=1198.9×2/3)
    private const float SliderH = 13.333f;

    private Label _progressLabel = null!;
    private Label _tipsLabel = null!;
    private TextureRect _fill = null!;
    private TextureRect _handle = null!;

    private string _path = "";
    private double _elapsed;
    private bool _done;
    private bool _failLogged;
    private bool _suppressSwitch; // 自检用:激活但不真切场景
    private float _value = -1.0f; // 已应用的进度值(避免重复摆放)

    public override void _Ready()
    {
        Game.Instance.SceneState = Game.GameState.UI;
        PlayerState.Instance.UpdateUiMode("LoadingScene"); // 原作 UpdateUIMode:Loading 全隐藏
        MessageBox.CloseCurrent();
        BuildUi();

        // Tips(原作 DataMgr._Config 唯一来源是服务器,已失效 → 离线保持场景默认"你知道吗？")
        var cfg = SaveService.Instance.RemoteConfig;
        if (cfg != null && cfg.ContainsKey("Tips"))
        {
            var arr = cfg["Tips"].AsGodotArray();
            if (arr.Count > 0)
                _tipsLabel.Text = arr[(int)(GD.Randi() % arr.Count)].AsString();
        }

        _path = Game.Instance.NextScenePath;
        var args = OS.GetCmdlineUserArgs();
        foreach (var a in args)
        {
            if (a.StartsWith("--loading-target:")) // 测试钩子:指定加载目标
                _path = a["--loading-target:".Length..];
        }
        if (System.Array.IndexOf(args, "--loading-selftest") >= 0)
        {
            _path = "res://scenes/ui/menu.tscn";
            _suppressSwitch = true; // 自检:验证激活状态但不真切场景(场景一换自检对象即被释放)
            Callable.From(() => RunSelfTest(stuck: false)).CallDeferred();
        }
        else if (System.Array.IndexOf(args, "--loading-selftest-stuck") >= 0)
        {
            _path = "";
            Callable.From(() => RunSelfTest(stuck: true)).CallDeferred();
        }
        foreach (var a in args)
        {
            if (a.StartsWith("--shot:"))
                Callable.From(() => TakeShot(a["--shot:".Length..])).CallDeferred();
        }

        if (_path.Length > 0)
        {
            var err = ResourceLoader.LoadThreadedRequest(_path);
            if (err != Error.Ok)
            {
                GD.PushError($"[Loading] 加载请求失败: {_path} (err {err})");
                _path = "";
            }
        }
        else
            GD.PushError("[Loading] 无目标场景(原作 DataMgr._LoadScene 为空时同样停滞)");
    }

    // ---------------------------------------------------------------- UI 构建

    private void BuildUi()
    {
        var root = UiKit.MakeRoot(GetNode("UILayer"));

        // BG:background4 全屏等比覆盖
        var bg = UiKit.TexRect("BG", UiKit.TexDir + "background4.png");
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bg.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
        root.AddChild(bg);

        // Title1:654×142.67 @(0,119.33)
        var title = UiKit.TexRect("Title1", LdDir + "Title1_bg.png");
        UiKit.Place(title, 0, 119.333f, 654, 142.667f);
        root.AddChild(title);
        var titleText = UiKit.MakeLabel("士兵打怪兽", 53, new Color(0.88309896f, 0.9466022f, 0.990566f));
        titleText.Name = "Text";
        UiKit.Place(titleText, 0, 0, 342.867f, 88.733f);
        titleText.Scale = new Vector2(1.3875f, 0.95f); // 原作文字横向拉伸
        title.AddChild(titleText);

        // Slider 容器:799.27×13.33 @(4.4,-258.09) scale (1.5,2)
        var slider = new Control { Name = "Slider", MouseFilter = Control.MouseFilterEnum.Ignore };
        UiKit.Place(slider, 4.4f, -258.093f, SliderW, SliderH);
        slider.Scale = new Vector2(1.5f, 2.0f);
        root.AddChild(slider);

        // 轨道:anchors (0,0.25)-(1,0.75) 高 26.67,竖向 scale 0.4
        var track = UiKit.TexRect("Background", LdDir + "bg_Progress.png");
        track.AnchorLeft = 0;
        track.AnchorRight = 1;
        track.AnchorTop = 0.25f;
        track.AnchorBottom = 0.75f;
        track.OffsetLeft = 0;
        track.OffsetRight = 0;
        track.OffsetTop = -13.333f;
        track.OffsetBottom = 13.333f;
        track.PivotOffset = new Vector2(SliderW / 2.0f, 16.667f);
        track.Scale = new Vector2(1, 0.39999762f);
        slider.AddChild(track);

        // 填充:左锚,宽度 = SliderW × value
        _fill = UiKit.TexRect("Fill", LdDir + "Progress1.png");
        _fill.AnchorLeft = 0;
        _fill.AnchorRight = 0;
        _fill.AnchorTop = 0.25f;
        _fill.AnchorBottom = 0.75f;
        _fill.OffsetLeft = 0;
        _fill.OffsetRight = 0;
        _fill.OffsetTop = -13.333f;
        _fill.OffsetBottom = 13.333f;
        slider.AddChild(_fill);

        // 星芒(原作 Handle):跟随值位;真值墨迹 27×12(横向),贴图按 134×60 渲染
        _handle = UiKit.TexRect("Handle", LdDir + "Progress2.png");
        UiKit.Place(_handle, -SliderW / 2.0f, 0, 134, 60);
        slider.AddChild(_handle);

        // ProgressValue:"60%" A001ff-Bold 25 @(382.8,-18.8)(容器 scale 下视觉 ≈38×2)
        _progressLabel = UiKit.MakeLabel("60%", 25, new Color(0.9103774f, 0.96309656f, 1), useBtt: false);
        _progressLabel.Name = "ProgressValue";
        _progressLabel.AddThemeFontOverride("font", GD.Load<Font>("res://assets/fonts/A001ff-Bold.otf"));
        UiKit.Place(_progressLabel, 382.8f, -18.8f, 66.667f, 40);
        slider.AddChild(_progressLabel);

        // Tips:"你知道吗？" btt 19 淡黄绿 @(0,-19.79),整宽居中
        _tipsLabel = UiKit.MakeLabel("你知道吗？", 19, new Color(0.9358429f, 0.9716981f, 0.77919185f));
        _tipsLabel.Name = "Tips";
        UiKit.Place(_tipsLabel, 0, -19.793f, SliderW, 26.667f);
        slider.AddChild(_tipsLabel);
    }

    // ---------------------------------------------------------------- 加载流程(LoadingScene.cs)

    public override void _Process(double delta)
    {
        if (_done)
            return;
        _elapsed += delta;
        if (_path.Length == 0)
            return; // 停滞(同原作协程死掉)
        var status = ResourceLoader.LoadThreadedGetStatus(_path);
        if (status == ResourceLoader.ThreadLoadStatus.Failed ||
            status == ResourceLoader.ThreadLoadStatus.InvalidResource)
        {
            if (!_failLogged)
            {
                _failLogged = true;
                GD.PushError("[Loading] 目标场景加载失败,停留 Loading: " + _path);
            }
            return;
        }
        if (status != ResourceLoader.ThreadLoadStatus.Loaded)
            return;
        UpdateProgress(1); // 原作:op.progress>=0.9 即显示 100%
        if (_elapsed < MinTime)
            return;
        _done = true;
        // 激活瞬间(原作):Battle 态、清怪物计数、重置玩家;菜单音乐由 AudioService 切场景后自动停
        Game.Instance.SceneState = Game.GameState.Battle;
        PlayerState.CurAliveMonster = 0;
        PlayerState.Instance.SetupPlayers(InputRouter.Instance.Mode);
        var packed = ResourceLoader.LoadThreadedGet(_path) as PackedScene;
        if (packed == null)
        {
            GD.PushError("[Loading] 取场景失败: " + _path);
            return;
        }
        if (!_suppressSwitch)
            GetTree().ChangeSceneToPacked(packed);
    }

    /// <summary>UpdateProgress:slider.value + 百分比文本(原作 Mathf.Round(v*100)+"%")</summary>
    private void UpdateProgress(float v)
    {
        if (Mathf.IsEqualApprox(v, _value))
            return;
        _value = v;
        _fill.OffsetRight = SliderW * v;
        float hx = -SliderW / 2.0f + SliderW * v;
        _handle.OffsetLeft = hx - 67;
        _handle.OffsetRight = hx + 67;
        _progressLabel.Text = Mathf.Round(v * 100).ToString() + "%";
    }

    // ---------------------------------------------------------------- 截图验证

    private async void TakeShot(string path)
    {
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--shot-res:"))
            {
                var wh = a["--shot-res:".Length..].Split('x');
                if (wh.Length == 2)
                    DisplayServer.WindowSetSize(new Vector2I(int.Parse(wh[0]), int.Parse(wh[1])));
            }
        }
        for (int i = 0; i < 30; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(Engine.GetSingleton("RenderingServer"), "frame_post_draw");
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(path);
        GD.Print("SHOT SAVED: ", path);
        GetTree().Quit();
    }

    // ---------------------------------------------------------------- 自检

    private async void RunSelfTest(bool stuck)
    {
        int fails = 0;
        void Check(bool ok, string what)
        {
            GD.Print((ok ? "PASS " : "FAIL ") + what);
            if (!ok) fails += 1;
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Check(_tipsLabel.Text == "你知道吗？", "Tips 默认文本(服务器配置失效)");
        Check(_progressLabel.Text == "60%", "进度文本初始 60%(原作场景默认值)");
        Check(Mathf.IsEqualApprox(_fill.OffsetRight, 0), "初始填充宽度 0");
        Check(Mathf.IsEqualApprox(_handle.OffsetLeft, -SliderW / 2.0f - 67), "星芒初始在条左端");
        Check(_tipsLabel.GetThemeFontSize("font_size") == 19, "Tips 字号 19(=28×2/3)");
        Check(!PlayerState.Instance.CoinObj.Visible, "Loading 隐藏金币 HUD(UpdateUIMode)");

        if (stuck)
        {
            // 无目标:5s 后仍停滞(原作协程死掉的状态)
            double t0 = Time.GetTicksMsec() / 1000.0;
            while (Time.GetTicksMsec() / 1000.0 - t0 < 5.0)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!_done, "无目标 5s 后仍停留 Loading");
            Check(_progressLabel.Text == "60%", "停滞时进度文本不变");
        }
        else
        {
            // 成功路径:Loaded→100%,满 4s→激活(Battle/重置玩家),随后切场景(Quit 抢先)
            double t0 = Time.GetTicksMsec() / 1000.0;
            while (!_done && Time.GetTicksMsec() / 1000.0 - t0 < 10.0)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(_progressLabel.Text == "100%", "加载完成后进度 100%");
            Check(Mathf.IsEqualApprox(_fill.OffsetRight, SliderW), "完成后填充全宽");
            Check(Mathf.IsEqualApprox(_handle.OffsetLeft, SliderW / 2.0f - 67), "星芒移到条右端");
            Check(_elapsed >= MinTime, "满足最少 4s(LoadingMinTime)");
            Check(_done, "满 4s 且加载完成 → 激活");
            Check(Game.Instance.SceneState == Game.GameState.Battle, "激活瞬间 SceneState=Battle");
            Check(PlayerState.CurAliveMonster == 0, "激活瞬间 CurAliveMonster=0");
        }

        GD.Print($"LOADING SELFTEST {(fails == 0 ? "PASS" : "FAIL")} (fails={fails})");
        (Engine.GetMainLoop() as SceneTree)!.Quit(fails > 0 ? 1 : 0);
    }
}
