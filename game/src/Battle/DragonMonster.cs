using Godot;

namespace FPSGame;

/// <summary>
/// Dragon 红/蓝/绿共用(6.3 Dragon;meta key 区分,attack_type: 红=Phy / 蓝=Ice / 绿=Poison):
/// 四点巡回飞行 FarWay→InCamera→Attack→CamOffset 循环(照 legacy dragon.gd):
/// 参数 Forward150/Right160/Up20/CamUp0.1/CamRight20/RightOffset15,
/// Pos2(InCamera)带 rand(-10,10) 横向与 rand(-20,20) 竖向抖动;
/// 入场点恒取右侧(原作左右随机分支为死代码,保留语义);非 Attack 段速度×2;
/// Attack 段与相机距离&lt;attack_radius(75) 播 FireBreathOnce(0.75 倍速)+ 火焰锥特效
/// (fire_breath.tscn 挂龙口 (0,1,-3);蓝/绿逐实例改火焰色调)+ 攻击事件基类半屏结算;
/// TakeDamage 播放中无敌(hit 返回空);死亡坠落 y&lt;-3 回收(不走基类 1.5s 定时)。
/// 三色变体材质由 .tscn BodyMaterial 注入。
/// </summary>
public partial class DragonMonster : Monster
{
    public enum FlySeg { FarWay, InCamera, Attack, CamOffset }

    public const float PForward = 150.0f;
    public const float PRight = 160.0f;
    public const float PUp = 20.0f;
    public const float PCamUp = 0.1f;
    public const float PCamRight = 20.0f;
    public const float PRightOffset = 15.0f;
    public const float Pos2JitterX = 10.0f;  // Pos2 横向 rand(-10,10)
    public const float Pos2JitterY = 20.0f;  // Pos2 竖向 rand(-20,20)
    public const float ArriveDist = 2.0f;
    public const float BreathAnimSpeed = 0.75f;
    public const float BreathClipLen = 0.8f; // clip 0.8s ÷ 0.75 倍速
    public const float FallRecycleY = -3.0f;
    public const string LocomotionAnim = "locomotion";

    private static readonly PackedScene FireBreathScene =
        GD.Load<PackedScene>("res://assets/effects/fire_breath.tscn");
    private static readonly AudioStream SndBreathFire =
        GD.Load<AudioStream>("res://assets/audio/effects/breath_fire.wav");
    private static readonly AudioStream SndBreathIce =
        GD.Load<AudioStream>("res://assets/audio/effects/breath_ice.wav");

    [Export] public Material BodyMaterial = null!; // M7 颜色变体材质(red/blue/green .tscn 配置)

    private FlySeg _segment = FlySeg.FarWay;
    private Vector3 _target;
    private GpuParticles3D _breathFx = null!;
    private Node3D _breathRoot = null!;
    private float _breathTime;
    private AudioStreamPlayer3D _breathAudio = null!;

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

    protected override void OnBorn()
    {
        _segment = FlySeg.FarWay;
        _breathTime = 0.0f;
        _target = MakePoint(_segment);
        EnsureBreathFx();
    }

    /// <summary>入场点恒取右侧(+right*160):原作左右随机分支为死代码恒右(方案 §10),保留语义并标注。</summary>
    private Vector3 MakePoint(FlySeg seg)
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null)
            return GlobalPosition;
        Vector3 p = cam.GlobalPosition;
        Vector3 fwd = -cam.GlobalBasis.Z;
        Vector3 right = cam.GlobalBasis.X;
        return seg switch
        {
            FlySeg.FarWay => p + fwd * PForward + right * PRight + Vector3.Up * PUp,
            FlySeg.InCamera => p + fwd * PCamRight + Vector3.Up * PCamUp
                + right * (float)GD.RandRange(-Pos2JitterX, Pos2JitterX)
                + Vector3.Up * (float)GD.RandRange(-Pos2JitterY, Pos2JitterY),
            // 攻击点取 attack_radius 内(75×0.8=60&lt;75),到位即进入吐息判定
            FlySeg.Attack => p + fwd * AttackRadius * 0.8f + right * PRightOffset,
            FlySeg.CamOffset => p + fwd * PCamRight + right * PRightOffset + Vector3.Up * PCamUp,
            _ => p,
        };
    }

    protected override void UpdateActive(float delta)
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null)
            return;
        if (_breathTime > 0.0f)
        {
            _breathTime -= delta;
            if (_breathTime <= 0.0f && _breathFx != null)
                _breathFx.Emitting = false;
        }
        // 非 Attack 段速度×2
        float speed = GetMoveSpeed() * (_segment == FlySeg.Attack ? 1.0f : 2.0f);
        Vector3 to = _target - GlobalPosition;
        if (to.Length() < ArriveDist)
        {
            _segment = (FlySeg)(((int)_segment + 1) % 4); // CamOffset 后回 FarWay,重随机(抖动在 MakePoint)
            _target = MakePoint(_segment);
            return;
        }
        Vector3 dir = to.Normalized();
        Vector3 rot = Rotation;
        rot.Y = YawTowards(rot.Y, Mathf.Atan2(-dir.X, -dir.Z), Info.TurnSpeed * delta);
        Rotation = rot;
        // 飞行直接位移(目标点可在地面以下,不用碰撞移动)
        GlobalPosition += dir * speed * delta;
        if (!IsCurrentAnim(LocomotionAnim) && !IsPlayingAny(Info.AttackAnims)
            && Anim.HasAnimation(LocomotionAnim))
            Anim.Play(LocomotionAnim, 0.2);
        if (_segment == FlySeg.Attack)
            UpdateBreath(cam);
    }

    /// <summary>Attack 段:与相机距离 &lt; attack_radius(75) → 吐息(0.75 倍速)+ 火焰锥;
    /// 伤害由攻击动画事件(基类 0.3s)半屏判定结算</summary>
    private void UpdateBreath(Camera3D cam)
    {
        if (IsPlayingAny(Info.AttackAnims))
            return;
        if (GlobalPosition.DistanceTo(cam.GlobalPosition) >= AttackRadius)
            return;
        Anim.Play(Info.AttackAnims[0], 0.1, BreathAnimSpeed);
        if (_breathFx != null)
        {
            _breathFx.Emitting = true;
            _breathTime = BreathClipLen / BreathAnimSpeed;
            _breathAudio?.Play();
        }
    }

    /// <summary>吐息特效:fire_breath.tscn 火焰锥挂龙口(0,1,-3);
    /// 蓝龙(冰)/绿龙(毒)改火焰色调(逐实例复制 draw_pass 材质,避免三色龙互相污染)</summary>
    private void EnsureBreathFx()
    {
        if (_breathRoot != null)
        {
            _breathFx.Emitting = false;
            return;
        }
        _breathRoot = FireBreathScene.Instantiate<Node3D>();
        _breathRoot.Position = new Vector3(0.0f, 1.0f, -3.0f); // 真实模型:龙口在 -Z 前方约 3m
        AddChild(_breathRoot);
        _breathFx = _breathRoot.GetNode<GpuParticles3D>("Flames");
        if (_breathFx.DrawPass1 is QuadMesh mesh0 && mesh0.Material is StandardMaterial3D mat0)
        {
            var mesh = (QuadMesh)mesh0.Duplicate();
            var flameMat = (StandardMaterial3D)mat0.Duplicate();
            flameMat.AlbedoColor = Info.AttackType switch
            {
                Game.AttackType.Ice => new Color(0.45f, 0.8f, 1.0f),
                Game.AttackType.Poison => new Color(0.55f, 1.0f, 0.4f),
                _ => new Color(1.0f, 0.75f, 0.4f),
            };
            mesh.Material = flameMat;
            _breathFx.DrawPass1 = mesh;
        }
        _breathAudio = new AudioStreamPlayer3D
        {
            Stream = Info.AttackType == Game.AttackType.Ice ? SndBreathIce : SndBreathFire,
            UnitSize = 10.0f,
        };
        AddChild(_breathAudio);
    }

    /// <summary>TakeDamage 状态中无敌:hit 返回空,不掉血</summary>
    public override string Hit(float attack, Vector3 point, Game.HitType hitType, PlayerState.Side side)
    {
        if (IsCurrentAnim(Info.DamageAnim) && Anim.IsPlaying())
            return "";
        return base.Hit(attack, point, hitType, side);
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
        if (_breathFx != null)
            _breathFx.Emitting = false;
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
