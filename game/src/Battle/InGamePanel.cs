using Godot;

namespace FPSGame;

/// <summary>
/// 战斗内三面板(1:1 Unity InGamePanel.prefab,World-Space 挂相机前 z=-1m,canvas 1280×720
/// 缩放 0.001):PauseBtn(右上暂停)/ PausePanel(暂停)/ ContinuePanel(续币:继续游戏)/
/// VectoryPanel(胜利)。贴图:messagebox 底板 / Btn_button03_n 按钮 / Btn_pause_n 暂停键 /
/// TitleLine_Left 标题装饰条 / VICTORY.png 胜利横幅 / coin.png。
/// 暂停=timeScale 0(Game.SetPaused);按钮=UiButton3D(layer3),枪射线暂停期可点。
/// 胜利只弹 VectoryPanel 无星数结算(照原作)。
/// </summary>
public partial class InGamePanel : Node3D
{
    public const float CanvasScale = 0.001f; // 1280×720 canvas → 1.28×0.72 m
    public const float PanelZ = -1.0f;       // 相机前方 1m(Godot -Z 前)

    public const string TexBtnN = "res://assets/textures/ui/Btn_button03_n.png";
    public const string TexBtnH = "res://assets/textures/ui/Btn_button03_h.png";
    public const string TexPauseN = "res://assets/textures/ui/Btn_pause_n.png";
    public const string TexBox = "res://assets/textures/ui/messagebox.png";
    public const string TexTitleLine = "res://assets/textures/ui/TitleLine_Left.png";
    public const string TexVictory = "res://assets/textures/ui/VICTORY.png";
    public const string TexCoin = "res://assets/textures/ui/coin.png";

    public static InGamePanel? Instance { get; private set; }

    private UiButton3D _pauseBtn = null!;
    private Node3D _pausePanel = null!;
    private Node3D _continuePanel = null!;
    private Node3D _victoryPanel = null!;
    private Label3D _timeText = null!;
    private Label3D _coinCount = null!;
    private UiButton3D _confirmBtn = null!;

    private bool _continueIsBullet;
    private int _continueSide;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.WhenPaused; // 暂停期 UI 照常
        // 场景里已挂在 Camera3D 下(照原作 prefab 父=主相机)
        Position = new Vector3(0, 0, PanelZ);
        Scale = Vector3.One * CanvasScale;

        BuildPauseBtn();
        BuildPausePanel();
        BuildContinuePanel();
        BuildVictoryPanel();
        if (GetTree().CurrentScene is LevelBase levelNode)
        {
            levelNode.OpenContinue += OpenContinue;
            levelNode.LevelVictory += Victory;
        }
        PlayerState.Instance.UiChanged += RefreshCoin;
        RefreshCoin();
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
        Vector2 canvasCenter, float z = 0.01f)
    {
        var lb = new Label3D
        {
            Name = name,
            Text = text,
            PixelSize = 0.001f, // 1  canvas px
            FontSize = fontSize,
            Modulate = color,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        UiTheme.ApplyLabel3D(lb);
        lb.Position = C(canvasCenter.X, canvasCenter.Y) + new Vector3(0, 0, z);
        return lb;
    }

    private static UiButton3D MakeButton(string name, string text, Vector2 size, Vector2 canvasCenter,
        System.Action cb, int fontSize = 52, Color? textColor = null)
    {
        var btn = UiButton3D.Create(text, size, cb, TexBtnN,
            textColor ?? new Color(0.9139f, 1.0f, 0.8208f), fontSize, 0.001f);
        btn.Name = name;
        btn.Position = C(canvasCenter.X, canvasCenter.Y) + new Vector3(0, 0, 0.02f);
        return btn;
    }

    /// <summary>标题 + 双侧 TitleLine 装饰(照 prefab Title 结构)</summary>
    private void AddTitle(Node3D panel, string text, float absX, float absY, float lineOffset)
    {
        panel.AddChild(MakeText("TitleText", text, 60, Colors.White, new Vector2(640 + absX, 360 - absY)));
        var left = MakeBox("LeftImage", new Vector2(200, 50), new Vector2(640 + absX - lineOffset, 360 - absY), TexTitleLine);
        panel.AddChild(left);
        var right = MakeBox("RightImg", new Vector2(200, 50), new Vector2(640 + absX + lineOffset, 360 - absY), TexTitleLine);
        right.Scale = new Vector3(-1, 1, 1); // 右侧镜像
        panel.AddChild(right);
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
        // 外层 120×120 顶部居中:深蓝横条 + "主菜单" 40 号 + 内层暂停图
        _pauseBtn = UiButton3D.Create("", new Vector2(120, 120), OpenPause, null, null, 40, 0.001f);
        _pauseBtn.Name = "PauseBtn";
        _pauseBtn.Position = C(640, 60) + new Vector3(0, 0, 0.02f);
        _pauseBtn.SetBgColor(new Color(0, 0, 0.4528f, 0.2353f));
        AddChild(_pauseBtn);
        // 子节点用按钮局部坐标(中心原点,y 向上):文字居下、暂停图居上
        var text = MakeText("Text", "主菜单", 40, Colors.White, new Vector2(640, 105));
        text.Position = new Vector3(0, -38, 0.01f);
        _pauseBtn.AddChild(text);
        var icon = MakeBox("PauseIcon", new Vector2(80, 80), new Vector2(640, 40), TexPauseN);
        icon.Position = new Vector3(0, 14, 0.01f);
        _pauseBtn.AddChild(icon);
    }

    private void BuildPausePanel()
    {
        _pausePanel = MakePanel("PausePanel", new Vector2(600, 500));
        AddChild(_pausePanel);
        AddTitle(_pausePanel, "暂停", 0, 185.8f, 173.2f);
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
        AddTitle(_continuePanel, "继续游戏", 5.2f, 202f, 190f);
        _timeText = MakeText("TimeText", "06：29", 40, new Color(0.8515f, 0.8579f, 0.9811f),
            new Vector2(640, 360 - 77.4f));
        _continuePanel.AddChild(_timeText);
        _continuePanel.AddChild(MakeText("InfoText",
            "继续游戏需要消耗一枚游戏币，每15分钟增加一枚游戏币，最高5枚。",
            40, new Color(0.9394f, 1.0f, 0.7170f), new Vector2(640, 360 + 33f)));
        _continuePanel.AddChild(MakeBox("Coin", new Vector2(64, 64), new Vector2(640 - 48.4f, 360 - 136f), TexCoin));
        _continuePanel.AddChild(MakeText("CoinX", "X", 32, Colors.White, new Vector2(640 - 48.4f + 58.8f, 360 - 136f)));
        _coinCount = MakeText("InGamePanelCoinText", "5", 42, new Color(1.0f, 0.8425f, 0.0f),
            new Vector2(640 - 48.4f + 103.7f, 360 - 136f));
        _continuePanel.AddChild(_coinCount);
        _confirmBtn = MakeButton("ContinueGameBtn", "继续游戏", new Vector2(200, 80),
            new Vector2(640 - 125f, 360 + 168f), ContinueGame, 40, Colors.White);
        _continuePanel.AddChild(_confirmBtn);
        _continuePanel.AddChild(MakeButton("MenuBtn", "主菜单", new Vector2(200, 80),
            new Vector2(640 + 137f, 360 + 168f), BackToMenu));
        _continuePanel.Visible = false;
    }

    private void BuildVictoryPanel()
    {
        _victoryPanel = MakePanel("VectoryPanel", new Vector2(800, 500));
        AddChild(_victoryPanel);
        AddTitle(_victoryPanel, "游戏胜利", 0, 185.8f, 235.75f);
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
    }

    private void ClosePause()
    {
        Game.Instance.SetPaused(false);
        _pausePanel.Visible = false;
    }

    /// <summary>续币面板。isOpen=true 弹尽(兑换子弹)/false 玩家死亡;side=出事侧</summary>
    public void OpenContinue(bool isOpen, int side)
    {
        if (Game.Instance.SceneState != Game.GameState.Battle)
            return;
        _continueIsBullet = isOpen;
        _continueSide = side;
        Game.Instance.SetPaused(true);
        _confirmBtn.SetEnabled(SaveService.Instance.Coin >= 1);
        _continuePanel.Visible = true;
        RefreshCoin();
    }

    /// <summary>续命:扣 1 币 → Relife(HP=100,子弹+MaxBullet)</summary>
    private void ContinueGame()
    {
        if (!SaveService.Instance.SpendCoin(1))
        {
            _confirmBtn.SetEnabled(false);
            return;
        }
        var p = PlayerState.Instance.GetPlayer((PlayerState.Side)_continueSide);
        p.Relife();
        Game.Instance.SetPaused(false);
        _continuePanel.Visible = false;
    }

    /// <summary>胜利:校验 Battle 态 → 暂停 + VectoryPanel + 藏 PauseBtn(照原作,无星数结算)</summary>
    public void Victory()
    {
        if (Game.Instance.SceneState != Game.GameState.Battle)
            return;
        Game.Instance.SetPaused(true);
        _victoryPanel.Visible = true;
        _pauseBtn.Visible = false;
    }

    /// <summary>出口:回 Menu,SceneState=UI</summary>
    private void BackToMenu()
    {
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
    }

    public override void _Process(double delta)
    {
        if (!_continuePanel.Visible)
            return;
        double t = SaveService.Instance.TimeToNextCoin();
        int m = (int)(t / 60.0);
        int s = (int)(t % 60.0);
        _timeText.Text = SaveService.Instance.Coin >= SaveService.Instance.MaxCoin
            ? "金币已满"
            : $"{m:D2}:{s:D2}";
    }

    private void RefreshCoin()
    {
        if (_coinCount != null)
            _coinCount.Text = SaveService.Instance.Coin.ToString();
    }
}
