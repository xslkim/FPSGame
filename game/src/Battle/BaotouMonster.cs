using Godot;

namespace FPSGame;

/// <summary>
/// 包头僵尸(L1 Boss):不移动(缓降 0.5/s);CD 到且处于 idle → 播 attack;
/// 0.3s 攻击事件发火球(演出弹道 + 1s 定时命中 Both);
/// 被打 0.5s 后在出生点 x±0.5~1.8 / z±0.5~2 随机瞬移并面向相机。
/// </summary>
public partial class BaotouMonster : Monster
{
    public const float SinkSpeed = 0.5f;
    public const float TeleportDelay = 0.5f;

    private Tween? _teleportTween;

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
        string idleName = Info.IdleAnim ?? Info.Idle2Anim;
        if (!IsCurrentAnim(idleName) && Anim.HasAnimation(idleName))
            Anim.Play(idleName, 0.3);
        if (AttackReady())
            DoAttack();
    }

    /// <summary>攻击事件:发火球(1s 后必中 Both)</summary>
    protected override void TriggerAttackEvent()
    {
        if (CurState == State.Dead || CurState == State.Idle)
            return;
        Fireball.Spawn(GetTree().CurrentScene, GlobalPosition + new Vector3(0.0f, 1.2f, 0.0f),
            GetAttack(), Info.AttackType);
    }

    protected override void OnHurt(Vector3 point, Game.HitType hitType, PlayerState.Side side)
    {
        _teleportTween?.Kill();
        _teleportTween = CreateTween();
        _teleportTween.TweenInterval(TeleportDelay);
        _teleportTween.TweenCallback(Callable.From(Teleport));
    }

    private void Teleport()
    {
        if (CurState == State.Dead || CurState == State.Idle)
            return;
        FlashAt(GlobalPosition); // 消失点闪现
        float ox = (float)GD.RandRange(0.5, 1.8) * (GD.Randf() > 0.5f ? 1.0f : -1.0f);
        float oz = (float)GD.RandRange(0.5, 2.0) * (GD.Randf() > 0.5f ? 1.0f : -1.0f);
        GlobalPosition = BornPos + new Vector3(ox, 0.0f, oz);
        FaceCamera();
        FlashAt(GlobalPosition); // 出现点闪现
    }

    /// <summary>瞬移闪现:位置放一发 teleport_flash</summary>
    private void FlashAt(Vector3 pos)
    {
        var fx = GD.Load<PackedScene>("res://assets/effects/teleport_flash.tscn")
            .Instantiate<EffectBase>();
        GetTree().CurrentScene.AddChild(fx);
        fx.GlobalPosition = pos + new Vector3(0.0f, 1.0f, 0.0f);
        fx.Activate();
    }
}
