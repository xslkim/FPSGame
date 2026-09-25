using Godot;

namespace FPSGame;

/// <summary>
/// 包头僵尸(L1 Boss):不移动(缓降 0.5/s);CD 到且处于 anim_idle → 播 anim_attack;
/// 0.3s 攻击事件 BaotouSkill 从左手骨(Bone_ L Hand)发火球(lastAttackTime=now);
/// 被打(Hit)→ lastAttackTime=now(5s 内不反击)+ ChangePositionStar 闪现(旧位,被打瞬间)
/// + 0.5s 后在出生点 x±1.8 / z±2 满幅随机瞬移并面向相机;Hurt 动画播完前不抢回 anim_idle。
/// </summary>
public partial class BaotouMonster : Monster
{
    public const float SinkSpeed = 0.5f;
    public const float TeleportDelay = 0.5f;

    private Tween? _teleportTween;
    private Node3D? _handAttach; // 火球发射点:原作 BaotouFireball 挂 Bone_ L Hand 下

    protected override void OnBorn() => IsBoss = true; // Boss 不超时自毁

    public override void _Ready()
    {
        base._Ready();
        // 弱点爱心(原作 FX_Pickup_Heart_01 挂脊柱骨;本体 Untagged 打不到,打爱心=打 Boss)
        Callable.From(() => BossHeart.Create(this)).CallDeferred();
    }

    protected override void UpdateActive(float delta)
    {
        Velocity = new Vector3(0.0f, IsOnFloor() ? 0.0f : -SinkSpeed, 0.0f);
        MoveAndSlide();
        if (IsPlayingAny(Info.AttackAnims))
            return;
        if (IsCurrentAnim(Info.DamageAnim) && Anim.IsPlaying())
            return; // Hurt 播完前不抢 idle(原作攻击门控也要求回到 anim_idle)
        string idleName = Info.IdleAnim ?? Info.Idle2Anim;
        if (!IsCurrentAnim(idleName) && Anim.HasAnimation(idleName))
            Anim.Play(idleName, 0.3);
        if (AttackReady())
            DoAttack();
    }

    /// <summary>攻击事件(原 BaotouSkill):lastAttackTime=now + 从左手骨发火球</summary>
    protected override void TriggerAttackEvent()
    {
        if (CurState == State.Dead || CurState == State.Idle)
            return;
        LastAttackTime = Time.GetTicksMsec() / 1000.0;
        Fireball.Spawn(GetTree().CurrentScene, HandPosition(), GetAttack(), Info.AttackType);
    }

    /// <summary>火球发射点:左手骨(原作 fireEffect.transform.position = handTrans.position,父=Bone_ L Hand)</summary>
    private Vector3 HandPosition()
    {
        if (_handAttach == null)
        {
            var skel = BodyNode.FindChild("Skeleton3D", true, false) as Skeleton3D;
            if (skel != null)
            {
                for (int i = 0; i < skel.GetBoneCount(); i++)
                {
                    if (skel.GetBoneName(i).Contains("L Hand"))
                    {
                        var a = new BoneAttachment3D { BoneIdx = i };
                        skel.AddChild(a);
                        _handAttach = a;
                        break;
                    }
                }
            }
        }
        return _handAttach?.GlobalPosition ?? GlobalPosition + new Vector3(0.0f, 1.2f, 0.0f);
    }

    /// <summary>被打(原作 BaotouMonster.Hit):重置攻击计时 + 星星闪现(旧位,被打瞬间)+ 0.5s 后瞬移</summary>
    protected override void OnHurt(Vector3 point, Game.HitType hitType, PlayerState.Side side)
    {
        LastAttackTime = Time.GetTicksMsec() / 1000.0;
        FlashAt(GlobalPosition); // ChangePositionStar.SetActive(false→true):被打瞬间旧位闪一次
        _teleportTween?.Kill();
        _teleportTween = CreateTween();
        _teleportTween.TweenInterval(TeleportDelay);
        _teleportTween.TweenCallback(Callable.From(Teleport));
    }

    /// <summary>0.5s 后瞬移:出生点 x±1.8 / z±2 满幅随机(原作 Random.Range(-1.8,1.8)/(-2,2)),面向相机</summary>
    private void Teleport()
    {
        if (CurState == State.Dead || CurState == State.Idle)
            return;
        float ox = (float)GD.RandRange(-1.8, 1.8);
        float oz = (float)GD.RandRange(-2.0, 2.0);
        GlobalPosition = BornPos + new Vector3(ox, 0.0f, oz);
        FaceCamera();
    }

    /// <summary>瞬移星星(原作 ChangePositionStar,随 Boss 的子物体特效):位置放一发 teleport_flash</summary>
    private void FlashAt(Vector3 pos)
    {
        var fx = GD.Load<PackedScene>("res://assets/effects/teleport_flash.tscn")
            .Instantiate<EffectBase>();
        GetTree().CurrentScene.AddChild(fx);
        fx.GlobalPosition = pos + new Vector3(0.0f, 1.0f, 0.0f);
        fx.Activate();
    }
}
