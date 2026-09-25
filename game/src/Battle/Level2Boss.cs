using Godot;

namespace FPSGame;

/// <summary>
/// Level2Boss(6.3 1:1):HP100+20/Lv,CD5(受击罚 3s);
/// 动画速度体系 Normal0.5/Attack0.3/Slow0.05,难度倍率由关卡设置(easy×1/hard×2.25/hell×4);
/// 出场 0.8s 震屏信号;移动目标=相机位置+forward*12、y=-8、速度 4(直接 translate 不用碰撞移动),
/// 朝目标以 5*dt 插值转身;到位后按 CD 攻击:
///   50% Skill1(物理单体,50% 选边优先、目标不活跃回退另一边;出手前按选边 transform.Rotate
///   SkillRight1=(-20,0,0)/SkillLeft1=(-20,80,0),Level2.unity 序列化值,Skill2 的 SkillRight2=(0,0,0) 为无操作)
///   / 50% Skill2(冰,雷柱特效激活 0.5s 后 Both,伤害×0.5);
/// 部位判定:Armour 只弹金属音效不掉血;Head/Body 掉血(FireSystem 默认 Body);
/// Damage02 状态中无敌;Dead 状态持续下沉(Unity m_char.Move(Vector3.down*dt)=1m/s);
/// 死亡不回收(关卡处理胜利与清理)。
/// </summary>
public partial class Level2Boss : Monster
{
    [Signal] public delegate void EntranceShakeEventHandler(double duration);

    public const float SpeedNormal = 0.5f;
    public const float SpeedAttack = 0.3f;
    public const float SpeedSlow = 0.05f;
    public const float MoveSpeed = 4.0f;
    public const float TurnLerp = 5.0f;
    public const float TargetForward = 12.0f;
    public const float TargetY = -8.0f;
    public const float ArriveDist = 0.5f;
    public const float Skill2Delay = 0.5f;
    public const float Skill2DmgRate = 0.5f;
    public const float HurtCdPenalty = 3.0f;
    public const double EntranceShakeTime = 0.8;

    /// <summary>Skill1 出手旋转(Level2.unity 序列化值,度;SkillRight2/SkillLeft2=(0,0,0) 无操作不实现)</summary>
    public static readonly Vector3 SkillRight1 = new(-20.0f, 0.0f, 0.0f);
    public static readonly Vector3 SkillLeft1 = new(-20.0f, 80.0f, 0.0f);

    private const string LightningScenePath = "res://assets/effects/lightning_pillar.tscn";
    private const string BodyMatPath = "res://assets/models/monsters/level2_boss/level2_boss_mat.tres";
    private const string WeaponMatPath = "res://assets/models/monsters/level2_boss/level2_boss_weapon_mat.tres";

    /// <summary>难度动画倍率,由关卡设置(easy 1 / hard 2.25 / hell 4)</summary>
    public double AnimSpeedRate = 1.0;

    // 自检统计:技能实际结算次数(Skill1/Skill2 事件)
    public int StatSkillCount;
    public int StatSkill1RotateCount;         // Skill1 出手旋转次数(L2-6)
    public Vector3 StatLastSkill1Rotate;      // 最近一次旋转向量(度)

    private bool _arrived;
    private Node3D? _lightningFx;
    private float _lightningTime;
    private Tween? _eventTween;
    private PlayerState.Side _skill1Side = PlayerState.Side.Right;

    public void SetDifficultyAnimRate(double rate) => AnimSpeedRate = rate;

    public override void _Ready()
    {
        base._Ready();
        ApplyMaterials();
    }

    private void ApplyMaterials()
    {
        var model = GetNodeOrNull<Node3D>("Model");
        if (model == null)
            return;
        var bodyMat = GD.Load<Material>(BodyMatPath);
        var weaponMat = GD.Load<Material>(WeaponMatPath);
        foreach (var node in model.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mi)
                continue;
            string n = mi.Name.ToString().ToLower();
            mi.SetSurfaceOverrideMaterial(0,
                n.Contains("sword") || n.Contains("spear") ? weaponMat : bodyMat);
        }
    }

    public override void Born(Vector3 pos, int level, float waittingTime)
    {
        base.Born(pos, level, waittingTime); // 基类末尾会播 Idle02(=Idle),速度 1
        _arrived = false;
        EmitSignal(SignalName.EntranceShake, EntranceShakeTime);
        GD.Print($"[L2Boss] entrance shake {EntranceShakeTime:0.0}s");
        PlayIdle(); // 覆盖为慢速体系
    }

    protected override void UpdateActive(float delta)
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null)
            return;
        if (_lightningTime > 0.0f)
        {
            _lightningTime -= delta;
            if (_lightningTime <= 0.0f && _lightningFx != null)
                _lightningFx.Visible = false;
        }
        if (IsPlayingAny(Info.AttackAnims) || (IsCurrentAnim(Info.DamageAnim) && Anim.IsPlaying()))
            return;
        Vector3 target = cam.GlobalPosition + (-cam.GlobalBasis.Z) * TargetForward;
        target.Y = TargetY;
        if (!_arrived)
        {
            Vector3 to = target - GlobalPosition;
            if (to.Length() < ArriveDist)
            {
                _arrived = true;
            }
            else
            {
                // 速度 4 直接 translate(不用碰撞移动);朝目标 5*dt 插值转身
                GlobalPosition += to.Normalized() * MoveSpeed * delta;
                LerpFace(to, delta);
                return;
            }
        }
        // 到位:面向相机,按 CD 攻击
        Vector3 toCam = cam.GlobalPosition - GlobalPosition;
        LerpFace(toCam, delta);
        if (!Anim.IsPlaying())
            PlayIdle();
        if (AttackReady())
        {
            LastAttackTime = Time.GetTicksMsec() / 1000.0;
            string skill = GD.Randf() < 0.5f ? "Skill1" : "Skill2"; // 50% Skill1 / 50% Skill2
            if (skill == "Skill1")
                _skill1Side = PickSkill1SideAndRotate(); // L2-6:出手前按选边 Rotate
            Anim.Play(skill, 0.1, SpeedAttack * (float)AnimSpeedRate);
            _eventTween?.Kill();
            _eventTween = CreateTween();
            _eventTween.TweenInterval(AttackEventDelay);
            _eventTween.TweenCallback(Callable.From(TriggerAttackEvent));
        }
    }

    /// <summary>Skill1 选边(Unity Attack:50% 先选右/左,不活跃回退另一边)并按选边本地旋转
    /// SkillRight1=(-20,0,0)/SkillLeft1=(-20,80,0)(Unity transform.Rotate,Space.Self)</summary>
    private PlayerState.Side PickSkill1SideAndRotate()
    {
        bool rightUp = IsSideUp(PlayerState.Side.Right);
        bool leftUp = IsSideUp(PlayerState.Side.Left);
        var side = GD.Randf() < 0.5f
            ? (rightUp ? PlayerState.Side.Right : PlayerState.Side.Left)
            : (leftUp ? PlayerState.Side.Left : PlayerState.Side.Right);
        var deg = side == PlayerState.Side.Right ? SkillRight1 : SkillLeft1;
        var t = Transform;
        t.Basis *= Basis.FromEuler(new Vector3(
            Mathf.DegToRad(deg.X), Mathf.DegToRad(deg.Y), Mathf.DegToRad(deg.Z)));
        Transform = t;
        StatSkill1RotateCount += 1;
        StatLastSkill1Rotate = deg;
        return side;
    }

    private static bool IsSideUp(PlayerState.Side side)
    {
        var p = PlayerState.Instance.GetPlayer(side);
        return p.Active && p.Hp > 0.0f;
    }

    private void LerpFace(Vector3 to, float delta)
    {
        var flat = new Vector2(to.X, to.Z);
        if (flat.Length() < 0.001f)
            return;
        Vector3 rot = Rotation;
        rot.Y = Mathf.LerpAngle(rot.Y, Mathf.Atan2(-to.X, -to.Z), TurnLerp * delta);
        Rotation = rot;
    }

    private void PlayIdle()
    {
        string idleName = Info.IdleAnim ?? Info.Idle2Anim;
        if (Anim.HasAnimation(idleName))
            Anim.Play(idleName, 0.3f, SpeedNormal * (float)AnimSpeedRate);
    }

    /// <summary>动画事件分派(0.3s,基类 AttackEventDelay):按当前技能结算</summary>
    protected override void TriggerAttackEvent()
    {
        if (CurState == State.Dead || CurState == State.Idle)
            return;
        if (IsCurrentAnim("Skill1"))
            HertPlayerSkill1();
        else if (IsCurrentAnim("Skill2"))
            HertPlayerSkill2();
    }

    /// <summary>Skill1:物理单体(选边在出手时已定,L2-6;目标不活跃由 HitPlayer 转嫁换边)</summary>
    private void HertPlayerSkill1()
    {
        StatSkillCount += 1;
        PlayerState.Instance.HitPlayer(GetAttack(), Game.AttackType.Phy, _skill1Side);
    }

    /// <summary>Skill2:冰,雷柱特效激活,0.5s 后 Both,伤害×0.5</summary>
    private void HertPlayerSkill2()
    {
        StatSkillCount += 1;
        ActivateLightning();
        float atk = GetAttack() * Skill2DmgRate;
        GetTree().CreateTimer(Skill2Delay).Timeout += () =>
        {
            if (CurState == State.Dead || CurState == State.Idle)
                return;
            PlayerState.Instance.HitPlayer(atk, Game.AttackType.Ice, PlayerState.Side.Both);
        };
    }

    /// <summary>雷柱特效:lightning_pillar.tscn,激活 0.5s 后隐藏(EffectBase 自隐,这里再兜底)</summary>
    private void ActivateLightning()
    {
        if (_lightningFx == null)
        {
            _lightningFx = GD.Load<PackedScene>(LightningScenePath).Instantiate<Node3D>();
            _lightningFx.Visible = false;
            GetTree().CurrentScene.AddChild(_lightningFx);
        }
        var cam = GetViewport().GetCamera3D();
        if (cam != null)
            _lightningFx.GlobalPosition = cam.GlobalPosition + (-cam.GlobalBasis.Z) * 4.0f;
        if (_lightningFx is EffectBase fx)
            fx.Activate(Skill2Delay);
        else
            _lightningFx.Visible = true;
        _lightningTime = Skill2Delay;
    }

    /// <summary>部位判定:Armour → 随机金属音效不掉血;Head/Body 掉血;Damage02 状态中无敌</summary>
    public override string Hit(float attack, Vector3 point, Game.HitType hitType, PlayerState.Side side)
    {
        if (CurState == State.Dead || CurState == State.Idle)
            return Info.ImpactTag;
        if (IsCurrentAnim(Info.DamageAnim) && Anim.IsPlaying())
            return ""; // Damage02 状态中无敌
        if (hitType == Game.HitType.Armour)
        {
            PlaySound($"metal_{GD.RandRange(1, 3)}"); // 随机金属音效(资源缺失时静默)
            return "Metal";
        }
        Hp -= attack;
        FireSystem.ShowDamage($"- {(int)attack}", point, this);
        HpBar.SetHp(Mathf.Max(Hp, 0.0f) / GetMaxHp());
        LastAttackTime += HurtCdPenalty; // 受击罚 3 秒 CD
        if (Hp <= 0.0f)
        {
            Die();
        }
        else
        {
            PlaySound("hurt");
            if (Anim.HasAnimation(Info.DamageAnim))
                Anim.Play(Info.DamageAnim, 0.1f, SpeedNormal * (float)AnimSpeedRate);
            OnHurt(point, hitType, side);
        }
        return Info.ImpactTag;
    }

    /// <summary>死亡:播 dead(Slow 0.05 慢速体系),发 died 信号,不回收(关卡处理胜利与清理);
    /// Dead 状态持续下沉(Unity Update Dead 分支 m_char.Move(Vector3.down*dt)=1m/s)</summary>
    protected async override void Die()
    {
        CurState = State.Dead;
        Velocity = Vector3.Zero;
        GetNode<CollisionShape3D>("CollisionShape3D")
            .SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
        PlaySound("dead");
        FireSystem.SpawnBloodFlower(this, new Vector3(0.0f, 1.0f, 0.0f));
        if (Anim.HasAnimation(Info.DeadAnim))
            Anim.Play(Info.DeadAnim, 0.1f, SpeedSlow * (float)AnimSpeedRate);
        OnDeath();
        EmitSignal(SignalName.Died, this);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); // 保持 async 签名同基类
    }

    /// <summary>L2-7:Dead 状态 1m/s 持续下沉(等效 Unity m_char.Move(down*dt)),由关卡胜利后清理</summary>
    public override void _PhysicsProcess(double delta)
    {
        if (CurState == State.Dead)
        {
            GlobalPosition += Vector3.Down * (float)delta;
            return;
        }
        base._PhysicsProcess(delta);
    }

    /// <summary>胜利后由关卡清理(基类 Deactivate 为 protected,这里开公共口)</summary>
    public void CleanupByLevel() => Deactivate();
}
