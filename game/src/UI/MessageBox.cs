using Godot;

namespace FPSGame;

/// <summary>
/// 通用弹框,1:1 移植原作 Assets/UI/MessageBox.prefab(800×500 居中):
/// 九宫格底 messagebox.png(border 65)、标题条 bg_Title_LargePanel03.png、
/// 左取消(46 号)/右确定(50 号)两个 300×100 按钮(Btn_button03_n/h)。
/// 按钮名保留 MessageButtonOk/MessageButtonCancel:打开期间枪的射线/扳机
/// 只响应 MessageButton*(照原作 UIController.cs);键盘由焦点导航承担
/// (打开时默认选中"确定",方向键左右切换,Enter 触发)。
/// 用法:MessageBox.ShowBox(parent, "标题", "内容", "确定", "取消", ok => ...);
/// </summary>
public partial class MessageBox : CanvasLayer
{
    /// <summary>枪瞄准/扳机交互时视作可命中对象(MenuScreen/GunUiController 遍历此分组)</summary>
    public const string Group = "gun_button";

    /// <summary>三按钮弹框的选择结果(移植版新增:原作弹框只有 确定/取消 两键)</summary>
    public enum Choice { Cancel, Ok, Extra }

    public static MessageBox? Current { get; private set; }

    public TextureButton OkButton = null!;
    public TextureButton CancelButton = null!;
    /// <summary>三按钮模式的中键(两按钮模式为 null)</summary>
    public TextureButton? ExtraButton { get; private set; }
    public Label TitleLabel { get; private set; } = null!;
    public Label ContentLabel { get; private set; } = null!;

    /// <summary>关闭回调(true=确定)</summary>
    public event System.Action<bool>? Closed;

    private System.Action<bool>? _cb;
    private System.Action<Choice>? _cb3;

    public static MessageBox ShowBox(Node parent, string title, string content,
        string okText = "确定", string cancelText = "取消", System.Action<bool>? cb = null)
    {
        CloseCurrent();
        var mb = new MessageBox { Name = "MessageBox" };
        parent.AddChild(mb);
        mb.Build(title, content, okText, cancelText, null, cb, null);
        Current = mb;
        return mb;
    }

    /// <summary>三按钮变体:左=cancelText、中=extraText、右=okText(移植版新增,用于"选择控制方式"加鼠标模式)</summary>
    public static MessageBox ShowBox3(Node parent, string title, string content,
        string okText, string cancelText, string extraText, System.Action<Choice>? cb)
    {
        CloseCurrent();
        var mb = new MessageBox { Name = "MessageBox" };
        parent.AddChild(mb);
        mb.Build(title, content, okText, cancelText, extraText, null, cb);
        Current = mb;
        return mb;
    }

    public static void CloseCurrent()
    {
        if (Current != null && GodotObject.IsInstanceValid(Current))
            Current.QueueFree();
        Current = null;
    }

    public static bool IsOpen() => Current != null && GodotObject.IsInstanceValid(Current);

    private static void Center(Control ctrl, float x, float y, float w, float h)
    {
        // Unity 中心锚点(弹框局部坐标,Y 向上)→ Godot(Y 向下)
        ctrl.SetAnchorsPreset(Control.LayoutPreset.Center);
        ctrl.OffsetLeft = x - w / 2.0f;
        ctrl.OffsetRight = x + w / 2.0f;
        ctrl.OffsetTop = -y - h / 2.0f;
        ctrl.OffsetBottom = -y + h / 2.0f;
    }

    private static Label MakeLabel(string text, int fontSize, Color color)
    {
        var l = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeFontOverride("font", GD.Load<Font>(UiKit.FontBttPath));
        return l;
    }

    private static TextureButton MakeButton(string nodeName)
    {
        var b = new TextureButton
        {
            Name = nodeName,
            TextureNormal = GD.Load<Texture2D>(UiKit.TexDir + "Btn_button03_n.png"),
            TextureHover = GD.Load<Texture2D>(UiKit.TexDir + "Btn_button03_h.png"),
            TexturePressed = GD.Load<Texture2D>(UiKit.TexDir + "Btn_button03_h.png"),
            TextureFocused = GD.Load<Texture2D>(UiKit.TexDir + "Btn_button03_h.png"),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.Scale,
            FocusMode = Control.FocusModeEnum.All,
        };
        b.AddToGroup(Group);
        return b;
    }

    private void Build(string title, string content, string okText, string cancelText,
        string? extraText, System.Action<bool>? cb, System.Action<Choice>? cb3)
    {
        _cb = cb;
        _cb3 = cb3;
        Layer = 20;
        // 逻辑分辨率恒为 1280×720(canvas_items+expand),弹框全屏锚定后居中
        var root0 = UiKit.MakeRoot(this);
        // 根:800×500 居中九宫格面板
        var root = new NinePatchRect
        {
            Name = "Panel",
            Texture = GD.Load<Texture2D>(UiKit.TexDir + "messagebox.png"),
            PatchMarginLeft = 65,
            PatchMarginTop = 65,
            PatchMarginRight = 65,
            PatchMarginBottom = 65,
        };
        root.SetAnchorsPreset(Control.LayoutPreset.Center);
        root.OffsetLeft = -400;
        root.OffsetRight = 400;
        root.OffsetTop = -250;
        root.OffsetBottom = 250;
        root0.AddChild(root);
        // 标题条底图:600×50 @(0, 207)
        var bar = UiKit.TexRect("TitleBar", UiKit.TexDir + "bg_Title_LargePanel03.png");
        Center(bar, 0.0f, 207.0f, 600.0f, 50.0f);
        root.AddChild(bar);
        // 标题:505×102 @(0, 208.07),btt 40(原 BestFit 10~40),#D8EDFF
        TitleLabel = MakeLabel(title, 40, new Color(0.847f, 0.929f, 1.0f));
        TitleLabel.Name = "Title";
        Center(TitleLabel, 0.0f, 208.07f, 505.0f, 102.0f);
        root.AddChild(TitleLabel);
        // 正文:600×160.91 @(0, 77.8),40 号(原作内置 Arial),色 (0.7338,0.8314,0.9151)
        ContentLabel = MakeLabel(content, 40, new Color(0.7338f, 0.8314f, 0.9151f));
        ContentLabel.Name = "Content";
        if (UiTheme.CjkFont() != null)
            ContentLabel.AddThemeFontOverride("font", UiTheme.CjkFont());
        Center(ContentLabel, 0.0f, 77.8f, 600.0f, 160.91f);
        root.AddChild(ContentLabel);
        // 按钮排布:两按钮照原作(300×100 @∓192/180.62);三按钮收窄为 240×100 @-250/0/+250
        bool three = extraText != null;
        float btnW = three ? 240.0f : 300.0f;
        float cancelX = three ? -250.0f : -192.0f;
        float okX = three ? 250.0f : 180.62f;
        // 取消(左):"取消" 46 号白
        CancelButton = MakeButton("MessageButtonCancel");
        Center(CancelButton, cancelX, -132.0f, btnW, 100.0f);
        CancelButton.Pressed += () => Close(false);
        root.AddChild(CancelButton);
        var cancelLabel = MakeLabel(cancelText, 46, Colors.White);
        cancelLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        CancelButton.AddChild(cancelLabel);
        // 中键(仅三按钮模式)
        if (three)
        {
            ExtraButton = MakeButton("MessageButtonExtra");
            Center(ExtraButton, 0.0f, -132.0f, btnW, 100.0f);
            ExtraButton.Pressed += () => Close(Choice.Extra);
            root.AddChild(ExtraButton);
            var extraLabel = MakeLabel(extraText!, 46, Colors.White);
            extraLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            ExtraButton.AddChild(extraLabel);
        }
        // 确定(右):"确定" 50 号白
        OkButton = MakeButton("MessageButtonOk");
        Center(OkButton, okX, -132.0f, btnW, 100.0f);
        OkButton.Pressed += () => Close(true);
        root.AddChild(OkButton);
        var okLabel = MakeLabel(okText, 50, Colors.White);
        okLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        OkButton.AddChild(okLabel);
        // 焦点导航(原作 EventSystem Explicit):取消 ↔ [中键 ↔] 确定
        if (three)
        {
            CancelButton.FocusNeighborRight = CancelButton.GetPathTo(ExtraButton!);
            ExtraButton!.FocusNeighborLeft = ExtraButton.GetPathTo(CancelButton);
            ExtraButton.FocusNeighborRight = ExtraButton.GetPathTo(OkButton);
            OkButton.FocusNeighborLeft = OkButton.GetPathTo(ExtraButton);
        }
        else
        {
            CancelButton.FocusNeighborRight = CancelButton.GetPathTo(OkButton);
            OkButton.FocusNeighborLeft = OkButton.GetPathTo(CancelButton);
        }
        // 打开即选中"确定"(原作 MessageBox.Show 里 SetSelectedGameObject)
        Callable.From(() => OkButton.GrabFocus()).CallDeferred();
    }

    public void PressOk() => Close(true);

    public void PressCancel() => Close(false);

    public void PressExtra() => Close(Choice.Extra);

    private void Close(bool ok) => Close(ok ? Choice.Ok : Choice.Cancel);

    private void Close(Choice choice)
    {
        if (Current == this)
            Current = null;
        AudioService.Instance.PlayUiSound();
        Closed?.Invoke(choice == Choice.Ok);
        _cb?.Invoke(choice == Choice.Ok);
        _cb3?.Invoke(choice);
        QueueFree();
    }
}
