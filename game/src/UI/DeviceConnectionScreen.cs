using Godot;

namespace FPSGame;

/// <summary>
/// 连接手机:1:1 移植 Unity Assets/UI/DeviceConnection.unity + DeviceConnection.cs。
/// 3D(SubViewport 透明;原作 WorldSpace Canvas 在相机前 621.6、FOV60 ≈ 1280×720 铺满):
///   相机 FOV60、平行光(euler 50,-30,0,色 (1,0.957,0.839));相机下挂双枪——
///   右 AK47(0.2,-0.0835,0.18,戒指/遥控器/鼠标路,激光红)、左 M4(-0.2,-0.0835,0.18,手机腿部路,激光绿),
///   枪口闪光 z=-0.162(原作 Sphere 节点值;原作闪光父链默认 inactive 永不显示,移植版修正为命中时播放,同菜单/选关)。
/// UI(逻辑分辨率 1280×720,Y 向上中心原点,照原作 RectTransform):
///   背景 background6 全屏等比覆盖;
///   说明文字底衬 650×500 @(-227,110) 黑 α0.3804,内文 btt 40 白 左上对齐 行距 1.1,4 行说明;
///   二维码IOS 组 380×380 @(424,103.3):bg_map 九宫格(22/25/22/21)、appdown.png 355×355 灰 0.802、
///     标题"手机控制器" btt 68 黄绿 (0.8586,0.9151,0.1640) @(0,220.5);
///   BackMenuBtn 360×100 @(0,-279) Btn_button03 九宫格(20) SpriteSwap,"返回" btt 60;
///   二维码Android 组 300×300 @(-490,77) 默认隐藏(原作 Image 组件禁用→无底图,只有二维码+"安卓"绿字;
///     原作没有任何代码切换它,照抄隐藏)。
/// 交互照原作:方向键 → 选中返回键;枪/鼠标扳机命中返回键 → 火光 + 回 Menu 场景;
///   进场 UpdateUIMode 隐藏金币 HUD,Menu 输入模式持续广播(等手机 UDP 8281/8282 接入)。
/// 有意未移植(原作默认 inactive 且无任何代码启用,属遗留):MovieImage/Movie ×3 的 startmov.mp4
///   视频播放(资产已转 startmov.ogv 备用)、GameObject 英语教学小游戏(AppleController)。
///   原作 BackMenuBtn onClick 第二绑定指向未实例化的 Utils.prefab(实际不响),移植版按框架惯例播 UI 音效。
/// </summary>
public partial class DeviceConnectionScreen : Node
{
    private const string MenuScene = "res://scenes/ui/menu.tscn";
    private const string DevDir = "res://assets/textures/ui/device/";
    private const string VpPrefix = "ViewportLayer/SubViewportContainer/SubViewport/";

    private SubViewport _subvp = null!;
    private Camera3D _camera = null!;
    private Node3D _gunAk = null!;   // 右路(戒指/遥控器/鼠标)
    private Node3D _gunM4 = null!;   // 左路(手机腿部)
    private MeshInstance3D _lazerAk = null!;
    private MeshInstance3D _lazerM4 = null!;
    private MuzzleFlash _muzzleAk = null!;
    private MuzzleFlash _muzzleM4 = null!;
    private Control _buttonRoot = null!;

    private Control _iosGroup = null!;
    private Control _androidGroup = null!;
    private UiSwapButton _backBtn = null!;
    private Label _instructionText = null!;

    private Control? _aimHover;
    private Vector2I _shotRes = Vector2I.Zero;
    private bool _shotNoBeam; // 截图验证:隐藏激光以便对照无设备真值

    public override void _Ready()
    {
        Game.Instance.SceneState = Game.GameState.UI;
        AudioService.Instance.PlayMenuMusic();
        PlayerState.Instance.UpdateUiMode("DeviceConnection"); // 原作 UpdateUIMode:本场景隐藏全部 HUD
        MessageBox.CloseCurrent();
        InputRouter.Instance.SetInputMode(InputRouter.InputMode.Menu); // Menu 模式持续广播(4.1)

        _subvp = GetNode<SubViewport>(VpPrefix.TrimEnd('/'));
        _camera = GetNode<Camera3D>(VpPrefix + "Camera3D");
        _gunAk = GetNode<Node3D>(VpPrefix + "Camera3D/AK47View");
        _gunM4 = GetNode<Node3D>(VpPrefix + "Camera3D/M4View");
        _lazerAk = GetNode<MeshInstance3D>(VpPrefix + "Camera3D/AK47View/Lazer");
        _lazerM4 = GetNode<MeshInstance3D>(VpPrefix + "Camera3D/M4View/Lazer");
        _muzzleAk = GetNode<MuzzleFlash>(VpPrefix + "Camera3D/AK47View/MuzzleFlash");
        _muzzleM4 = GetNode<MuzzleFlash>(VpPrefix + "Camera3D/M4View/MuzzleFlash");

        SyncViewportSize();
        GetViewport().SizeChanged += SyncViewportSize;
        BuildUi();

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
        if (System.Array.IndexOf(args, "--deviceconnection-selftest") >= 0)
            Callable.From(() => RunSelfTest()).CallDeferred();
        foreach (var a in args)
        {
            if (a.StartsWith("--shot:"))
                Callable.From(() => TakeShot(a["--shot:".Length..])).CallDeferred();
        }
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

        BuildInstruction();
        BuildIosQrGroup();
        BuildBackButton();
        BuildAndroidQrGroup();
    }

    /// <summary>说明文字:底衬 650×500 @(-227,110) 黑 α0.3804;内文 620×500 @(10.87,-55.09) btt 40 左上 行距1.1</summary>
    private void BuildInstruction()
    {
        var backing = new ColorRect
        {
            Name = "说明文字",
            Color = new Color(0, 0, 0, 0.38039216f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        UiKit.Place(backing, -227, 110, 650, 500);
        _buttonRoot.AddChild(backing);

        // 原作 Text 自动换行的实测效果(真值逐行扫描):第 1 行在 "App" 后折行,此处用显式 \n 固定同款折行点;
        // btt 在 Godot 基准行高 41,真值实测行距 51 → line_spacing 取 +10 对齐
        _instructionText = UiKit.MakeLabel(
            "1、手机扫码下载“蓝牙枪控制器”App\n，连接到和电视同一个WiFi\n" +
            "2、单人玩家选择右手持枪，双人玩需要两部手机分别持枪\n" +
            "3、双手握持手机对准电视机中心、然后点击瞄准电视，枪能跟随手机运动则表示连接成功.\n" +
            "4、可以购买蓝牙枪获得最佳体验", 40, Colors.White);
        _instructionText.Name = "Text";
        _instructionText.HorizontalAlignment = HorizontalAlignment.Left;
        _instructionText.VerticalAlignment = VerticalAlignment.Top;
        _instructionText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _instructionText.AddThemeConstantOverride("line_spacing", 10);
        UiKit.Place(_instructionText, 10.87f, -55.09f, 620, 500);
        backing.AddChild(_instructionText);
    }

    /// <summary>二维码IOS 组:bg_map 380×380 @(424,103.3) + appdown 355×355 灰 + "手机控制器" 68 黄绿</summary>
    private void BuildIosQrGroup()
    {
        _iosGroup = new Control { Name = "二维码IOS", MouseFilter = Control.MouseFilterEnum.Ignore };
        UiKit.Place(_iosGroup, 424, 103.3f, 380, 380);
        _buttonRoot.AddChild(_iosGroup);

        var bg = new NinePatchRect
        {
            Name = "BG",
            Texture = GD.Load<Texture2D>(UiKit.TexDir + "levelchoose/bg_map.png"),
            PatchMarginLeft = 22,
            PatchMarginTop = 25,
            PatchMarginRight = 22,
            PatchMarginBottom = 21,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _iosGroup.AddChild(bg);

        var qr = UiKit.TexRect("二维码", DevDir + "appdown.png");
        qr.Modulate = new Color(0.8018868f, 0.8018868f, 0.8018868f);
        UiKit.Place(qr, 0, 0, 355, 355);
        _iosGroup.AddChild(qr);

        var title = UiKit.MakeLabel("手机控制器", 68, new Color(0.858629f, 0.9150943f, 0.16402635f));
        title.Name = "Text";
        UiKit.Place(title, 0, 220.5f, 400, 120.388306f);
        _iosGroup.AddChild(title);
    }

    /// <summary>BackMenuBtn:360×100 @(0,-279) Btn_button03 九宫格 SpriteSwap,"返回" 60 号;原作 tag=Button+BoxCollider</summary>
    private void BuildBackButton()
    {
        _backBtn = UiSwapButton.Create("BackMenuBtn",
            UiKit.TexDir + "Btn_button03_n.png", UiKit.TexDir + "Btn_button03_h.png", 20, 20, 20, 20);
        UiKit.Place(_backBtn, 0, -279, 360, 100);
        var label = UiKit.MakeLabel("返回", 60, Colors.White);
        label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backBtn.AddChild(label);
        _backBtn.Pressed += OnBack;
        _backBtn.Pressed += AudioService.Instance.PlayUiSound;
        _buttonRoot.AddChild(_backBtn);
    }

    /// <summary>二维码Android 组:默认隐藏(原作 active=0 且无代码启用);Image 组件禁用 → 无底图</summary>
    private void BuildAndroidQrGroup()
    {
        _androidGroup = new Control
        {
            Name = "二维码Android",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        UiKit.Place(_androidGroup, -490, 77, 300, 300);
        _buttonRoot.AddChild(_androidGroup);

        var qr = UiKit.TexRect("二维码", DevDir + "android_down.png");
        qr.Modulate = new Color(0.8018868f, 0.8018868f, 0.8018868f);
        UiKit.Place(qr, 0, 0, 260, 260);
        _androidGroup.AddChild(qr);

        var label = UiKit.MakeLabel("安卓", 48, new Color(0.32529372f, 0.8018868f, 0.34702018f));
        label.Name = "Text";
        UiKit.Place(label, 0, 179.36f, 246.08191f, 120.388306f);
        _androidGroup.AddChild(label);
    }

    // ---------------------------------------------------------------- 交互(DeviceConnection.cs)

    /// <summary>返回菜单(原作 BackToMenu:SceneState=UI + LoadScene("Menu"))</summary>
    public void OnBack()
    {
        Game.Instance.SceneState = Game.GameState.UI;
        Game.Instance.ChangeScene(MenuScene);
    }

    /// <summary>原作 Update:任意方向键按下 → EventSystem 选中 BackMenuBtn</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } k &&
            (k.Keycode is Key.Left or Key.Right or Key.Up or Key.Down))
            _backBtn.GrabFocus();
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
        var router = InputRouter.Instance;
        // 右枪 AK47
        bool rActive = RightGunActive() && !_shotNoBeam;
        _lazerAk.Visible = rActive;
        if (rActive)
        {
            var aim = router.GetRightAim();
            if (aim.IsScreenPoint)
            {
                // 鼠标模拟光枪:枪口指向鼠标射线方向
                var dir = _camera.ProjectRayNormal(aim.ScreenPos);
                var localDir = (_camera.GlobalTransform.Basis.Inverse() * dir).Normalized();
                _gunAk.Quaternion = new Quaternion(Vector3.Forward, localDir);
                // 瞄准悬停 = 焦点视觉(MessageBox 按钮由原生 hover 承担)
                var b = ButtonAtLogicalPoint(UiKit.WindowToLogical(GetViewport(), aim.ScreenPos), skipBoxButtons: true);
                if (b != _aimHover)
                {
                    _aimHover = b;
                    _aimHover?.GrabFocus();
                }
            }
            else
            {
                _gunAk.Quaternion = aim.Rotation;
                _aimHover = null;
            }
        }
        else
        {
            if (_gunAk.Quaternion != Quaternion.Identity)
                _gunAk.Quaternion = Quaternion.Identity;
            _aimHover = null;
        }
        // 左枪 M4(仅手机腿部四元数)
        bool lActive = router.LegConnected;
        _lazerM4.Visible = lActive;
        _gunM4.Quaternion = lActive ? router.GetLeftAim().Rotation : Quaternion.Identity;
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

    /// <summary>--shot:&lt;png&gt;:默认状态截图(约 30 帧后;对照真值 unity_device)</summary>
    private void TakeShot(string path)
    {
        ApplyShotWindow();
        SaveShot(path, 30);
    }

    // ---------------------------------------------------------------- 自检(--deviceconnection-selftest)

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

        // 布局与内容(照原作 RectTransform 测绘值;中心锚定,期望值按实际画布中心算——headless 视口是 1280×1280)
        float cx = _buttonRoot.Size.X / 2.0f, cy = _buttonRoot.Size.Y / 2.0f;
        Check(_iosGroup.Visible, "iOS 二维码组默认可见");
        Check(!_androidGroup.Visible, "Android 二维码组默认隐藏(原作 active=0)");
        Check(Mathf.IsEqualApprox(_iosGroup.GetGlobalRect().Position.X, cx + 424 - 190), "iOS 组 x=874@720p");
        Check(Mathf.IsEqualApprox(_iosGroup.GetGlobalRect().Position.Y, cy - 103.3f - 190), "iOS 组 y=66.7@720p");
        Check(_iosGroup.Size.X == 380 && _iosGroup.Size.Y == 380, "iOS 组 380×380");
        Check(_instructionText.Text.StartsWith("1、手机扫码下载“蓝牙枪控制器”App"), "说明文字第1行");
        Check(_instructionText.Text.Contains("4、可以购买蓝牙枪获得最佳体验"), "说明文字第4行");
        Check(_instructionText.GetThemeFontSize("font_size") == 40, "说明文字 40 号");
        Check(_instructionText.HorizontalAlignment == HorizontalAlignment.Left, "说明文字左对齐(原作 align=0)");
        Check(_backBtn.Size.X == 360 && _backBtn.Size.Y == 100, "返回键 360×100");
        Check(Mathf.IsEqualApprox(_backBtn.GetGlobalRect().Position.X, cx - 180), "返回键 x=460@720p(居中)");
        Check(Mathf.IsEqualApprox(_backBtn.GetGlobalRect().Position.Y, cy + 279 - 50), "返回键 y=589@720p");
        Check(_backBtn.GetChild<Label>(1).Text == "返回", "返回键文字");
        Check(!PlayerState.Instance.CoinObj.Visible, "连接模式金币 HUD 隐藏(UpdateUIMode)");
        Check(InputRouter.Instance.Mode == InputRouter.InputMode.Menu, "Menu 输入模式");
        Check(InputRouter.Instance.IsBroadcasting(), "无设备时持续广播");

        // 方向键 → 焦点返回键(原作 Update)
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Left, Pressed = true });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(GetViewport().GuiGetFocusOwner() == _backBtn, "方向键→焦点返回键");

        // 鼠标瞄准返回键 → 悬停焦点(Simulate 路径对任意窗口尺寸确定,画布坐标先映射到视口)
        var backCanvas = _backBtn.GetGlobalRect().GetCenter();
        var vpPos = GetViewport().GetCanvasTransform() * backCanvas;
        InputRouter.Instance.MouseGun.SimulateMove(vpPos);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(GetViewport().GuiGetFocusOwner() == _backBtn, "鼠标瞄准悬停=焦点返回键");

        // 扳机命中返回键 → AK47 火光 + 触发 OnBack(会引发场景切换,其后不得再 await,直接收尾退出)
        InputRouter.Instance.MouseGun.SimulateTrigger();
        Check(_muzzleAk.Visible, "扳机命中返回键→AK47 枪口火光播放");
        Check(!_muzzleM4.Visible, "M4 火光不动");

        GD.Print($"DEVICECONNECTION SELFTEST {(fails == 0 ? "PASS" : "FAIL")} (fails={fails})");
        (Engine.GetMainLoop() as SceneTree)!.Quit(fails > 0 ? 1 : 0);
    }
}
