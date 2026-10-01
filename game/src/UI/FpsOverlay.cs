using Godot;

namespace FPSGame;

/// <summary>全局帧率显示。F3 切换，场景切换和暂停时仍可使用。</summary>
public partial class FpsOverlay : CanvasLayer
{
    private const double RefreshInterval = 0.25;
    private Label _label = null!;
    private double _elapsed;

    public override void _Ready()
    {
        Layer = 100;
        ProcessMode = ProcessModeEnum.Always;

        var panel = new PanelContainer
        {
            Name = "FpsPanel",
            Position = new Vector2(14, 14),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.02f, 0.04f, 0.07f, 0.78f),
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 5,
            ContentMarginBottom = 5,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        _label = new Label
        {
            Name = "FpsValue",
            Text = "FPS: --",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.AddThemeColorOverride("font_color", new Color(0.8f, 1.0f, 0.9f));
        _label.AddThemeFontSizeOverride("font_size", 22);
        panel.AddChild(_label);
        AddChild(panel);
        Visible = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--show-fps") >= 0;
    }

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Keycode: Key.F3, Pressed: true, Echo: false })
            return;
        Visible = !Visible;
        _elapsed = RefreshInterval;
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        _elapsed += delta;
        if (_elapsed < RefreshInterval)
            return;
        _elapsed = 0.0;
        _label.Text = $"FPS: {Engine.GetFramesPerSecond():0}";
    }
}
