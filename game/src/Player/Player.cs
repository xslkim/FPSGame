namespace FPSGame;

/// <summary>
/// 单个玩家数据(5.5 换枪 / 8.3 经济)。对应原作 PlayerSystem 内部玩家结构。
/// </summary>
public sealed class Player
{
    public enum HurtState { Normal, Frozen, Poison }

    public const float MaxHp = 100.0f;

    public float Hp = MaxHp;
    public int Bullet;
    public int Guns;           // 已解锁枪位掩码 1 << gunType
    public int GunType = 2;    // 当前枪,起始手枪 HandGun
    public HurtState Status = HurtState.Normal;
    public bool Active;
    public int StatusSeq;      // 受击序号,防止过期冰冻定时器误清状态

    public void Born()
    {
        Hp = MaxHp;
        Guns = (1 << 0) | (1 << 1) | (1 << 2); // AK47 | M4 | HandGun
        GunType = 2;
        Bullet = SaveService.Instance.MaxBullet;
        Status = HurtState.Normal;
        Active = true;
        StatusSeq += 1;
    }

    /// <summary>向后找已解锁枪,回绕;只一把返回 false 不动画(5.5 节)</summary>
    public bool NextGun()
    {
        int count = 0;
        for (int t = 0; t < SaveService.Instance.GunTypeNum; t++)
        {
            if ((Guns & (1 << t)) != 0)
                count += 1;
        }
        if (count <= 1)
            return false;
        for (int i = 1; i <= SaveService.Instance.GunTypeNum; i++)
        {
            int cand = (GunType + i) % SaveService.Instance.GunTypeNum;
            if ((Guns & (1 << cand)) != 0)
            {
                GunType = cand;
                PlayerState.Instance.NotifyUiChanged();
                return true;
            }
        }
        return false;
    }

    public void UseBullet(int n)
    {
        Bullet = System.Math.Max(0, Bullet - n);
        PlayerState.Instance.NotifyUiChanged();
    }

    /// <summary>续命:HP=100 且 Bullet += MaxBullet(8.3 节)</summary>
    public void Relife()
    {
        Hp = MaxHp;
        Bullet += SaveService.Instance.MaxBullet;
        Status = HurtState.Normal;
        PlayerState.Instance.NotifyUiChanged();
    }

    /// <summary>补给箱解锁枪(AddGun)</summary>
    public void AddGun(int type) => Guns |= 1 << type;
}
