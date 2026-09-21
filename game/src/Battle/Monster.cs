using Godot;

namespace FPSGame;

/// <summary>
/// 怪物基类:生命周期(出生等待→追击→攻击→死亡回收)全量实现。
/// 对应原作 MonsterBase.cs / legacy monster_base.gd:
///   出生站桩 WaittingTime(每 5s 插播 Idle02)→ 追相机进 AttackRadius 且 CD 到 → 播攻击动画
///   → 0.3s 动画事件 EventAttack(按屏幕 x 分侧命中,转嫁在 PlayerState)→ 死亡播 Dead + 血花
///   → 1.5s 回收;非 Boss 25s(宝箱 20s)超时自毁。刷怪点:相机前方 ±fov 射线(最长 len)。
/// 数值全来自 MonsterInfo(data/monster_meta.json),派生类只做行为差异。
/// </summary>
public partial class Monster : CharacterBody3D
{
    public enum State { Idle, Waiting, Active, Dead }

    public const float MaxLifeTime = 25.0f;   // 原 ResetMaxLifeTimeDeath 恒 25s(保留行为)
    public const float FallSpeed = 3.0f;      // 等待期持续下坠速度
    public const float Idle2Interval = 5.0f;
    public const float RecycleDelay = 1.5f;   // 死亡 1.5s 后回收
    public const float Gravity = 9.8f;
    public const uint BornRayMask = 0xFFFFFFF5; // 排除 layer2(Enemy)与 layer4(CameraWall)
    public const float AttackEventDelay = 0.3f; // 攻击动画事件(原动画事件≈起手后,legacy 定 0.3s)

    [Signal] public delegate void DiedEventHandler(Monster monster);

    [Export] public string MetaKey = "";
    [Export] public bool IsBoss;

    public int Level;
    public float WaittingTime;                 // 由关卡按难度设置:Easy rand(3,8)/Hard rand(0,2)/Hell 0
    public double LastAttackTime = -99.0;      // 出生即可攻
    public float MaxDistance = 2000.0f;

    public MonsterInfo Info = null!;
    public float Hp;
    public float AttackRadius => Info.AttackRadius;
    public Node3D BodyNode => HasNode("Model") ? GetNode<Node3D>("Model") : GetNode<Node3D>("Body");

    protected State CurState = State.Idle;
    protected Vector3 BornPos;
    protected float LifeTime;
    protected float WaitTime;
    protected float Idle2Time;
    protected AnimationPlayer Anim = null!;
    protected AudioStreamPlayer3D Audio = null!;
    protected MonsterHpBar HpBar = null!;

    private CollisionShape3D _col = null!;
    private Tween? _attackEventTween;

    public bool IsActiveState => CurState != State.Idle;
    public bool IsDead => CurState == State.Dead;

    public override void _Ready()
    {
        Info = MonsterInfo.Load(MetaKey);
        Anim = GetNode<AnimationPlayer>("AnimationPlayer");
        Audio = GetNode<AudioStreamPlayer3D>("AudioStreamPlayer3D");
        HpBar = GetNode<MonsterHpBar>("HpAnchor");
        _col = GetNode<CollisionShape3D>("CollisionShape3D");
        Deactivate();
    }

    // ------------------------------------------------ 生命周期

    /// <summary>出生:HP 满 → 计数+1 → 碰撞启用 → 等待期</summary>
    public virtual void Born(Vector3 pos, int level, float waittingTime)
    {
        Level = level;
        WaittingTime = waittingTime;
        LastAttackTime = -99.0;
        Hp = GetMaxHp();
        BornPos = pos;
        GlobalPosition = pos;
        Rotation = Vector3.Zero;
        Velocity = Vector3.Zero;
        LifeTime = 0.0f;
        WaitTime = 0.0f;
        Idle2Time = 0.0f;
        CurState = State.Waiting;
        Visible = true;
        SetProcess(true);
        SetPhysicsProcess(true);
        _col.SetDeferred(CollisionShape3D.PropertyName.Disabled, false);
        HpBar.SetHp(1.0f);
        PlayerState.CurAliveMonster += 1;
        OnBorn();
        if (Anim.HasAnimation(Info.Idle2Anim))
            Anim.Play(Info.Idle2Anim);
    }

    // ---- 等级公式(原作 6.1.6)----
    public float GetMaxHp() => Info.Hp + (Level > 0 ? Info.HpLevelRate * Level : 0.0f);
    public float GetMoveSpeed() => Info.MoveSpeed + (Level > 0 ? Info.MoveSpeedLevelRate * Level : 0.0f);
    public float GetAttackCd() => Info.AttackCd - (Level > 0 ? Info.AttackCdLevelRate * Level : 0.0f);
    public virtual float GetAttack() =>
        Info.Attack + (Level > 0 && Info.AttackLevelBonus > 0.0f ? Info.AttackLevelBonus * Level : 0.0f);

    public override void _PhysicsProcess(double delta)
    {
        if (Game.Instance.IsGamePause || CurState == State.Idle)
            return;
        float d = (float)delta;
        LifeTime += d;
        float limit = Info.LifeActiveTime > 0.0f ? Info.LifeActiveTime : MaxLifeTime;
        if (!IsBoss && CurState != State.Dead && LifeTime >= limit)
        {
            Recycle(); // 超时自毁(无死亡演出,也推进波次)
            return;
        }
        switch (CurState)
        {
            case State.Waiting: UpdateWaiting(d); break;
            case State.Active: UpdateActive(d); break;
        }
    }

    /// <summary>等待期:持续下坠;每 5s 插播 Idle02;超 WaittingTime 进追击</summary>
    protected virtual void UpdateWaiting(float delta)
    {
        WaitTime += delta;
        Idle2Time += delta;
        Velocity = new Vector3(0.0f, IsOnFloor() ? 0.0f : -FallSpeed, 0.0f);
        MoveAndSlide();
        if (Idle2Time >= Idle2Interval)
        {
            Idle2Time = 0.0f;
            if (!IsCurrentAnim(Info.Idle2Anim) && Anim.HasAnimation(Info.Idle2Anim))
                Anim.Play(Info.Idle2Anim, 0.3);
        }
        if (WaitTime >= WaittingTime)
        {
            CurState = State.Active;
            EnterActive();
        }
    }

    /// <summary>追击 + 攻击:目标=相机(y 压平);进半径且 CD 到且在移动动画 → 攻击</summary>
    protected virtual void MoveToPlayerAndAttack(float delta, string locomotionName, float maxDistance)
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null)
            return;
        if (IsPlayingAny(Info.AttackAnims) || (IsCurrentAnim(Info.DamageAnim) && Anim.IsPlaying()))
        {
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
            ApplyGravity(delta);
            MoveAndSlide();
            return;
        }
        Vector3 target = cam.GlobalPosition;
        target.Y = GlobalPosition.Y;
        Vector3 toTarget = target - GlobalPosition;
        float flatDist = new Vector2(toTarget.X, toTarget.Z).Length();
        float realDist = GlobalPosition.DistanceTo(cam.GlobalPosition);
        if (flatDist < AttackRadius && AttackReady() && IsCurrentAnim(locomotionName)
            && realDist < maxDistance)
        {
            DoAttack();
            return;
        }
        Vector3 dir = new(toTarget.X, 0.0f, toTarget.Z);
        if (dir.LengthSquared() > 0.0001f)
            dir = dir.Normalized();
        Vector3 rot = Rotation;
        rot.Y = YawTowards(rot.Y, Mathf.Atan2(-dir.X, -dir.Z), Info.TurnSpeed * delta);
        Rotation = rot;
        float speed = GetMoveSpeed();
        if (flatDist < 3.0f)
            speed = Mathf.Min(speed, 1.0f); // 近身 3m 内移速钳到 1
        Velocity = new Vector3(dir.X * speed, Velocity.Y, dir.Z * speed);
        ApplyGravity(delta);
        MoveAndSlide();
        if (!IsCurrentAnim(locomotionName) && Anim.HasAnimation(locomotionName))
            Anim.Play(locomotionName, 0.2);
    }

    /// <summary>随机播一个攻击动画;0.3s 后触发攻击事件(原动画事件)</summary>
    protected virtual void DoAttack()
    {
        LastAttackTime = Time.GetTicksMsec() / 1000.0;
        string anim = Info.AttackAnims[GD.RandRange(0, Info.AttackAnims.Length - 1)];
        Anim.Play(anim, 0.1);
        _attackEventTween?.Kill();
        _attackEventTween = CreateTween();
        _attackEventTween.TweenInterval(AttackEventDelay);
        _attackEventTween.TweenCallback(Callable.From(TriggerAttackEvent));
    }

    protected bool AttackReady() => Time.GetTicksMsec() / 1000.0 - LastAttackTime >= GetAttackCd();

    /// <summary>攻击事件(原 EventAttack):按怪在屏幕 x 分侧 → PlayerState 结算(含转嫁)</summary>
    protected virtual void TriggerAttackEvent()
    {
        if (CurState == State.Dead || CurState == State.Idle)
            return;
        PlayerState.Instance.HitPlayer(GetAttack(), Info.AttackType, PickTargetSide());
    }

    protected PlayerState.Side PickTargetSide()
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null || cam.IsPositionBehind(GlobalPosition))
            return PlayerState.Side.Right;
        float sx = cam.UnprojectPosition(GlobalPosition).X;
        float w = GetViewport().GetVisibleRect().Size.X;
        return sx > w * 0.5f ? PlayerState.Side.Right : PlayerState.Side.Left;
    }

    /// <summary>受击(FireSystem 约定签名)。Body 全伤;≤0 → 血花+Dead+死亡音+关碰撞→1.5s 回收</summary>
    public virtual string Hit(float attack, Vector3 point, Game.HitType hitType, PlayerState.Side side)
    {
        if (CurState == State.Dead || CurState == State.Idle)
            return Info.ImpactTag;
        Hp -= attack;
        FireSystem.ShowDamage($"- {(int)attack}", point, this);
        HpBar.SetHp(Mathf.Max(Hp, 0.0f) / GetMaxHp());
        if (Hp <= 0.0f)
            Die();
        else
        {
            PlaySound("hurt");
            if (Anim.HasAnimation(Info.DamageAnim))
                Anim.Play(Info.DamageAnim, 0.1);
            OnHurt(point, hitType, side);
        }
        return Info.ImpactTag;
    }

    protected async virtual void Die()
    {
        CurState = State.Dead;
        Velocity = Vector3.Zero;
        _col.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
        PlaySound("dead");
        FireSystem.SpawnBloodFlower(this, new Vector3(0.0f, 1.0f, 0.0f));
        if (Anim.HasAnimation(Info.DeadAnim))
            Anim.Play(Info.DeadAnim, 0.1, Info.DeadAnimSpeed);
        OnDeath();
        await ToSignal(GetTree().CreateTimer(RecycleDelay), SceneTreeTimer.SignalName.Timeout);
        Recycle();
    }

    /// <summary>回收:隐藏 + 停 process + 计数-1,回对象池</summary>
    protected virtual void Recycle()
    {
        if (CurState == State.Idle)
            return;
        PlayerState.CurAliveMonster -= 1;
        Deactivate();
        EmitSignal(SignalName.Died, this);
    }

    protected virtual void Deactivate()
    {
        CurState = State.Idle;
        Visible = false;
        Velocity = Vector3.Zero;
        SetProcess(false);
        SetPhysicsProcess(false);
        _col?.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
        Anim?.Stop();
    }

    // ------------------------------------------------ 刷怪点(原 GetBornPosition)

    /// <summary>相机前方 ±maxFov 随机偏转,射线最长 maxLength,命中退 0.5m;落点做地面探测</summary>
    public Vector3 GetBornPosition(float maxFov, float maxLength)
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null)
            return GlobalPosition;
        Vector3 forward = -cam.GlobalBasis.Z;
        Vector3 flat = new(forward.X, 0.0f, forward.Z);
        if (flat.LengthSquared() < 0.001f)
            flat = Vector3.Forward;
        Vector3 dir = flat.Normalized().Rotated(Vector3.Up, Mathf.DegToRad((float)GD.RandRange(-maxFov, maxFov)));
        Vector3 from = cam.GlobalPosition;
        Vector3 to = from + dir * maxLength;
        var space = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(from, to, BornRayMask);
        var result = space.IntersectRay(query);
        Vector3 pos = result.Count == 0 ? to : (Vector3)result["position"] - dir * 0.5f;
        var ground = space.IntersectRay(PhysicsRayQueryParameters3D.Create(
            pos + new Vector3(0.0f, 0.5f, 0.0f), pos + new Vector3(0.0f, -2.5f, 0.0f), BornRayMask));
        pos.Y = ground.Count == 0 ? 0.0f : ((Vector3)ground["position"]).Y;
        return pos;
    }

    /// <summary>出生射线参数:meta born_override 优先,否则关卡默认</summary>
    public Vector2 GetBornParams(float defaultFov, float defaultLen) =>
        Info.BornMaxFov.HasValue
            ? new Vector2(Info.BornMaxFov.Value, Info.BornMaxLength!.Value)
            : new Vector2(defaultFov, defaultLen);

    /// <summary>面向相机(飞斧出生 / 包头瞬移后)</summary>
    public void FaceCamera()
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null)
            return;
        Vector3 dir = cam.GlobalPosition - GlobalPosition;
        dir.Y = 0.0f;
        if (dir.LengthSquared() > 0.001f)
        {
            dir = dir.Normalized();
            Vector3 rot = Rotation;
            rot.Y = Mathf.Atan2(-dir.X, -dir.Z);
            Rotation = rot;
        }
    }

    // ------------------------------------------------ 动画/声音辅助

    public bool IsCurrentAnim(string name) => Anim.CurrentAnimation == name;

    protected bool IsPlayingAny(string[] names) =>
        Anim.IsPlaying() && System.Array.IndexOf(names, Anim.CurrentAnimation) >= 0;

    protected virtual void ApplyGravity(float delta) =>
        Velocity = new Vector3(Velocity.X, IsOnFloor() ? 0.0f : Velocity.Y - Gravity * delta, Velocity.Z);

    /// <summary>转向(RotateTowards 语义)</summary>
    protected static float YawTowards(float current, float target, float maxDelta)
    {
        float diff = Mathf.Wrap(target - current, -Mathf.Pi, Mathf.Pi);
        return current + Mathf.Clamp(diff, -maxDelta, maxDelta);
    }

    protected void PlaySound(string soundName)
    {
        // 每怪独立目录优先 assets/audio/monsters/<key>/<name>,再共享 assets/audio/monsters/<name>
        string[] exts = { ".wav", ".ogg", ".mp3" };
        foreach (var ext in exts)
        {
            string own = $"res://assets/audio/monsters/{MetaKey}/{soundName}{ext}";
            if (ResourceLoader.Exists(own))
            {
                Audio.Stream = GD.Load<AudioStream>(own);
                Audio.Play();
                return;
            }
        }
        foreach (var ext in exts)
        {
            string shared = $"res://assets/audio/monsters/{soundName}{ext}";
            if (ResourceLoader.Exists(shared))
            {
                Audio.Stream = GD.Load<AudioStream>(shared);
                Audio.Play();
                return;
            }
        }
    }

    // ------------------------------------------------ 派生钩子

    protected virtual void OnBorn() { }
    protected virtual void EnterActive() { }
    protected virtual void UpdateActive(float delta) =>
        MoveToPlayerAndAttack(delta, "locomotion", MaxDistance);
    protected virtual void OnHurt(Vector3 point, Game.HitType hitType, PlayerState.Side side) { }
    protected virtual void OnDeath() { }
}
