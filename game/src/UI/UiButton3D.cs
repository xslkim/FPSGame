using Godot;

namespace FPSGame;

/// <summary>
/// 3D 空间 UI 按钮(统一方案):StaticBody3D(layer 3 Button)+ 占位 quad + Label3D 文字。
/// 被 GunUiController 射线命中 → OnShot();也可用鼠标点击(同射线)或 Shortcut 键触发。
/// 用 UiButton3D.Create(text, size, cb) 构建,AddChild 后摆 Position 即可。
/// </summary>
public partial class UiButton3D : StaticBody3D
{
    [Signal] public delegate void PressedEventHandler();

    public string Text = "";
    public System.Action? OnPressed;
    public Key Shortcut = Key.None;
    public bool Enabled = true;

    private Vector2 _size = new(0.6f, 0.22f);
    private Color _bgColor = new(0.16f, 0.35f, 0.6f, 0.95f);
    private string? _texturePath;
    private Color _textColor = Colors.White;
    private int _fontSize = 64;
    private float _pixelSize = 0.0025f;
    private MeshInstance3D _mesh = null!;
    private Label3D _label = null!;
    private StandardMaterial3D _mat = null!;

    public static UiButton3D Create(string text, Vector2? size = null, System.Action? cb = null,
        string? texturePath = null, Color? textColor = null, int fontSize = 64, float pixelSize = 0.0025f)
    {
        return new UiButton3D
        {
            Text = text,
            _size = size ?? new Vector2(0.6f, 0.22f),
            OnPressed = cb,
            _texturePath = texturePath,
            _textColor = textColor ?? Colors.White,
            _fontSize = fontSize,
            _pixelSize = pixelSize,
        };
    }

    public override void _Ready()
    {
        CollisionLayer = 0b100; // layer 3 Button
        CollisionMask = 0;
        ProcessMode = ProcessModeEnum.Always; // 暂停期面板按钮仍可按
        AddToGroup("ui_button_3d");
        _mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = _bgColor,
            // 无贴图也要让 AlbedoColor alpha 生效(战斗暂停键底框 alpha 0.2353)
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        if (_texturePath != null && ResourceLoader.Exists(_texturePath))
            _mat.AlbedoTexture = GD.Load<Texture2D>(_texturePath);
        _mesh = new MeshInstance3D
        {
            Name = "Bg",
            Mesh = new QuadMesh { Size = _size, Material = _mat },
        };
        AddChild(_mesh);
        var col = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(_size.X, _size.Y, 0.05f) },
        };
        AddChild(col);
        _label = new Label3D
        {
            Name = "Text",
            Text = Text,
            PixelSize = _pixelSize,
            FontSize = _fontSize,
            Modulate = _textColor,
            Position = new Vector3(0, 0, 0.01f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        UiTheme.ApplyLabel3D(_label);
        AddChild(_label);
        // 透明队列按实例深度排序,微小 z 差可能排不进底图之前;给文字排序偏移兜底
        _label.SortingOffset = 0.1f;
        UpdateVisual();
    }

    /// <summary>GunUiController 命中回调(与鼠标/键盘同路径)</summary>
    public void OnShot() => Trigger();

    public void Trigger()
    {
        if (!Enabled)
            return;
        EmitSignal(SignalName.Pressed);
        OnPressed?.Invoke();
        var tw = CreateTween();
        tw.TweenProperty(this, "scale", Vector3.One * 0.92f, 0.05);
        tw.TweenProperty(this, "scale", Vector3.One, 0.1);
    }

    public void SetText(string t)
    {
        Text = t;
        if (_label != null)
            _label.Text = t;
    }

    public void SetEnabled(bool v)
    {
        Enabled = v;
        UpdateVisual();
    }

    public void SetBgColor(Color c)
    {
        _bgColor = c;
        UpdateVisual();
    }

    /// <summary>显隐 + 碰撞联动:隐藏时枪射线不可命中(等价原作 GameObject.SetActive)</summary>
    public void SetActiveVisible(bool v)
    {
        Visible = v;
        CollisionLayer = v ? 0b100u : 0u;
    }

    public override void _Process(double delta)
    {
        // 自身或父级面板隐藏时禁碰撞(原作 SetActive(false) 碰撞体即失效;
        // 只设 Visible 的隐藏按钮会继续吃开枪射线)
        uint want = IsVisibleInTree() ? 0b100u : 0u;
        if (CollisionLayer != want)
            CollisionLayer = want;
    }

    private void UpdateVisual()
    {
        if (_mat != null)
            _mat.AlbedoColor = Enabled ? _bgColor : new Color(0.3f, 0.3f, 0.3f, 0.7f);
    }
}
