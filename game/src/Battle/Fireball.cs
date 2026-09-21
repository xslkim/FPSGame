using Godot;

namespace FPSGame;

/// <summary>
/// 包头僵尸的火球(BaotouFireball):演出弹道(朝相机飞)+ 命中即扣血(必中,原碰撞必中语义)。
/// 原作走 CollisionEnter → 爆炸音 → 0.1s 后 HitPlayer(Both);重写为飞抵相机后同样结算。
/// </summary>
public partial class Fireball : Node3D
{
    public const float Speed = 12.0f;      // 演出飞行速度(视觉)
    public const float HitDelay = 1.0f;    // 发射后必中结算时机(legacy 语义)

    private float _attack;
    private Game.AttackType _type;
    private float _t;

    public static void Spawn(Node parent, Vector3 pos, float attack, Game.AttackType type)
    {
        var fb = new Fireball { _attack = attack, _type = type };
        parent.AddChild(fb);
        fb.GlobalPosition = pos;
        // 视觉:火光球(billboard 发光片)
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoColor = new Color(1.0f, 0.55f, 0.15f),
            AlbedoTexture = GD.Load<Texture2D>("res://assets/effects/textures/fireball_core.png"),
        };
        var mi = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(0.5f, 0.5f) },
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        fb.AddChild(mi);
        var light = new OmniLight3D
        {
            LightColor = new Color(1.0f, 0.5f, 0.1f),
            LightEnergy = 2.0f,
            OmniRange = 3.0f,
        };
        fb.AddChild(light);
        if (ResourceLoader.Exists("res://assets/audio/effects/fireball.wav"))
        {
            var asp = new AudioStreamPlayer3D
            {
                Stream = GD.Load<AudioStream>("res://assets/audio/effects/fireball.wav"),
            };
            fb.AddChild(asp);
            asp.Play();
        }
    }

    public override void _Process(double delta)
    {
        float d = (float)delta;
        _t += d;
        // 演出:朝相机飞
        var cam = GetViewport().GetCamera3D();
        if (cam != null && _t < HitDelay)
        {
            Vector3 dir = (cam.GlobalPosition - GlobalPosition).Normalized();
            GlobalPosition += dir * Speed * d;
        }
        if (_t >= HitDelay)
        {
            PlayerState.Instance.HitPlayer(_attack, _type, PlayerState.Side.Both);
            QueueFree();
        }
    }
}
