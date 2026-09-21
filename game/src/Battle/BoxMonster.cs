using Godot;

namespace FPSGame;

/// <summary>
/// 牙齿宝箱:HP1、移速 3、20 秒自毁。逃跑跳:每 2 秒反向跳一次(6.2/6.3)。
/// 死亡掉落按箱类型:Bullet → +BoxBullet 弹;GunAK/GunM4 → 解锁对应枪位。
/// </summary>
public partial class BoxMonster : Monster
{
    public enum BoxKind { Bullet, GunAK, GunM4 }

    public const float HopInterval = 2.0f;
    public const float HopVy = 4.0f;

    public BoxKind Kind = BoxKind.Bullet;

    private Vector3 _moveDir = Vector3.Forward;
    private float _hopTime;

    protected override void OnBorn()
    {
        // 初始逃跑方向:背向相机
        var cam = GetViewport().GetCamera3D();
        if (cam != null)
        {
            Vector3 d = GlobalPosition - cam.GlobalPosition;
            d.Y = 0.0f;
            if (d.LengthSquared() > 0.01f)
                _moveDir = d.Normalized();
        }
        _hopTime = 0.0f;
    }

    protected override void UpdateActive(float delta)
    {
        _hopTime += delta;
        if (_hopTime >= HopInterval)
        {
            _hopTime = 0.0f;
            _moveDir = -_moveDir; // 每 2 秒反向
            if (IsOnFloor())
                Velocity = new Vector3(Velocity.X, HopVy, Velocity.Z);
        }
        Velocity = new Vector3(_moveDir.X * GetMoveSpeed(), Velocity.Y, _moveDir.Z * GetMoveSpeed());
        ApplyGravity(delta);
        MoveAndSlide();
        if (_moveDir.LengthSquared() > 0.01f)
        {
            Vector3 rot = Rotation;
            rot.Y = YawTowards(rot.Y, Mathf.Atan2(-_moveDir.X, -_moveDir.Z), Info.TurnSpeed * delta);
            Rotation = rot;
        }
    }

    /// <summary>掉落:对所有活跃玩家生效;死亡点放掉落光效</summary>
    protected override void OnDeath()
    {
        var fx = GD.Load<PackedScene>("res://assets/effects/pickup_drop.tscn").Instantiate<EffectBase>();
        GetTree().CurrentScene.AddChild(fx);
        fx.GlobalPosition = GlobalPosition + new Vector3(0.0f, 0.5f, 0.0f);
        fx.Activate();
        foreach (var side in new[] { PlayerState.Side.Left, PlayerState.Side.Right })
        {
            var p = PlayerState.Instance.GetPlayer(side);
            if (!p.Active)
                continue;
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
        }
        PlayerState.Instance.NotifyUiChanged();
    }
}
