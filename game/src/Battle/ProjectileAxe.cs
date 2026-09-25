using Godot;

namespace FPSGame;

/// <summary>
/// 飞斧投射物(1:1 原作 ProjectileAxe.prefab + ProjectileEx.cs):
/// 速度 2m/s 直线飞行(无重力)、寿命 10s、三轴自旋 Random.Range(-360,-720)°/s;
/// 命中判定 = 进入相机空间 |z|&lt;0.5 且 |x|&lt;1 且 |y|&lt;1(无碰撞体,纯坐标判定),
/// 命中按斧头在屏幕 x 分左右侧扣血(原作 HitPlayer),命中后斧头继续飞 1s 消失(原作 Destroy 1s)。
/// 视觉复用斧头怪手持斧(axe_02)网格(原作 ProjectileAxe.prefab 为同源斧头网格)。
/// </summary>
public partial class ProjectileAxe : Node3D
{
    public const float Speed = 2.0f;        // ProjectileEx.m_speed
    public const float LifeTime = 10.0f;    // ProjectileEx.m_lifeTime
    public const float HitFreeDelay = 1.0f; // 原作 Destroy(gameObject, 1f)
    public const float CamHitZ = 0.5f;      // 相机空间命中盒(ProjectileEx.cs:107)
    public const float CamHitXy = 1.0f;

    private Vector3 _dir;
    private Vector3 _spinDeg;
    private float _attack;
    private Game.AttackType _type;
    private float _life = LifeTime;
    private bool _hitPlayer;
    private float _hitTime;

    /// <summary>从手斧网格出处生成:位置/朝向/缩放与出手瞬间的手斧逐位一致(原作手斧旋转抛出)</summary>
    public static void Spawn(Node parent, MeshInstance3D? srcMesh, Vector3 pos, Vector3 dir,
        float attack, Game.AttackType type)
    {
        var p = GD.Load<PackedScene>("res://scenes/battle/projectile_axe.tscn").Instantiate<ProjectileAxe>();
        parent.AddChild(p);
        p._dir = dir.Normalized();
        p._attack = attack;
        p._type = type;
        // 原作 m_rotationMin(-360)~m_rotationMax(-720) 每轴随机
        p._spinDeg = new Vector3(
            (float)GD.RandRange(-720.0, -360.0),
            (float)GD.RandRange(-720.0, -360.0),
            (float)GD.RandRange(-720.0, -360.0));
        var meshInst = p.GetNode<MeshInstance3D>("AxeMesh");
        if (srcMesh?.Mesh != null)
        {
            meshInst.Mesh = srcMesh.Mesh;
            for (int i = 0; i < srcMesh.GetSurfaceOverrideMaterialCount(); i++)
                meshInst.SetSurfaceOverrideMaterial(i, srcMesh.GetSurfaceOverrideMaterial(i));
            p.GlobalTransform = srcMesh.GlobalTransform; // 朝向/缩放逐位(含 FBX 烘焙缩放)
        }
        p.GlobalPosition = pos;
    }

    public override void _Process(double delta)
    {
        float d = (float)delta;
        _life -= d;
        if (_life <= 0.0f)
        {
            QueueFree();
            return;
        }
        if (_hitPlayer)
        {
            _hitTime += d;
            if (_hitTime >= HitFreeDelay)
            {
                QueueFree();
                return;
            }
        }
        // 自旋(原作 transform.Rotate 局部轴)
        RotateObjectLocal(Vector3.Right, Mathf.DegToRad(_spinDeg.X) * d);
        RotateObjectLocal(Vector3.Up, Mathf.DegToRad(_spinDeg.Y) * d);
        RotateObjectLocal(Vector3.Back, Mathf.DegToRad(_spinDeg.Z) * d);
        GlobalPosition += _dir * Speed * d;
        if (!_hitPlayer)
            CheckHit();
    }

    /// <summary>相机空间盒判定(原作 worldToCameraMatrix):|z|&lt;0.5 且 |x|&lt;1 且 |y|&lt;1</summary>
    private void CheckHit()
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null)
            return;
        Vector3 local = cam.GlobalTransform.AffineInverse() * GlobalPosition;
        if (Mathf.Abs(local.Z) < CamHitZ && Mathf.Abs(local.X) < CamHitXy && Mathf.Abs(local.Y) < CamHitXy)
            HitPlayer(cam);
    }

    /// <summary>命中:按斧头屏幕 x 分侧扣血(原作 ProjectileEx.HitPlayer),1s 后消失</summary>
    private void HitPlayer(Camera3D cam)
    {
        var side = PlayerState.Side.Right;
        if (!cam.IsPositionBehind(GlobalPosition))
        {
            float sx = cam.UnprojectPosition(GlobalPosition).X;
            float w = GetViewport().GetVisibleRect().Size.X;
            side = sx > w * 0.5f ? PlayerState.Side.Right : PlayerState.Side.Left;
        }
        PlayerState.Instance.HitPlayer(_attack, _type, side);
        _hitPlayer = true;
    }
}
