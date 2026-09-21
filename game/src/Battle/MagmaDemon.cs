using Godot;

namespace FPSGame;

/// <summary>
/// MagmaDemonBlue(6.3 MagmaDemon,照 legacy magma_demon.gd):
/// 出生自定义:忽略关卡传入点,相机前 10m + 四方向(右/左/上/下,Right=2/Up=2 + OutOffset=6,
/// 合计 8)随机一边;先飞到屏幕内随机点(相机前 8~12m、±4m 横向、-1~3m 竖向),
/// 到位面向相机进 idle → CD 到 → attack01/02 循环;
/// 受击动画半速重播,播完回 idle 恢复;死亡坠落 y&lt;-3 回收;等待期悬浮。
/// 移速恒 8(meta move_speed=8,出生 level=0 即 8);颜色变体材质 .tscn BodyMaterial 注入。
/// </summary>
public partial class MagmaDemon : Monster
{
    public const float BornForward = 10.0f;
    public const float BornRight = 2.0f;
    public const float BornUp = 2.0f;
    public const float BornOutOffset = 6.0f;
    public const float FlyArrive = 0.5f;
    public const float HurtAnimSpeed = 0.5f;
    public const float FallRecycleY = -3.0f;

    public enum Phase { FlyIn, Combat }

    [Export] public Material BodyMaterial = null!; // M7 颜色变体材质(blue .tscn 配置)

    /// <summary>出生方向(自检用):0 右 / 1 左 / 2 上 / 3 下</summary>
    public int BornAxis { get; private set; } = -1;

    private Phase _phase = Phase.FlyIn;
    private Vector3 _screenPoint;

    public override void _Ready()
    {
        base._Ready();
        if (BodyMaterial != null)
        {
            foreach (var mi in FindChildren("*", "MeshInstance3D", true, false))
            {
                if (mi is MeshInstance3D m)
                    m.SetSurfaceOverrideMaterial(0, BodyMaterial);
            }
        }
    }

    /// <summary>出生自定义:相机前 10m + 四向随机一边(偏移 2+6=8)</summary>
    protected override void OnBorn()
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null)
            return;
        Vector3 p = cam.GlobalPosition + -cam.GlobalBasis.Z * BornForward;
        BornAxis = (int)(GD.Randi() % 4);
        Vector3 off = BornAxis switch
        {
            0 => cam.GlobalBasis.X * (BornRight + BornOutOffset),
            1 => -cam.GlobalBasis.X * (BornRight + BornOutOffset),
            2 => Vector3.Up * (BornUp + BornOutOffset),
            _ => -Vector3.Up * (BornUp + BornOutOffset),
        };
        GlobalPosition = p + off;
        BornPos = GlobalPosition;
    }

    /// <summary>进入活跃:先飞到屏幕内随机点</summary>
    protected override void EnterActive()
    {
        _phase = Phase.FlyIn;
        var cam = GetViewport().GetCamera3D();
        if (cam != null)
            _screenPoint = cam.GlobalPosition + -cam.GlobalBasis.Z * (float)GD.RandRange(8.0, 12.0)
                + cam.GlobalBasis.X * (float)GD.RandRange(-4.0, 4.0)
                + Vector3.Up * (float)GD.RandRange(-1.0, 3.0);
    }

    protected override void UpdateActive(float delta)
    {
        if (_phase == Phase.FlyIn)
        {
            Vector3 to = _screenPoint - GlobalPosition;
            if (to.Length() < FlyArrive)
            {
                _phase = Phase.Combat;
                FaceCamera(); // 到位 LookAt 相机
                PlayIdle();
                return;
            }
            // 飞行直接位移(可穿地面,演出弹道语义)
            GlobalPosition += to.Normalized() * GetMoveSpeed() * delta;
            return;
        }
        // Combat:hover + idle → CD → attack01/02 循环
        Velocity = Vector3.Zero;
        MoveAndSlide();
        if (IsPlayingAny(Info.AttackAnims))
            return;
        if (IsCurrentAnim(Info.DamageAnim) && Anim.IsPlaying())
            return; // 受击动画(半速)播完回 idle 恢复
        string idleName = Info.IdleAnim ?? Info.Idle2Anim;
        if (!IsCurrentAnim(idleName))
            PlayIdle();
        if (AttackReady())
            DoAttack();
    }

    private void PlayIdle()
    {
        string idleName = Info.IdleAnim ?? Info.Idle2Anim;
        if (Anim.HasAnimation(idleName))
            Anim.Play(idleName, 0.3);
    }

    /// <summary>受击动画半速(基类 hit 已按原速播,这里重播为半速)</summary>
    protected override void OnHurt(Vector3 point, Game.HitType hitType, PlayerState.Side side)
    {
        if (Anim.HasAnimation(Info.DamageAnim))
            Anim.Play(Info.DamageAnim, 0.1, HurtAnimSpeed);
    }

    /// <summary>死亡:坠落 y&lt;-3 回收(不调基类 1.5s 定时回收)</summary>
    protected override void Die()
    {
        CurState = State.Dead;
        Velocity = Vector3.Zero;
        GetNode<CollisionShape3D>("CollisionShape3D")
            .SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
        PlaySound("dead");
        FireSystem.SpawnBloodFlower(this, new Vector3(0.0f, 1.0f, 0.0f));
        if (Anim.HasAnimation(Info.DeadAnim))
            Anim.Play(Info.DeadAnim, 0.1, Info.DeadAnimSpeed);
        OnDeath();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (CurState == State.Dead)
        {
            float d = (float)delta;
            Velocity = new Vector3(Velocity.X, Velocity.Y - Gravity * d, Velocity.Z);
            GlobalPosition += Velocity * d;
            if (GlobalPosition.Y < FallRecycleY)
                Recycle();
            return;
        }
        base._PhysicsProcess(delta);
    }

    /// <summary>飞行怪等待期悬浮(不下坠),等待时长仍按难度语义</summary>
    protected override void UpdateWaiting(float delta)
    {
        WaitTime += delta;
        Velocity = Vector3.Zero;
        if (WaitTime >= WaittingTime)
        {
            CurState = State.Active;
            EnterActive();
        }
    }
}
