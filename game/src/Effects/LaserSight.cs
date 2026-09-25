using Godot;

namespace FPSGame;

/// <summary>
/// 激光瞄准器(还原原作 FPS Pack Lazer:右手红 / 左手绿、加色混合、贴图滚动;
/// Assets/AssetTools/FPS Pack/Materials/Effects/Lazer.mat · LazerLeft.mat · FPSLazer.shader)。
/// 光束 = 圆柱体从枪口延伸到瞄准点(战斗延伸到射线命中点,UI 界面延伸到虚拟画布平面),
/// 终点红点 = billboard 光斑,标示实际命中位置(3D 模型或 3D 按钮;2D UI 用 UiAimGuide)。
/// TopLevel 节点,端点一律传全局坐标;滚动动画自驱动,宿主每帧只需 SetBeam/HideBeam。
/// </summary>
public partial class LaserSight : Node3D
{
    public static readonly Color RightRed = new(0.990566f, 0.0f, 0.0f, 0.8156863f);          // 原作 Lazer.mat
    public static readonly Color LeftGreen = new(0.0f, 0.99215686f, 0.13022026f, 0.8156863f); // 原作 LazerLeft.mat

    public const string BeamTexturePath = "res://assets/effects/textures/lazer.png";
    public const float TilePerUnit = 2.5f;  // 原作 _MainTex 平铺 500 / 线长 200
    public const float ScrollSpeed = 6.0f;  // 原作 _Direction 滚动(视觉等效)
    public const float MinLength = 1e-3f;

    private static Texture2D? _glowTex;

    /// <summary>程序化径向光斑(带 alpha 平方衰减;assets 的 glow_circle.png 无 alpha 通道会带黑底,故自绘)</summary>
    public static Texture2D GlowTexture
    {
        get
        {
            if (_glowTex != null)
                return _glowTex;
            var img = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float d = new Vector2(x - 31.5f, y - 31.5f).Length() / 32.0f;
                float a = Mathf.Clamp(1.0f - d, 0.0f, 1.0f);
                img.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, a * a));
            }
            _glowTex = ImageTexture.CreateFromImage(img);
            return _glowTex;
        }
    }

    private MeshInstance3D _beam = null!;
    private StandardMaterial3D _mat = null!;
    private Sprite3D? _dot;
    private double _scroll;

    /// <summary>创建激光:tint=颜色,radius=远端光束半径(原作战斗 widthCurve 远端 0.03/2),
    /// withDot=是否带终点红点,nearRadius=近端(枪口)半径(原作 widthCurve 近端 0.0089/2 近细远粗;
    /// 不传则与 radius 相同成圆柱)</summary>
    public static LaserSight Create(Color tint, float radius, bool withDot, float dotWorldSize = 0.09f, float nearRadius = -1.0f)
    {
        var l = new LaserSight { Name = "LaserSight", TopLevel = true, Visible = false };
        l._mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, // 原作 Lighting Off
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,          // 原作 Blend SrcAlpha One
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = tint,
            TextureRepeat = true,
        };
        if (ResourceLoader.Exists(BeamTexturePath))
        {
            // lazer.png 亮纹沿贴图 U 轴;圆柱体 V 轴才沿光束方向,转 90° 对齐(原作 LineRenderer U 沿线长)
            var img = GD.Load<Texture2D>(BeamTexturePath).GetImage();
            img.Rotate90(ClockDirection.Clockwise);
            l._mat.AlbedoTexture = ImageTexture.CreateFromImage(img);
        }
        l._beam = new MeshInstance3D
        {
            Name = "Beam",
            // 圆柱高沿本地 Y,转 90° 后沿父级 Z 轴;根节点 -Z 指向目标。
            // 转 +90° 后圆柱 Top(+Y)朝 +Z 即枪口(近)端 → TopRadius=近端细,BottomRadius=远端粗
            // (原作 LineRenderer widthCurve 0.0089→0.03,线性,沿线长 200m)
            Rotation = new Vector3(Mathf.Pi / 2.0f, 0.0f, 0.0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new CylinderMesh
            {
                TopRadius = nearRadius >= 0.0f ? nearRadius : radius,
                BottomRadius = radius,
                Height = 1.0f,
                RadialSegments = 8,
                Material = l._mat,
            },
        };
        l.AddChild(l._beam);
        if (withDot)
        {
            var tex = GlowTexture;
            l._dot = new Sprite3D
            {
                Name = "Dot",
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Texture = tex,
                Modulate = new Color(tint.R, tint.G, tint.B, 1.0f),
                PixelSize = dotWorldSize / Mathf.Max(tex.GetWidth(), 1),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            l.AddChild(l._dot);
        }
        return l;
    }

    /// <summary>显示并拉伸光束:全局 from → to,终点红点贴在 to(长度≈0 时隐藏)</summary>
    public void SetBeam(Vector3 fromGlobal, Vector3 toGlobal)
    {
        var d = toGlobal - fromGlobal;
        float len = d.Length();
        if (len < MinLength)
        {
            Visible = false;
            return;
        }
        Visible = true;
        // LookAt 使 -Z 指向目标;dir 接近 ±Up 时换参考轴避免退化
        var up = Mathf.Abs(d.Normalized().Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
        GlobalTransform = new Transform3D(Basis.Identity, fromGlobal).LookingAt(toGlobal, up);
        _beam.Scale = new Vector3(1.0f, len, 1.0f);
        _beam.Position = new Vector3(0.0f, 0.0f, -len * 0.5f);
        _mat.Uv1Scale = new Vector3(1.0f, len * TilePerUnit, 1.0f);
        if (_dot != null)
        {
            _dot.Visible = true;
            _dot.GlobalPosition = toGlobal;
        }
    }

    /// <summary>红点移位(如命中点沿法线略抬起防深度冲突)</summary>
    public void SetDotPosition(Vector3 globalPos)
    {
        if (_dot != null)
            _dot.GlobalPosition = globalPos;
    }

    /// <summary>终点红点显隐(光束未命中时通常只留光束)</summary>
    public void ShowDot(bool v)
    {
        if (_dot != null)
            _dot.Visible = v && Visible;
    }

    public void HideBeam() => Visible = false;

    /// <summary>当前是否带红点(自检测试用)</summary>
    public bool HasDot => _dot != null;

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        _scroll = (_scroll + ScrollSpeed * delta) % 1.0;
        _mat.Uv1Offset = new Vector3(0.0f, -(float)_scroll, 0.0f);
    }
}
