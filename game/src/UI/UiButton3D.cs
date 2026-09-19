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
    private MeshInstance3D _mesh = null!;
    private Label3D _label = null!;
    private StandardMaterial3D _mat = null!;

    public static UiButton3D Create(string text, Vector2? size = null, System.Action? cb = null)
    {
        return new UiButton3D
        {
            Text = text,
            _size = size ?? new Vector2(0.6f, 0.22f),
            OnPressed = cb,
        };
    }

    public override void _Ready()
    {
        CollisionLayer = 0b100; // layer 3 Button
        CollisionMask = 0;
        AddToGroup("ui_button_3d");
        _mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = _bgColor,
        };
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
            PixelSize = 0.0025f,
            FontSize = 64,
            Modulate = Colors.White,
            Position = new Vector3(0, 0, 0.01f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        UiTheme.ApplyLabel3D(_label);
        AddChild(_label);
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

    private void UpdateVisual()
    {
        if (_mat != null)
            _mat.AlbedoColor = Enabled ? _bgColor : new Color(0.3f, 0.3f, 0.3f, 0.7f);
    }
}
