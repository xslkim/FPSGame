using Godot;

namespace FPSGame;

/// <summary>
/// 牙齿宝箱(原作 BoxMonster.cs + 场景三箱序列化):HP1、移速 3(实际不移动)。
/// 真值流程:原地等待 WaittingTime(school_day 三箱全 20s,BoxAk.prefab 本体为 15 但本关未用),
/// 等待期每 Idle02Time 秒插播 LickAttack(BoxBullet/BoxAK=3s、BoxM4=5s);LifeActiveTime=20s 到
/// → HP=0 → Invoke("DestorySelf", 1.5) 自灭(无掉落、无死亡演出)。
/// 逃跑跳分支(RunAway)与自灭同帧,原作实际永不触发,不移植。
/// 被打(HP1 即死)才掉落,只发受击侧(原作 OnDead(bool Right)):子弹箱 +BoxBullet(60) 弹、
/// AK/M4 箱解锁对应枪;原作无掉落光效/音效。
/// </summary>
public partial class BoxMonster : Monster
{
    public enum BoxKind { Bullet, GunAK, GunM4 }

    public BoxKind Kind = BoxKind.Bullet;

    private bool _expiring;

    /// <summary>按箱 kind 取 prefab/场景序列化等待时间(school_day:Bullet 20 / AK 20 / M4 20)</summary>
    public static float WaittingTimeOf(BoxKind kind) => KindValue(kind, "waitting_time", 20.0f);

    /// <summary>按箱 kind 取 Idle02Time(BoxBullet/BoxAK=3、BoxM4=5)</summary>
    public static float Idle2IntervalOf(BoxKind kind) => KindValue(kind, "idle2_interval", 3.0f);

    private static float KindValue(BoxKind kind, string key, float def)
    {
        var info = MonsterInfo.Load("box");
        if (info.Kinds == null)
            return def;
        string k = kind switch
        {
            BoxKind.GunAK => "gun_ak",
            BoxKind.GunM4 => "gun_m4",
            _ => "bullet",
        };
        if (info.Kinds.ContainsKey(k))
        {
            var d = info.Kinds[k].AsGodotDictionary();
            if (d.ContainsKey(key))
                return (float)d[key].AsDouble();
        }
        return def;
    }

    protected override void OnBorn()
    {
        _expiring = false;
        Info.Idle2Interval = Idle2IntervalOf(Kind); // 等待期插播 LickAttack 的间隔
    }

    /// <summary>等待结束(与 20s 自灭同帧,原作 RunAway 分支永不触发):原地站立</summary>
    protected override void UpdateActive(float delta)
    {
        Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
        ApplyGravity(delta);
        MoveAndSlide();
    }

    /// <summary>20s 到:原作 HP=0 → Invoke("DestorySelf", 1.5)——无掉落、无死亡演出</summary>
    protected override void OnLifeTimeout()
    {
        if (_expiring)
            return;
        _expiring = true;
        Hp = 0.0f;
        GetTree().CreateTimer(RecycleDelay).Timeout += () =>
        {
            if (CurState != State.Idle)
                Recycle();
        };
    }

    /// <summary>掉落(原作 OnDead(bool Right)):只发受击侧;子弹箱 +60 弹、枪箱解锁对应枪;无掉落 FX</summary>
    protected override void OnDeath()
    {
        var p = PlayerState.Instance.GetPlayer(LastHitSide);
        if (p.Active)
        {
            switch (Kind)
            {
                case BoxKind.Bullet:
                    p.Bullet += SaveService.Instance.BoxBullet;
                    break;
                case BoxKind.GunAK:
                    p.AddGun(0);
                    break;
                case BoxKind.GunM4:
                    p.AddGun(1);
                    break;
            }
            PlayerState.Instance.NotifyUiChanged();
        }
    }
}
