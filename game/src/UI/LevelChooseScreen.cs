using Godot;

namespace FPSGame;

/// <summary>
/// LevelChoose(8.1):13 关按钮两页翻页,星/锁/分数/排名,扣币选关,难度面板。
/// 解锁:L1 无前置;其余要求前一关 star>0(默认本地全 3 星全解锁,8.3)。
/// 注:视觉 1:1 移植待"选关界面"步骤;本版先保证框架与交互逻辑等价。
/// </summary>
public partial class LevelChooseScreen : Node3D
{
    private const string MenuScene = "res://scenes/ui/menu.tscn";
    private const string LoadingScene = "res://scenes/ui/loading.tscn";
    private const int PerPage = 7;
    private const int LevelCount = 13;
    private const float PageOffset = 3.2f;

    /// <summary>已建关卡映射;未建 → MessageBox "敬请期待"</summary>
    private static readonly System.Collections.Generic.Dictionary<int, string> SceneMap = new()
    {
        [0] = "res://scenes/levels/level1_story.tscn",
        [1] = "res://scenes/levels/level2.tscn",
        [2] = "res://scenes/levels/level3.tscn",
        [3] = "res://scenes/levels/level4.tscn",
    };

    private static readonly string[] DiffNames = { "Easy", "Hard", "Hell" };

    [Export] public NodePath ButtonRootPath = "ButtonRoot";

    private int _page;
    private Node3D _pagesRoot = null!;
    private readonly System.Collections.Generic.List<UiButton3D> _levelButtons = new();
    private Node3D _diffPanel = null!;
    private Label3D _coinLabel = null!;
    private Tween? _pageTween;
    private double _lastPressTime = -99.0;
    private int _pendingLevel = -1;

    public override void _Ready()
    {
        Game.Instance.SceneState = Game.GameState.UI;
        MessageBox.CloseCurrent();
        Build();
        Refresh();
    }

    private void Build()
    {
        var root = GetNode<Node3D>(ButtonRootPath);
        AddLabel(root, "选择关卡", 88, new Vector3(0, 0.95f, 0));
        _coinLabel = AddLabel(root, "", 44, new Vector3(0.9f, 0.95f, 0));
        _pagesRoot = new Node3D { Name = "PagesRoot" };
        root.AddChild(_pagesRoot);
        for (int page = 0; page < 2; page++)
        {
            var pageNode = new Node3D { Name = $"Page{page}" };
            pageNode.Position = new Vector3(PageOffset * page, 0, 0);
            _pagesRoot.AddChild(pageNode);
            for (int i = page * PerPage; i < System.Math.Min((page + 1) * PerPage, LevelCount); i++)
            {
                int idx = i - page * PerPage;
                int levelI = i;
                var b = UiButton3D.Create("", new Vector2(1.0f, 0.26f), () => SelectLevel(levelI));
                b.Name = $"BtnLevel{i + 1}";
                b.Shortcut = Key.Key1 + ((i + 1) % 10); // 键盘备选:数字键
                b.Position = new Vector3(0.0f, 0.6f - idx * 0.36f, 0);
                pageNode.AddChild(b);
                _levelButtons.Add(b);
            }
        }
        var prev = UiButton3D.Create("< 上页", new Vector2(0.55f, 0.2f), () => TurnPage(-1));
        prev.Name = "BtnPrevPage";
        prev.Position = new Vector3(-0.7f, -0.62f, 0);
        root.AddChild(prev);
        var next = UiButton3D.Create("下页 >", new Vector2(0.55f, 0.2f), () => TurnPage(1));
        next.Name = "BtnNextPage";
        next.Position = new Vector3(0.7f, -0.62f, 0);
        root.AddChild(next);
        var back = UiButton3D.Create("返回", new Vector2(0.55f, 0.2f), BackToMenu);
        back.Name = "BtnBack";
        back.Position = new Vector3(0, -0.92f, 0);
        root.AddChild(back);
        BuildDiffPanel(root);
    }

    private void BuildDiffPanel(Node3D root)
    {
        _diffPanel = new Node3D { Name = "DifficultyPanel" };
        root.AddChild(_diffPanel);
        AddLabel(_diffPanel, "选择难度", 72, new Vector3(0, 0.5f, 0.05f));
        for (int d = 0; d < 3; d++)
        {
            int dd = d;
            var b = UiButton3D.Create(DiffNames[d], new Vector2(0.7f, 0.24f), () => ChooseDifficulty(dd));
            b.Name = $"BtnDiff{d}";
            b.Position = new Vector3(0, 0.16f - d * 0.32f, 0.05f);
            _diffPanel.AddChild(b);
        }
        var cancel = UiButton3D.Create("取消", new Vector2(0.7f, 0.24f), () => ShowDiffPanel(false));
        cancel.Name = "BtnDiffCancel";
        cancel.SetBgColor(new Color(0.45f, 0.2f, 0.2f, 0.95f));
        cancel.Position = new Vector3(0, -0.85f, 0.05f);
        _diffPanel.AddChild(cancel);
        _diffPanel.Visible = false;
    }

    private static Label3D AddLabel(Node parent, string text, int fontSize, Vector3 pos)
    {
        var l = new Label3D
        {
            Text = text,
            FontSize = fontSize,
            PixelSize = 0.0022f,
            Position = pos,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        UiTheme.ApplyLabel3D(l);
        parent.AddChild(l);
        return l;
    }

    /// <summary>星/锁/分数/排名刷新(数据源 SaveService.LevelState)</summary>
    public void Refresh()
    {
        for (int i = 0; i < _levelButtons.Count; i++)
        {
            var st = SaveService.Instance.LevelState[i];
            int star = SaveService.GetInt(st, "star", 0);
            string text = $"Level {i + 1}\n";
            if (IsUnlocked(i))
            {
                string stars = "";
                for (int s = 0; s < 3; s++)
                    stars += s < star ? "*" : "-";
                text += $"{stars}  S:{SaveService.GetInt(st, "score", 0)} R:{SaveService.GetInt(st, "rank", 0)}";
                _levelButtons[i].SetBgColor(new Color(0.16f, 0.35f, 0.6f, 0.95f));
            }
            else
            {
                text += "LOCK";
                _levelButtons[i].SetBgColor(new Color(0.25f, 0.25f, 0.28f, 0.9f));
            }
            _levelButtons[i].SetText(text);
        }
    }

    private static bool IsUnlocked(int i) =>
        i == 0 || SaveService.GetInt(SaveService.Instance.LevelState[i - 1], "star", 0) > 0;

    /// <summary>选关:未解锁弹框 → 查币(不足弹框)→ 扣 1 币 → 0.5s 后难度面板;1 秒防抖</summary>
    public async void SelectLevel(int i)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (now - _lastPressTime < 1.0)
            return;
        _lastPressTime = now;
        if (!IsUnlocked(i))
        {
            MessageBox.ShowBox(this, "未解锁", $"需要先通关 Level {i}。", "确定", "返回");
            return;
        }
        if (SaveService.Instance.Coin < 1)
        {
            MessageBox.ShowBox(this, "金币不足",
                "游戏币不足,每 3 分钟 +1(上限 10),请稍后再来。", "确定", "返回");
            return;
        }
        SaveService.Instance.SpendCoin(1);
        _pendingLevel = i;
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        if (_pendingLevel == i)
            ShowDiffPanel(true);
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
            MessageBox.ShowBox(this, "敬请期待", $"Level {_pendingLevel + 1} 正在建设中。", "确定", "返回");
            return;
        }
        Game.Instance.NextScenePath = scene;
        Game.Instance.ChangeScene(LoadingScene);
    }

    public void TurnPage(int d)
    {
        int newPage = Mathf.Clamp(_page + d, 0, 1);
        if (newPage == _page)
            return;
        _page = newPage;
        _pageTween?.Kill();
        _pageTween = CreateTween();
        _pageTween.TweenProperty(_pagesRoot, "position:x", -PageOffset * _page, 0.35)
            .SetTrans(Tween.TransitionType.Sine);
    }

    public void BackToMenu() => Game.Instance.ChangeScene(MenuScene);

    private void ShowDiffPanel(bool v)
    {
        _diffPanel.Visible = v;
        _pagesRoot.Visible = !v;
    }

    public override void _Process(double delta)
    {
        _coinLabel.Text = $"金币: {SaveService.Instance.Coin}/{SaveService.Instance.MaxCoin}";
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } k && k.Keycode == Key.Escape)
        {
            if (_diffPanel.Visible)
                ShowDiffPanel(false);
            else
                BackToMenu();
        }
    }
}
