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
/// 部位判定:骨骼护甲不掉血;胸口 HurtSphere(原作 HitType.Head)才掉血;
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
    private PlayerState.Side _skill1Side = PlayerState.Side.Right;
    private readonly System.Collections.Generic.List<MonsterHitRegion> _hitRegions = new();
    public MonsterHitRegion WeakSpot { get; private set; } = null!;
    public MonsterHitRegion HeadArmour { get; private set; } = null!;

    public void SetDifficultyAnimRate(double rate) => AnimSpeedRate = rate;

    public override void _Ready()
    {
        base._Ready();
        ApplyMaterials();
        CreateHitRegions();
    }

    private void CreateHitRegions()
    {
        var skeleton = BodyNode.FindChild("Skeleton3D", true, false) as Skeleton3D
            ?? throw new System.InvalidOperationException("Level2Boss requires its skeletal hit regions.");
        // Level2.unity: 16 Armour spheres and HurtSphere on Spine1. FBX bone-local
        // positions reflect X (e.g. Spine1 Unity x=-.239707, imported x=+.239735).
        void Sphere(string name, string bone, Vector3 unityOffset, float radius, Game.HitType type)
        {
            var region = MonsterHitRegion.AttachSphere(this, skeleton, name, bone,
                new Vector3(-unityOffset.X, unityOffset.Y, unityOffset.Z), radius, type);
            _hitRegions.Add(region);
            if (type == Game.HitType.Head) WeakSpot = region;
            if (bone == "Bip001 Head") HeadArmour = region;
        }
        Sphere("HurtSphere", "Bip001 Spine1", new(.025f, -.125f, -.007f), .15f, Game.HitType.Head);
        Sphere("LeftUpperArmArmour", "Bip001 L UpperArm", new(-.062f, 0, .062f), .1f, Game.HitType.Armour);
        Sphere("LeftCalfArmour", "Bip001 L Calf", Vector3.Zero, .15f, Game.HitType.Armour);
        Sphere("LeftForearmArmour", "Bip001 L Forearm", Vector3.Zero, .1f, Game.HitType.Armour);
        Sphere("RightClavicleArmour", "Bip001 R Clavicle", new(-.08f, 0, -.066f), .2f, Game.HitType.Armour);
        Sphere("RightThighArmour", "Bip001 R Thigh", new(-.066f, .077f, 0), .18f, Game.HitType.Armour);
        Sphere("LeftClavicleArmour", "Bip001 L Clavicle", new(-.092f, -.025f, .079f), .15f, Game.HitType.Armour);
        Sphere("SpineArmour", "Bip001 Spine1", new(-.111f, 0, 0), .2f, Game.HitType.Armour);
        Sphere("RightCalfArmour", "Bip001 R Calf", new(.1f, 0, 0), .18f, Game.HitType.Armour);
        Sphere("LeftThighArmour", "Bip001 L Thigh", new(-.094f, 0, 0), .15f, Game.HitType.Armour);
        Sphere("HeadArmour", "Bip001 Head", new(-.0641f, .0446f, .0094f), .15f, Game.HitType.Armour);
        Sphere("RightToeArmour", "Bip001 R Toe0", new(.11f, .306f, 0), .13f, Game.HitType.Armour);
        Sphere("RightUpperArmArmour", "Bip001 R UpperArm", new(-.191f, 0, 0), .14f, Game.HitType.Armour);
        Sphere("LeftHandArmour", "Bip001 L Hand", new(.033f, 0, 0), .1f, Game.HitType.Armour);
        Sphere("LeftFootArmour", "Bip001 L Foot", new(.2f, -.125f, .046f), .13f, Game.HitType.Armour);
        Sphere("PelvisArmour", "Bip001 Pelvis", new(-.22f, .053f, .006f), .2f, Game.HitType.Armour);
        Sphere("RightForearmArmour", "Bip001 R Forearm", new(-.126f, 0, 0), .12f, Game.HitType.Armour);
        // Level2.unity HurtSphere has a visible sphere mesh with Level2Boss.mat's golden tint.
        WeakSpot.AddChild(new MeshInstance3D
        {
            Name = "WeakCore",
            Mesh = new SphereMesh { Radius = .15f, Height = .3f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(.6132076f, .5089606f, 0),
                AlbedoTexture = GD.Load<Texture2D>("res://assets/models/monsters/level2_boss/weak_core_lava.png"),
                Roughness = .5f,
            }
        });
        // The locomotion capsule must not intercept shots ahead of the small weak point.
        CollisionLayer = 0;
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
        Anim.Advance(0);
        foreach (var region in _hitRegions) region.SyncActivation();
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
                if (!IsCurrentAnim("Run") && Anim.HasAnimation("Run"))
                {
                    SetAnimationSpeed(SpeedNormal);
                    Anim.Play("Run", 0.2);
                }
                return;
            }
        }
        // 到位:面向相机,按 CD 攻击
        Vector3 toCam = cam.GlobalPosition - GlobalPosition;
        LerpFace(toCam, delta);
        if (!Anim.IsPlaying() || IsCurrentAnim("Run"))
            PlayIdle();
        if (AttackReady())
        {
            LastAttackTime = Time.GetTicksMsec() / 1000.0;
            string skill = GD.Randf() < 0.5f ? "Skill1" : "Skill2"; // 50% Skill1 / 50% Skill2
            if (skill == "Skill1")
                _skill1Side = PickSkill1SideAndRotate(); // L2-6:出手前按选边 Rotate
            SetAnimationSpeed(SpeedAttack);
            Anim.Play(skill, 0);
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
            -Mathf.DegToRad(deg.X), -Mathf.DegToRad(deg.Y), Mathf.DegToRad(deg.Z)));
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
        var target = Basis.FromEuler(new Vector3(0, Mathf.Atan2(-to.X, -to.Z), 0)).GetRotationQuaternion();
        Quaternion = Quaternion.Slerp(target, Mathf.Clamp(TurnLerp * delta, 0, 1));
    }

    private void PlayIdle()
    {
        string idleName = Info.IdleAnim ?? Info.Idle2Anim;
        SetAnimationSpeed(SpeedNormal);
        if (Anim.HasAnimation(idleName))
            Anim.Play(idleName, 0.3f);
    }

    private void SetAnimationSpeed(float speed) => Anim.SpeedScale = speed * (float)AnimSpeedRate;

    protected override void HandleCombatAnimationEvent(string eventName, string parameter)
    {
        switch (eventName)
        {
            case "StartSlow": SetAnimationSpeed(SpeedSlow); break;
            case "StopSlow": SetAnimationSpeed(SpeedAttack); break;
            case "HertPlayerSkill1":
                SetAnimationSpeed(SpeedNormal);
                LastAttackTime = Time.GetTicksMsec() / 1000.0;
                HertPlayerSkill1();
                break;
            case "HertPlayerSkill2":
                SetAnimationSpeed(SpeedNormal);
                LastAttackTime = Time.GetTicksMsec() / 1000.0;
                HertPlayerSkill2();
                break;
        }
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
        ScheduleLifeAction(Skill2Delay, () =>
        {
            if (CurState == State.Dead || CurState == State.Idle)
                return;
            PlayerState.Instance.HitPlayer(atk, Game.AttackType.Ice, PlayerState.Side.Both);
        });
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

    /// <summary>Only the authored chest weak point damages HP; armour and body do not.</summary>
    public override string Hit(float attack, Vector3 point, Game.HitType hitType, PlayerState.Side side)
    {
        if (CurState == State.Dead || CurState == State.Idle)
            return Info.ImpactTag;
        if (IsCurrentAnim(Info.DamageAnim) && Anim.IsPlaying())
            return ""; // Damage02 状态中无敌
        if (hitType != Game.HitType.Head)
        {
            PlaySound($"metal_{GD.RandRange(1, 3)}"); // 随机金属音效(资源缺失时静默)
            return "Metal";
        }
        LastHitSide = side;
        Hp -= attack;
        FireSystem.ShowDamage($"- {(int)attack}", point, this);
        HpBar.SetHp(Mathf.Max(Hp, 0.0f) / GetMaxHp());
        LastAttackTime = Time.GetTicksMsec() / 1000.0 + HurtCdPenalty; // Unity: Time.time + 3
        if (Hp <= 0.0f)
        {
            Die();
        }
        else
        {
            PlaySound("hurt");
            SetAnimationSpeed(SpeedNormal);
            if (Anim.HasAnimation(Info.DamageAnim))
                Anim.Play(Info.DamageAnim, 0.1f);
            OnHurt(point, hitType, side);
        }
        return "Blood";
    }

    /// <summary>死亡:播 dead(Normal 0.5 速度),发 died 信号,不回收(关卡处理胜利与清理);
    /// Dead 状态持续下沉(Unity Update Dead 分支 m_char.Move(Vector3.down*dt)=1m/s)</summary>
    protected override void Die()
    {
        CurState = State.Dead;
        foreach (var region in _hitRegions) region.SyncActivation();
        Velocity = Vector3.Zero;
        GetNode<CollisionShape3D>("CollisionShape3D")
            .SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
        PlaySound("dead");
        FireSystem.SpawnBloodFlower(this, new Vector3(0.0f, 1.0f, 0.0f));
        SetAnimationSpeed(SpeedNormal);
        if (Anim.HasAnimation(Info.DeadAnim))
            Anim.Play(Info.DeadAnim, 0.1f);
        OnDeath();
        EmitSignal(SignalName.Died, this);
    }

    /// <summary>L2-7:Dead 状态 1m/s 持续下沉(等效 Unity m_char.Move(down*dt)),由关卡胜利后清理</summary>
    public override void _PhysicsProcess(double delta)
    {
        if (Game.Instance.IsGamePause) return;
        if (CurState == State.Dead)
        {
            GlobalPosition += Vector3.Down * (float)delta;
            return;
        }
        base._PhysicsProcess(delta);
    }

    /// <summary>胜利后由关卡清理(基类 Deactivate 为 protected,这里开公共口)</summary>
    public void CleanupByLevel() => Deactivate();

    protected override void Deactivate()
    {
        base.Deactivate();
        foreach (var region in _hitRegions) region.SyncActivation();
    }
}
