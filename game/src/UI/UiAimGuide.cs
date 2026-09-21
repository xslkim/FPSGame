using Godot;

namespace FPSGame;

/// <summary>
/// 2D 激光指引(画在 UI 最上层 layer 30,保证激光压在 2D UI 之上):
/// 光束(Line2D,lazer.png 平铺加色,从枪口投影点到瞄准点)+ 瞄准红点;
/// 悬停按钮(焦点)时光束加粗、红点放大,并叠加脉冲光晕——焦点发亮效果。
/// 颜色与 3D 版 LaserSight 一致(右红/左绿)。2D UI 画在 3D 视口上层,
/// 故 UI 界面的激光必须用 2D 画;战斗场景用 3D LaserSight(命中 3D 模型/3D 按钮)。
/// </summary>
public partial class UiAimGuide : CanvasLayer
{
    public const int GuideLayer = 30;
    public const float BeamWidth = 5.0f;
    public const float BeamHoverWidth = 8.0f;
    public const float HoverScale = 1.6f;
    public const float GlowAlpha = 0.55f;
    public const float GlowPulse = 0.30f;

    private TextureRect _beam = null!;
    private TextureRect _dot = null!;
    private TextureRect _glow = null!;
    private float _dotSize;
    private bool _hover;
    private double _t;

    /// <summary>创建并挂到界面根:tint=颜色(右红/左绿),dotSize=红点逻辑像素直径</summary>
    public static UiAimGuide Create(Node parent, Color tint, float dotSize = 28.0f)
    {
        var g = new UiAimGuide { Name = "AimGuide", Layer = GuideLayer };
        g._dotSize = dotSize;
        var add = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        // 光束:旋转的 TextureRect,lazer.png 亮纹纵向压扁后亮核正好落在光束中线
        // (Line2D 在本引擎构建上不渲染,故用与红点同一渲染路径的 TextureRect)
        g._beam = new TextureRect
        {
            Name = "Beam",
            Texture = GD.Load<Texture2D>(LaserSight.BeamTexturePath),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Modulate = new Color(tint.R, tint.G, tint.B, 0.85f),
            Material = add,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        g.AddChild(g._beam);
        // 悬停光晕(焦点发亮,脉冲;平时隐藏)
        g._glow = new TextureRect
        {
            Name = "Glow",
            Texture = LaserSight.GlowTexture,
            Modulate = new Color(tint.R, tint.G, tint.B, 0.0f),
            Size = new Vector2(dotSize * 3.0f, dotSize * 3.0f),
            PivotOffset = new Vector2(dotSize * 1.5f, dotSize * 1.5f),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = add,
            Visible = false,
        };
        g.AddChild(g._glow);
        // 核心红点
        g._dot = new TextureRect
        {
            Name = "Dot",
            Texture = LaserSight.GlowTexture,
            Modulate = new Color(tint.R, tint.G, tint.B, 1.0f),
            Size = new Vector2(dotSize, dotSize),
            PivotOffset = new Vector2(dotSize * 0.5f, dotSize * 0.5f),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        g.AddChild(g._dot);
        parent.AddChild(g);
        return g;
    }

    /// <summary>显示指引:光束 枪口逻辑点 → 瞄准逻辑点,红点贴瞄准点;hover=悬停按钮(发亮)</summary>
    public void SetAim(Vector2 muzzleLogical, Vector2 targetLogical, bool hover)
    {
        _hover = hover;
        var d = targetLogical - muzzleLogical;
        float len = d.Length();
        float w = hover ? BeamHoverWidth : BeamWidth;
        _beam.Visible = len > 1.0f;
        if (_beam.Visible)
        {
            // 矩形 (len×w) 绕左中点(枪口)旋转到瞄准方向
            _beam.Size = new Vector2(len, w);
            _beam.Position = muzzleLogical - new Vector2(0.0f, w * 0.5f);
            _beam.PivotOffset = new Vector2(0.0f, w * 0.5f);
            _beam.Rotation = d.Angle();
        }
        _glow.Visible = true;
        _glow.Position = targetLogical - Vector2.One * (_dotSize * 1.5f);
        _dot.Visible = true;
        _dot.Position = targetLogical - Vector2.One * (_dotSize * 0.5f);
        _dot.Scale = Vector2.One * (hover ? HoverScale : 1.0f);
    }

    public void HideGuide()
    {
        _beam.Visible = false;
        _dot.Visible = false;
        _glow.Visible = false;
    }

    /// <summary>当前瞄准点(自检测试用)</summary>
    public Vector2 CurrentTarget => _dot.Position + Vector2.One * (_dotSize * 0.5f);

    public override void _Process(double delta)
    {
        if (!_glow.Visible)
            return;
        _t += delta;
        // 悬停光晕:脉冲发亮;非悬停完全收起
        float a = _hover ? GlowAlpha + GlowPulse * Mathf.Sin((float)_t * 6.0f) : 0.0f;
        var c = _glow.Modulate;
        _glow.Modulate = new Color(c.R, c.G, c.B, a);
    }
}
