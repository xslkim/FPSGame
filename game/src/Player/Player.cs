namespace FPSGame;

/// <summary>
/// 单个玩家数据(5.5 换枪 / 8.3 经济)。对应原作 PlayerSystem 内部玩家结构。
/// </summary>
public sealed class Player
{
    public enum HurtState { Normal, Frozen, Poison }

    public const float MaxHp = 100.0f;

    public float Hp = MaxHp;
    public int Guns;           // 已解锁枪位掩码 1 << gunType
    public int GunType = 2;    // 当前枪,起始手枪 HandGun
    public HurtState Status = HurtState.Normal;
    public bool Active;
    public int StatusSeq;      // 受击序号,防止过期冰冻定时器误清状态

    private int _bullet;

    /// <summary>子弹数。外部直接加(如宝箱 p.Bullet += n)→ setter 触发 PlayerState 加子弹放大动画
    /// (原作 AddBulletAni 由 PlayerSystem.AddBullet 启动);Born/Relife 写 _bullet 绕过(原作无动画)</summary>
    public int Bullet
    {
        get => _bullet;
        set
        {
            int old = _bullet;
            _bullet = value;
            if (value > old && Active)
                PlayerState.Instance?.OnBulletAdded(this, old, value);
        }
    }

    public void Born()
    {
        Hp = MaxHp;
        Guns = (1 << 0) | (1 << 1) | (1 << 2); // AK47 | M4 | HandGun
        GunType = 2;
        // 原作 Player.cs:50-57:IsDebug → 90,否则 UserData.MaxBullet(120)
        _bullet = Game.Instance.IsDebug ? 90 : SaveService.Instance.MaxBullet;
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

    /// <summary>续命:HP=100 且 Bullet += MaxBullet(8.3 节;原作 Relife 直接加,无放大动画)</summary>
    public void Relife()
    {
        Hp = MaxHp;
        _bullet += SaveService.Instance.MaxBullet;
        Status = HurtState.Normal;
        PlayerState.Instance.NotifyUiChanged();
    }

    /// <summary>补给箱解锁枪(AddGun)</summary>
    public void AddGun(int type) => Guns |= 1 << type;
}
