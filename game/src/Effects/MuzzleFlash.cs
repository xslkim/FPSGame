using Godot;

namespace FPSGame;

/// <summary>
/// 枪口火光:1:1 移植 Unity FPS Pack 的 MuzzleFlash1.prefab(Assets/UI/Impark/)。
/// Fire() 一次 = 原作 SetActive(false)→(true) 重触发:
///   Flame — Flame1.png 2×4 翻页(8 帧 / 0.15s),Particles/Additive,染色 (1,0.803,0.706);
///   Smoke — Smoke.png 8×8 翻页(64 帧,frameOverTime 曲线前快后慢),染色 (0.596,0.596,0.596,0.161),
///           生长曲线(0.43→0.89→1),寿命取中值 1.5s(原作 2 次 burst 合并为一层);
///   Light — FPSLightCurves:0→0.979(0.005s)→0(0.15s),色 (1,0.393,0),范围 6,
///           位置照 prefab 在枪身上方偏后。
/// 子节点偏移照 prefab 原值(Z 按 Godot 相机约定取反);quad 尺寸按 Unity 真值截图
/// 实测标定(原作 stretched-billboard 的呈现远小于 startSize 原值 0.4/1.25)。
/// 翻页用材质的 UV1 offset/scale 实现(AtlasTexture 不适用于 3D 材质)。
/// </summary>
public partial class MuzzleFlash : Node3D
{
    private const float FlameLife = 0.15f;   // UVModule:8 帧在 0.15s 寿命内播完
    private const float SmokeLife = 1.5f;    // startLifetime 1~2 取中值
    private const float LightPeak = 0.9793887f; // FPSLightCurves 峰值 × GraphIntensityMultiplier(1)

    private StandardMaterial3D _flameMat = null!;
    private StandardMaterial3D _smokeMat = null!;
    private MeshInstance3D _flame = null!;
    private MeshInstance3D _smoke = null!;
    private OmniLight3D _light = null!;

    private float _t = -1.0f; // <0 = 空闲

    /// <summary>调试:截图隔离用(隐藏烟雾/火焰层)</summary>
    public bool DebugNoSmoke;
    public bool DebugNoFlame;

    public override void _Ready()
    {
        Visible = false;
        // prefab 子节点 Flame @(0,0,-0.092) 绕 Z 转 180°(原作欧拉 (-180,180,0),火焰指向枪口前方);
        // Smoke @(0,0,-0.021) / Point light @(0,0.44,-0.87)
        // Flame 不用 billboard:面片固定在枪口平面(法线朝相机),随枪口旋转而沿枪管拉长,
        // 近似原作 stretched billboard;烟雾球状用 billboard 即可。
        _flame = MakeQuad("Flame", new Vector3(0, 0, 0.092f), 0.18f, 0.09f,
            UiKit.TexFxDir + "fps_flame1.png", 2, 4,
            BaseMaterial3D.BlendModeEnum.Add, new Color(1, 0.8032454f, 0.7058823f),
            false, out _flameMat);
        _flame.Rotation = new Vector3(0, 0, Mathf.Pi);
        _smoke = MakeQuad("Smoke", new Vector3(0, 0, 0.021f), 0.125f, 0.125f,
            UiKit.TexFxDir + "fps_smoke.png", 8, 8,
            BaseMaterial3D.BlendModeEnum.Mix, new Color(0.5955882f, 0.5955882f, 0.5955882f, 0.16078432f),
            true, out _smokeMat);
        _light = new OmniLight3D
        {
            Name = "Light",
            Position = new Vector3(0, 0.44f, 0.87f),
            LightColor = new Color(1, 0.39310342f, 0),
            LightEnergy = 0.0f,
            OmniRange = 6.0f,
        };
        AddChild(_light);
    }

    /// <summary>开火:重播火光/烟雾翻页 + 灯光曲线(可连击重触发)</summary>
    public void Fire()
    {
        _t = 0.0f;
        Visible = true;
        _flame.Visible = !DebugNoFlame;
        _smoke.Visible = !DebugNoSmoke;
        float s = SizeCurve(0.0f);
        _smoke.Scale = new Vector3(s, s, s);
        ApplyFrame(_flameMat, 2, 4, 0);
        ApplyFrame(_smokeMat, 8, 8, 0);
        _light.LightEnergy = 0.0f;
    }

    public override void _Process(double delta)
    {
        if (_t < 0.0f)
            return;
        _t += (float)delta;
        if (_t <= FlameLife)
        {
            ApplyFrame(_flameMat, 2, 4, (int)(_t / FlameLife * 8.0f));
            _light.LightEnergy = LightCurve(_t / FlameLife);
        }
        else if (_flame.Visible)
        {
            _flame.Visible = false;
            _light.LightEnergy = 0.0f;
        }
        float f = _t / SmokeLife;
        if (f >= 1.0f)
        {
            _t = -1.0f;
            Visible = false;
            return;
        }
        ApplyFrame(_smokeMat, 8, 8, (int)(FrameCurve(f) * 64.0f * 0.9999f));
        float s = SizeCurve(f);
        _smoke.Scale = new Vector3(s, s, s);
    }

    // ---------------------------------------------------------------- 内部

    private MeshInstance3D MakeQuad(string nodeName, Vector3 pos, float w, float h,
        string texPath, int cols, int rows,
        BaseMaterial3D.BlendModeEnum blend, Color tint, bool billboard, out StandardMaterial3D mat)
    {
        mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = blend,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = billboard
                ? BaseMaterial3D.BillboardModeEnum.Enabled
                : BaseMaterial3D.BillboardModeEnum.Disabled,
            AlbedoColor = tint,
            AlbedoTexture = LoadAdditiveFriendly(texPath, blend),
            Uv1Scale = new Vector3(1.0f / cols, 1.0f / rows, 1.0f),
        };
        var mi = new MeshInstance3D
        {
            Name = nodeName,
            Position = pos,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new QuadMesh { Size = new Vector2(w, h) },
            MaterialOverride = mat,
        };
        AddChild(mi);
        return mi;
    }

    /// <summary>切到翻页第 index 帧(行优先,与 Unity UVModule 一致)</summary>
    private static void ApplyFrame(StandardMaterial3D mat, int cols, int rows, int index)
    {
        index = Mathf.Clamp(index, 0, cols * rows - 1);
        mat.Uv1Offset = new Vector3((float)(index % cols) / cols, (float)(index / cols) / rows, 0.0f);
    }

    /// <summary>
    /// 加色混合贴图预处理:原作火焰贴图 alpha 全≈1(黑底不透),直接 Add 混合会把
    /// 透明视口的隐藏底色矩形带出来;把 alpha 改为亮度(黑→0)后黑底才真正透明。
    /// </summary>
    private static Texture2D LoadAdditiveFriendly(string path, BaseMaterial3D.BlendModeEnum blend)
    {
        var tex = GD.Load<Texture2D>(path);
        if (blend != BaseMaterial3D.BlendModeEnum.Add)
            return tex;
        var img = tex.GetImage();
        if (img == null)
            return tex;
        img.Convert(Image.Format.Rgba8);
        for (int y = 0; y < img.GetHeight(); y++)
        for (int x = 0; x < img.GetWidth(); x++)
        {
            var p = img.GetPixel(x, y);
            p.A = Mathf.Max(p.R, Mathf.Max(p.G, p.B));
            img.SetPixel(x, y, p);
        }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>FPSLightCurves 的 LightCurve:(0,0)→(0.0362,0.979)→(1,0) 分段线性近似</summary>
    private static float LightCurve(float f)
    {
        const float peakT = 0.03624959f;
        return f <= peakT
            ? LightPeak * (f / peakT)
            : LightPeak * (1.0f - (f - peakT) / (1.0f - peakT));
    }

    /// <summary>Smoke 的 SizeModule:(0,0.428)→(0.082,0.891)→(1,1) 分段线性近似</summary>
    private static float SizeCurve(float f)
    {
        const float k = 0.08232692f;
        return f <= k
            ? Mathf.Lerp(0.4276316f, 0.89105415f, f / k)
            : Mathf.Lerp(0.89105415f, 1.0f, (f - k) / (1.0f - k));
    }

    /// <summary>Smoke 的 UVModule frameOverTime:(0,0)→(0.106,0.467)→(1,1) 分段线性近似</summary>
    private static float FrameCurve(float f)
    {
        const float k = 0.10570842f;
        return f <= k
            ? Mathf.Lerp(0.0f, 0.46739244f, f / k)
            : Mathf.Lerp(0.46739244f, 1.0f, (f - k) / (1.0f - k));
    }
}
