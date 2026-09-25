using Godot;

namespace FPSGame;

/// <summary>
/// Dragon 红/蓝/绿共用(6.3 Dragon;meta key 区分,attack_type: 红=Phy / 蓝=Ice / 绿=Poison):
/// 四点巡回飞行 FarWay→InCamera→Attack→CamOffset 循环,逐帧公式照 Unity DragonMonster.cs:43-92
/// InitMovePosition(参数 Forward150/Right160/Up20/CamUp0.1/CamRight20/RightOffset15):
///   Pos1(FarWay)  = cam + fwd·(150/2) + right·(160/2)          (无 up 分量)
///   Pos2(InCamera)= cam + fwd·150 + right·(15±10) + up·(20±20)
///   Pos3(Attack)  = cam + up·0.1 + (Pos3-Pos2).normalized·20   (贴脸:过相机点沿远离 Pos2 方向 20m)
///   Pos4(CamOffset)= Pos3 + right·20
/// born 时直接瞬移到 FarWay(Unity born() transform.position=FarWay),首个移动目标 InCamera;
/// 入场恒取右侧(原作 Random.Range(0,100)>0 仅 1% 走左,保留"恒右"语义并标注);
/// 非 Attack 段移速×2 且飞行动画 speed=2(Unity born/每循环 m_ani.speed=2);
/// Attack 段与相机距离&lt;attack_radius(75) 播 FireBreathOnce(0.75 倍速)+ 火焰锥特效
/// (fire_breath.tscn 挂龙口 (0,1,-3);蓝/绿逐实例改火焰色调)+ 攻击事件基类半屏结算;
/// TakeDamage 播放中无敌(hit 返回空);死亡恒速 3/s 下落(Unity DropToDie m_char.Move(down·3·dt)),
/// y&lt;-3 回收(不走基类 1.5s 定时)。三色变体材质由 .tscn BodyMaterial 注入。
/// </summary>
public partial class DragonMonster : Monster
{
    public enum FlySeg { FarWay, InCamera, Attack, CamOffset }

    public const float PForward = 150.0f;  // Pos2 前距;Pos1 取其半
    public const float PRight = 160.0f;    // Pos1 右距取其半(80)
    public const float PUp = 20.0f;        // Pos2 竖向基准
    public const float PCamUp = 0.1f;      // Pos3 相机上方偏移
    public const float PCamRight = 20.0f;  // Pos4 横向偏移
    public const float PRightOffset = 15.0f; // Pos2 横向基准
    public const float Pos2JitterX = 10.0f;  // Pos2 横向 rand(-10,10)
    public const float Pos2JitterY = 20.0f;  // Pos2 竖向 rand(-20,20)
    public const float AttackPointDist = 20.0f; // Pos3 距相机上方点 20m
    public const float ArriveDist = 2.0f;
    public const float FlyAnimSpeed = 2.0f;    // 非 Attack 段飞行动画倍速(Unity m_ani.speed=2)
    public const float BreathAnimSpeed = 0.75f;
    public const float BreathClipLen = 0.8f; // clip 0.8s ÷ 0.75 倍速
    public const float DeadFallSpeed = 3.0f; // 死亡恒速下落(Unity down*3*dt)
    public const float FallRecycleY = -3.0f;
    public const string LocomotionAnim = "locomotion";

    private static readonly PackedScene FireBreathScene =
        GD.Load<PackedScene>("res://assets/effects/fire_breath.tscn");
    private static readonly AudioStream SndBreathFire =
        GD.Load<AudioStream>("res://assets/audio/effects/breath_fire.wav");
    private static readonly AudioStream SndBreathIce =
        GD.Load<AudioStream>("res://assets/audio/effects/breath_ice.wav");

    [Export] public Material BodyMaterial = null!; // M7 颜色变体材质(red/blue/green .tscn 配置)

    // 自检钩子:born 时四点快照与瞬移落点(Level4 断言四点公式/出生瞬移)
    public Vector3 StatTeleportPos { get; private set; }
    private readonly Vector3[] _points = new Vector3[4];

    private FlySeg _segment = FlySeg.FarWay;
    private Vector3 _target;
    private GpuParticles3D _breathFx = null!;
    private Node3D _breathRoot = null!;
    private float _breathTime;
    private AudioStreamPlayer3D _breathAudio = null!;

    /// <summary>当前四点快照(自检用,索引=FlySeg)</summary>
    public Vector3[] GetMovePoints() => (Vector3[])_points.Clone();

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

    /// <summary>born:四点初始化后直接瞬移到 FarWay,首个目标 InCamera(Unity born() 语义)</summary>
    protected override void OnBorn()
    {
        _breathTime = 0.0f;
        InitMovePosition();
        GlobalPosition = _points[(int)FlySeg.FarWay]; // L4-2:出生瞬移到画外远点
        StatTeleportPos = GlobalPosition;
        _segment = FlySeg.InCamera;
        _target = _points[(int)FlySeg.InCamera];
        EnsureBreathFx();
    }

    /// <summary>四点公式(Unity InitMovePosition,isRight 恒 true;up/right 取相机基向量)</summary>
    private void InitMovePosition()
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null)
            return;
        Vector3 p = cam.GlobalPosition;
        Vector3 fwd = -cam.GlobalBasis.Z;
        Vector3 right = cam.GlobalBasis.X;
        Vector3 up = cam.GlobalBasis.Y;
        Vector3 pos1 = p + fwd * (PForward / 2.0f) + right * (PRight / 2.0f);
        Vector3 pos2 = p + fwd * PForward
            + right * (PRightOffset + (float)GD.RandRange(-Pos2JitterX, Pos2JitterX))
            + up * (PUp + (float)GD.RandRange(-Pos2JitterY, Pos2JitterY));
        Vector3 pos3 = p + up * PCamUp;
        pos3 += (pos3 - pos2).Normalized() * AttackPointDist;
        Vector3 pos4 = pos3 + right * PCamRight;
        _points[(int)FlySeg.FarWay] = pos1;
        _points[(int)FlySeg.InCamera] = pos2;
        _points[(int)FlySeg.Attack] = pos3;
        _points[(int)FlySeg.CamOffset] = pos4;
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
            // 段推进:FarWay→InCamera→Attack→CamOffset→FarWay;到 Attack 后重算四点(原作循环重启)
            _segment = (FlySeg)(((int)_segment + 1) % 4);
            if (_segment == FlySeg.CamOffset)
            {
                InitMovePosition();
                if (_breathFx != null)
                    _breathFx.Emitting = false; // VFX.SetActive(false)
            }
            _target = _points[(int)_segment];
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
            // L4-8:非 Attack 段飞行动画 speed=2(Unity m_ani.speed=2)
            Anim.Play(LocomotionAnim, 0.2, _segment == FlySeg.Attack ? 1.0f : FlyAnimSpeed);
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

    /// <summary>死亡:恒速 3/s 坠落,y&lt;-3 回收(不调基类 1.5s 定时回收)</summary>
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
            // L4-9:恒速 3/s 下落(Unity DropToDie m_char.Move(Vector3.down*3*dt))
            GlobalPosition += Vector3.Down * (DeadFallSpeed * (float)delta);
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
