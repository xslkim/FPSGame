using Godot;

namespace FPSGame;

/// <summary>
/// ToonShoot / ToonShootAlien(6.3 ToonMonster 1:1):
/// born 时动画 SpeedScale=1+0.5×Lv;先走向分配的 FireWindow(距离>0.1 速度 3),
/// 到位后面向相机,CD 到播 reload;0.3s 动画事件(toon_shoot):视觉子弹(速度 50,1s 自隐)
/// + 射击音 + 0.5s 后定时命中(50% 选边,目标不活跃由 HitPlayer 转嫁另一侧)。
/// 死亡/回收释放窗口占用;换装:头/身/腿各随机一件,extra 配件逐件随机显隐。
/// </summary>
public partial class ToonMonster : Monster
{
    public const float WalkSpeed = 3.0f;
    public const float ArriveDist = 0.1f;
    public const string ReloadAnim = "reload"; // meta attack_anims=["reload"]
    public const float BulletSpeed = 50.0f;
    public const float BulletLife = 1.0f;
    public const float HitDelay = 0.5f;

    // M7 材质:militia 全身件同图集,武器独立图集;alien 拼装(宇航服/头盔/步枪)
    private const string ToonMatPath = "res://assets/models/monsters/toon/toon_mat.tres";
    private const string WeaponMatPath = "res://assets/models/monsters/toon/toon_weapon_mat.tres";
    private const string AlienSuitMatPath = "res://assets/models/monsters/toon_alien/alien_suit_mat.tres";
    private const string AlienHelmetMatPath = "res://assets/models/monsters/toon_alien/alien_helmet_mat.tres";
    private const string AlienRifleMatPath = "res://assets/models/monsters/toon_alien/alien_rifle_mat.tres";

    /// <summary>由 Level2 在 born 前分配(FireWindow.PickFree)</summary>
    public FireWindow? FireWindow;

    public override void _Ready()
    {
        base._Ready();
        ApplyMaterials();
        ChangeAppearance(); // 未 born 前也给完整形态(池陈列)
    }

    protected override void OnBorn()
    {
        Anim.SpeedScale = 1.0f + 0.5f * Level; // ToonShootAlien 动画 1+0.5Lv 倍速
        ChangeAppearance();
    }

    // ------------------------------------------------ 材质/换装(M7)

    private void ApplyMaterials()
    {
        var model = GetNodeOrNull<Node3D>("Model");
        if (model == null)
            return;
        var toonMat = GD.Load<Material>(ToonMatPath);
        var weaponMat = GD.Load<Material>(WeaponMatPath);
        var suitMat = GD.Load<Material>(AlienSuitMatPath);
        var helmetMat = GD.Load<Material>(AlienHelmetMatPath);
        var rifleMat = GD.Load<Material>(AlienRifleMatPath);
        bool alien = MetaKey == "toon_shoot_alien";
        foreach (var node in model.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mi)
                continue;
            string n = mi.Name.ToString().ToLower();
            if (alien)
            {
                if (n.Contains("spacesuit"))
                    mi.SetSurfaceOverrideMaterial(0, suitMat);
                else if (n.Contains("helmet"))
                    mi.SetSurfaceOverrideMaterial(0, helmetMat);
                else if (n.Contains("battlerifle"))
                    mi.SetSurfaceOverrideMaterial(0, rifleMat);
            }
            else
            {
                mi.SetSurfaceOverrideMaterial(0, n.Contains("weapon") ? weaponMat : toonMat);
            }
        }
    }

    /// <summary>换装(ChangeApperance):militia 头/身/腿各随机一件,extra 配件逐件随机显隐</summary>
    private void ChangeAppearance()
    {
        var sk = GetNodeOrNull<Skeleton3D>("Model/Skeleton3D");
        if (sk == null)
            return;
        var heads = new Godot.Collections.Array<MeshInstance3D>();
        var bodys = new Godot.Collections.Array<MeshInstance3D>();
        var legs = new Godot.Collections.Array<MeshInstance3D>();
        var extras = new Godot.Collections.Array<MeshInstance3D>();
        foreach (var c in sk.GetChildren())
        {
            if (c is not MeshInstance3D mi)
                continue;
            string n = mi.Name.ToString();
            if (n.StartsWith("head_") || n.StartsWith("Head_"))
                heads.Add(mi);
            else if (n.StartsWith("Body_"))
                bodys.Add(mi);
            else if (n.StartsWith("Legs_"))
                legs.Add(mi);
            else if (n.StartsWith("extra_"))
                extras.Add(mi);
        }
        foreach (var group in new[] { heads, bodys, legs })
        {
            foreach (var mi in group)
                mi.Visible = false;
            if (group.Count > 0)
                group[GD.RandRange(0, group.Count - 1)].Visible = true;
        }
        foreach (var mi in extras)
            mi.Visible = GD.Randf() < 0.5f;
    }

    // ------------------------------------------------ 行为

    protected override void UpdateActive(float delta)
    {
        if (IsPlayingAny(Info.AttackAnims) || (IsCurrentAnim(Info.DamageAnim) && Anim.IsPlaying()))
        {
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
            ApplyGravity(delta);
            MoveAndSlide();
            return;
        }
        if (FireWindow != null && IsInstanceValid(FireWindow)
            && GlobalPosition.DistanceTo(FireWindow.GlobalPosition) > ArriveDist)
        {
            // 走向窗口(距离>0.1 时速度 3)
            Vector3 dir = FireWindow.GlobalPosition - GlobalPosition;
            dir.Y = 0.0f;
            if (dir.LengthSquared() > 0.0001f)
            {
                dir = dir.Normalized();
                Vector3 rot = Rotation;
                rot.Y = YawTowards(rot.Y, Mathf.Atan2(-dir.X, -dir.Z), Info.TurnSpeed * delta);
                Rotation = rot;
            }
            Velocity = new Vector3(dir.X * WalkSpeed, Velocity.Y, dir.Z * WalkSpeed);
            ApplyGravity(delta);
            MoveAndSlide();
            if (!IsCurrentAnim("locomotion") && Anim.HasAnimation("locomotion"))
                Anim.Play("locomotion", 0.2);
            return;
        }
        // 到位:面向相机,CD 到播 reload
        Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
        ApplyGravity(delta);
        MoveAndSlide();
        FaceCamera();
        if (AttackReady())
        {
            DoAttack(); // 基类:记 CD + 播 reload + 0.3s 后 TriggerAttackEvent
        }
        else if (!Anim.IsPlaying() && Anim.HasAnimation(Info.Idle2Anim))
        {
            Anim.Play(Info.Idle2Anim, 0.3);
        }
    }

    /// <summary>动画事件 toon_shoot:视觉子弹 + 射击音 + 0.5s 后定时命中(50% 选边)</summary>
    protected override void TriggerAttackEvent()
    {
        if (CurState == State.Dead || CurState == State.Idle)
            return;
        ToonBullet.Spawn(GetTree().CurrentScene, GlobalPosition + new Vector3(0.0f, 1.2f, 0.0f),
            BulletSpeed, BulletLife);
        PlaySound("shoot");
        float atk = GetAttack();
        Game.AttackType atype = Info.AttackType;
        GetTree().CreateTimer(HitDelay).Timeout += () =>
        {
            if (CurState == State.Dead || CurState == State.Idle)
                return;
            // 50% 选边;目标侧不活跃由 PlayerState.HitPlayer 转嫁另一侧
            var side = GD.Randf() < 0.5f ? PlayerState.Side.Right : PlayerState.Side.Left;
            PlayerState.Instance.HitPlayer(atk, atype, side);
        };
    }

    // ------------------------------------------------ 死亡/回收释放窗口

    protected override void OnDeath() => ReleaseWindow();

    protected override void Recycle()
    {
        ReleaseWindow();
        base.Recycle();
    }

    private void ReleaseWindow()
    {
        if (FireWindow != null && IsInstanceValid(FireWindow))
            FireWindow.Release(this);
        FireWindow = null;
    }
}

/// <summary>
/// Toon 视觉子弹(EnemyBullet 思路,5.2):直线飞向相机,速度 50,1s 自隐,无伤害。
/// </summary>
public partial class ToonBullet : Node3D
{
    private Vector3 _dir = Vector3.Forward;
    private float _speed = 50.0f;
    private float _life = 1.0f;
    private float _t;

    public static void Spawn(Node parent, Vector3 from, float speed, float life)
    {
        var b = new ToonBullet { _speed = speed, _life = life };
        parent.AddChild(b);
        b.GlobalPosition = from;
    }

    public override void _Ready()
    {
        var mesh = new MeshInstance3D();
        var sphere = new SphereMesh { Radius = 0.08f, Height = 0.16f };
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.9f, 0.2f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.85f, 0.15f),
        };
        sphere.Material = mat;
        mesh.Mesh = sphere;
        AddChild(mesh);
        var cam = GetViewport().GetCamera3D();
        if (cam != null)
            _dir = (cam.GlobalPosition - GlobalPosition).Normalized();
    }

    public override void _Process(double delta)
    {
        float d = (float)delta;
        _t += d;
        GlobalPosition += _dir * _speed * d;
        if (_t >= _life)
            QueueFree();
    }
}
