using Godot;

namespace FPSGame;

/// <summary>
/// UiKit:2D UI 构建辅助。坐标约定与 Unity 一致:中心锚点、Y 向上(内部转 Godot Y 向下)。
/// 逻辑分辨率恒为 1280×720(canvas_items+expand 自动适配任意物理分辨率)。
/// </summary>
public static class UiKit
{
    public const string TexDir = "res://assets/textures/ui/";
    public const string TexMenuDir = "res://assets/textures/ui/menu/";
    public const string TexFxDir = "res://assets/effects/textures/";
    public const string FontBttPath = "res://assets/fonts/btt.ttf";

    /// <summary>全屏锚定根容器(不占位、不吞输入)</summary>
    public static Control MakeRoot(Node parent, string name = "Root")
    {
        var root = new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        parent.AddChild(root);
        return root;
    }

    /// <summary>Unity 中心锚点(尺寸 sizeDelta,Y 向上)+ 可选缩放 → Godot 中心锚点(Y 向下)</summary>
    public static void Place(Control c, float x, float yUp, float w, float h, float sx = 1.0f, float sy = 1.0f)
    {
        c.SetAnchorsPreset(Control.LayoutPreset.Center);
        c.OffsetLeft = x - w / 2.0f;
        c.OffsetRight = x + w / 2.0f;
        c.OffsetTop = -yUp - h / 2.0f;
        c.OffsetBottom = -yUp + h / 2.0f;
        c.PivotOffset = new Vector2(w / 2.0f, h / 2.0f);
        c.Scale = new Vector2(sx, sy);
    }

    /// <summary>Unity 点锚点布局:anchor (ax,ay)(Y 向上)、pivot (px,py)、anchoredPosition (x,yUp)、sizeDelta (w,h)。
    /// ax/ay=0.5 时与 Place 完全一致;anchor 贴边的元素(如 BackBtn 右上)随 expand 模式扩展区正确吸附。</summary>
    public static void PlaceAnchored(Control c, float ax, float ay, float px, float py,
        float x, float yUp, float w, float h)
    {
        c.AnchorLeft = ax;
        c.AnchorRight = ax;
        c.AnchorTop = 1.0f - ay;
        c.AnchorBottom = 1.0f - ay;
        c.OffsetLeft = x - px * w;
        c.OffsetRight = x + (1.0f - px) * w;
        c.OffsetTop = -(yUp + (1.0f - py) * h);
        c.OffsetBottom = -(yUp - py * h);
        c.PivotOffset = new Vector2(px * w, (1.0f - py) * h);
    }

    public static TextureRect TexRect(string name, string texPath)
    {
        return new TextureRect
        {
            Name = name,
            Texture = GD.Load<Texture2D>(texPath),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    public static Label MakeLabel(string text, int fontSize, Color color, bool useBtt = true)
    {
        var l = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_color", color);
        if (useBtt)
            l.AddThemeFontOverride("font", GD.Load<Font>(FontBttPath));
        return l;
    }

    /// <summary>窗口像素(鼠标/枪口投影)→ 画布逻辑坐标;任意分辨率/宽高比下都正确</summary>
    public static Vector2 WindowToLogical(Viewport vp, Vector2 windowPos) =>
        vp.GetCanvasTransform().AffineInverse() * windowPos;
}
