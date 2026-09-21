using Godot;

namespace FPSGame;

/// <summary>
/// 屏幕激光红点(2D):贴逻辑坐标的光斑,与枪口 3D 激光束(LaserSight)配合,
/// 精确标示"现在开枪会命中哪个 2D UI"。悬停到按钮上时放大,给出明确命中反馈。
/// 层级 30(高于 MessageBox 20),保证任何弹框上也能看到瞄准点。
/// </summary>
public partial class UiAimDot : CanvasLayer
{
    public const int DotLayer = 30;
    public const float HoverScale = 1.6f;

    private TextureRect _rect = null!;
    private float _size;

    /// <summary>创建并挂到界面根:tint=颜色(右红/左绿,与 LaserSight 同色),size=逻辑像素直径</summary>
    public static UiAimDot Create(Node parent, Color tint, float size = 28.0f)
    {
        var d = new UiAimDot { Name = "AimDot", Layer = DotLayer };
        d._size = size;
        d._rect = new TextureRect
        {
            Name = "Dot",
            Texture = LaserSight.GlowTexture,
            Modulate = new Color(tint.R, tint.G, tint.B, 1.0f),
            Size = new Vector2(size, size),
            PivotOffset = new Vector2(size * 0.5f, size * 0.5f),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        d.AddChild(d._rect);
        parent.AddChild(d);
        return d;
    }

    /// <summary>把红点放到画布逻辑坐标(中心对齐);hover=true 悬停按钮时放大</summary>
    public void SetPoint(Vector2 logical, bool hover)
    {
        _rect.Visible = true;
        _rect.Position = logical - Vector2.One * (_size * 0.5f);
        _rect.Scale = Vector2.One * (hover ? HoverScale : 1.0f);
    }

    public void HideDot() => _rect.Visible = false;

    /// <summary>当前位置(自检测试用)</summary>
    public Vector2 CurrentCenter => _rect.Position + Vector2.One * (_size * 0.5f);
}
