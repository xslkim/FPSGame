using Godot;

namespace FPSGame;

/// <summary>
/// 树皮石头怪 RockWarrior(L3 Boss):HP300,半径 36,CD8,Poison 攻击(meta attack_type=2)。
/// 1:1 原作 RockWarrior.cs:直线追击(MonsterBase.GetMoveSpeed,3m 内降速 1),
/// 进半径且 IsFaceToCamera(±15°) 放 atk01;动画事件 RockAttack → 10 倍大火球
/// (Fireball.Spawn,localScale×10,1s 定时命中 Both);
/// 硬化皮肤 Hit() override:仅 atk01 动画期间吃全额伤害并播受击动画/音效,
/// 其他状态每发只掉 1 血且无受击演出。死亡 → 基类 Boss 回收 → Victory。
/// </summary>
public partial class RockWarriorBoss : Monster
{
    public const float AttackFacing = 15.0f;  // 原作 IsFaceToCamera 15°
    public const float HardenedDamage = 1.0f; // 硬化皮肤单发伤害
    public const float FireballScale = 10.0f; // 原作 RockAttack localScale×10
    public const float FireballHeight = 1.5f;

    /// <summary>M7 材质(RockWarrior.png),在 .tscn 配置</summary>
    [Export] public Material? BodyMaterial;

    /// <summary>自检/调试:最后一发火球</summary>
    public Fireball? LastFireball { get; private set; }

    private string AtkAnim => Info.AttackAnims.Length > 0 ? Info.AttackAnims[0] : "atk01";

    public override void _Ready()
    {
        base._Ready();
        AnimTrackUtil.StripMethodTracks(Anim); // clip 自带 rock_attack 方法轨与 TriggerAttackEvent Tween 重复
        if (BodyMaterial != null)
        {
            foreach (var n in BodyNode.FindChildren("*", "MeshInstance3D", true, false))
                if (n is MeshInstance3D mi)
                    mi.SetSurfaceOverrideMaterial(0, BodyMaterial);
        }
    }

    protected override void UpdateActive(float delta)
    {
        // 原作用 AnimatorStateInfo.IsName("Locomotion") 门控
        if (IsPlayingAny(Info.AttackAnims) || (IsCurrentAnim(Info.DamageAnim) && Anim.IsPlaying()))
        {
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
            ApplyGravity(delta);
            MoveAndSlide();
            return;
        }
        var cam = GetViewport().GetCamera3D();
        if (cam == null)
            return;
        Vector3 target = cam.GlobalPosition;
        target.Y = GlobalPosition.Y;
        Vector3 to = target - GlobalPosition;
        float d = to.Length();
        if (d < AttackRadius && IsFaceToCamera(to))
        {
            if (AttackReady())
                DoAttack();
            else
            {
                Velocity = new Vector3(0.0f, Velocity.Y, 0.0f); // 原作 Move(Vector3.down*dt)
                ApplyGravity(delta);
                MoveAndSlide();
            }
            return;
        }
        Vector3 dir = d > 0.0001f ? to / d : Vector3.Zero;
        Vector3 rot = Rotation;
        rot.Y = YawTowards(rot.Y, Mathf.Atan2(-dir.X, -dir.Z), Info.TurnSpeed * delta);
        Rotation = rot;
        float speed = GetMoveSpeed();
        if (d < 3.0f)
            speed = Mathf.Min(speed, 1.0f); // 原作 MonsterBase.GetMoveSpeed 3m 内降速 1
        Velocity = new Vector3(dir.X * speed, Velocity.Y, dir.Z * speed);
        ApplyGravity(delta);
        MoveAndSlide();
        if (!IsCurrentAnim("locomotion") && Anim.HasAnimation("locomotion"))
            Anim.Play("locomotion", 0.2);
    }

    /// <summary>动画事件 RockAttack:10 倍大火球(定时命中 Both,Poison 类型走 meta)</summary>
    protected override void TriggerAttackEvent()
    {
        if (CurState == State.Dead || CurState == State.Idle)
            return;
        var parent = GetTree().CurrentScene;
        Fireball.Spawn(parent, GlobalPosition + new Vector3(0.0f, FireballHeight, 0.0f),
            GetAttack(), Info.AttackType);
        // Fireball.Spawn 同步挂载,最后一子即本次火球;原作 localScale×10 视觉
        if (parent.GetChild(parent.GetChildCount() - 1) is Fireball fb)
        {
            fb.Scale = Vector3.One * FireballScale;
            LastFireball = fb;
        }
    }

    /// <summary>硬化皮肤:非 atk01 动画状态只掉 1 血,不播受击动画/音效;atk01 中全额掉血</summary>
    public override string Hit(float attack, Vector3 point, Game.HitType hitType, PlayerState.Side side)
    {
        if (CurState == State.Dead || CurState == State.Idle)
            return Info.ImpactTag;
        if (IsCurrentAnim(AtkAnim) && Anim.IsPlaying())
            return base.Hit(attack, point, hitType, side);
        Hp -= HardenedDamage;
        FireSystem.ShowDamage($"- {(int)HardenedDamage}", point, this);
        HpBar.SetHp(Mathf.Max(Hp, 0.0f) / GetMaxHp());
        if (Hp <= 0.0f)
            Die();
        return Info.ImpactTag;
    }

    /// <summary>原作 IsFaceToCamera:到相机(y 压平)与 forward 夹角 < 15°</summary>
    private bool IsFaceToCamera(Vector3 flatToCam)
    {
        if (flatToCam.LengthSquared() < 0.0001f)
            return false;
        float desiredYaw = Mathf.Atan2(-flatToCam.X, -flatToCam.Z);
        return Mathf.Abs(Mathf.Wrap(desiredYaw - Rotation.Y, -Mathf.Pi, Mathf.Pi))
            < Mathf.DegToRad(AttackFacing);
    }
}
