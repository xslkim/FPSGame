using Godot;

namespace FPSGame;

/// <summary>
/// 包头僵尸的火球(原作 BaotouFireball.prefab + EffectSettings(Fireball1.prefab) + BaotouFireball.cs):
/// 发射音(fireball_launch.wav ≈ FireFireAS)→ 脱父沿发射朝向直线飞(MoveSpeed=4,EffectSettings:4774)
/// → 撞相机前 0.3m 的 CameraWall → 爆炸音 FireExplosion4(fireball_hit.wav)+ 爆炸闪光
/// → 0.1s 后 HitPlayer(Both) 双手各扣攻击(15)。
/// 原作碰撞走 LayerMask=512(仅 CameraWall),相机战斗中静止,"撞墙"等价为进入相机前 0.35m。
/// </summary>
public partial class Fireball : Node3D
{
    public const float Speed = 4.0f;           // EffectSettings MoveSpeed(Fireball1.prefab:4774)
    public const float CameraWallDist = 0.35f; // CameraWall:相机前 0.3m 厚 0.1(school_day.unity:117346-117400)
    public const float DamageDelay = 0.1f;     // 原作 Invoke("HitPlayer", 0.1f)
    public const float ExplosionTime = 0.3f;   // 爆炸闪光时长(视觉)
    public const float MaxFlightTime = 8.0f;   // 兜底(相机静止必撞墙)

    private float _attack;
    private Game.AttackType _type;
    private Vector3 _dir;
    private float _t;
    private bool _exploded;
    private MeshInstance3D _quad = null!;
    private OmniLight3D _light = null!;

    public static void Spawn(Node parent, Vector3 pos, float attack, Game.AttackType type)
    {
        var fb = new Fireball { _attack = attack, _type = type };
        parent.AddChild(fb);
        fb.GlobalPosition = pos;
        var cam = fb.GetViewport().GetCamera3D();
        fb._dir = cam != null
            ? (cam.GlobalPosition - pos).Normalized()
            : -fb.GlobalBasis.Z;
        // 视觉:火光球(billboard 发光片)
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            BillboardKeepScale = true, // billboard 默认丢弃世界缩放;Boss 火球 ×10(原作 localScale×10)须生效
            AlbedoColor = new Color(1.0f, 0.55f, 0.15f),
            AlbedoTexture = GD.Load<Texture2D>("res://assets/effects/textures/fireball_core.png"),
        };
        fb._quad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(0.5f, 0.5f) },
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        fb.AddChild(fb._quad);
        fb._light = new OmniLight3D
        {
            LightColor = new Color(1.0f, 0.5f, 0.1f),
            LightEnergy = 2.0f,
            OmniRange = 3.0f,
        };
        fb.AddChild(fb._light);
        // 发射音(原作 FireFireAS.Play())
        fb.PlaySound3d("res://assets/audio/effects/fireball_launch.wav", pos);
    }

    public override void _Process(double delta)
    {
        float d = (float)delta;
        _t += d;
        if (_exploded)
        {
            // 爆炸闪光:放大淡出后消失
            float k = Mathf.Clamp(_t / ExplosionTime, 0.0f, 1.0f);
            _quad.Scale = Vector3.One * (1.0f + k * 2.0f);
            var c = ((StandardMaterial3D)_quad.MaterialOverride).AlbedoColor;
            ((StandardMaterial3D)_quad.MaterialOverride).AlbedoColor = new Color(c.R, c.G, c.B, 1.0f - k);
            _light.LightEnergy = 2.0f * (1.0f - k);
            if (_t >= ExplosionTime)
                QueueFree();
            return;
        }
        GlobalPosition += _dir * Speed * d;
        var cam = GetViewport().GetCamera3D();
        bool hitWall = false;
        if (cam != null)
        {
            // 撞 CameraWall:火球进入相机前 0.35m(相机空间 z ≥ -0.35)
            Vector3 local = cam.GlobalTransform.AffineInverse() * GlobalPosition;
            hitWall = local.Z >= -CameraWallDist;
        }
        if (hitWall || _t >= MaxFlightTime)
            Explode();
    }

    /// <summary>撞墙爆炸(原作 CollisionEnter):爆炸音 FireExplosion4 + 0.1s 后 HitPlayer(Both)</summary>
    private void Explode()
    {
        _exploded = true;
        _t = 0.0f;
        PlaySound3d("res://assets/audio/effects/fireball_hit.wav", GlobalPosition);
        float attack = _attack;
        var type = _type;
        GetTree().CreateTimer(DamageDelay).Timeout += () =>
            PlayerState.Instance.HitPlayer(attack, type, PlayerState.Side.Both);
    }

    /// <summary>3D 音效(挂场景根,火球消失后余音不受影响)</summary>
    private void PlaySound3d(string path, Vector3 pos)
    {
        if (!ResourceLoader.Exists(path))
            return;
        var asp = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path) };
        GetTree().CurrentScene.AddChild(asp);
        asp.GlobalPosition = pos;
        asp.Finished += asp.QueueFree;
        asp.Play();
    }
}
