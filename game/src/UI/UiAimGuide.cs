using Godot;

namespace FPSGame;

/// <summary>
/// 2D 激光指引(画在 UI 最上层 layer 30,保证激光压在 2D UI 之上),1:1 复刻原作
/// UIController.cs + FPS Pack Lazer(LineRenderer)的视觉效果:
///   光束 = 原作 LineRenderer(枪原点 → 枪前方 200m,线宽 0.02295→0.03,Lazer.mat
///   红/绿加色):两端世界宽度分别投影成像素宽,近端粗、远端细,与原作透视锥形一致。
///   实现为 K 段旋转 TextureRect(宽度逐段线性收敛)——本引擎构建的 Polygon2D 不吃
///   CanvasItemMaterial 加色且对 CW 顶点序剔除,Line2D 不渲染,故全走已验证的
///   TextureRect 路径(lazer.png 为加色设计:RGB 亮纹、无 alpha)。
///   光点 = 原作 Flash 命中光斑:3D 命中(如怪兽)贴在 hit.point - 枪前向×10 的投影处
///   (原作 UIController.cs:83-96);悬停按钮贴在瞄准点;平时贴瞄准点作鼠标反馈。
///   悬停按钮(焦点)时光点放大,并叠加脉冲光晕——焦点发亮效果。
/// 颜色与 3D 版 LaserSight 一致(右红/左绿)。战斗场景用 3D LaserSight(命中 3D 模型)。
/// </summary>
public partial class UiAimGuide : CanvasLayer
{
    public const int GuideLayer = 30;
    public const float BeamLength = 200.0f;    // 原作 LineRenderer 端点 z:0→200
    public const float WidthNear = 0.02295f;   // 原作 widthCurve 起点(世界宽)
    public const float WidthFar = 0.03f;       // 原作 widthCurve 终点
    public const int BeamSegments = 5;         // 锥形分段数(宽度逐段收敛)
    public const float MinBeamPixels = 2.0f;   // 光束投影过短(正对镜头)时不画
    public const float HoverScale = 1.6f;
    public const float GlowAlpha = 0.55f;
    public const float GlowPulse = 0.30f;
    public const float HitBackOffset = 10.0f;  // 原作:命中敌人时光点自命中点回退 10m

    private readonly TextureRect[] _beamSegs = new TextureRect[BeamSegments];
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
        var beamTex = GD.Load<Texture2D>(LaserSight.BeamTexturePath);
        // 光束 K 段:旋转 TextureRect,lazer.png 亮纹纵向压扁后亮核落在段中线
        for (int i = 0; i < BeamSegments; i++)
        {
            var seg = new TextureRect
            {
                Name = $"Beam{i}",
                Texture = beamTex,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Modulate = new Color(tint.R, tint.G, tint.B, 0.85f),
                Material = add,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false,
            };
            g.AddChild(seg);
            g._beamSegs[i] = seg;
        }
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

    /// <summary>每帧更新激光:光束 = 枪口原点 → 枪前向 200m 的屏幕投影(锥形);
    /// 光点 = 3D 命中点(hitWorld,怪物等)回退 10m 的投影,无 3D 命中时贴 uiTarget(按钮/鼠标反馈);
    /// hoverButton=悬停按钮(发亮)。cam 为 SubViewport 内相机,投影点即画布逻辑坐标。</summary>
    public void SetAim(Camera3D cam, Vector3 muzzleWorld, Vector3 forward, Vector3? hitWorld,
        Vector2 uiTarget, bool hoverButton)
    {
        _hover = hoverButton;
        Vector3 farWorld = muzzleWorld + forward * BeamLength;
        bool beamOk = !cam.IsPositionBehind(muzzleWorld) && !cam.IsPositionBehind(farWorld);
        if (beamOk)
        {
            Vector2 p0 = cam.UnprojectPosition(muzzleWorld);
            Vector2 p1 = cam.UnprojectPosition(farWorld);
            var d = p1 - p0;
            float len = d.Length();
            if (len < MinBeamPixels)
            {
                beamOk = false;
            }
            else
            {
                float w0 = ProjectedWidth(cam, muzzleWorld, WidthNear);
                float w1 = ProjectedWidth(cam, farWorld, WidthFar);
                float angle = d.Angle();
                for (int i = 0; i < BeamSegments; i++)
                {
                    var a = p0 + d * (i / (float)BeamSegments);
                    var b = p0 + d * ((i + 1) / (float)BeamSegments);
                    float w = Mathf.Max(Mathf.Lerp(w0, w1, (i + 0.5f) / BeamSegments), 1.0f);
                    var seg = _beamSegs[i];
                    // 矩形 (segLen×w) 绕左中点(段起点)旋转到瞄准方向
                    seg.Size = new Vector2((b - a).Length() + 1.0f, w); // +1 防段间细缝
                    seg.Position = a - new Vector2(0.0f, w * 0.5f);
                    seg.PivotOffset = new Vector2(0.0f, w * 0.5f);
                    seg.Rotation = angle;
                    seg.Visible = true;
                }
            }
        }
        if (!beamOk)
            foreach (var seg in _beamSegs)
                seg.Visible = false;
        // 光点:3D 命中(怪物)贴命中点回退 10m 的投影;否则贴瞄准点(按钮/鼠标反馈)
        Vector2 dotPos = uiTarget;
        if (hitWorld is Vector3 hit && !cam.IsPositionBehind(hit - forward * HitBackOffset))
            dotPos = cam.UnprojectPosition(hit - forward * HitBackOffset);
        _glow.Visible = true;
        _glow.Position = dotPos - Vector2.One * (_dotSize * 1.5f);
        _dot.Visible = true;
        _dot.Position = dotPos - Vector2.One * (_dotSize * 0.5f);
        _dot.Scale = Vector2.One * (hoverButton ? HoverScale : 1.0f);
    }

    /// <summary>世界宽度在该深度上的屏幕像素宽(沿相机右方向取两点投影之差)</summary>
    private static float ProjectedWidth(Camera3D cam, Vector3 pos, float worldWidth)
    {
        var right = cam.GlobalBasis.X.Normalized() * (worldWidth * 0.5f);
        float px = (cam.UnprojectPosition(pos + right) - cam.UnprojectPosition(pos - right)).Length() * 0.5f;
        return Mathf.Max(px, 0.3f); // 远端亚像素时保留发丝宽(原作 LineRenderer 亦然)
    }

    public void HideGuide()
    {
        foreach (var seg in _beamSegs)
            seg.Visible = false;
        _dot.Visible = false;
        _glow.Visible = false;
    }

    /// <summary>当前光点位置(自检测试用)</summary>
    public Vector2 CurrentTarget => _dot.Position + Vector2.One * (_dotSize * 0.5f);

    /// <summary>光束当前是否可见(自检测试用)</summary>
    public bool BeamVisible => _beamSegs[0].Visible;

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
