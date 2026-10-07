using Godot;

namespace FPSGame;

/// <summary>
/// ToonShoot / ToonShootAlien(6.3 ToonMonster 1:1):
/// born 时动画 SpeedScale=1+0.5×Lv;先走向分配的 FireWindow(距离>0.1 速度 3),
/// 到位后面向相机,CD 到播 reload→shoot;原始 ToonShoot 动画事件发视觉子弹(速度 50,1s 自隐)
/// + 射击音 + 0.5s 后定时命中(50% 选边,目标不活跃由 HitPlayer 转嫁另一侧)。
/// 死亡/回收释放窗口占用;换装:头/身/腿各随机一件,extra 配件逐件随机显隐。
/// </summary>
public partial class ToonMonster : Monster
{
    public const float WalkSpeed = 3.0f;
    public const float ArriveDist = 0.1f;
    public const string ReloadAnim = "reload";
    public const string ShootAnim = "shoot";
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
    private Skeleton3D? _headSkeleton;
    private int _headBone = -1;
    private CollisionShape3D? _headCollision;

    public Vector3 HeadHitPosition => _headCollision != null ? _headCollision.GlobalPosition : GlobalPosition;

    public override void _Ready()
    {
        base._Ready();
        Info.AttackAnims = new[] { ReloadAnim, ShootAnim };
        EnsureAttachments(); // 导出包里 tscn 挂在 FBX 实例子树内的节点会静默丢失 → 代码补挂(编辑器已存在则跳过)
        ApplyMaterials();
        ChangeAppearance(); // 未 born 前也给完整形态(池陈列)
        _headSkeleton = FindChild("Skeleton3D", true, false) as Skeleton3D;
        if (_headSkeleton != null)
        {
            _headBone = _headSkeleton.FindBone(MetaKey == "toon_shoot_alien" ? "bn_Head" : "Bip001 Head");
            _headCollision = new CollisionShape3D { Name = "HeadHitShape", Disabled = true,
                Shape = new SphereShape3D { Radius = 0.24f } };
            AddChild(_headCollision);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_headCollision != null && _headSkeleton != null && _headBone >= 0 && IsActiveState && !IsDead)
            _headCollision.GlobalPosition = (_headSkeleton.GlobalTransform * _headSkeleton.GetBoneGlobalPose(_headBone)).Origin;
        base._PhysicsProcess(delta);
    }

    protected override void Deactivate()
    {
        _headCollision?.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
        base.Deactivate();
    }

    public override void Born(Vector3 pos, int level, float waittingTime)
    {
        base.Born(pos, level, waittingTime);
        // Apply the full idle pose before the first visible frame.
        Anim.Advance(0);
        _headCollision?.SetDeferred(CollisionShape3D.PropertyName.Disabled, false);
    }

    /// <summary>头盔/步枪(alien)与 AK47(militia)的 BoneAttachment 代码补挂</summary>
    private void EnsureAttachments()
    {
        var skel = FindChild("Skeleton3D", true, false) as Skeleton3D;
        if (skel == null)
            return;
        if (MetaKey == "toon_shoot_alien")
        {
            if (skel.FindChild("HeadAttach") == null)
            {
                var attach = new BoneAttachment3D { Name = "HeadAttach", BoneName = "bn_Head" };
                skel.AddChild(attach);
                var helmet = GD.Load<PackedScene>(
                    "res://assets/models/monsters/toon_alien/helmet_m_alpha.fbx").Instantiate<Node3D>();
                helmet.Name = "Helmet";
                attach.AddChild(helmet);
            }
            if (skel.FindChild("RifleAttach") == null)
            {
                var attach = new BoneAttachment3D { Name = "RifleAttach", BoneName = "PIV_RifleHandle" };
                skel.AddChild(attach);
                var rifle = GD.Load<PackedScene>(
                    "res://assets/models/monsters/toon_alien/rifle_battlerifle.fbx").Instantiate<Node3D>();
                rifle.Name = "Rifle";
                attach.AddChild(rifle);
            }
        }
        else if (MetaKey == "toon_shoot")
        {
            if (skel.FindChild("WeaponAttach") == null)
            {
                var attach = new BoneAttachment3D { Name = "WeaponAttach", BoneName = "WeaponContainer" };
                skel.AddChild(attach);
                var ak = GD.Load<PackedScene>(
                    "res://assets/models/monsters/toon/weapon_ak47.FBX").Instantiate<Node3D>();
                ak.Name = "AK47";
                ak.Scale = Vector3.One * 0.4228f; // toon.tscn 序列化值
                attach.AddChild(ak);
            }
        }
    }

    protected override void OnBorn()
    {
        LastAttackTime = Time.GetTicksMsec() / 1000.0; // Unity ToonMonster.born()
        Anim.SpeedScale = 1.0f + 0.5f * Level; // ToonShootAlien 动画 1+0.5Lv 倍速
        ChangeAppearance();
    }

    protected override void UpdateWaiting(float delta)
    {
        if (FireWindow != null && IsInstanceValid(FireWindow))
        {
            // Unity ToonSolder/Alien1 have CapsuleCollider, not CharacterController.
            // Window actors never call MonsterBase.UpdateWaitting or fall on entry.
            CurState = State.Active;
            UpdateActive(delta);
            return;
        }
        base.UpdateWaiting(delta);
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

    protected override void DoAttack() => Anim.Play(ReloadAnim, 0);

    protected override void HandleCombatAnimationEvent(string eventName, string parameter)
    {
        if (eventName == "BeginShoot") Anim.Play(ShootAnim, 0.25);
        else base.HandleCombatAnimationEvent(eventName, parameter);
    }

    protected override void UpdateActive(float delta)
    {
        // Window actors retain their authored platform height (Level2 bridges).
        // Street actors have no window: keep settling even during reload/hurt.
        if (FireWindow == null || !IsInstanceValid(FireWindow))
        {
            Velocity = new Vector3(0, Velocity.Y, 0);
            ApplyGravity(delta);
            MoveAndSlide();
            FaceCamera();
            if (IsPlayingAny(Info.AttackAnims) || (IsCurrentAnim(Info.DamageAnim) && Anim.IsPlaying()))
                return;
            if (AttackReady()) DoAttack();
            else if (!Anim.IsPlaying() && Anim.HasAnimation(Info.Idle2Anim))
                Anim.Play(Info.Idle2Anim, 0.3);
            return;
        }
        if (IsPlayingAny(Info.AttackAnims) || (IsCurrentAnim(Info.DamageAnim) && Anim.IsPlaying()))
        {
            Velocity = Vector3.Zero;
            return;
        }
        Vector3 windowDelta = FireWindow != null && IsInstanceValid(FireWindow)
            ? FireWindow.GlobalPosition - GlobalPosition : Vector3.Zero;
        windowDelta.Y = 0;
        if (windowDelta.Length() > ArriveDist)
        {
            // 走向窗口(距离>0.1 时速度 3)。原作 UpdateMoveTo:dir.y=0,纯水平移动,
            // 出生 y 与窗口 y 同值(prefab 序列化对),全程保持高度不下坠——
            // 带重力会沉穿廊桥缝隙掉到墙底,永远到不了窗(实机"怪物掉下去"根因)
            Vector3 dir = windowDelta;
            dir.Y = 0.0f;
            if (dir.LengthSquared() > 0.0001f)
            {
                dir = dir.Normalized();
                Vector3 rot = Rotation;
                rot.Y = YawTowards(rot.Y, Mathf.Atan2(-dir.X, -dir.Z), Info.TurnSpeed * delta);
                Rotation = rot;
            }
            float speed = Mathf.Min(WalkSpeed, windowDelta.Length() / Mathf.Max(delta, 0.001f));
            Velocity = Vector3.Zero;
            // Original UpdateMoveTo's m_char == null branch: authored entrances
            // can cross doorway geometry. The shot capsule still blocks gun rays.
            GlobalPosition += new Vector3(dir.X * speed, 0.0f, dir.Z * speed) * delta;
            if (!IsCurrentAnim("locomotion") && Anim.HasAnimation("locomotion"))
                Anim.Play("locomotion", 0.2);
            return;
        }
        // 到位:面向相机,CD 到播 reload。原作到点后不再调 m_char.Move → 无重力定身,
        // 保持窗口高度(廊桥有缝隙也不下坠)
        Velocity = Vector3.Zero;
        FaceCamera();
        if (AttackReady())
        {
            DoAttack(); // 播 reload；动画事件记录射击 CD。
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
        LastAttackTime = Time.GetTicksMsec() / 1000.0; // Unity toon_shoot animation event
        ToonBullet.Spawn(GetTree().CurrentScene, GlobalPosition + new Vector3(0.0f, 1.2f, 0.0f),
            BulletSpeed, BulletLife);
        PlaySound("shoot");
        float atk = GetAttack();
        Game.AttackType atype = Info.AttackType;
        ScheduleLifeAction(HitDelay, () =>
        {
            if (CurState == State.Dead || CurState == State.Idle)
                return;
            // 50% 选边;目标侧不活跃由 PlayerState.HitPlayer 转嫁另一侧
            var side = GD.Randf() < 0.5f ? PlayerState.Side.Right : PlayerState.Side.Left;
            PlayerState.Instance.HitPlayer(atk, atype, side);
        });
    }

    // ------------------------------------------------ 死亡/回收释放窗口

    protected override void OnDeath()
    {
        _headCollision?.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
        ReleaseWindow();
    }

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
        var camera = b.GetViewport().GetCamera3D();
        if (camera != null) b._dir = (camera.GlobalPosition - from).Normalized();
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
