using Godot;

namespace FPSGame;

/// <summary>
/// Boss 弱点爱心(1:1 Baotou.prefab 的 FX_Pickup_Heart_01:挂在脊柱骨上的悬浮爱心,
/// tag=Enemy + BoxHead 转发 Boss)。Boss 本体打不到(身体 layer5 只起弹着特效),
/// 只有打爱心才掉血。视觉:billboard 红心(heart.png)+ 呼吸脉动 + 上下浮动。
/// </summary>
public partial class BossHeart : StaticBody3D
{
    public const float HeartSize = 0.35f;      // 爱心视觉尺寸(米)
    public const float BobAmp = 0.06f;         // 浮动幅度
    public const float BobSpeed = 2.5f;
    public const float PulseAmp = 0.12f;       // 脉动缩放幅度

    private Monster _boss = null!;
    private MeshInstance3D _sprite = null!;
    private CollisionShape3D _col = null!;
    private bool _lastActive;
    private double _t;

    /// <summary>挂到 Boss 身上:优先脊柱骨(BoneAttachment3D 找 Spine1),失败退固定偏移</summary>
    public static BossHeart Create(Monster boss)
    {
        var h = new BossHeart { Name = "BossHeart" };
        h._boss = boss;
        // 命中盒:0.5³(原作 BoxCollider)
        h._col = new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One * 0.5f } };
        h.AddChild(h._col);
        h.CollisionLayer = 2; // Enemy 层(FireSystem 射线按此判敌)
        h.CollisionMask = 0;
        // 红心 billboard(heart.png 黑底无 alpha → 加色混合,黑=不可见;染红)
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1.0f, 0.25f, 0.45f, 0.95f),
            AlbedoTexture = GD.Load<Texture2D>("res://assets/effects/textures/heart.png"),
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        };
        h._sprite = new MeshInstance3D
        {
            Name = "Heart",
            Mesh = new QuadMesh { Size = new Vector2(HeartSize, HeartSize), Material = mat },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        h.AddChild(h._sprite);
        // 挂脊柱骨(原作父=Bone_ Spine1,局部偏移 -0.096,0.189,0.089);找不到退 Boss 胸口偏移
        var skel = boss.FindChild("Skeleton3D", true, false) as Skeleton3D;
        int bone = -1;
        if (skel != null)
            for (int i = 0; i < skel.GetBoneCount(); i++)
            {
                if (skel.GetBoneName(i).Contains("Spine1"))
                {
                    bone = i;
                    break;
                }
            }
        if (skel != null && bone >= 0)
        {
            var attach = new BoneAttachment3D { BoneIdx = bone };
            skel.AddChild(attach);
            attach.AddChild(h);
            h.Position = new Vector3(0.096f, 0.189f, -0.089f); // 原作局部偏移(坐标系镜像)
        }
        else
        {
            boss.AddChild(h);
            h.Position = new Vector3(0.0f, 1.45f, 0.0f); // 胸口高度兜底
        }
        return h;
    }

    /// <summary>命中转发(对应原作 BoxHead:BoxHead.HitType 恒 Body)</summary>
    public string Hit(float attack, Vector3 point, int hitType, int side) =>
        _boss.Hit(attack, point, (Game.HitType)hitType, (PlayerState.Side)side);

    public override void _Process(double delta)
    {
        // Boss 池化隐藏时关闭碰撞(隐藏尸体挡射线)
        bool active = _boss.IsActiveState && !_boss.IsDead;
        if (active != _lastActive)
        {
            _lastActive = active;
            _col.SetDeferred(CollisionShape3D.PropertyName.Disabled, !active);
        }
        _t += delta;
        // 呼吸脉动 + 上下浮动
        float pulse = 1.0f + PulseAmp * Mathf.Sin((float)_t * 4.0f);
        _sprite.Scale = Vector3.One * pulse;
        var p = _sprite.Position;
        p.Y = BobAmp * Mathf.Sin((float)_t * BobSpeed);
        _sprite.Position = p;
    }
}
