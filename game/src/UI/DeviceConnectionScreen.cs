using Godot;

namespace FPSGame;

/// <summary>
/// DeviceConnection(8.1):二维码(占位)+ 说明,UDP 配对,射"返回"回 Menu。
/// 进场 InputRouter 持续广播(Menu 模式);双路连接状态实时显示。
/// 注:视觉 1:1 移植(含教学视频)待"连接手机界面"步骤;本版先保证框架与逻辑等价。
/// </summary>
public partial class DeviceConnectionScreen : Node3D
{
    private const string MenuScene = "res://scenes/ui/menu.tscn";

    [Export] public NodePath ButtonRootPath = "ButtonRoot";

    private Label3D _statusLabel = null!;

    public override void _Ready()
    {
        Game.Instance.SceneState = Game.GameState.UI;
        MessageBox.CloseCurrent();
        InputRouter.Instance.SetInputMode(InputRouter.InputMode.Menu); // Menu 模式持续广播(4.1)
        var root = GetNode<Node3D>(ButtonRootPath);
        AddLabel(root, "连接手机", 88, new Vector3(0, 0.85f, 0));
        AddLabel(root, "手机与电脑连接同一 Wi-Fi,\n打开手机 App 后自动搜索设备(UDP 8281/8282)。\n连接成功后下方状态变为「已连接」。",
            44, new Vector3(0, 0.42f, 0));
        // 二维码占位图
        var qr = new MeshInstance3D { Name = "QRPlaceholder" };
        var q = new QuadMesh { Size = new Vector2(0.5f, 0.5f) };
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.9f, 0.9f, 0.9f),
        };
        q.Material = mat;
        qr.Mesh = q;
        qr.Position = new Vector3(0, -0.12f, 0);
        root.AddChild(qr);
        AddLabel(qr, "QR", 96, new Vector3(0, 0, 0.01f));
        _statusLabel = AddLabel(root, "", 44, new Vector3(0, -0.58f, 0));
        var back = UiButton3D.Create("返回", new Vector2(0.7f, 0.24f),
            () => Game.Instance.ChangeScene(MenuScene));
        back.Name = "BtnBack";
        back.Position = new Vector3(0, -0.9f, 0);
        root.AddChild(back);
    }

    public override void _Process(double delta)
    {
        var router = InputRouter.Instance;
        string ring = router.RingConnected ? $"已连接({router.RingIp})" : "未连接";
        string leg = router.LegConnected ? $"已连接({router.LegIp})" : "未连接";
        _statusLabel.Text = $"右手(R): {ring}    左手(L): {leg}";
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
}
