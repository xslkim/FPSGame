using Godot;

namespace FPSGame;

/// <summary>
/// 飞斧头僵尸:攻击半径 = 7+rand(0,3)(原作 int 重载,born 时定);面向相机出生。
/// 出生射线参数走 meta born_override(±15°/12m)。
/// Skill 攻击 = 扔斧(原作 ZombieSkill.cs + MonsterAZ@Skill 动画事件):
///   起手 TakeHandAxe@0.224s(头后斧 axe_01 隐、手斧 axe_02 显)→ ThrowAxe@0.600s
///   (双斧隐,生成斧头投射物,4s 后 ResetAxe 复原)。
/// 命中率 HitRate=0.15 × 难度倍率(原作 _levelMeta.DiffRateHard/Hell,按本关 level_meta diff_rate);
/// 未命中目标点上下±且左右±各偏移 3+rand(0,3)m;伤害由投射物命中结算(见 ProjectileAxe)。
/// 原作 Skill 无 EventAttack 事件 → lastAttackTime 不更新,回 Locomotion 即可再攻(CD 对飞斧不生效)。
/// </summary>
public partial class FlyAxeMonster : Monster
{
    public const double TakeHandAxeTime = 0.22410919; // MonsterAZ@Skill.FBX.meta 动画事件
    public const double ThrowAxeTime = 0.60037744;
    public const double AxeResetDelay = 4.0;          // 原作 Invoke("ResetAxe", 4)

    private Node3D? _axeHead;   // axe_01(头后斧,待机常显)
    private Node3D? _axeHand;   // axe_02(手斧,Skill 期间显)
    private MeshInstance3D? _axeHandMesh;
    private int _axeEpoch;      // 原作 CancelInvoke("ResetAxe"):新一轮起手取消待复原
    private Tween? _skillTween;

    public override void _Ready()
    {
        base._Ready();
        _axeHead = FindAxeNode("axe_01");
        _axeHand = FindAxeNode("axe_02");
        _axeHandMesh = FindAxeMesh(_axeHand);
        ResetAxe(); // 原作 ZombieSkill.Start → ResetAxe(待机只显头后斧)
    }

    private Node3D? FindAxeNode(string name)
    {
        var skel = BodyNode.GetNodeOrNull<Node3D>("Skeleton3D");
        var n = skel?.GetNodeOrNull<Node3D>(name);
        return n ?? BodyNode.FindChild(name, true, false) as Node3D;
    }

    private static MeshInstance3D? FindAxeMesh(Node3D? axeNode)
    {
        if (axeNode is MeshInstance3D mi)
            return mi;
        if (axeNode == null)
            return null;
        foreach (var c in axeNode.GetChildren())
            if (c is MeshInstance3D m)
                return m;
        foreach (var c in axeNode.FindChildren("*", "MeshInstance3D", true, false))
            if (c is MeshInstance3D m2)
                return m2;
        return null;
    }

    protected override void OnBorn()
    {
        if (Info.AttackRadiusBase.HasValue)
            Info.AttackRadius = Info.AttackRadiusBase.Value
                + GD.RandRange(0, (int)(Info.AttackRadiusRand ?? 0.0f)); // 原作 int 重载 7/8/9
        FaceCamera();
        ResetAxe();
    }

    /// <summary>Skill 攻击:播 Skill(零混合)+ 动画事件节奏 TakeHandAxe/ThrowAxe;
    /// 不排基类 0.3s 近战事件(伤害由斧头投射物结算),不动 LastAttackTime(原作不更新)</summary>
    protected override void DoAttack()
    {
        Anim.Play(Info.AttackAnims[0], 0.0f); // "Skill",原作 CrossFade(...,0)
        _skillTween?.Kill();
        _skillTween = CreateTween();
        _skillTween.TweenInterval(TakeHandAxeTime);
        _skillTween.TweenCallback(Callable.From(TakeHandAxe));
        _skillTween.TweenInterval(ThrowAxeTime - TakeHandAxeTime);
        _skillTween.TweenCallback(Callable.From(ThrowAxe));
    }

    /// <summary>近战事件对飞斧不存在(伤害由投射物);空覆盖防基类排程误伤</summary>
    protected override void TriggerAttackEvent() { }

    private void TakeHandAxe()
    {
        if (CurState != State.Active)
            return;
        _axeEpoch++; // CancelInvoke("ResetAxe")
        if (_axeHand != null)
            _axeHand.Visible = true;
        if (_axeHead != null)
            _axeHead.Visible = false;
    }

    private void ThrowAxe()
    {
        if (CurState != State.Active)
            return;
        if (_axeHand != null)
            _axeHand.Visible = false;
        if (_axeHead != null)
            _axeHead.Visible = false;
        int epoch = _axeEpoch;
        GetTree().CreateTimer(AxeResetDelay).Timeout += () =>
        {
            if (CurState != State.Idle && epoch == _axeEpoch)
                ResetAxe();
        };
        // 命中率 × 难度倍率(原作 ZombieSkill.ThrowAxe)
        float hitRate = HitRateByDifficulty();
        bool hit = GD.Randf() < hitRate;
        var cam = GetViewport().GetCamera3D();
        Vector3 from = _axeHandMesh?.GlobalPosition ?? GlobalPosition + new Vector3(0.0f, 1.4f, 0.0f);
        Vector3 dir;
        if (cam == null)
        {
            dir = -GlobalBasis.Z;
        }
        else if (hit)
        {
            dir = (cam.GlobalPosition - from).Normalized();
        }
        else
        {
            // 未命中:目标点上下±且左右±各偏 3+rand(0,3)m(原作 int 重载 → 3/4/5)
            float offset = 3.0f + GD.RandRange(0, 3);
            Vector3 cpos = cam.GlobalPosition;
            cpos += cam.GlobalBasis.Y.Normalized() * (GD.RandRange(0, 2) == 0 ? offset : -offset);
            cpos += cam.GlobalBasis.X.Normalized() * (GD.RandRange(0, 2) == 0 ? offset : -offset);
            dir = (cpos - from).Normalized();
        }
        ProjectileAxe.Spawn(GetTree().CurrentScene, _axeHandMesh, from, dir, GetAttack(), Info.AttackType);
    }

    /// <summary>待机姿态:手斧隐、头后斧显(原作 ResetAxe)</summary>
    private void ResetAxe()
    {
        if (_axeHand != null)
            _axeHand.Visible = false;
        if (_axeHead != null)
            _axeHead.Visible = true;
    }

    /// <summary>原作 _meta.HitRate × _levelMeta.DiffRateHard(1.8)/DiffRateHell(2.6)(按本关 level_meta diff_rate)</summary>
    private float HitRateByDifficulty()
    {
        var diff = Game.Instance.CurrentDifficulty;
        if (diff == Game.Difficulty.Easy)
            return Info.HitRate;
        string key = diff == Game.Difficulty.Hard ? "hard" : "hell";
        double mult = diff == Game.Difficulty.Hard ? 1.8 : 2.6; // 原作 Level1 GetLevelMeta 兜底
        if (GetTree().CurrentScene is LevelBase level)
        {
            var levels = SaveService.Get(SaveService.Instance.GetLevelMeta(), "levels",
                new Godot.Collections.Dictionary()).AsGodotDictionary();
            if (levels.ContainsKey(level.LevelKey))
            {
                var rates = SaveService.Get(levels[level.LevelKey].AsGodotDictionary(), "diff_rate",
                    new Godot.Collections.Dictionary()).AsGodotDictionary();
                if (rates.ContainsKey(key))
                    mult = rates[key].AsDouble();
            }
        }
        return Info.HitRate * (float)mult;
    }
}
