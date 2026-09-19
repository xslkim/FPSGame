using Godot;

namespace FPSGame;

/// <summary>
/// StartUp(1:1 移植 Unity Assets/UI/StartUp.unity + NewScene.cs):
/// 场景只活 1 帧 → 直接 LoadScene("Menu");无 logo/视频/进度条。
/// 另含原作中默认隐藏(m_IsActive=0)的调试 Canvas:FPS 数字 Text "100"
/// + ReStart 按钮(NumberDisplay.ReStart:数字 100→500,3 秒 EasyOut 缓动)。
/// </summary>
public partial class StartupScreen : Node
{
    private const string MenuScene = "res://scenes/ui/menu.tscn";

    private Label _debugNumber = null!;

    public override async void _Ready()
    {
        Game.Instance.SceneState = Game.GameState.UI;
        BuildDebugCanvas();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Game.Instance.ChangeScene(MenuScene);
    }

    /// <summary>调试画布(原作隐藏,原样保留隐藏状态)</summary>
    private void BuildDebugCanvas()
    {
        var layer = new CanvasLayer { Name = "DebugCanvas", Visible = false };
        AddChild(layer);
        _debugNumber = new Label { Name = "Text", Text = "100" };
        _debugNumber.AddThemeFontSizeOverride("font_size", 60);
        _debugNumber.SetAnchorsPreset(Control.LayoutPreset.Center);
        _debugNumber.OffsetLeft = -150;
        _debugNumber.OffsetRight = 150;
        _debugNumber.OffsetTop = -50;
        _debugNumber.OffsetBottom = 50;
        layer.AddChild(_debugNumber);
        var btn = new Button { Name = "Button", Text = "Button" };
        btn.AddThemeFontSizeOverride("font_size", 14);
        btn.AddThemeColorOverride("font_color", new Color(0.19607843f, 0.19607843f, 0.19607843f));
        btn.SetAnchorsPreset(Control.LayoutPreset.Center);
        btn.OffsetLeft = 41.4f - 80;
        btn.OffsetRight = 41.4f + 80;
        btn.OffsetTop = -129.2f - 15;
        btn.OffsetBottom = -129.2f + 15;
        btn.Pressed += Restart;
        layer.AddChild(btn);
    }

    /// <summary>NumberDisplay.ReStart():100 → 500,3 秒,EasyOut</summary>
    private void Restart()
    {
        float from = float.Parse(_debugNumber.Text);
        var tw = CreateTween().SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.TweenMethod(Callable.From<float>(v => _debugNumber.Text = Mathf.RoundToInt(v).ToString()),
            from, 500.0f, 3.0);
    }
}
