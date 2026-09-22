using Godot;

namespace FPSGame;

/// <summary>
/// 怪物头顶血条(世界空间,1:1 Monster/HpReduceNumber.prefab 的 MonstHp:60×10 底
/// + Hp_Yellow 填充 ×红色 tint(1,0,0),scale 0.01 世界画布语义)。掉血飘字池在 FireSystem。
/// </summary>
public partial class MonsterHpBar : Node3D
{
    private MeshInstance3D _fill = null!;
    private float _maxWidth = 0.6f; // 60px × 0.01
    private float _height = 0.1f;   // 10px × 0.01

    public override void _Ready()
    {
        var bgTex = GD.Load<Texture2D>("res://assets/textures/ui/Hp_Green_bg.png");
        var fillTex = GD.Load<Texture2D>("res://assets/textures/ui/Hp_Yellow.png");
        var bgMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoTexture = bgTex,
        };
        var bg = new MeshInstance3D
        {
            Name = "Bg",
            Mesh = new QuadMesh { Size = new Vector2(_maxWidth, _height), Material = bgMat },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(bg);
        var fillMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoTexture = fillTex,
            AlbedoColor = new Color(1.0f, 0.0f, 0.0f, 1.0f), // 原作 HpReduceNumber fill 红色 tint
        };
        _fill = new MeshInstance3D
        {
            Name = "Fill",
            Mesh = new QuadMesh { Size = new Vector2(_maxWidth, _height), Material = fillMat },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_fill);
        SetHp(1.0f);
    }

    /// <summary>value 0~1;填充左锚右缩(照 Slider direction L→R)</summary>
    public void SetHp(float value)
    {
        value = Mathf.Clamp(value, 0.0f, 1.0f);
        Vector3 s = _fill.Scale;
        s.X = Mathf.Max(value, 0.0001f);
        _fill.Scale = s;
        Vector3 p = _fill.Position;
        p.X = -(_maxWidth - _maxWidth * value) * 0.5f;
        _fill.Position = p;
        Visible = value > 0.0f; // 原作血条常显(出生即见,满血满条)
    }
}
