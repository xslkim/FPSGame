using Godot;

namespace FPSGame;

/// <summary>
/// 战斗内三面板(1:1 Unity InGamePanel.prefab,World-Space 挂相机前 z=-1m,canvas 1280×720,
/// 根缩放 (0.00115,0.00115,0.01)):PauseBtn(顶中暂停)/ PausePanel(暂停)/ ContinuePanel(续币:
/// 继续游戏/兑换子弹)/ VectoryPanel(胜利)。贴图:messagebox 底板 / Btn_button03_n 按钮 /
/// Btn_pause_n 暂停键 / TitleLine_Left 标题装饰条 / VICTORY.png 胜利横幅 / coin.png。
/// Label3D PixelSize=1.0:面板局部 1 单位 = 1 canvas px(字号数字即 canvas 像素,根缩放换算到米)。
/// 暂停=timeScale 0(Game.SetPaused);按钮=UiButton3D(layer3),枪射线暂停期可点。
/// 胜利只弹 VectoryPanel 无星数结算(照原作)。
/// 暂停键:原作开场不显示,开战(LevelBase.StartBattle,BattleStartTime>0)才出现;任一面板打开
/// 隐藏、关闭恢复(原作 InGamePanel.cs OpenPause/OpenContinue/Victory/BackToGame/CloseContinue)。
/// 金币回复:仅 Continue 面板打开期间推进(SaveService.TickCoinRegen,原作 Update 门控),平时不回复。
/// 截图挂接:--panel-shot:&lt;path&gt;:&lt;pause|continue|bullet|victory&gt;:&lt;delaySec&gt;(从右往左拆,兼容盘符)。
/// </summary>
public partial class InGamePanel : Node3D
{
    public const float CanvasScale = 0.00115f; // 1280×720 canvas → 1.472×0.828 m(原作 prefab 根 scale)
    public const float CanvasScaleZ = 0.01f;   // 原作 prefab 根 z scale
    public const float PanelZ = -1.0f;         // 相机前方 1m(Godot -Z 前)

    public const string TexBtnN = "res://assets/textures/ui/Btn_button03_n.png";
    public const string TexBtnH = "res://assets/textures/ui/Btn_button03_h.png";
    public const string TexPauseN = "res://assets/textures/ui/Btn_pause_n.png";
    public const string TexBox = "res://assets/textures/ui/messagebox.png";
    public const string TexTitleLine = "res://assets/textures/ui/TitleLine_Left.png";
    public const string TexVictory = "res://assets/textures/ui/VICTORY.png";
    public const string TexCoin = "res://assets/textures/ui/coin.png";

    public static InGamePanel? Instance { get; private set; }

    private LevelBase? _level;
    private UiButton3D _pauseBtn = null!;
    private Node3D _pausePanel = null!;
    private Node3D _continuePanel = null!;
    private Node3D _victoryPanel = null!;
    private Label3D _continueTitle = null!;
    private Label3D _timeText = null!;
    private Label3D _infoText = null!;
    private Label3D _coinCount = null!;
    private UiButton3D _confirmBtn = null!;

    private bool _continueIsBullet;
    private int _continueSide;
    private double _openContinueTime; // BackToMenu 1s 防误触(原作 startOpenContinueTime)

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always; // 暂停期 UI 照常 + 非暂停期轮询暂停键显隐
        // 场景里已挂在 Camera3D 下(照原作 prefab 父=主相机)
        Position = new Vector3(0, 0, PanelZ);
        Scale = new Vector3(CanvasScale, CanvasScale, CanvasScaleZ);

        BuildPauseBtn();
        BuildPausePanel();
        BuildContinuePanel();
        BuildVictoryPanel();
        if (GetTree().CurrentScene is LevelBase levelNode)
        {
            _level = levelNode;
            levelNode.OpenContinue += OpenContinue;
            levelNode.LevelVictory += Victory;
        }
        PlayerState.Instance.UiChanged += RefreshCoin;
        RefreshCoin();
        CheckPanelShotArgs();
    }

    public override void _ExitTree() => Instance = null;

    // ------------------------------------------------ 构建辅助(canvas px → 面板局部 3D)

    /// <summary>canvas 坐标(左上原点)→ 局部 3D(中心原点,Y 向上)</summary>
    private static Vector3 C(float x, float y) => new(x - 640.0f, 360.0f - y, 0.0f);

    private static Node3D MakeBox(string name, Vector2 size, Vector2 canvasCenter, string texPath,
        Color? tint = null, float z = 0.0f)
    {
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = tint ?? Colors.White,
        };
        if (ResourceLoader.Exists(texPath))
            mat.AlbedoTexture = GD.Load<Texture2D>(texPath);
        var mi = new MeshInstance3D
        {
            Name = name,
            Mesh = new QuadMesh { Size = size, Material = mat },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        mi.Position = C(canvasCenter.X, canvasCenter.Y) + new Vector3(0, 0, z);
        return mi;
    }

    private static Label3D MakeText(string name, string text, int fontSize, Color color,
        Vector2 canvasCenter, float z = 0.01f, float width = 0.0f)
    {
        var lb = new Label3D
        {
            Name = name,
            Text = text,
            PixelSize = 1.0f, // 面板局部 1 单位 = 1 canvas px(根缩放换算到米)
            FontSize = fontSize,
            Modulate = color,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (width > 0.0f)
        {
            lb.Width = width; // 原作 Text 矩形内换行(InfoText 400×150)
            lb.AutowrapMode = TextServer.AutowrapMode.Arbitrary; // CJK 按字断行
        }
        UiTheme.ApplyLabel3D(lb);
        lb.Position = C(canvasCenter.X, canvasCenter.Y) + new Vector3(0, 0, z);
        lb.SortingOffset = 0.1f; // 透明排序兜底,防被底板盖住
        return lb;
    }

    private static UiButton3D MakeButton(string name, string text, Vector2 size, Vector2 canvasCenter,
        System.Action cb, int fontSize = 52, Color? textColor = null)
    {
        var btn = UiButton3D.Create(text, size, cb, TexBtnN,
            textColor ?? new Color(0.9139f, 1.0f, 0.8208f), fontSize, 1.0f);
        btn.Name = name;
        btn.Position = C(canvasCenter.X, canvasCenter.Y) + new Vector3(0, 0, 0.02f);
        return btn;
    }

    /// <summary>标题 + 双侧 TitleLine 装饰(照 prefab Title 结构;装饰条按 prefab 面板中心坐标,
    /// 与标题 x 偏移无关;右条镜像)。返回标题 Label3D(Continue 面板运行时要改文案)。</summary>
    private Label3D AddTitle(Node3D panel, string text, float absX, float absY,
        float lineLeft, float lineRight, int fontSize)
    {
        var title = MakeText("TitleText", text, fontSize, Colors.White, new Vector2(640 + absX, 360 - absY));
        panel.AddChild(title);
        var left = MakeBox("LeftImage", new Vector2(200, 50), new Vector2(640 - lineLeft, 360 - absY), TexTitleLine);
        panel.AddChild(left);
        var right = MakeBox("RightImg", new Vector2(200, 50), new Vector2(640 + lineRight, 360 - absY), TexTitleLine);
        right.Scale = new Vector3(-1, 1, 1); // 右侧镜像
        panel.AddChild(right);
        return title;
    }

    private Node3D MakePanel(string name, Vector2 size)
    {
        var p = new Node3D { Name = name };
        p.AddChild(MakeBox("Bg", size, new Vector2(640, 360), TexBox,
            new Color(1, 1, 1, 0.8745f)));
        return p;
    }

    // ------------------------------------------------ UI 构建(布局照 prefab 测绘)

    private void BuildPauseBtn()
    {
        // 原作:底框 120×120 半透明 (0,0,0.4528,0.2353) + 子 Text 空串 + 暂停图标全幅拉伸 120×120
        _pauseBtn = UiButton3D.Create("", new Vector2(120, 120), OpenPause, null, null, 40, 1.0f);
        _pauseBtn.Name = "PauseBtn";
        _pauseBtn.Position = C(640, 60) + new Vector3(0, 0, 0.02f);
        _pauseBtn.SetBgColor(new Color(0, 0, 0.4528f, 0.2353f));
        AddChild(_pauseBtn);
        var icon = MakeBox("PauseIcon", new Vector2(120, 120), new Vector2(640, 60), TexPauseN);
        icon.Position = new Vector3(0, 0, 0.01f);
        _pauseBtn.AddChild(icon);
        if (icon is MeshInstance3D iconMi)
            iconMi.SortingOffset = 0.1f; // 同 UiButton3D 文字:透明排序兜底,防被半透明底框盖住
        _pauseBtn.SetActiveVisible(false); // 开战才显示(原作开场无暂停键)
    }

    private void BuildPausePanel()
    {
        _pausePanel = MakePanel("PausePanel", new Vector2(600, 500));
        AddChild(_pausePanel);
        AddTitle(_pausePanel, "暂停", 0, 185.8f, 173.2f, 173.1f, 60);
        _pausePanel.AddChild(MakeButton("BackGameBtn", "返回游戏", new Vector2(400, 100),
            new Vector2(646, 315), ClosePause));
        _pausePanel.AddChild(MakeButton("MenuBtn", "主菜单", new Vector2(400, 100),
            new Vector2(646, 483), BackToMenu));
        _pausePanel.Visible = false;
    }

    private void BuildContinuePanel()
    {
        _continuePanel = MakePanel("ContinuePanel", new Vector2(500, 500));
        AddChild(_continuePanel);
        _continueTitle = AddTitle(_continuePanel, "继续游戏", 5.2f, 202f, 190f, 199f, 50);
        _timeText = MakeText("TimeText", "06：29", 40, new Color(0.8515f, 0.8579f, 0.9811f),
            new Vector2(640, 360 - 77.4f));
        _continuePanel.AddChild(_timeText);
        _infoText = MakeText("InfoText", InfoTextFor(false),
            40, new Color(0.9394f, 1.0f, 0.7170f), new Vector2(640, 360 + 33f), 0.01f, 400f);
        _continuePanel.AddChild(_infoText);
        _continuePanel.AddChild(MakeBox("Coin", new Vector2(64, 64), new Vector2(640 - 48.4f, 360 - 136f), TexCoin));
        _continuePanel.AddChild(MakeText("CoinX", "X", 32, Colors.White, new Vector2(640 - 48.4f + 58.8f, 360 - 136f)));
        _coinCount = MakeText("InGamePanelCoinText", "5", 42, new Color(1.0f, 0.8425f, 0.0f),
            new Vector2(640 - 48.4f + 103.7f, 360 - 136f));
        _continuePanel.AddChild(_coinCount);
        _confirmBtn = MakeButton("ContinueGameBtn", "继续游戏", new Vector2(200, 80),
            new Vector2(640 - 125f, 360 + 168f), ContinueGame, 40, Colors.White);
        _continuePanel.AddChild(_confirmBtn);
        _continuePanel.AddChild(MakeButton("MenuBtn", "主菜单", new Vector2(200, 80),
            new Vector2(640 + 137f, 360 + 168f), BackToMenu, 40, Colors.White));
        _continuePanel.Visible = false;
    }

    private void BuildVictoryPanel()
    {
        _victoryPanel = MakePanel("VectoryPanel", new Vector2(800, 500));
        AddChild(_victoryPanel);
        AddTitle(_victoryPanel, "游戏胜利", 0, 185.8f, 235.75f, 239.7f, 60);
        _victoryPanel.AddChild(MakeBox("VictoryImg", new Vector2(400, 100),
            new Vector2(640, 360 - 24.5f), TexVictory));
        _victoryPanel.AddChild(MakeButton("VectoryPanelBtn", "主菜单", new Vector2(300, 80),
            new Vector2(646, 360 + 128f), BackToMenu));
        _victoryPanel.Visible = false;
    }

    // ------------------------------------------------ 行为(原作 InGamePanel.cs)

    /// <summary>暂停:仅 Battle 态,timeScale=0</summary>
    private void OpenPause()
    {
        if (Game.Instance.SceneState != Game.GameState.Battle || Game.Instance.IsGamePause)
            return;
        Game.Instance.SetPaused(true);
        _pausePanel.Visible = true;
        UpdatePauseBtn();
    }

    private void ClosePause()
    {
        Game.Instance.SetPaused(false);
        _pausePanel.Visible = false;
        UpdatePauseBtn();
    }

    /// <summary>原作 OpenContinue 动态 Info:AddCoinTime/MaxCoin 生成(180s/10 → "每3分钟…最高10枚")</summary>
    private static string InfoTextFor(bool bullet)
    {
        var sv = SaveService.Instance;
        return (bullet ? "兑换子弹" : "继续游戏") + "需要消耗一枚游戏币，每"
            + sv.AddCoinTime / 60 + "分钟增加一枚游戏币，最高" + sv.MaxCoin + "枚。";
    }

    /// <summary>续币面板。isOpen=true 弹尽(兑换子弹)/false 玩家死亡;side=出事侧</summary>
    public void OpenContinue(bool isOpen, int side)
    {
        if (Game.Instance.SceneState != Game.GameState.Battle)
            return;
        _continueIsBullet = isOpen;
        _continueSide = side;
        Game.Instance.SetPaused(true);
        // 原作 OpenContinue:标题/Info/按钮文案按"兑换子弹/继续游戏"切换;币不足按钮显示"币不足"(不置灰)
        _continueTitle.Text = isOpen ? "兑换子弹" : "继续游戏";
        _infoText.Text = InfoTextFor(isOpen);
        _confirmBtn.SetEnabled(true);
        _confirmBtn.SetText(SaveService.Instance.Coin >= 1
            ? (isOpen ? "兑换子弹" : "继续游戏")
            : "币不足");
        _continuePanel.Visible = true;
        _openContinueTime = Time.GetTicksMsec() / 1000.0;
        RefreshCoin();
        UpdatePauseBtn();
    }

    /// <summary>续命:扣 1 币 → Relife(HP=100,子弹+MaxBullet);币不足直接拦截(原作 isCoinEnough)</summary>
    private void ContinueGame()
    {
        if (SaveService.Instance.Coin < 1)
            return;
        if (!SaveService.Instance.SpendCoin(1))
            return;
        var p = PlayerState.Instance.GetPlayer((PlayerState.Side)_continueSide);
        p.Relife();
        Game.Instance.SetPaused(false);
        _continuePanel.Visible = false;
        UpdatePauseBtn();
    }

    /// <summary>胜利:校验 Battle 态 → 暂停 + VectoryPanel(照原作,无星数结算;暂停键联动隐藏)</summary>
    public void Victory()
    {
        if (Game.Instance.SceneState != Game.GameState.Battle)
            return;
        Game.Instance.SetPaused(true);
        _victoryPanel.Visible = true;
        UpdatePauseBtn();
    }

    /// <summary>出口:回 Menu,SceneState=UI;Continue 打开 1s 内忽略(原作防误触)</summary>
    private void BackToMenu()
    {
        if (Time.GetTicksMsec() / 1000.0 - _openContinueTime < 1.0)
            return;
        Game.Instance.SetPaused(false);
        Game.Instance.SceneState = Game.GameState.UI;
        Game.Instance.ChangeScene("res://scenes/ui/menu.tscn");
    }

    /// <summary>自检用:关闭所有面板并恢复</summary>
    public void CloseAll()
    {
        Game.Instance.SetPaused(false);
        _pausePanel.Visible = false;
        _continuePanel.Visible = false;
        _victoryPanel.Visible = false;
        UpdatePauseBtn();
    }

    /// <summary>暂停键显隐:开战后才显示;任一面板打开时隐藏、关闭恢复(原作 SetActive 联动)。
    /// L1 开场 19s 内 BattleStartTime=0 → 隐藏;L2/L3/L4 无开场进场即战 → 立即显示。</summary>
    private void UpdatePauseBtn()
    {
        bool anyPanel = _pausePanel.Visible || _continuePanel.Visible || _victoryPanel.Visible;
        bool battle = Game.Instance.SceneState == Game.GameState.Battle
            && (_level == null || _level.BattleStartTime > 0.0);
        _pauseBtn.SetActiveVisible(battle && !anyPanel);
    }

    public override void _Process(double delta)
    {
        UpdatePauseBtn();
        if (!_continuePanel.Visible)
            return;
        // 原作 InGamePanel.Update:仅 Continue 面板打开期间计时加币,平时不回复
        SaveService.Instance.TickCoinRegen();
        double left = SaveService.Instance.TimeToNextCoin();
        int m = (int)(left / 60.0);
        int s = (int)(left % 60.0);
        _timeText.Text = m + ":" + s; // 原作 min + ":" + second 不补零
        RefreshCoin();
    }

    private void RefreshCoin()
    {
        if (_coinCount != null)
            _coinCount.Text = SaveService.Instance.Coin.ToString();
    }

    // ------------------------------------------------ 面板截图挂接(布局/文字验证用)

    private void CheckPanelShotArgs()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (!a.StartsWith("--panel-shot:"))
                continue;
            string rest = a["--panel-shot:".Length..];
            float delay = 2.0f;
            int ci = rest.LastIndexOf(':');
            if (ci > 1 && float.TryParse(rest[(ci + 1)..], out float d))
            {
                delay = d;
                rest = rest[..ci];
            }
            string panel = "pause";
            ci = rest.LastIndexOf(':');
            if (ci > 1)
            {
                panel = rest[(ci + 1)..];
                rest = rest[..ci];
            }
            PanelShot(panel, rest, delay);
        }
    }

    /// <summary>等开战 → 开指定面板 → delay 秒截图退出(面板开=暂停,SceneTreeTimer 默认暂停期仍走时)</summary>
    private async void PanelShot(string panel, string path, float delay)
    {
        while (_level != null && _level.BattleStartTime <= 0.0)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        switch (panel)
        {
            case "continue": OpenContinue(false, 0); break;
            case "bullet": OpenContinue(true, 0); break;
            case "victory": Victory(); break;
            default: OpenPause(); break;
        }
        await ToSignal(GetTree().CreateTimer(delay), SceneTreeTimer.SignalName.Timeout);
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(path.Replace('/', '\\'));
        GD.Print($"[InGamePanel] panel shot saved: {path}");
        GetTree().Quit();
    }
}
