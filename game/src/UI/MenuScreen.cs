using Godot;

namespace FPSGame;

/// <summary>
/// 主菜单:1:1 移植 Unity Assets/UI/Menu.unity + MenuController.cs。
/// 3D(SubViewport 透明叠加在背景 UI 之上、按钮之下,复现原作 WorldSpace Canvas
///   z=623.2 的深度序):相机 FOV60、平行光强度3、RockWarrior(×100,z=450,yaw190°)
///   循环 Idle02、相机下挂 M4+激光+枪口火光(MuzzleFlash1.prefab 移植,命中按钮时播放)。
/// UI:逻辑分辨率 1280×720(canvas_items+expand,任意物理分辨率自适应);
///   背景 background4 全屏等比覆盖、科幻圆环组(±20°/s 反转)、标题"士兵打怪兵"、
///   4 个 SpriteSwap 主按钮(单人/双人/手机/退出,设置隐藏)、金币 HUD(PlayerState)。
/// 交互:方向键焦点导航(默认选中单人游戏)、手机体感枪瞄准+扳机、
///   鼠标模拟光枪(移动=瞄准,左键=扳机;无实体枪时自动生效);
///   "选择控制方式"弹框含移植版新增的"鼠标"模式(手机/遥控器/鼠标三键)。
/// </summary>
public partial class MenuScreen : Node
{
    private const string LevelChooseScene = "res://scenes/ui/level_choose.tscn";
    private const string DeviceConnectionScene = "res://scenes/ui/device_connection.tscn";
    private const string VpPrefix = "ViewportLayer/SubViewportContainer/SubViewport/";

    private TextureButton _btnOne = null!;
    private TextureButton _btnTwo = null!;
    private TextureButton _btnDevice = null!;
    private TextureButton _btnSetting = null!;
    private TextureButton _btnExit = null!;

    private SubViewport _subvp = null!;
    private Camera3D _camera = null!;
    private Node3D _gun = null!;
    private MeshInstance3D _lazer = null!;
    private MuzzleFlash _muzzle = null!;
    private Node3D _monster = null!;
    private Control _buttonRoot = null!;

    private Vector2I _shotRes = Vector2I.Zero;
    private TextureButton? _aimHover; // 鼠标瞄准下的焦点按钮(避免重复 GrabFocus)

    public override void _Ready()
    {
        Game.Instance.SceneState = Game.GameState.UI;
        AudioService.Instance.PlayMenuMusic();
        InputRouter.Instance.SetInputMode(InputRouter.InputMode.Menu);
        PlayerState.Instance.UpdateUiMode("Menu");

        _subvp = GetNode<SubViewport>(VpPrefix.TrimEnd('/'));
        _camera = GetNode<Camera3D>(VpPrefix + "Camera3D");
        _gun = GetNode<Node3D>(VpPrefix + "Camera3D/M4View");
        _lazer = GetNode<MeshInstance3D>(VpPrefix + "Camera3D/M4View/Lazer");
        _muzzle = GetNode<MuzzleFlash>(VpPrefix + "Camera3D/M4View/MuzzleFlash");
        _monster = GetNode<Node3D>(VpPrefix + "RockWarrior");

        SyncViewportSize();
        GetViewport().SizeChanged += SyncViewportSize;
        BuildUi();
        // 怪物 idle 循环(原作 MainnenuController.controller 唯一状态)
        var ap = _monster.GetNode<AnimationPlayer>("AnimationPlayer");
        var anim = ap.GetAnimation("Idle02");
        if (anim != null)
        {
            anim.LoopMode = Animation.LoopModeEnum.Linear;
            ap.Play("Idle02");
        }
        // 怪物材质(原作 monster_086.mat)
        var mat = GD.Load<Material>("res://assets/models/monsters/rock_warrior/rock_warrior_mat.tres");
        foreach (var mi in _monster.GetNode("Model").FindChildren("*", "MeshInstance3D", true, false))
            ((MeshInstance3D)mi).MaterialOverride = mat;
        // 枪身材质(原作 1K_M4TXTR.mat:贴图 × 0.783 灰;FBX 导入材质丢失贴图,统一代码覆盖)
        // 注意只盖 Model 子树——MuzzleFlash 的火焰/烟雾 quad 也是 M4View 下的 MeshInstance3D
        var gunMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.783019f, 0.783019f, 0.783019f),
            AlbedoTexture = GD.Load<Texture2D>("res://assets/models/guns/m4/m4_tex.png"),
            Roughness = 0.85f,
        };
        foreach (var mi in _gun.GetNode("Model").FindChildren("*", "MeshInstance3D", true, false))
            ((MeshInstance3D)mi).MaterialOverride = gunMat;
        // 默认选中"单人游戏"(原作 MenuController.Start)
        Callable.From(() => _btnOne.GrabFocus()).CallDeferred();
        ConfigService.FetchRemoteConfig(this);
        InputRouter.Instance.TriggerRight += OnGunTrigger;
        InputRouter.Instance.MouseGun.Triggered += OnMouseTrigger;

        var args = OS.GetCmdlineUserArgs();
        foreach (var a in args)
        {
            if (a.StartsWith("--shot-res:"))
            {
                var wh = a["--shot-res:".Length..].Split('x');
                if (wh.Length == 2)
                    _shotRes = new Vector2I(int.Parse(wh[0]), int.Parse(wh[1]));
            }
        }
        if (System.Array.IndexOf(args, "--menu-selftest") >= 0)
            Callable.From(() => RunSelfTest()).CallDeferred();
        if (System.Array.IndexOf(args, "--e2e-mouse-flow") >= 0)
            Callable.From(() => RunE2eMouseFlow()).CallDeferred();
        foreach (var a in args)
        {
            if (a.StartsWith("--shot:"))
                Callable.From(() => TakeShot(a["--shot:".Length..])).CallDeferred();
            else if (a.StartsWith("--shot-box:"))
            {
                // 打开单人弹框后再截屏(MessageBox 视觉验证)
                OnePlayer();
                Callable.From(() => TakeShot(a["--shot-box:".Length..])).CallDeferred();
            }
            else if (a.StartsWith("--shot-flash:"))
                Callable.From(() => TakeShotFlash(a["--shot-flash:".Length..])).CallDeferred();
        }
    }

    public override void _ExitTree()
    {
        // C# 事件(非 Godot 信号)不会在节点释放时自动退订:残留已释放对象的处理器
        // 会在事件触发时抛 ObjectDisposedException 并打断后续调用链(选关卡点击失效的根因)
        if (InputRouter.Instance == null)
            return;
        InputRouter.Instance.TriggerRight -= OnGunTrigger;
        InputRouter.Instance.MouseGun.Triggered -= OnMouseTrigger;
    }

    /// <summary>截图验证:--shot-flash:&lt;path&gt;,开火后第 3 帧截屏(火光翻页/灯光峰值期)。</summary>
    private async void TakeShotFlash(string path)
    {
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        if (_shotRes != Vector2I.Zero)
            DisplayServer.WindowSetSize(_shotRes);
        InputRouter.Instance.MouseGun.SimulateMove(
            _shotRes != Vector2I.Zero ? (Vector2)(_shotRes / 2) : new Vector2(640, 360));
        var args = OS.GetCmdlineUserArgs();
        _muzzle.DebugNoSmoke = System.Array.IndexOf(args, "--flash-nosmoke") >= 0;
        _muzzle.DebugNoFlame = System.Array.IndexOf(args, "--flash-noflame") >= 0;
        for (int i = 0; i < 10; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _muzzle.Fire();
        for (int i = 0; i < 3; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(Engine.GetSingleton("RenderingServer"), "frame_post_draw");
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(path);
        GD.Print("SHOT SAVED: ", path);
        GetTree().Quit();
    }

    /// <summary>截图验证:--shot:&lt;path&gt;,约 30 帧后截屏退出(含圆环旋转/怪物动画帧)。
    /// 默认全屏启动,截图前先切回窗口模式并应用 --shot-res 指定的窗口尺寸。</summary>
    private async void TakeShot(string path)
    {
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        if (_shotRes != Vector2I.Zero)
            DisplayServer.WindowSetSize(_shotRes);
        // 鼠标光枪默认 (0,0) 会让枪口指向左上角;截图统一把瞄准点放到窗口中心(= 原作待机朝向)
        InputRouter.Instance.MouseGun.SimulateMove(
            _shotRes != Vector2I.Zero ? (Vector2)(_shotRes / 2) : new Vector2(640, 360));
        for (int i = 0; i < 30; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(Engine.GetSingleton("RenderingServer"), "frame_post_draw");
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(path);
        GD.Print("SHOT SAVED: ", path);
        GetTree().Quit();
    }

    // ---------------------------------------------------------------- UI 构建

    private void BuildUi()
    {
        // 原作 Canvas 是 WorldSpace(z=623.2):怪物(z=450)挡在背景图之前、按钮之下。
        // Godot 分层复现:BGLayer(-1)<3D(SubViewport)<UILayer(1)<HUD(10)<MessageBox(20)。
        var uiRoot = UiKit.MakeRoot(GetNode("BGLayer"));
        _buttonRoot = UiKit.MakeRoot(GetNode("UILayer"));

        // 背景填充:全屏锚定 + 等比覆盖,任意宽高比下都铺满且无黑边
        var bgFill = UiKit.TexRect("BGFill", UiKit.TexDir + "background4.png");
        bgFill.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bgFill.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
        uiRoot.AddChild(bgFill);

        // BG:1920×1080 scale 0.6667 居中(等效铺满 1280×720)——纯布局容器,
        // 不带贴图,保持原作的父子变换链(圆环组/标题都是它的子级,坐标链不变)
        var bg = new Control { Name = "BG", MouseFilter = Control.MouseFilterEnum.Ignore };
        UiKit.Place(bg, 0, 0, 1920, 1080, 0.6666667f, 0.6666667f);
        uiRoot.AddChild(bg);

        // 科幻圆环组:@(-507,225) 915×455 scale 0.96586,底层星图
        var circle = UiKit.TexRect("SciFiLargeCircle", UiKit.TexMenuDir + "scifi_circle_stars.png");
        UiKit.Place(circle, -507, 225, 915, 455, 0.96586007f, 0.96586007f);
        bg.AddChild(circle);
        // 四层圆盘:584×584 @(-32.096,30.025) scale 1.0353467;1/2 层 ±20°/s 互反转
        for (int i = 1; i <= 4; i++)
        {
            TextureRect ring;
            if (i is 1 or 2)
            {
                var jr = new JustRotate { Name = $"Ring{i}", Speed = i == 1 ? 20.0f : -20.0f };
                jr.Texture = GD.Load<Texture2D>(UiKit.TexMenuDir + $"scifi_circle_{i}.png");
                jr.StretchMode = TextureRect.StretchModeEnum.Scale;
                jr.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
                jr.MouseFilter = Control.MouseFilterEnum.Ignore;
                ring = jr;
            }
            else
            {
                ring = UiKit.TexRect($"Ring{i}", UiKit.TexMenuDir + $"scifi_circle_{i}.png");
            }
            UiKit.Place(ring, -32.09576f, 30.025047f, 584, 584, 1.0353467f, 1.0353467f);
            circle.AddChild(ring);
        }
        // 圆环下弧线:441×137 @(-9.318,-199.822) scale 1.0353467
        var line = UiKit.TexRect("CircleLine", UiKit.TexMenuDir + "scifi_circle_line.png");
        UiKit.Place(line, -9.318f, -199.822f, 441, 137, 1.0353467f, 1.0353467f);
        circle.AddChild(line);

        // 隐藏装饰图(原作 BG>Image,bg_Title.png,默认 inactive)
        var deco = UiKit.TexRect("Image", UiKit.TexMenuDir + "bg_Title.png");
        UiKit.Place(deco, -513, 223, 960, 519);
        deco.Visible = false;
        bg.AddChild(deco);

        // 标题 Title2:"士兵打怪兵" btt 80 号,色 (0.8676,0.9393,1)
        var title = UiKit.MakeLabel("士兵打怪兵", 80, new Color(0.8676f, 0.9393f, 1.0f));
        title.Name = "Title2";
        UiKit.Place(title, -543, 264, 521, 109, 1.2041858f, 0.9712007f);
        bg.AddChild(title);
        // 标题上划线 bg_Title2 / 下划线 bg_Title1(子级,scale 0.8304366/1.0296533)
        var underTop = UiKit.TexRect("Image1", UiKit.TexMenuDir + "bg_Title2.png");
        UiKit.Place(underTop, -0.83044434f, 53.850906f, 698, 24, 0.8304366f, 1.0296533f);
        title.AddChild(underTop);
        var underBottom = UiKit.TexRect("Image2", UiKit.TexMenuDir + "bg_Title1.png");
        UiKit.Place(underBottom, -0.83044434f, -53.541946f, 664, 36, 0.8304366f, 1.0296533f);
        title.AddChild(underBottom);

        // 主按钮:505×102,常态 Btn_MainMenu_n,悬停/选中 _h
        // mouse_filter=IGNORE:指针交互统一走"瞄准+扳机"路径(鼠标模拟光枪),
        // 悬停视觉由瞄准焦点(focused 贴图=hover 贴图)承担,与体感枪行为一致。
        _btnOne = MainButton("Btn_OnePlayer", "单人游戏", 620, 270, -103, UiKit.TexDir + "Btn_MainMenu_h.png");
        _btnTwo = MainButton("Btn_TwoPlayer", "双人合作", 582, 166, -92, UiKit.TexDir + "Btn_MainMenu_h.png");
        _btnDevice = MainButton("Btn_Device", "连接手机", 545, 59, -82, UiKit.TexDir + "Btn_button03_h.png");
        _btnSetting = MainButton("Btn_Setting", "游戏设置", 511, -54, -75, UiKit.TexDir + "Btn_button03_h.png");
        _btnExit = MainButton("Btn_Exit", "退出游戏", 620, -315, -97, UiKit.TexDir + "Btn_button03_h.png");
        _btnSetting.Visible = false; // 原作 inactive

        _btnOne.Pressed += OnePlayer;
        _btnTwo.Pressed += TwoPlayer;
        _btnDevice.Pressed += DeviceConnect;
        _btnExit.Pressed += ExitGame;
        // 原作:除退出外每个按钮 onClick 还绑 Utils.PlayMenuSound()
        foreach (var b in new[] { _btnOne, _btnTwo, _btnDevice, _btnSetting })
            b.Pressed += AudioService.Instance.PlayUiSound;

        // 焦点导航(原作 EventSystem Explicit)
        LinkFocus(_btnOne, null, _btnTwo);
        LinkFocus(_btnTwo, _btnOne, _btnDevice);
        LinkFocus(_btnDevice, _btnTwo, _btnExit);
        LinkFocus(_btnExit, _btnDevice, null);
    }

    private TextureButton MainButton(string nodeName, string text, float x, float yUp,
        float textDx, string pressedTex)
    {
        var b = new TextureButton
        {
            Name = nodeName,
            TextureNormal = GD.Load<Texture2D>(UiKit.TexDir + "Btn_MainMenu_n.png"),
            TextureHover = GD.Load<Texture2D>(UiKit.TexDir + "Btn_MainMenu_h.png"),
            TextureFocused = GD.Load<Texture2D>(UiKit.TexDir + "Btn_MainMenu_h.png"),
            TexturePressed = GD.Load<Texture2D>(pressedTex),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.Scale,
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        b.AddToGroup(MessageBox.Group);
        UiKit.Place(b, x, yUp, 505, 102);
        _buttonRoot.AddChild(b);
        // 文字:填满按钮整体左移 textDx,btt 40(原 BestFit 10~40),#D8EDFF
        var l = UiKit.MakeLabel(text, 40, new Color(0.847f, 0.929f, 1.0f));
        l.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        l.OffsetLeft = textDx;
        l.OffsetRight = textDx;
        b.AddChild(l);
        return b;
    }

    private static void LinkFocus(TextureButton b, TextureButton? up, TextureButton? down)
    {
        if (up != null)
            b.FocusNeighborTop = b.GetPathTo(up);
        if (down != null)
            b.FocusNeighborBottom = b.GetPathTo(down);
    }

    // ---------------------------------------------------------------- 按钮行为(MenuController.cs)

    public void OnePlayer()
    {
        bool ring = RingConnected(), leg = LegConnected();
        if (!ring && !leg)
        {
            _monster.Hide();
            // 移植版新增第三键"鼠标":原作只有 手机/遥控器 两键
            MessageBox.ShowBox3(this, "选择控制方式", "可以选择用手机控制玩游戏哟！",
                "手机", "遥控器", "鼠标",
                choice =>
                {
                    switch (choice)
                    {
                        case MessageBox.Choice.Ok: // 手机
                            Game.Instance.ChangeScene(DeviceConnectionScene);
                            break;
                        case MessageBox.Choice.Cancel: // 遥控器
                            InputRouter.Instance.SetInputMode(InputRouter.InputMode.ControllerOrRight);
                            Game.Instance.ChangeScene(LevelChooseScene);
                            break;
                        case MessageBox.Choice.Extra: // 鼠标
                            InputRouter.Instance.SetInputMode(InputRouter.InputMode.Mouse);
                            Game.Instance.ChangeScene(LevelChooseScene);
                            break;
                    }
                    _monster.Show();
                });
        }
        else if (ring && leg)
        {
            MessageBox.ShowBox(this, "选择持枪方式", "单人游戏只能连接一个手机哟", "右手持枪", "左手持枪",
                right =>
                {
                    InputRouter.Instance.SetInputMode(
                        right ? InputRouter.InputMode.OnlyRight : InputRouter.InputMode.OnlyLeft);
                    Game.Instance.ChangeScene(LevelChooseScene);
                    _monster.Show();
                });
            // 原作 bug 保真:MenuController.cs:50 弹框外多跳一次 LevelChoose
            Game.Instance.ChangeScene(LevelChooseScene);
        }
        else if (ring)
        {
            InputRouter.Instance.SetInputMode(InputRouter.InputMode.OnlyRight);
            Game.Instance.ChangeScene(LevelChooseScene);
        }
        else
        {
            InputRouter.Instance.SetInputMode(InputRouter.InputMode.OnlyLeft);
            Game.Instance.ChangeScene(LevelChooseScene);
        }
    }

    public void TwoPlayer()
    {
        if (RingConnected() && LegConnected())
        {
            InputRouter.Instance.SetInputMode(InputRouter.InputMode.RightAndLeft);
            Game.Instance.ChangeScene(LevelChooseScene);
        }
        else
        {
            _monster.Hide();
            MessageBox.ShowBox(this, "连接手机", "需要用手机控制才能双人游戏", "确定", "取消",
                confirm =>
                {
                    if (confirm)
                        Game.Instance.ChangeScene(DeviceConnectionScene);
                    _monster.Show();
                    _btnTwo.GrabFocus();
                });
        }
    }

    public void DeviceConnect() => Game.Instance.ChangeScene(DeviceConnectionScene);

    public void ExitGame() => GetTree().Quit();

    /// <summary>原作 InputManager.isRingConnected/isLegConnected(物理连接;键盘回落模式视作右手已连)</summary>
    private static bool RingConnected() =>
        InputRouter.Instance.RingConnected || InputRouter.Instance.RingIp != "";

    private static bool LegConnected() =>
        InputRouter.Instance.LegConnected || InputRouter.Instance.LegIp != "";

    // ---------------------------------------------------------------- 瞄准 + 扳机

    private void SyncViewportSize()
    {
        var size = (Vector2I)GetViewport().GetVisibleRect().Size;
        if (size.X <= 0 || size.Y <= 0)
            return; // headless 启动首帧可视区为 0,跳过(保持场景默认 1280×720)
        _subvp.Size = size;
    }

    private bool GunActive() =>
        RingConnected() || InputRouter.Instance.MouseGun.IsActiveForRight(InputRouter.Instance);

    public override void _Process(double delta)
    {
        var router = InputRouter.Instance;
        var aim = router.GetRightAim();
        bool active = GunActive();
        _lazer.Visible = active;
        if (!active)
        {
            if (_gun.Quaternion != Quaternion.Identity)
                _gun.Quaternion = Quaternion.Identity;
            _aimHover = null;
            return;
        }
        if (aim.IsScreenPoint)
        {
            // 鼠标模拟光枪:枪口指向鼠标射线方向
            var dir = _camera.ProjectRayNormal(aim.ScreenPos);
            var localDir = (_camera.GlobalTransform.Basis.Inverse() * dir).Normalized();
            _gun.Quaternion = new Quaternion(Vector3.Forward, localDir);
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
            _gun.Quaternion = aim.Rotation;
            _aimHover = null;
        }
    }

    /// <summary>体感枪/键盘扳机(右路):枪口旋转路径</summary>
    private void OnGunTrigger()
    {
        if (!RingConnected())
            return;
        var b = ButtonAtLogicalPoint(RotationAimLogicalPoint(), skipBoxButtons: false);
        if (b != null)
        {
            // 原作 UIController:枪口火光(_ImpactEffect1)只在命中按钮时重播
            _muzzle.Fire();
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
            _muzzle.Fire();
            b.EmitSignal(BaseButton.SignalName.Pressed);
        }
    }

    /// <summary>枪口旋转 → SubViewport 像素 → 画布逻辑坐标</summary>
    private Vector2 RotationAimLogicalPoint()
    {
        var dir = _camera.GlobalBasis * (GunMath.PhoneToGunRotation(InputRouter.Instance.RawRingRotation) * Vector3.Forward);
        var point = _camera.UnprojectPosition(_camera.GlobalPosition + dir * 100.0f);
        return UiKit.WindowToLogical(GetViewport(), point);
    }

    /// <summary>逻辑坐标命中的按钮;MessageBox 打开时只认 MessageButton*(照原作);
    /// skipBoxButtons(鼠标路径)时再跳过 MessageButton*,避免与原生点击双重触发</summary>
    private TextureButton? ButtonAtLogicalPoint(Vector2 logical, bool skipBoxButtons)
    {
        bool boxOpen = MessageBox.IsOpen();
        foreach (var n in GetTree().GetNodesInGroup(MessageBox.Group))
        {
            if (n is not TextureButton b || !b.Visible || !b.GetGlobalRect().HasPoint(logical))
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

    // ---------------------------------------------------------------- 自检(--menu-selftest)

    /// <summary>端到端复现(--e2e-mouse-flow):单人游戏→弹框选"鼠标"→跳选关;
    /// 后续断言由 LevelChooseScreen 的同名参数钩子接管。</summary>
    private async void RunE2eMouseFlow()
    {
        for (int i = 0; i < 5; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        OnePlayer();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!MessageBox.IsOpen())
        {
            GD.Print("E2E MOUSE FLOW FAIL: 单人弹框未打开");
            (Engine.GetMainLoop() as SceneTree)!.Quit(1);
            return;
        }
        MessageBox.Current!.PressExtra(); // 鼠标 → Mouse 模式 → LevelChoose(帧末切场景)
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
        Check(GetViewport().GuiGetFocusOwner() == _btnOne, "默认选中单人游戏");
        Check(_btnOne.Size == new Vector2(505, 102), "按钮尺寸 505x102");
        Check(PlayerState.Instance.CoinObj.Visible, "菜单模式金币可见");
        var ring1 = FindChild("Ring1", true, false) as JustRotate;
        float r0 = ring1!.Rotation;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(!Mathf.IsEqualApprox(ring1.Rotation, r0), "圆环在旋转");
        Check(_monster.GetNode<AnimationPlayer>("AnimationPlayer").IsPlaying(), "怪物 idle 播放中");
        // 焦点导航链
        foreach (var (action, expect) in new[]
        {
            ("ui_down", "Btn_TwoPlayer"), ("ui_down", "Btn_Device"),
            ("ui_down", "Btn_Exit"), ("ui_up", "Btn_Device"),
        })
        {
            var ev = new InputEventAction { Action = action, Pressed = true };
            Input.ParseInputEvent(ev);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var owner = GetViewport().GuiGetFocusOwner();
            Check(owner != null && owner.Name == expect, $"导航 {action} → {expect}");
        }
        // 鼠标模拟光枪:移动到"双人合作"上 → 悬停=焦点;左键=扳机 → 弹框。
        // 坐标链:按钮中心(画布)→ canvasTransform → finalTransform → 窗口像素。
        // headless 窗口尺寸为 0,finalTransform 退化,改用 MouseGun 模拟钩子。
        var btnTwoCanvas = _btnTwo.GetGlobalRect().GetCenter();
        var mg = InputRouter.Instance.MouseGun;
        if (DisplayServer.WindowGetSize().X > 0)
        {
            var winPos = GetViewport().GetFinalTransform() *
                (GetViewport().GetCanvasTransform() * btnTwoCanvas);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = winPos });
        }
        else
            mg.SimulateMove(btnTwoCanvas);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(GetViewport().GuiGetFocusOwner() == _btnTwo, "鼠标瞄准悬停=焦点双人合作");
        if (DisplayServer.WindowGetSize().X > 0)
        {
            var winPos = GetViewport().GetFinalTransform() *
                (GetViewport().GetCanvasTransform() * btnTwoCanvas);
            Input.ParseInputEvent(new InputEventMouseButton
                { ButtonIndex = MouseButton.Left, Pressed = true, Position = winPos });
        }
        else
            mg.SimulateTrigger();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(MessageBox.IsOpen(), "鼠标扳机命中双人→弹框");
        Check(_muzzle.Visible, "扳机命中按钮→枪口火光播放");
        if (MessageBox.IsOpen())
        {
            MessageBox.Current!.PressCancel();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        // 双人弹框(无设备)
        TwoPlayer();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(MessageBox.IsOpen(), "双人弹框打开");
        Check(!_monster.Visible, "弹框时怪物隐藏");
        if (MessageBox.IsOpen())
        {
            Check(MessageBox.Current!.TitleLabel.Text == "连接手机", "双人弹框标题");
            Check(MessageBox.Current.ContentLabel.Text == "需要用手机控制才能双人游戏", "双人弹框正文");
            Check(MessageBox.Current.OkButton.GetChild<Label>(0).Text == "确定", "双人弹框确定");
            MessageBox.Current.PressCancel();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(_monster.Visible, "取消后怪物恢复");
            Check(GetViewport().GuiGetFocusOwner() == _btnTwo, "取消后回选双人按钮");
        }
        // 单人弹框(无设备):三按钮 手机/遥控器/鼠标(鼠标为移植版新增)
        OnePlayer();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(MessageBox.IsOpen(), "单人弹框打开");
        if (MessageBox.IsOpen())
        {
            Check(MessageBox.Current!.TitleLabel.Text == "选择控制方式", "单人弹框标题");
            Check(MessageBox.Current.ContentLabel.Text == "可以选择用手机控制玩游戏哟！", "单人弹框正文");
            Check(MessageBox.Current.OkButton.GetChild<Label>(0).Text == "手机", "单人弹框确定=手机");
            Check(MessageBox.Current.CancelButton.GetChild<Label>(0).Text == "遥控器", "单人弹框取消=遥控器");
            Check(MessageBox.Current.ExtraButton != null &&
                MessageBox.Current.ExtraButton.GetChild<Label>(0).Text == "鼠标", "单人弹框中键=鼠标");
            // 同帧连测两条分支(ChangeScene 帧末才生效,断言即时):
            MessageBox.Current.PressExtra(); // 鼠标 → Mouse 模式 → LevelChoose
            Check(InputRouter.Instance.Mode == InputRouter.InputMode.Mouse, "鼠标→Mouse");
            Check(PlayerState.Instance.PlayerRight.Active, "鼠标模式激活右玩家");
            OnePlayer(); // 重开弹框(ShowBox3 内部先 CloseCurrent)
            Check(MessageBox.IsOpen(), "单人弹框重开");
            MessageBox.Current!.PressCancel(); // 遥控器 → ControllerOrRight → LevelChoose
            Check(InputRouter.Instance.Mode == InputRouter.InputMode.ControllerOrRight, "遥控器→ControllerOrRight");
        }
        GD.Print($"MENU SELFTEST {(fails == 0 ? "PASS" : "FAIL")} (fails={fails})");
        // 回调里已跳场景,本节点可能已析构,走 main_loop 退出
        (Engine.GetMainLoop() as SceneTree)!.Quit(fails > 0 ? 1 : 0);
    }
}
