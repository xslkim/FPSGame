using Godot;

namespace FPSGame;

/// <summary>
/// 怪物基类:生命周期(出生等待→追击→攻击→死亡回收)全量实现。
/// 对应原作 MonsterBase.cs / legacy monster_base.gd:
///   出生进 controller 默认态(不抢播 Idle02)→ 等待期每 Idle02Time 秒插播 Idle02、3m/s 下坠落地
///   → 水平距进 AttackRadius 且 CD 到且在 Locomotion 且 3D 距 &lt; maxDistance → 站桩播攻击动画(CD 期原地等待)
///   → 0.3s 动画事件 EventAttack(lastAttackTime=now,按屏幕 x 分侧命中,转嫁在 PlayerState)→ 死亡播 Dead + 血花(挂原点)
///   → 1.5s 回收;非 Boss 25s(宝箱 20s,HP=0 后 1.5s 缓期)超时自毁。
///   刷怪点:相机 3D forward ±fov 射线(最长 len),落点 y -= CC.center.y + 0.1(悬空出生,重力落地)。
/// 数值全来自 MonsterInfo(data/monster_meta.json),派生类只做行为差异。
/// </summary>
public partial class Monster : CharacterBody3D
{
    public enum State { Idle, Waiting, Active, Dead }

    public const float MaxLifeTime = 25.0f;   // 原 ResetMaxLifeTimeDeath 恒 25s(保留行为)
    public const float FallSpeed = 3.0f;      // 等待期持续下坠速度
    public const float RecycleDelay = 1.5f;   // 死亡 1.5s 后回收
    public const float Gravity = 9.8f;
    public const uint BornRayMask = 0xFFFFFFF1; // 排除 layer2(Enemy)/layer3(UI按钮)/layer4(CameraWall)
    // 注:UI 按钮(隐藏面板的按钮碰撞仍激活,挂相机前 1m)不挡出生射线——原作隐藏 UI 不参与物理射线
    public const float AttackEventDelay = 0.3f; // 攻击动画事件(原动画事件≈起手后,legacy 定 0.3s)

    [Signal] public delegate void DiedEventHandler(Monster monster);

    [Export] public string MetaKey = "";
    [Export] public bool IsBoss;

    public int Level;
    public float WaittingTime;                 // 由关卡按难度设置:Easy rand(3,8)/Hard rand(0,2)/Hell 0
    public double LastAttackTime = -99.0;      // 出生即可攻

    public MonsterInfo Info = null!;
    public float Hp;
    /// <summary>碰撞胶囊中心高度(瞄准点/测试用)</summary>
    public float AimCenterY => _col != null ? _col.Position.Y : 0.5f;
    public float AttackRadius => Info.AttackRadius;
    public Node3D BodyNode => HasNode("Model") ? GetNode<Node3D>("Model") : GetNode<Node3D>("Body");

    protected State CurState = State.Idle;
    protected Vector3 BornPos;
    protected float LifeTime;
    protected float WaitTime;
    protected float Idle2Time;
    protected AnimationPlayer Anim = null!;
    protected AudioStreamPlayer3D Audio = null!;
    protected MonsterHpBar HpBar = null!;      // 可空:宝箱无血条(原作 BoxBullet.prefab 无 HpReduceNumber)
    protected PlayerState.Side LastHitSide = PlayerState.Side.Right; // 最后一击侧(原作 OnDead(bool Right))

    private CollisionShape3D _col = null!;
    private Tween? _attackEventTween;

    public bool IsActiveState => CurState != State.Idle;
    public bool IsDead => CurState == State.Dead;

    public override void _Ready()
    {
        Info = MonsterInfo.Load(MetaKey);
        Anim = GetNode<AnimationPlayer>("AnimationPlayer");
        Audio = GetNode<AudioStreamPlayer3D>("AudioStreamPlayer3D");
        HpBar = GetNodeOrNull<MonsterHpBar>("HpAnchor")!;
        _col = GetNode<CollisionShape3D>("CollisionShape3D");
        // clip 自带的 event_attack/baotou_skill 方法轨与基类 0.3s 事件 Tween 重复,剥离(同 WolfMonster 先例)
        AnimTrackUtil.StripMethodTracks(Anim);
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
        HpBar?.SetHp(1.0f);
        PlayerState.CurAliveMonster += 1;
        OnBorn();
        // 出生进控制器默认态(原作 Animator m_DefaultState:骷髅/斧/飞斧/牛=Locomotion、
        // 宝箱=Idle、Boss=anim_idle),不抢播 Idle02(等待期每 Idle02Time 才插播一次)
        string bornAnim = Info.BornAnim.Length > 0 ? Info.BornAnim : (Info.IdleAnim ?? "locomotion");
        if (Anim.HasAnimation(bornAnim))
            Anim.Play(bornAnim);
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
            OnLifeTimeout(); // 超时自毁(宝箱:HP=0 → 1.5s 缓期回收,原作 Invoke DestorySelf)
            return;
        }
        switch (CurState)
        {
            case State.Waiting: UpdateWaiting(d); break;
            case State.Active: UpdateActive(d); break;
        }
    }

    /// <summary>寿命到:默认即刻回收(无死亡演出,也推进波次);宝箱覆盖为 1.5s 缓期</summary>
    protected virtual void OnLifeTimeout() => Recycle();

    /// <summary>等待期:持续下坠;每 Idle02Time 秒插播一次 Idle02(播完回默认态);超 WaittingTime 进追击</summary>
    protected virtual void UpdateWaiting(float delta)
    {
        WaitTime += delta;
        Idle2Time += delta;
        Velocity = new Vector3(0.0f, IsOnFloor() ? 0.0f : -FallSpeed, 0.0f);
        MoveAndSlide();
        if (Idle2Time >= Info.Idle2Interval)
        {
            Idle2Time = 0.0f;
            if (Info.Idle2Anim.Length > 0 && !IsCurrentAnim(Info.Idle2Anim) && Anim.HasAnimation(Info.Idle2Anim))
                Anim.Play(Info.Idle2Anim, 0.3);
        }
        else if (!Anim.IsPlaying())
        {
            // Idle02 播完回默认姿态(原作 controller 播完自动回默认态;牛魔王无 Idle02 状态恒 Locomotion)
            string bornAnim = Info.BornAnim.Length > 0 ? Info.BornAnim : (Info.IdleAnim ?? "locomotion");
            if (!IsCurrentAnim(bornAnim) && Anim.HasAnimation(bornAnim))
                Anim.Play(bornAnim, 0.3);
        }
        if (WaitTime >= WaittingTime)
        {
            CurState = State.Active;
            EnterActive();
        }
    }

    /// <summary>追击 + 攻击(原作 MonsterBase.MoveToPlayerAndAttack:168-210):
    /// 攻击/受伤动画播放中原地站桩;水平距 &lt; AttackRadius 且过 CD 且在 Locomotion 且 3D 距 &lt; maxDistance
    /// → 站桩攻击;半径内 CD 未到原地等待(不收脚走进相机);半径外才转身移动(3m 内移速钳 1)</summary>
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
        // 攻击/受伤播完 → 回 Locomotion(原作 Animator 播完自动回默认态,后续攻击门控也要求 Locomotion)
        if (!IsCurrentAnim(locomotionName) && Anim.HasAnimation(locomotionName))
            Anim.Play(locomotionName, 0.2);
        Vector3 target = cam.GlobalPosition;
        target.Y = GlobalPosition.Y;
        Vector3 toTarget = target - GlobalPosition;
        float flatDist = new Vector2(toTarget.X, toTarget.Z).Length();
        if (flatDist < AttackRadius)
        {
            float realDist = GlobalPosition.DistanceTo(cam.GlobalPosition);
            if (AttackReady() && IsCurrentAnim(locomotionName) && realDist < maxDistance)
            {
                DoAttack();
                return;
            }
            // 半径内 CD 未到:原地等待(只重力,不继续逼近/不收脚走进相机)
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
            ApplyGravity(delta);
            MoveAndSlide();
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
    }

    /// <summary>随机播一个攻击动画(原作 CrossFade(att,0) 零混合);0.3s 后触发攻击事件(原动画事件)</summary>
    protected virtual void DoAttack()
    {
        string anim = Info.AttackAnims[GD.RandRange(0, Info.AttackAnims.Length - 1)];
        Anim.Play(anim, 0.0f);
        _attackEventTween?.Kill();
        _attackEventTween = CreateTween();
        _attackEventTween.TweenInterval(AttackEventDelay);
        _attackEventTween.TweenCallback(Callable.From(TriggerAttackEvent));
    }

    protected bool AttackReady() => Time.GetTicksMsec() / 1000.0 - LastAttackTime >= GetAttackCd();

    /// <summary>攻击事件(原 EventAttack:lastAttackTime=now,按怪在屏幕 x 分侧 → PlayerState 结算(含转嫁))</summary>
    protected virtual void TriggerAttackEvent()
    {
        if (CurState == State.Dead || CurState == State.Idle)
            return;
        LastAttackTime = Time.GetTicksMsec() / 1000.0;
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
        LastHitSide = side; // 原作 OnDead(bool Right):掉落只发受击侧
        FireSystem.ShowDamage($"- {(int)attack}", point, this);
        HpBar?.SetHp(Mathf.Max(Hp, 0.0f) / GetMaxHp());
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
        FireSystem.SpawnBloodFlower(this, Vector3.Zero); // 原作血花挂怪原点(贴脚底)
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

    /// <summary>相机 3D forward(带俯仰)绕相机 up ±maxFov 随机偏转,射线最长 maxLength,命中退 0.5m;
    /// 落点 y -= 胶囊中心 y + 0.1(原作 LevelBase.UpdateMonsterBorn:231-233,不做贴地探测,
    /// 怪可悬空出生,靠等待期 3m/s 下坠落地——可见"空降"过程)</summary>
    public Vector3 GetBornPosition(float maxFov, float maxLength)
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null)
            return GlobalPosition;
        Vector3 up = cam.GlobalBasis.Y.Normalized();
        Vector3 dir = (-cam.GlobalBasis.Z).Normalized()
            .Rotated(up, Mathf.DegToRad((float)GD.RandRange(-maxFov, maxFov)));
        Vector3 from = cam.GlobalPosition;
        Vector3 to = from + dir * maxLength;
        var space = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(from, to, BornRayMask);
        var result = space.IntersectRay(query);
        Vector3 pos = result.Count == 0 ? to : (Vector3)result["position"] - dir * 0.5f;
        pos.Y -= _col != null ? _col.Position.Y : 0.0f; // pos.y -= CC.center.y
        pos.Y += 0.1f;
        // 防嵌地(原作 Unity CC.Move 会自动把嵌入地面的胶囊推出去,Godot CharacterBody3D 不会):
        // 原点埋进脚下 1.6m 内的地面时抬到地面表面;悬空出生(高于地面/下方无地面)不贴地,
        // 仍由等待期 3m/s 下坠落地(保留原作"空降"过程)
        var dep = space.IntersectRay(PhysicsRayQueryParameters3D.Create(
            pos + new Vector3(0.0f, 1.2f, 0.0f), pos + new Vector3(0.0f, -0.4f, 0.0f), BornRayMask));
        if (dep.Count != 0 && ((Vector3)dep["position"]).Y > pos.Y)
            pos.Y = ((Vector3)dep["position"]).Y;
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

    protected bool IsPlayingAny(string[] names)
    {
        if (!Anim.IsPlaying())
            return false;
        foreach (var n in names)
            if (Anim.CurrentAnimation == n) // StringName==string 内容比较(IndexOf 装箱永假,勿用)
                return true;
        return false;
    }

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
        MoveToPlayerAndAttack(delta, "locomotion", Info.MaxDistance);
    protected virtual void OnHurt(Vector3 point, Game.HitType hitType, PlayerState.Side side) { }
    protected virtual void OnDeath() { }
}
