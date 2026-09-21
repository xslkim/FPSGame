using Godot;

namespace FPSGame;

/// <summary>
/// 选关:1:1 移植 Unity Assets/UI/LevelChoose.unity + LevelChoose.cs / LevelState.cs。
/// 3D(SubViewport 透明,深度序照原作 WorldSpace Canvas):相机 FOV60、平行光(euler 50,-30,0,
///   色 (1,0.957,0.839));相机下挂双枪——右 AK47(0.25,-0.11,0.2,戒指/遥控器/鼠标路)、
///   左 M4(-0.25,-0.11,0.22,手机腿部路),各带激光(LaserSight 右红/左绿,照原作
///   Lazer.mat/LazerLeft.mat:枪口→虚拟画布平面 z=621.6)+屏幕红点(UiAimDot 标示 2D 命中)
///   +枪口火光(命中按钮时播放,照菜单)。
/// UI:逻辑分辨率 1280×720;背景 background6 全屏等比覆盖;返回(右上 anchor 1,1);
///   LevelGroup(anchor 0,0.5,x=150 起)13 关按钮(300×300,bg_map 九宫格 22/25/22/21,
///   页内间距 340、页宽 1280,翻页 100px/帧≈6000px/s,Level5~13 原作默认 inactive);
///   左右箭头(anchor 0/1,0.5);难度面板 DifficuleChoosePanel(700×600 messagebox.png α0.8745,
///   "选择难度"+TitleLine 装饰,简单/困难/地狱 500×110 带图标)。
/// 交互照原作 clickDelegate:关卡键(无 Button 组件→枪声)先查币(不足弹"游戏币"框并隐藏关卡组)、
///   再查前置关星数(未解锁弹"关卡解锁"框),通过则扣 1 币、0.5s 后出难度面板;
///   返回键:面板开着先关面板(1s 防抖),否则回菜单;Esc 直接回菜单。
/// 关卡数据照 LevelState.cs:星≥1 熄锁亮星1,≥2 亮星2,≥3 亮星3;得分>0 显示"得分:N",
///   排名>0 显示"排名:N"(原作默认存档 3 星/3 分/排名 1,见 UserMeta.cs)。
/// 注意:原作 AK47 侧火光父级 Sphere(1) 默认 inactive 永不显示,移植版修正为正常播放(同菜单);
///   枪上 Movie(RawImage+VideoPlayer startmov.mp4 默认不播,渲染透明)未移植,属连接手机流程。
/// </summary>
public partial class LevelChooseScreen : Node
{
    private const string MenuScene = "res://scenes/ui/menu.tscn";
    private const string LoadingScene = "res://scenes/ui/loading.tscn";
    private const string LcDir = "res://assets/textures/ui/levelchoose/";
    private const int LevelCount = 13;
    private const float GroupHomeX = 150.0f;  // LevelGroup 初始 anchoredPosition.x(原作 150,-0.03)
    private const float PageWidth = 1280.0f;  // targetPos = 150 + page*-1280
    private const float PageSpeed = 6000.0f;  // 原作 100/帧(60fps 等效)
    private const string VpPrefix = "ViewportLayer/SubViewportContainer/SubViewport/";
    private const float CanvasZ = 621.6f; // 原作 WorldSpace Canvas 距相机 621.6(激光束终点平面)

    /// <summary>已建关卡映射;未建 → MessageBox "敬请期待"(战斗关卡未移植,已知)</summary>
    private static readonly System.Collections.Generic.Dictionary<int, string> SceneMap = new()
    {
        [0] = "res://scenes/levels/level1_story.tscn",
        [1] = "res://scenes/levels/level2.tscn",
        [2] = "res://scenes/levels/level3.tscn",
        [3] = "res://scenes/levels/level4.tscn",
    };

    // 每关缩略图(原作 Level7/10 也用 level4.jpg;Level5~13 隐藏不占视觉)
    private static readonly string[] Thumbs =
    {
        "level1.jpg", "level2.jpg", "level3.jpg", "level4.jpg",
        "lock.jpg", "lock.jpg", "level4.jpg", "lock.jpg", "lock.jpg", "level4.jpg",
        "lock.jpg", "lock.jpg", "lock.jpg",
    };

    private SubViewport _subvp = null!;
    private Camera3D _camera = null!;
    private Node3D _gunAk = null!;   // 右路(戒指/遥控器/鼠标)
    private Node3D _gunM4 = null!;   // 左路(手机腿部)
    private LaserSight _laserAk = null!;
    private LaserSight _laserM4 = null!;
    private UiAimDot _dotAk = null!;
    private UiAimDot _dotM4 = null!;
    private MuzzleFlash _muzzleAk = null!;
    private MuzzleFlash _muzzleM4 = null!;
    private Control _buttonRoot = null!;

    private Control _levelGroup = null!;
    private UiSwapButton _backBtn = null!;
    private UiSwapButton _leftBtn = null!;
    private UiSwapButton _rightBtn = null!;
    private Control _diffPanel = null!;
    private readonly UiSwapButton[] _levelButtons = new UiSwapButton[LevelCount];
    private readonly Control[] _locks = new Control[LevelCount];
    private readonly Control[][] _stars = new Control[LevelCount][];
    private readonly Control[] _scoreObjs = new Control[LevelCount];
    private readonly Label[] _scoreValues = new Label[LevelCount];
    private readonly Control[] _rankObjs = new Control[LevelCount];
    private readonly Label[] _rankValues = new Label[LevelCount];

    private int _curPage;
    private int _targetPage;
    private bool _buttonMove;     // 原作 isBttonMove
    private float _targetX;
    private bool _backClicked;    // 原作 backBtnClicked(1s 防抖)
    private double _backClickTime;
    private int _pendingLevel = -1;
    private Control? _aimHover;
    private Vector2I _shotRes = Vector2I.Zero;
    private bool _shotNoBeam; // 截图验证:隐藏激光以便对照无设备真值

    public override void _Ready()
    {
        Game.Instance.SceneState = Game.GameState.UI;
        AudioService.Instance.PlayMenuMusic();
        PlayerState.Instance.UpdateUiMode("LevelChoose");
        MessageBox.CloseCurrent();

        _subvp = GetNode<SubViewport>(VpPrefix.TrimEnd('/'));
        _camera = GetNode<Camera3D>(VpPrefix + "Camera3D");
        _gunAk = GetNode<Node3D>(VpPrefix + "Camera3D/AK47View");
        _gunM4 = GetNode<Node3D>(VpPrefix + "Camera3D/M4View");
        _muzzleAk = GetNode<MuzzleFlash>(VpPrefix + "Camera3D/AK47View/MuzzleFlash");
        _muzzleM4 = GetNode<MuzzleFlash>(VpPrefix + "Camera3D/M4View/MuzzleFlash");
        // 激光瞄准器(原作右红/左绿):枪口 → 画布平面;屏幕红点精确标示 2D 命中
        _laserAk = LaserSight.Create(LaserSight.RightRed, 0.0115f, withDot: false);
        _laserM4 = LaserSight.Create(LaserSight.LeftGreen, 0.0115f, withDot: false);
        _subvp.AddChild(_laserAk);
        _subvp.AddChild(_laserM4);
        _dotAk = UiAimDot.Create(this, LaserSight.RightRed);
        _dotM4 = UiAimDot.Create(this, LaserSight.LeftGreen);

        SyncViewportSize();
        GetViewport().SizeChanged += SyncViewportSize;
        BuildUi();
        Refresh();

        // 枪身材质(FBX 导入丢贴图,代码覆盖;只盖 Model 子树,勿波及 MuzzleFlash quad)
        OverrideGunMat(_gunM4, "res://assets/models/guns/m4/m4_tex.png", new Color(0.783019f, 0.783019f, 0.783019f));
        OverrideGunMat(_gunAk, "res://assets/models/guns/ak47/ak47_tex.png", new Color(0.8584906f, 0.8584906f, 0.8584906f));

        var router = InputRouter.Instance;
        router.TriggerRight += OnRightTrigger;
        router.TriggerLeft += OnLeftTrigger;
        router.MouseGun.Triggered += OnMouseTrigger;

        var args = OS.GetCmdlineUserArgs();
        _shotNoBeam = System.Array.IndexOf(args, "--shot-nobeam") >= 0;
        foreach (var a in args)
        {
            if (a.StartsWith("--shot-res:"))
            {
                var wh = a["--shot-res:".Length..].Split('x');
                if (wh.Length == 2)
                    _shotRes = new Vector2I(int.Parse(wh[0]), int.Parse(wh[1]));
            }
        }
        if (System.Array.IndexOf(args, "--levelchoose-selftest") >= 0)
            Callable.From(() => RunSelfTest()).CallDeferred();
        if (System.Array.IndexOf(args, "--e2e-mouse-flow") >= 0)
            Callable.From(() => RunE2eMouseCheck()).CallDeferred();
        foreach (var a in args)
        {
            if (a.StartsWith("--shot:"))
                Callable.From(() => TakeShot(a["--shot:".Length..])).CallDeferred();
            else if (a.StartsWith("--shot-aim:"))
                Callable.From(() => TakeShotAim(a["--shot-aim:".Length..])).CallDeferred();
            else if (a.StartsWith("--shot-p2:"))
                Callable.From(() => TakeShotP2(a["--shot-p2:".Length..])).CallDeferred();
            else if (a.StartsWith("--shot-diff:"))
                Callable.From(() => TakeShotDiff(a["--shot-diff:".Length..])).CallDeferred();
        }
    }

    public override void _ExitTree()
    {
        // C# 事件(非 Godot 信号)不会在节点释放时自动退订,必须手动退(见 MenuScreen 注)
        if (InputRouter.Instance == null)
            return;
        InputRouter.Instance.TriggerRight -= OnRightTrigger;
        InputRouter.Instance.TriggerLeft -= OnLeftTrigger;
        InputRouter.Instance.MouseGun.Triggered -= OnMouseTrigger;
    }

    private static void OverrideGunMat(Node3D gun, string texPath, Color tint)
    {
        var mat = new StandardMaterial3D
        {
            AlbedoColor = tint,
            AlbedoTexture = GD.Load<Texture2D>(texPath),
            Roughness = 0.85f,
        };
        foreach (var mi in gun.GetNode("Model").FindChildren("*", "MeshInstance3D", true, false))
            ((MeshInstance3D)mi).MaterialOverride = mat;
    }

    // ---------------------------------------------------------------- UI 构建

    private void BuildUi()
    {
        var uiRoot = UiKit.MakeRoot(GetNode("BGLayer"));
        _buttonRoot = UiKit.MakeRoot(GetNode("UILayer"));

        // 背景 background6(Simple,无 border):全屏等比覆盖
        var bgFill = UiKit.TexRect("BGFill", UiKit.TexDir + "background6.png");
        bgFill.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bgFill.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
        uiRoot.AddChild(bgFill);

        // 返回:anchor(1,1) 300×80 @(-150,-40),Sliced Btn_button03(border 20),"返回" 60 号
        _backBtn = UiSwapButton.Create("BackBtn",
            UiKit.TexDir + "Btn_button03_n.png", UiKit.TexDir + "Btn_button03_h.png", 20, 20, 20, 20);
        UiKit.PlaceAnchored(_backBtn, 1, 1, 0.5f, 0.5f, -150, -40, 300, 80);
        var backLabel = UiKit.MakeLabel("返回", 60, Colors.White);
        backLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backBtn.AddChild(backLabel);
        _backBtn.Pressed += OnBack;
        _backBtn.Pressed += AudioService.Instance.PlayUiSound;
        _buttonRoot.AddChild(_backBtn);

        // LevelGroup:anchor(0,0.5) pivot(0,0.5) @(150,-0.03),翻页改 OffsetLeft
        _levelGroup = new Control { Name = "LevelGroup", MouseFilter = Control.MouseFilterEnum.Ignore };
        UiKit.PlaceAnchored(_levelGroup, 0, 0.5f, 0, 0.5f, GroupHomeX, -0.03f, 100, 100);
        _buttonRoot.AddChild(_levelGroup);
        for (int i = 0; i < LevelCount; i++)
            BuildLevelButton(i);

        // 翻页箭头:Simple 贴图 120×100,左 anchor(0,0.5) @(0,-20.2) / 右 anchor(1,0.5)
        _leftBtn = UiSwapButton.Create("LeftBtn", LcDir + "Btn_GoBack1_n.png", LcDir + "Btn_GoBack1_h.png");
        UiKit.PlaceAnchored(_leftBtn, 0, 0.5f, 0, 0.5f, 0, -20.2f, 120, 100);
        _leftBtn.Pressed += () => TurnPage(-1);
        _buttonRoot.AddChild(_leftBtn);
        _rightBtn = UiSwapButton.Create("RightBtn", LcDir + "Btn_Right_n.png", LcDir + "Btn_Right1_h.png");
        UiKit.PlaceAnchored(_rightBtn, 1, 0.5f, 1, 0.5f, 0, -20.2f, 120, 100);
        _rightBtn.Pressed += () => TurnPage(1);
        _buttonRoot.AddChild(_rightBtn);

        BuildDiffPanel();

        // 焦点导航(原作 BackBtn/箭头 Navigation=None,难度键 Automatic;此处为键盘回落自洽)
        _backBtn.FocusNeighborBottom = _backBtn.GetPathTo(_levelButtons[0]);
        _leftBtn.FocusNeighborRight = _leftBtn.GetPathTo(_levelButtons[0]);
        _rightBtn.FocusNeighborLeft = _rightBtn.GetPathTo(_levelButtons[2]);
    }

    private void BuildLevelButton(int i)
    {
        int page = i / 3, slot = i % 3; // 页内 3 个间距 340,页宽 1280(原作布局规律)
        float x = page * PageWidth + slot * 340.0f;
        // 关卡按钮原作无 Button 组件(仅 tag+BoxCollider+Image):无 hover 贴图,normal=hover 同图
        var b = UiSwapButton.Create($"Level{i + 1}", LcDir + "bg_map.png", LcDir + "bg_map.png", 22, 25, 22, 21);
        UiKit.PlaceAnchored(b, 0, 0.5f, 0, 0.5f, x, 0, 300, 300);
        b.Visible = i < 4; // Level5~13 原作默认 inactive
        int idx = i;
        b.Pressed += () =>
        {
            AudioService.Instance.PlayUiSound(); // 原作无 Button → GetGunSound()(映射同一播放器)
            SelectLevel(idx);
        };
        _levelGroup.AddChild(b);
        _levelButtons[i] = b;

        // LevelText:"第N关" 48 号 @(0,167),宽 120(1~5 关)/140(6~13 关)
        var lt = UiKit.MakeLabel($"第{i + 1}关", 48, Colors.White);
        lt.Name = "LevelText";
        UiKit.Place(lt, 0, 167, i < 5 ? 120 : 140, 60);
        b.AddChild(lt);
        // img:缩略图 260×260 @(0,0),灰 0.8018868
        var img = UiKit.TexRect("img", LcDir + Thumbs[i]);
        img.Modulate = new Color(0.8018868f, 0.8018868f, 0.8018868f);
        UiKit.Place(img, 0, 0, 260, 260);
        b.AddChild(img);
        // 星 ×3:Icon_star0(灰) 60×60 @(-90/-3.9/75, -121),默认隐藏,Refresh 点亮
        _stars[i] = new Control[3];
        float[] starX = { -90.0f, -3.899994f, 75.0f };
        for (int s = 0; s < 3; s++)
        {
            var star = UiKit.TexRect($"Star{s + 1}", LcDir + "Icon_star0.png");
            star.Modulate = new Color(0.8018868f, 0.8018868f, 0.8018868f);
            UiKit.Place(star, starX[s], -121, 60, 60);
            star.Visible = false;
            b.AddChild(star);
            _stars[i][s] = star;
        }
        // Lock:80×80 @(100,110) 灰,Level1 默认不激活,Refresh 按星数熄
        var lockIcon = UiKit.TexRect("Lock", LcDir + "Lock.png");
        lockIcon.Modulate = new Color(0.8018868f, 0.8018868f, 0.8018868f);
        UiKit.Place(lockIcon, 100.000015f, 110, 80, 80);
        lockIcon.Visible = i > 0;
        b.AddChild(lockIcon);
        _locks[i] = lockIcon;
        // ScoreText:200×60 @(50,-181),值 40 号内置 Arial(→默认字体) 绿;子标签"得分："btt 白 @(-108,0)
        (_scoreObjs[i], _scoreValues[i]) = BuildStatLine(b, "ScoreText", 50, -181f, -108,
            "得分：", new Color(0.3018868f, 1, 0.3165904f));
        // RankText:200×60 @(50,-225.1),橙;子标签"排名：" @(-107,0)
        (_rankObjs[i], _rankValues[i]) = BuildStatLine(b, "RankText", 50, -225.1f, -107,
            "排名：", new Color(1, 0.6577192f, 0.30196083f));
    }

    private static (Control, Label) BuildStatLine(Control parent, string name, float x, float yUp,
        float labelX, string labelText, Color valueColor)
    {
        var obj = new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        UiKit.Place(obj, x, yUp, 200, 60);
        parent.AddChild(obj);
        var value = UiKit.MakeLabel("", 40, valueColor, useBtt: false);
        value.Name = "Value";
        value.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        obj.AddChild(value);
        var label = UiKit.MakeLabel(labelText, 40, Colors.White);
        label.Name = "Text";
        UiKit.Place(label, labelX, 0, 120, 60);
        obj.AddChild(label);
        return (obj, value);
    }

    private void BuildDiffPanel()
    {
        // DifficuleChoosePanel:700×600 居中,messagebox.png Sliced(65) α0.8745,默认隐藏
        _diffPanel = new Control
        {
            Name = "DifficuleChoosePanel",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        UiKit.Place(_diffPanel, 0, 0, 700, 600);
        _buttonRoot.AddChild(_diffPanel);
        var bg = new NinePatchRect
        {
            Name = "BG",
            Texture = GD.Load<Texture2D>(UiKit.TexDir + "messagebox.png"),
            PatchMarginLeft = 65,
            PatchMarginTop = 65,
            PatchMarginRight = 65,
            PatchMarginBottom = 65,
            Modulate = new Color(1, 1, 1, 0.8745098f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _diffPanel.AddChild(bg);

        // Title 空容器 @(0,-8.4) 100×100,装饰链保持原作父子结构
        var titleC = new Control { Name = "Title", MouseFilter = Control.MouseFilterEnum.Ignore };
        UiKit.Place(titleC, 0, -8.4f, 100, 100);
        _diffPanel.AddChild(titleC);
        var title = UiKit.MakeLabel("选择难度", 80, Colors.White);
        title.Name = "Text";
        UiKit.Place(title, 0.000061035156f, 239.5999f, 160, 30);
        titleC.AddChild(title);
        var leftImg = UiKit.TexRect("LeftImage", LcDir + "TitleLine_Left.png");
        UiKit.Place(leftImg, -300, 239.60007f, 300, 80);
        titleC.AddChild(leftImg);
        var rightImg = UiKit.TexRect("RightImg", LcDir + "TitleLine_Left.png");
        UiKit.Place(rightImg, 299.3f, 239.6f, 300, 80);
        rightImg.Scale = new Vector2(-1, 1); // 原作绕 Y 转 180°(同一贴图镜像)
        titleC.AddChild(rightImg);

        // 难度三键:500×110 Sliced Btn_button03,文字 70 号 (0.914,1,0.821) @(45,0) 400×100
        BuildDiffButton("EasyBtn", "简单", 100.9f, "Icon_Happy.png", new Color(0.748857f, 1, 0), -111.15f, 100, 0);
        BuildDiffButton("HardBtn", "困难", -42.2f, "Icon_Cry.png", new Color(1, 0.8185371f, 0), -109.3f, 100, 1);
        BuildDiffButton("HellBtn", "地狱", -195.7f, "kulou.png", new Color(1, 0.24073383f, 0), -114.42f, 82.3f, 2);
    }

    private void BuildDiffButton(string name, string text, float yUp, string icon, Color iconTint,
        float iconX, float iconH, int diff)
    {
        var b = UiSwapButton.Create(name,
            UiKit.TexDir + "Btn_button03_n.png", UiKit.TexDir + "Btn_button03_h.png", 20, 20, 20, 20);
        UiKit.Place(b, 6, yUp, 500, 110);
        var label = UiKit.MakeLabel(text, 70, new Color(0.91390604f, 1, 0.8207547f));
        UiKit.Place(label, 45, 0, 400, 100);
        b.AddChild(label);
        var ic = UiKit.TexRect("Image", LcDir + icon);
        ic.Modulate = iconTint;
        UiKit.Place(ic, iconX, 0, 100, iconH);
        b.AddChild(ic);
        b.Pressed += () => ChooseDifficulty(diff);
        b.Pressed += AudioService.Instance.PlayUiSound;
        _diffPanel.AddChild(b);
    }

    // ---------------------------------------------------------------- 数据刷新(LevelState.cs)

    /// <summary>星/锁/得分/排名按存档刷新:星≥1 熄锁亮星1,≥2 亮星2,≥3 亮星3;得分/排名 >0 才显示</summary>
    public void Refresh()
    {
        for (int i = 0; i < LevelCount; i++)
        {
            var st = SaveService.Instance.LevelState[i];
            int star = SaveService.GetInt(st, "star", 0);
            int score = SaveService.GetInt(st, "score", 0);
            int rank = SaveService.GetInt(st, "rank", 0);
            _locks[i].Visible = i > 0 && star < 1; // Level1 的 Lock 原作默认 inactive,永不显示
            for (int s = 0; s < 3; s++)
                _stars[i][s].Visible = star >= s + 1;
            _scoreObjs[i].Visible = score > 0;
            if (score > 0)
                _scoreValues[i].Text = score.ToString();
            _rankObjs[i].Visible = rank > 0;
            if (rank > 0)
                _rankValues[i].Text = rank.ToString();
        }
    }

    // ---------------------------------------------------------------- 交互(LevelChoose.cs)

    /// <summary>选关:币不足弹"游戏币"框(并隐藏关卡组)→ 查前置关星数 → 扣 1 币,0.5s 后难度面板</summary>
    public void SelectLevel(int i)
    {
        if (SaveService.Instance.Coin <= 0)
        {
            // 原作 cancelText="确定"(左)/confirmText="取消"(右),参数错位保真
            MessageBox.ShowBox(this, "游戏币", "没有足够的游戏币了，请等候游戏币增加或购买蓝牙枪",
                "取消", "确定", _ => _levelGroup.Visible = true);
            _levelGroup.Visible = false;
            return;
        }
        if (i > 0 && SaveService.GetInt(SaveService.Instance.LevelState[i - 1], "star", 0) <= 0)
        {
            MessageBox.ShowBox(this, "关卡解锁", "需要通过上一关，本关才能解锁哟", "取消", "确定", null);
            return;
        }
        if (i == 0 && _buttonMove) // 原作仅 Level1 查 isBttonMove,保真
            return;
        ChooseDifficult(i);
    }

    private void ChooseDifficult(int level)
    {
        _pendingLevel = level;
        SaveService.Instance.SpendCoin(1); // 原作走网络 ReduceCoinFps,移植版本地扣
        _levelGroup.Visible = false;
        GetTree().CreateTimer(0.5).Timeout += () =>
        {
            if (_pendingLevel == level)
                ShowDiffPanel(true);
        };
    }

    private void ShowDiffPanel(bool v)
    {
        _diffPanel.Visible = v;
        _levelGroup.Visible = !v;
    }

    public void ChooseDifficulty(int d)
    {
        Game.Instance.CurrentDifficulty = d switch
        {
            0 => Game.Difficulty.Easy,
            1 => Game.Difficulty.Hard,
            _ => Game.Difficulty.Hell,
        };
        ShowDiffPanel(false);
        if (!SceneMap.TryGetValue(_pendingLevel, out var scene))
        {
            // 战斗关卡未移植(已知):提示后回到关卡组
            MessageBox.ShowBox(this, "敬请期待", $"Level {_pendingLevel + 1} 正在建设中。", "确定", "返回",
                _ => _levelGroup.Visible = true);
            return;
        }
        Game.Instance.NextScenePath = scene;
        Game.Instance.ChangeScene(LoadingScene);
    }

    /// <summary>翻页:targetPos = 150 + page×(-1280),_Process 里 6000px/s 滑到位</summary>
    public void TurnPage(int d)
    {
        if (!_levelGroup.Visible) // 原作 LevelBtnGroup.activeSelf 门控
            return;
        int t = _curPage + d;
        if (t < 0 || t > 1)
            return;
        _buttonMove = true;
        _targetPage = t;
        _targetX = GroupHomeX + t * -PageWidth;
    }

    /// <summary>返回:难度面板开着先关面板(1s 防抖),否则回菜单(原作 BackBtn 分支)</summary>
    public void OnBack()
    {
        if (_backClicked)
            return;
        if (_diffPanel.Visible)
        {
            ShowDiffPanel(false);
            _backClicked = true;
            _backClickTime = Time.GetTicksMsec() / 1000.0;
        }
        else
            Game.Instance.ChangeScene(MenuScene);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } k && k.Keycode == Key.Escape)
            Game.Instance.ChangeScene(MenuScene); // 原作 Esc 无条件回菜单
    }

    // ---------------------------------------------------------------- 瞄准 + 扳机(双枪,照 UIController.cs)

    private void SyncViewportSize()
    {
        var size = (Vector2I)GetViewport().GetVisibleRect().Size;
        if (size.X <= 0 || size.Y <= 0)
            return; // headless 首帧可视区为 0,保持场景默认 1280×720
        _subvp.Size = size;
    }

    private static bool RingConnected() =>
        InputRouter.Instance.RingConnected || InputRouter.Instance.RingIp != "";

    /// <summary>右枪(AK47)激活 = 戒指连接(含键盘回落)或鼠标模拟光枪;左枪(M4)= 腿部连接</summary>
    private bool RightGunActive() =>
        RingConnected() || InputRouter.Instance.MouseGun.IsActiveForRight(InputRouter.Instance);

    public override void _Process(double delta)
    {
        // 翻页滑动(原作 Update:每帧 ±100 直到 targetPos)
        if (_buttonMove && _levelGroup.Visible)
        {
            float x = _levelGroup.OffsetLeft;
            float dir = Mathf.Sign(_targetX - x);
            x += dir * PageSpeed * (float)delta;
            if ((dir > 0 && x >= _targetX) || (dir < 0 && x <= _targetX))
            {
                x = _targetX;
                _curPage = _targetPage;
                _buttonMove = false;
            }
            _levelGroup.OffsetLeft = x;
            _levelGroup.OffsetRight = x + 100.0f;
        }
        // 返回防抖 1s(原作 Update)
        if (_backClicked && Time.GetTicksMsec() / 1000.0 - _backClickTime > 1.0)
            _backClicked = false;

        var router = InputRouter.Instance;
        // 右枪 AK47
        bool rActive = RightGunActive() && !_shotNoBeam;
        if (rActive)
        {
            var aim = router.GetRightAim();
            Vector3 dir;
            Vector2 logical;
            if (aim.IsScreenPoint)
            {
                // 鼠标模拟光枪:枪口指向鼠标射线方向
                dir = _camera.ProjectRayNormal(aim.ScreenPos);
                var localDir = (_camera.GlobalTransform.Basis.Inverse() * dir).Normalized();
                _gunAk.Quaternion = new Quaternion(Vector3.Forward, localDir);
                logical = UiKit.WindowToLogical(GetViewport(), aim.ScreenPos);
                // 瞄准悬停 = 焦点视觉(MessageBox 按钮由原生 hover 承担)
                var b = ButtonAtLogicalPoint(logical, skipBoxButtons: true);
                if (b != _aimHover)
                {
                    _aimHover = b;
                    _aimHover?.GrabFocus();
                }
            }
            else
            {
                _gunAk.Quaternion = aim.Rotation;
                dir = _camera.GlobalBasis * (aim.Rotation * Vector3.Forward);
                logical = RotationAimLogicalPoint(left: false);
                _aimHover = null;
            }
            var mz = _muzzleAk.GlobalPosition;
            _laserAk.SetBeam(mz, LaserSight.PlanePoint(mz, dir, CanvasZ));
            _dotAk.SetPoint(logical, ButtonAtLogicalPoint(logical, skipBoxButtons: true) != null);
        }
        else
        {
            if (_gunAk.Quaternion != Quaternion.Identity)
                _gunAk.Quaternion = Quaternion.Identity;
            _aimHover = null;
            _laserAk.HideBeam();
            _dotAk.HideDot();
        }
        // 左枪 M4(仅手机腿部四元数)
        bool lActive = router.LegConnected && !_shotNoBeam;
        if (lActive)
        {
            var rot = GunMath.PhoneToGunRotation(router.RawLegRotation);
            _gunM4.Quaternion = rot;
            var dir = _camera.GlobalBasis * (rot * Vector3.Forward);
            var mz = _muzzleM4.GlobalPosition;
            _laserM4.SetBeam(mz, LaserSight.PlanePoint(mz, dir, CanvasZ));
            var logical = RotationAimLogicalPoint(left: true);
            _dotM4.SetPoint(logical, ButtonAtLogicalPoint(logical, skipBoxButtons: true) != null);
        }
        else
        {
            _gunM4.Quaternion = Quaternion.Identity;
            _laserM4.HideBeam();
            _dotM4.HideDot();
        }
    }

    /// <summary>右路扳机(戒指/键盘回落):枪口旋转路径,命中按钮 → AK47 火光 + 按下</summary>
    private void OnRightTrigger()
    {
        if (!RingConnected())
            return;
        var b = ButtonAtLogicalPoint(RotationAimLogicalPoint(left: false), skipBoxButtons: false);
        if (b != null)
        {
            _muzzleAk.Fire();
            b.EmitSignal(BaseButton.SignalName.Pressed);
        }
    }

    private void OnLeftTrigger()
    {
        if (!InputRouter.Instance.LegConnected)
            return;
        var b = ButtonAtLogicalPoint(RotationAimLogicalPoint(left: true), skipBoxButtons: false);
        if (b != null)
        {
            _muzzleM4.Fire();
            b.EmitSignal(BaseButton.SignalName.Pressed);
        }
    }

    /// <summary>鼠标扳机(左键):屏幕点路径(MessageBox 按钮由原生点击承担,跳过防双重触发)</summary>
    private void OnMouseTrigger()
    {
        var router = InputRouter.Instance;
        if (!router.MouseGun.IsActiveForRight(router))
            return;
        var b = ButtonAtLogicalPoint(
            UiKit.WindowToLogical(GetViewport(), router.MouseGun.AimPos), skipBoxButtons: true);
        if (b != null)
        {
            _muzzleAk.Fire(); // 鼠标 = 右玩家 → AK47(原作 _Gun1)
            b.EmitSignal(BaseButton.SignalName.Pressed);
        }
    }

    /// <summary>枪口旋转 → SubViewport 像素 → 画布逻辑坐标(left=true 用腿部四元数)</summary>
    private Vector2 RotationAimLogicalPoint(bool left)
    {
        var raw = left ? InputRouter.Instance.RawLegRotation : InputRouter.Instance.RawRingRotation;
        var dir = _camera.GlobalBasis * (GunMath.PhoneToGunRotation(raw) * Vector3.Forward);
        var point = _camera.UnprojectPosition(_camera.GlobalPosition + dir * 100.0f);
        return UiKit.WindowToLogical(GetViewport(), point);
    }

    /// <summary>逻辑坐标命中的按钮;MessageBox 打开时只认 MessageButton*(照原作);
    /// skipBoxButtons(鼠标路径)时再跳过 MessageButton*,避免与原生点击双重触发</summary>
    private Control? ButtonAtLogicalPoint(Vector2 logical, bool skipBoxButtons)
    {
        bool boxOpen = MessageBox.IsOpen();
        foreach (var n in GetTree().GetNodesInGroup(MessageBox.Group))
        {
            if (n is not Control b || !b.IsVisibleInTree() || !b.GetGlobalRect().HasPoint(logical))
                continue;
            bool isBoxButton = ((string)b.Name).StartsWith("MessageButton");
            if (boxOpen && !isBoxButton)
                continue;
            if (skipBoxButtons && isBoxButton)
                continue;
            return b;
        }
        return null;
    }

    // ---------------------------------------------------------------- 截图验证

    private void ApplyShotWindow()
    {
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        if (_shotRes != Vector2I.Zero)
            DisplayServer.WindowSetSize(_shotRes);
        // 鼠标光枪默认 (0,0) 会让枪口指向左上角;截图统一把瞄准点放到窗口中心(= 原作待机朝向)
        InputRouter.Instance.MouseGun.SimulateMove(
            _shotRes != Vector2I.Zero ? (Vector2)(_shotRes / 2) : new Vector2(640, 360));
    }

    private async void SaveShot(string path, int waitFrames)
    {
        for (int i = 0; i < waitFrames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(Engine.GetSingleton("RenderingServer"), "frame_post_draw");
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(path);
        GD.Print("SHOT SAVED: ", path);
        GetTree().Quit();
    }

    /// <summary>--shot:&lt;png&gt;:默认页截图(约 30 帧后)</summary>
    private void TakeShot(string path)
    {
        ApplyShotWindow();
        SaveShot(path, 30);
    }

    /// <summary>--shot-aim:&lt;png&gt;:瞄准第 1 关按钮截图(红色激光束+红点悬停放大态)</summary>
    private void TakeShotAim(string path)
    {
        ApplyShotWindow();
        InputRouter.Instance.MouseGun.SimulateMove(_levelButtons[0].GetGlobalRect().GetCenter());
        SaveShot(path, 30);
    }

    /// <summary>--shot-p2:&lt;png&gt;:翻到第 2 页后截图(对照真值 unity_level_p2)</summary>
    private void TakeShotP2(string path)
    {
        ApplyShotWindow();
        TurnPage(1);
        SaveShot(path, 30);
    }

    /// <summary>--shot-diff:&lt;png&gt;:打开难度面板后截图(对照真值 unity_level_diff)</summary>
    private void TakeShotDiff(string path)
    {
        ApplyShotWindow();
        ShowDiffPanel(true);
        SaveShot(path, 30);
    }

    // ---------------------------------------------------------------- 自检(--levelchoose-selftest)

    /// <summary>端到端复现(--e2e-mouse-flow,由 MenuScreen 跳入):Mouse 模式下
    /// 真实窗口坐标瞄准+点击第 1 关,断言悬停/扣币/火光/难度面板。</summary>
    private async void RunE2eMouseCheck()
    {
        int fails = 0;
        void Check(bool ok, string what)
        {
            GD.Print((ok ? "PASS " : "FAIL ") + what);
            if (!ok) fails += 1;
        }
        for (int i = 0; i < 10; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        SaveService.Instance.Coin = 10; // 内存改币,避免弹"游戏币"框(不落盘)
        var vp = GetViewport();
        GD.Print($"PROBE VisibleRect={vp.GetVisibleRect().Size} canvas={vp.GetCanvasTransform()} " +
            $"final={vp.GetFinalTransform()} subvp={_subvp.Size} win={DisplayServer.WindowGetSize()}");
        var router = InputRouter.Instance;
        Check(router.Mode == InputRouter.InputMode.Mouse, "模式=Mouse");
        Check(router.MouseGun.IsActiveForRight(router), "鼠标光枪激活");
        // 真实窗口坐标瞄准第 1 关中心(与用户物理鼠标同一事件管线)
        var lvl1Canvas = _levelButtons[0].GetGlobalRect().GetCenter();
        var winPos = GetViewport().GetFinalTransform() *
            (GetViewport().GetCanvasTransform() * lvl1Canvas);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = winPos });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        // Godot 在投递输入事件前已把窗口像素坐标转成画布逻辑坐标(stretch 逆变换)
        Check(router.MouseGun.AimPos.DistanceTo(lvl1Canvas) < 1.0f,
            $"AimPos 更新 ({router.MouseGun.AimPos} vs {lvl1Canvas})");
        Check(GetViewport().GuiGetFocusOwner() == _levelButtons[0], "鼠标瞄准悬停=焦点第1关");
        int coinBefore = SaveService.Instance.Coin;
        Input.ParseInputEvent(new InputEventMouseButton
            { ButtonIndex = MouseButton.Left, Pressed = true, Position = winPos });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(SaveService.Instance.Coin == coinBefore - 1, "点击第1关→扣1币");
        Check(_muzzleAk.Visible, "AK47 火光播放");
        await ToSignal(GetTree().CreateTimer(0.7), SceneTreeTimer.SignalName.Timeout);
        Check(_diffPanel.Visible, "难度面板显示");
        GD.Print($"E2E MOUSE FLOW {(fails == 0 ? "PASS" : "FAIL")} (fails={fails})");
        (Engine.GetMainLoop() as SceneTree)!.Quit(fails > 0 ? 1 : 0);
    }

    private async void RunSelfTest()
    {
        int fails = 0;
        void Check(bool ok, string what)
        {
            GD.Print((ok ? "PASS " : "FAIL ") + what);
            if (!ok) fails += 1;
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // 确定性:内存里强制原作默认存档(3 星/3 分/排名 1),不落盘
        SaveService.Instance.Coin = 10;
        for (int i = 0; i < SaveService.LevelCount; i++)
            SaveService.Instance.LevelState[i] = new Godot.Collections.Dictionary
                { ["star"] = 3, ["score"] = 3, ["rank"] = 1 };
        Refresh();

        Check(_levelGroup.Visible, "关卡组默认可见");
        Check(Mathf.IsEqualApprox(_levelGroup.OffsetLeft, GroupHomeX), "关卡组初始 x=150");
        Check(_levelButtons.Length == LevelCount, "13 个关卡按钮");
        bool visOk = true;
        for (int i = 0; i < LevelCount; i++)
            visOk &= _levelButtons[i].Visible == (i < 4);
        Check(visOk, "Level1~4 可见、Level5~13 隐藏(原作 inactive)");
        Check(!_locks[0].Visible && _stars[0][0].Visible && _stars[0][2].Visible, "L1 熄锁亮 3 星");
        Check(_scoreObjs[0].Visible && _scoreValues[0].Text == "3", "L1 得分 3 可见");
        Check(_rankObjs[0].Visible && _rankValues[0].Text == "1", "L1 排名 1 可见");
        Check(!_diffPanel.Visible, "难度面板默认隐藏");
        Check(PlayerState.Instance.CoinObj.Visible, "选关模式金币 HUD 可见");

        // 翻页:右键 → x 滑到 -1130,curPage=1;再按不动;左键滑回
        TurnPage(1);
        Check(_buttonMove, "翻页开始");
        double t0 = Time.GetTicksMsec() / 1000.0;
        while (_buttonMove && Time.GetTicksMsec() / 1000.0 - t0 < 2.0)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(!_buttonMove && _curPage == 1, "翻到第 2 页");
        Check(Mathf.IsEqualApprox(_levelGroup.OffsetLeft, GroupHomeX - PageWidth), "第 2 页 x=-1130");
        TurnPage(1);
        Check(!_buttonMove && _curPage == 1, "第 2 页右翻无效");
        TurnPage(-1);
        t0 = Time.GetTicksMsec() / 1000.0;
        while (_buttonMove && Time.GetTicksMsec() / 1000.0 - t0 < 2.0)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(_curPage == 0 && Mathf.IsEqualApprox(_levelGroup.OffsetLeft, GroupHomeX), "左翻回第 1 页");

        // 鼠标瞄准悬停 Level1 → 焦点;扳机 → AK47 火光 + 扣币 + 0.5s 后难度面板
        var mg = InputRouter.Instance.MouseGun;
        var lvl1Canvas = _levelButtons[0].GetGlobalRect().GetCenter();
        if (DisplayServer.WindowGetSize().X > 0)
        {
            var winPos = GetViewport().GetFinalTransform() *
                (GetViewport().GetCanvasTransform() * lvl1Canvas);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = winPos });
        }
        else
            mg.SimulateMove(lvl1Canvas);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(GetViewport().GuiGetFocusOwner() == _levelButtons[0], "鼠标瞄准悬停=焦点第1关");
        int coinBefore = SaveService.Instance.Coin;
        if (DisplayServer.WindowGetSize().X > 0)
        {
            var winPos = GetViewport().GetFinalTransform() *
                (GetViewport().GetCanvasTransform() * lvl1Canvas);
            Input.ParseInputEvent(new InputEventMouseButton
                { ButtonIndex = MouseButton.Left, Pressed = true, Position = winPos });
        }
        else
            mg.SimulateTrigger();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(_muzzleAk.Visible, "扳机命中第1关→AK47 枪口火光播放");
        Check(!_muzzleM4.Visible, "M4 火光不动");
        Check(SaveService.Instance.Coin == coinBefore - 1, "选关扣 1 币");
        Check(!_levelGroup.Visible, "难度流程关卡组隐藏");
        await ToSignal(GetTree().CreateTimer(0.7), SceneTreeTimer.SignalName.Timeout);
        Check(_diffPanel.Visible, "0.5s 后难度面板显示");

        // 面板开着按返回 → 关面板回关卡组(不跳场景)
        OnBack();
        Check(!_diffPanel.Visible && _levelGroup.Visible, "面板开着返回→关面板");

        // 未解锁弹框(内存改 L1 星=0 拦选择;L2 星=0 显示锁——锁看本关星数,照 LevelState.cs)
        SaveService.Instance.LevelState[0] = new Godot.Collections.Dictionary
            { ["star"] = 0, ["score"] = 0, ["rank"] = 0 };
        SaveService.Instance.LevelState[1] = new Godot.Collections.Dictionary
            { ["star"] = 0, ["score"] = 0, ["rank"] = 0 };
        Refresh();
        Check(_locks[1].Visible && !_stars[1][0].Visible, "L2 上锁熄星");
        SelectLevel(1);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(MessageBox.IsOpen(), "未解锁弹框打开");
        if (MessageBox.IsOpen())
        {
            Check(MessageBox.Current!.TitleLabel.Text == "关卡解锁", "未解锁弹框标题");
            Check(MessageBox.Current.ContentLabel.Text == "需要通过上一关，本关才能解锁哟", "未解锁弹框正文");
            Check(MessageBox.Current.OkButton.GetChild<Label>(0).Text == "取消", "未解锁右键=取消(原作错位)");
            MessageBox.Current.PressCancel();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        // 币不足弹框(内存改币=0)
        SaveService.Instance.Coin = 0;
        SelectLevel(0);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(MessageBox.IsOpen(), "币不足弹框打开");
        Check(!_levelGroup.Visible, "币不足时关卡组隐藏");
        if (MessageBox.IsOpen())
        {
            Check(MessageBox.Current!.TitleLabel.Text == "游戏币", "币不足弹框标题");
            Check(MessageBox.Current.ContentLabel.Text == "没有足够的游戏币了，请等候游戏币增加或购买蓝牙枪", "币不足弹框正文");
            MessageBox.Current.PressOk();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(_levelGroup.Visible, "币不足弹框关闭后关卡组恢复");
        }
        SaveService.Instance.Coin = 10;

        // 难度选择 → LoadGame(最后测:触发 ChangeScene)
        _pendingLevel = 0;
        ChooseDifficulty(0);
        Check(Game.Instance.CurrentDifficulty == Game.Difficulty.Easy, "简单难度设置");
        Check(Game.Instance.NextScenePath == SceneMap[0], "Loading 目标=level1");

        GD.Print($"LEVELCHOOSE SELFTEST {(fails == 0 ? "PASS" : "FAIL")} (fails={fails})");
        (Engine.GetMainLoop() as SceneTree)!.Quit(fails > 0 ? 1 : 0);
    }
}
