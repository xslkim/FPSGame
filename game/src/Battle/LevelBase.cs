using Godot;

namespace FPSGame;

/// <summary>
/// 关卡基类:难度倍率 / 波次推进 / 机位切换+冻结刷怪 / 补给箱概率 / 胜负与续币 / BGM。
/// 对应原作 LevelBase.cs(波参数 GetLevelMeta 硬编码,移植进 data/level_meta.json)。
/// 派生(如 Level1)负责开场演出,结束后调 StartBattle()。
/// </summary>
public partial class LevelBase : Node3D
{
    [Signal] public delegate void LevelVictoryEventHandler();
    [Signal] public delegate void OpenContinueEventHandler(bool isOpen, int side);

    public const float CamBlendTime = 1.5f;    // 机位切换 Tween(Cinemachine blend 近似)
    public const float SpawnFreezeTime = 2.0f; // 切机位后冻结刷怪 2 秒
    public const float VictoryDelay = 2.0f;    // 胜利延迟(原作 Invoke 2s,单次触发)

    [Export] public string LevelKey = "level1";
    [Export] public NodePath[] CamPositionPaths = System.Array.Empty<NodePath>();
    [Export] public NodePath PoolPath = "MonsterPool";
    [Export] public NodePath CameraPath = "Camera3D";
    [Export] public NodePath FireSystemPath = "Camera3D/FireSystem";
    [Export] public NodePath BgmPlayerPath = "BGM";

    protected Node3D[] CamPositions = System.Array.Empty<Node3D>();
    protected MonsterPool Pool = null!;
    protected Camera3D Camera = null!;
    protected FireSystem FireSys = null!;
    protected AudioStreamPlayer BgmPlayer = null!;

    protected Godot.Collections.Dictionary Meta = new();
    protected Godot.Collections.Array Groups = new();
    protected double DiffRate = 1.0;

    protected int CurGroup = -1;
    protected int MonsterLeft;
    protected bool DebugAutoKill;   // 自检:怪出生后自动击杀,跑通全流程
    protected float TimeScaleTest = 1.0f; // 自检加速

    protected Godot.Collections.Dictionary Cur = new();
    protected float SpawnTimer;
    protected float FreezeTimer;
    private float _dbgTimer;
    protected int PoolIdx;
    protected bool BattleActive;
    protected int VictoryState; // 0 未触发 / 1 倒计时中 / 2 已发信号
    protected Monster? Boss;

    // 自检统计
    public int StatCamSwitches;
    public int StatFreezes;
    public int StatBoxRolls;
    public bool StatBossBgm;
    public readonly System.Collections.Generic.List<int> StatGroupsStarted = new();
    public double BattleStartTime;
    public int LastStars;

    public override void _Ready()
    {
        LoadMeta();
        Pool = GetNodeOrNull<MonsterPool>(PoolPath)!;
        Camera = GetNodeOrNull<Camera3D>(CameraPath)!;
        FireSys = GetNodeOrNull<FireSystem>(FireSystemPath)!;
        BgmPlayer = GetNodeOrNull<AudioStreamPlayer>(BgmPlayerPath)!;
        var cams = new System.Collections.Generic.List<Node3D>();
        foreach (var p in CamPositionPaths)
        {
            var n = GetNodeOrNull<Node3D>(p);
            if (n != null)
                cams.Add(n);
        }
        CamPositions = cams.ToArray();
        RegisterMonsterTypes();
        PlayerState.Instance.PlayerDied += OnPlayerDied;
        if (FireSys != null)
            FireSys.OpenContinue += (isOpen, side) => EmitSignal(SignalName.OpenContinue, isOpen, side);
        ResetGameState();
        EnterLevel();
    }

    /// <summary>类型→场景注册(各关按需覆盖/补充)</summary>
    protected virtual void RegisterMonsterTypes()
    {
        Pool.RegisterType("bull",
            GD.Load<PackedScene>("res://scenes/battle/monsters/bull.tscn"), 8);
        Pool.RegisterType("axe_zombie",
            GD.Load<PackedScene>("res://scenes/battle/monsters/axe_zombie.tscn"), 8);
        Pool.RegisterType("fly_axe_zombie",
            GD.Load<PackedScene>("res://scenes/battle/monsters/fly_axe_zombie.tscn"), 8);
        Pool.RegisterType("skeleton",
            GD.Load<PackedScene>("res://scenes/battle/monsters/skeleton.tscn"), 8);
        Pool.RegisterType("baotou",
            GD.Load<PackedScene>("res://scenes/battle/monsters/baotou.tscn"), 1);
        Pool.RegisterType("box",
            GD.Load<PackedScene>("res://scenes/battle/monsters/box_monster.tscn"), 2);
    }

    /// <summary>进场状态:Battle + 计数清零 + 重置玩家;关卡 Start 强制 ControllerOrRight</summary>
    protected virtual void ResetGameState()
    {
        Game.Instance.SceneState = Game.GameState.Battle;
        PlayerState.CurAliveMonster = 0;
        InputRouter.Instance.SetInputMode(InputRouter.InputMode.ControllerOrRight);
        PlayerState.Instance.UpdateUiMode("Battle"); // 战斗 HUD(原作 UpdateUIMode 其余=Battle)
    }

    /// <summary>派生覆盖:开场演出,结束后调 StartBattle()</summary>
    protected virtual void EnterLevel() => StartBattle();

    /// <summary>开场结束/无开场 → 开战:开输入、绑枪、播 bgm、开 G0</summary>
    protected virtual void StartBattle()
    {
        InputRouter.Instance.FireEnabled = true;
        BattleStartTime = Time.GetTicksMsec() / 1000.0;
        if (FireSys != null)
        {
            FireSys.BindPlayers();
            FireSys.SetProcess(true);
            FireSys.Visible = true;
        }
        PlayerState.Instance.ShowBattleBullets();
        PlayMusic(SaveService.Get(Meta, "bgm", "").AsString());
        BattleActive = true;
        StartGroup(0);
    }

    protected void LoadMeta()
    {
        var levels = SaveService.Get(SaveService.Instance.GetLevelMeta(), "levels",
            new Godot.Collections.Dictionary()).AsGodotDictionary();
        Meta = levels.ContainsKey(LevelKey)
            ? levels[LevelKey].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        Groups = SaveService.Get(Meta, "groups", new Godot.Collections.Array()).AsGodotArray();
        var rates = SaveService.Get(Meta, "diff_rate",
            new Godot.Collections.Dictionary { ["easy"] = 1.0, ["hard"] = 1.5, ["hell"] = 2.0 })
            .AsGodotDictionary();
        DiffRate = Game.Instance.CurrentDifficulty switch
        {
            Game.Difficulty.Hard => rates["hard"].AsDouble(),
            Game.Difficulty.Hell => rates["hell"].AsDouble(),
            _ => rates["easy"].AsDouble(),
        };
    }

    /// <summary>难度公式:数量=(int)(基础×倍率)、间隔=基础÷倍率、存活上限=(int)(上限×倍率)</summary>
    protected Godot.Collections.Dictionary CalcGroupParams(int i)
    {
        var g = Groups[i].AsGodotDictionary();
        return new Godot.Collections.Dictionary
        {
            ["num"] = (int)(SaveService.Get(g, "monster_num", 0).AsDouble() * DiffRate),
            ["interval"] = SaveService.Get(g, "interval", 3.0).AsDouble() / DiffRate,
            ["max_alive"] = (int)(SaveService.Get(g, "max_alive", 2.0).AsDouble() * DiffRate),
            ["bullet_box_rate"] = (float)SaveService.Get(g, "bullet_box_rate", 0.05).AsDouble(),
            ["pool"] = SaveService.Get(g, "pool", new Godot.Collections.Array()).AsGodotArray(),
        };
    }

    protected virtual void StartGroup(int i)
    {
        CurGroup = i;
        StatGroupsStarted.Add(i);
        Cur = CalcGroupParams(i);
        MonsterLeft = SaveService.Get(Cur, "num", 0).AsInt32();
        PoolIdx = 0;
        SpawnTimer = 0.0f;
        FreezeTimer = SpawnFreezeTime * TimeScaleTest; // 切机位后冻结刷怪 2 秒
        StatFreezes += 1;
        SwitchCamera(i);
        if (i == SaveService.Get(Meta, "boss_group", -1).AsInt32()
            && SaveService.Get(Meta, "boss", "").AsString() != "")
        {
            SpawnBoss();
            PlayMusic(SaveService.Get(Meta, "boss_bgm", "").AsString());
            StatBossBgm = true;
        }
        GD.Print($"[{LevelKey}] group {i} start: num={MonsterLeft} " +
            $"interval={SaveService.Get(Cur, "interval", 3.0).AsDouble():0.00} " +
            $"max_alive={SaveService.Get(Cur, "max_alive", 0).AsInt32()}");
    }

    protected void SwitchCamera(int i)
    {
        if (Camera == null || i >= CamPositions.Length || CamPositions[i] == null)
            return;
        StatCamSwitches += 1;
        var tw = CreateTween();
        tw.TweenProperty(Camera, "global_transform", CamPositions[i].GlobalTransform,
            CamBlendTime * Mathf.Max(TimeScaleTest, 0.05f)).SetTrans(Tween.TransitionType.Sine);
    }

    protected void SpawnBoss()
    {
        string key = SaveService.Get(Meta, "boss", "").AsString();
        Boss = Pool.GetMonster(key);
        if (Boss == null)
        {
            GD.PushError($"[{LevelKey}] boss pool empty: {key}");
            return;
        }
        var bornOverride = SaveService.Get(Meta, "boss_born_override",
            new Godot.Collections.Dictionary()).AsGodotDictionary();
        float fov = (float)SaveService.Get(bornOverride, "max_fov",
            SaveService.Get(Meta, "born_max_fov", 33.0)).AsDouble();
        float len = (float)SaveService.Get(bornOverride, "max_length",
            SaveService.Get(Meta, "born_max_length", 8.0)).AsDouble();
        var prm = Boss.GetBornParams(fov, len);
        Boss.Born(Boss.GetBornPosition(prm.X, prm.Y), 0, 0.0f);
        Boss.Died += _ => { };
        GD.Print($"[{LevelKey}] boss {key} born at {Boss.GlobalPosition}");
        if (DebugAutoKill)
            GetTree().CreateTimer(0.15).Timeout += () =>
            {
                if (Boss != null && Boss.IsActiveState && !Boss.IsDead)
                    Boss.Hit(99999.0f, Boss.GlobalPosition, Game.HitType.Body, PlayerState.Side.Right);
            };
    }

    public override void _Process(double delta)
    {
        if (!BattleActive || Game.Instance.IsGamePause)
            return;
        _dbgTimer += (float)delta;
        if (DebugAutoKill && _dbgTimer > 5.0f)
        {
            _dbgTimer = 0.0f;
            GD.Print($"[dbg] g={CurGroup} left={MonsterLeft} alive={PlayerState.CurAliveMonster} freeze={FreezeTimer:0.0} spawnT={SpawnTimer:0.00}");
        }
        if (FreezeTimer > 0.0f)
            FreezeTimer -= (float)delta;
        else
        {
            SpawnTimer += (float)delta;
            if (SpawnTimer >= SaveService.Get(Cur, "interval", 3.0).AsSingle() * TimeScaleTest)
            {
                SpawnTimer = 0.0f;
                SpawnTick();
            }
        }
        CheckProgress();
    }

    /// <summary>刷怪 tick:先判子弹箱 → gun_rate 判枪箱(AK/M4 各半)→ 从本波 pool 轮询取怪</summary>
    protected virtual void SpawnTick()
    {
        if (MonsterLeft <= 0)
            return;
        if (SaveService.Get(Cur, "max_alive", 0).AsInt32() <= 0)
            return;
        if (PlayerState.CurAliveMonster >= SaveService.Get(Cur, "max_alive", 0).AsInt32())
            return;
        var poolArr = SaveService.Get(Cur, "pool", new Godot.Collections.Array()).AsGodotArray();
        if (poolArr.Count == 0)
        {
            MonsterLeft = 0; // 空波(L4 G0/G6 语义)
            return;
        }
        string key = "";
        int boxKind = -1;
        StatBoxRolls += 1;
        if (Pool.HasInactive("box"))
        {
            if (GD.Randf() < SaveService.Get(Cur, "bullet_box_rate", 0.05).AsSingle())
            {
                key = "box";
                boxKind = (int)BoxMonster.BoxKind.Bullet;
            }
            else if (GD.Randf() < SaveService.Get(Meta, "gun_rate", 0.02).AsSingle())
            {
                key = "box";
                boxKind = GD.Randf() < 0.5f
                    ? (int)BoxMonster.BoxKind.GunAK
                    : (int)BoxMonster.BoxKind.GunM4;
            }
        }
        if (key == "")
        {
            key = poolArr[PoolIdx % poolArr.Count].AsString();
            PoolIdx += 1;
        }
        var m = Pool.GetMonster(key);
        if (m == null)
        {
            if (boxKind < 0)
                PoolIdx -= 1; // 池满,下 tick 重试同一只
            return;
        }
        if (m is BoxMonster bm)
            bm.Kind = (BoxMonster.BoxKind)boxKind;
        // 出生点:±33°(FOV>50 → ±40°)/ 8m,按波覆盖 born_length_override
        float fov = SaveService.Get(Meta, "born_max_fov", 33.0).AsSingle();
        if (Camera != null && Camera.Fov > 50.0f)
            fov = SaveService.Get(Meta, "born_max_fov_wide", 40.0).AsSingle();
        float length = SaveService.Get(Meta, "born_max_length", 8.0).AsSingle();
        var overrides = SaveService.Get(Meta, "born_length_override",
            new Godot.Collections.Dictionary()).AsGodotDictionary();
        string gk = CurGroup.ToString();
        if (overrides.ContainsKey(gk))
            length = (float)overrides[gk].AsDouble();
        var prm = m.GetBornParams(fov, length);
        m.Born(m.GetBornPosition(prm.X, prm.Y), 0, DifficultyWaittingTime());
        MonsterLeft -= 1; // 箱子占本波配额
        if (DebugAutoKill)
            GetTree().CreateTimer(0.15).Timeout += () =>
            {
                if (m != null && m.IsActiveState && !m.IsDead)
                    m.Hit(99999.0f, m.GlobalPosition, Game.HitType.Body, PlayerState.Side.Right);
            };
    }

    /// <summary>等待时长按难度:Easy rand(3,8) / Hard rand(0,2) / Hell 0</summary>
    protected static float DifficultyWaittingTime() =>
        Game.Instance.CurrentDifficulty switch
        {
            Game.Difficulty.Hard => (float)GD.RandRange(0.0, 2.0),
            Game.Difficulty.Hell => 0.0f,
            _ => (float)GD.RandRange(3.0, 8.0),
        };

    protected virtual void CheckProgress()
    {
        if (VictoryState != 0)
            return;
        // 胜利:Boss 死(有 boss 关)/ 清完最后一波(无 boss 关)
        if (SaveService.Get(Meta, "boss", "").AsString() != "")
        {
            if (Boss != null && !Boss.IsActiveState)
            {
                TriggerVictory();
                return;
            }
        }
        else if (CurGroup == Groups.Count - 1 && MonsterLeft == 0
            && PlayerState.CurAliveMonster == 0)
        {
            TriggerVictory();
            return;
        }
        // 波次推进:待刷=0 且场上存活=0 → 下一波
        if (CurGroup >= 0 && CurGroup < Groups.Count - 1
            && MonsterLeft == 0 && PlayerState.CurAliveMonster == 0)
            StartGroup(CurGroup + 1);
    }

    /// <summary>胜利延迟 2s,单次触发(修原作每帧重复注册 bug)</summary>
    protected async void TriggerVictory()
    {
        VictoryState = 1;
        SettleStars();
        GD.Print($"[{LevelKey}] level clear → victory in {VictoryDelay:0.0}s");
        await ToSignal(GetTree().CreateTimer(VictoryDelay), SceneTreeTimer.SignalName.Timeout);
        if (VictoryState != 1)
            return;
        VictoryState = 2;
        GD.Print($"[{LevelKey}] level_victory (stars={LastStars})");
        EmitSignal(SignalName.LevelVictory);
    }

    /// <summary>通关星级本地结算(替代原作服务器下发):通关保底 1 星;活跃玩家平均 HP≥50 +1;
    /// 用时 ≤par_time(默认 300s)+1。分数=星级×1000+平均 HP×10;取历史最高落盘。</summary>
    protected void SettleStars()
    {
        var order = SaveService.Get(SaveService.Instance.GetLevelMeta(), "level_order",
            new Godot.Collections.Array()).AsGodotArray();
        int idx = order.IndexOf(LevelKey);
        if (idx < 0)
            return;
        float hpSum = 0.0f;
        int n = 0;
        foreach (var side in new[] { PlayerState.Side.Left, PlayerState.Side.Right })
        {
            var p = PlayerState.Instance.GetPlayer(side);
            if (p.Active)
            {
                hpSum += p.Hp;
                n += 1;
            }
        }
        float avgHp = hpSum / Mathf.Max(n, 1);
        double elapsed = Time.GetTicksMsec() / 1000.0 - BattleStartTime;
        float par = SaveService.Get(Meta, "par_time", 300.0).AsSingle();
        LastStars = 1 + (avgHp >= 50.0f ? 1 : 0) + (elapsed <= par ? 1 : 0);
        int score = LastStars * 1000 + (int)avgHp * 10;
        SaveService.Instance.SetLevelResult(idx, LastStars, score, 0);
    }

    protected void OnPlayerDied(int side)
    {
        GD.Print($"[{LevelKey}] player_died side={side} → open_continue(false)");
        EmitSignal(SignalName.OpenContinue, false, side);
    }

    protected void PlayMusic(string path)
    {
        if (BgmPlayer == null || path.Length == 0)
            return;
        if (!ResourceLoader.Exists(path))
        {
            GD.PushWarning($"[{LevelKey}] music missing: {path}");
            return;
        }
        BgmPlayer.Stream = GD.Load<AudioStream>(path);
        BgmPlayer.Play();
    }
}
