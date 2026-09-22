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

    public const float CamBlendTime = 1.0f;    // 机位切换 Tween(原作自定义 Blend 资产 Level1.asset:1s)
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
    protected readonly System.Collections.Generic.List<(Monster Inst, int Level)> _slots = new(); // 本波槽位
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
        // 波次槽位:每槽预绑定一只池实例+等级("type@lv";原作槽位=场景预摆实例,含 null 空槽)
        _slots.Clear();
        foreach (var e in SaveService.Get(Cur, "pool", new Godot.Collections.Array()).AsGodotArray())
        {
            string entry = e.AsString();
            string type = entry;
            int level = 0;
            int at = entry.IndexOf('@');
            if (at > 0)
            {
                type = entry[..at];
                level = int.Parse(entry[(at + 1)..]);
            }
            var inst = Pool.GetMonster(type);
            if (inst != null)
                _slots.Add((inst, level));
        }
        if (i == SaveService.Get(Meta, "boss_group", -1).AsInt32()
            && SaveService.Get(Meta, "boss", "").AsString() != "")
        {
            SpawnBoss();
            PlayMusic(SaveService.Get(Meta, "boss_bgm", "").AsString());
            StatBossBgm = true;
        }
        GD.Print($"[{LevelKey}] group {i} start: num={MonsterLeft} " +
            $"interval={SaveService.Get(Cur, "interval", 3.0).AsDouble():0.00} " +
            $"max_alive={SaveService.Get(Cur, "max_alive", 0).AsInt32()} slots={_slots.Count}");
    }

    protected void SwitchCamera(int i)
    {
        if (Camera == null || i >= CamPositions.Length || CamPositions[i] == null)
            return;
        StatCamSwitches += 1;
        var tw = CreateTween();
        tw.TweenProperty(Camera, "global_transform", CamPositions[i].GlobalTransform,
            CamBlendTime * Mathf.Max(TimeScaleTest, 0.05f))
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut); // 原作 EaseInOut
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
        // 原作 InitBoss:Baotou.born() + SetActive——Boss 用场景预摆固定点(z=66),不走随机出生
        var bornPos = SaveService.Get(Meta, "boss_pos",
            new Godot.Collections.Dictionary { ["x"] = 0.0, ["y"] = 0.885, ["z"] = 66.0 }).AsGodotDictionary();
        var pos = new Vector3((float)bornPos["x"].AsDouble(), (float)bornPos["y"].AsDouble(),
            (float)bornPos["z"].AsDouble());
        Boss.Born(pos, 0, 0.0f);
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

    /// <summary>刷怪 tick:先判子弹箱 → gun_rate 判枪箱(AK/M4 各半)→ 随机槽位扫第一个未激活怪
    /// (原作 FindReadyMonster:Random.Range 起点 + 线性扫描;宝箱/枪箱占配额)</summary>
    protected virtual void SpawnTick()
    {
        if (MonsterLeft <= 0)
            return;
        if (SaveService.Get(Cur, "max_alive", 0).AsInt32() <= 0)
            return;
        if (PlayerState.CurAliveMonster >= SaveService.Get(Cur, "max_alive", 0).AsInt32())
            return;
        Monster m = null;
        int boxKind = -1;
        StatBoxRolls += 1;
        if (Pool.HasInactive("box"))
        {
            if (GD.Randf() < SaveService.Get(Cur, "bullet_box_rate", 0.05).AsSingle())
            {
                m = Pool.GetMonster("box");
                boxKind = (int)BoxMonster.BoxKind.Bullet;
            }
            else if (GD.Randf() < SaveService.Get(Meta, "gun_rate", 0.02).AsSingle())
            {
                m = Pool.GetMonster("box");
                boxKind = GD.Randf() < 0.5f
                    ? (int)BoxMonster.BoxKind.GunAK
                    : (int)BoxMonster.BoxKind.GunM4;
            }
        }
        int level = 0;
        if (m == null)
        {
            if (_slots.Count == 0)
            {
                MonsterLeft = 0; // 空波(L4 G0/G6 语义)
                return;
            }
            // 随机起点 + 前向扫描第一个未激活槽位(原作 FindReadyMonster)
            int idx = GD.RandRange(0, _slots.Count - 1);
            for (int k = 0; k < _slots.Count; k++)
            {
                var s = _slots[(idx + k) % _slots.Count];
                if (!s.Inst.IsActiveState)
                {
                    m = s.Inst;
                    level = s.Level;
                    break;
                }
            }
            if (m == null)
                return; // 槽位全忙,下 tick 重试
        }
        if (m is BoxMonster bm)
            bm.Kind = (BoxMonster.BoxKind)boxKind;
        // 出生点:±33°(FOV>50 → ±40°)/ 8m,born_fov_override 按波覆盖(原作 GroupMaxBornFov)
        float fov = SaveService.Get(Meta, "born_max_fov", 33.0).AsSingle();
        if (Camera != null && Camera.Fov > 50.0f)
            fov = SaveService.Get(Meta, "born_max_fov_wide", 40.0).AsSingle();
        float length = SaveService.Get(Meta, "born_max_length", 8.0).AsSingle();
        var fovOverrides = SaveService.Get(Meta, "born_fov_override",
            new Godot.Collections.Dictionary()).AsGodotDictionary();
        string gk = CurGroup.ToString();
        if (fovOverrides.ContainsKey(gk))
            fov = (float)fovOverrides[gk].AsDouble();
        var lenOverrides = SaveService.Get(Meta, "born_length_override",
            new Godot.Collections.Dictionary()).AsGodotDictionary();
        if (lenOverrides.ContainsKey(gk))
            length = (float)lenOverrides[gk].AsDouble();
        var prm = m.GetBornParams(fov, length);
        m.Born(m.GetBornPosition(prm.X, prm.Y), level, 0.0f); // WaittingTime 由关卡钩子按类型设置
        OnMonsterBorn(m);
        MonsterLeft -= 1; // 箱子占本波配额
        if (DebugAutoKill)
            GetTree().CreateTimer(0.15).Timeout += () =>
            {
                if (m != null && m.IsActiveState && !m.IsDead)
                    m.Hit(99999.0f, m.GlobalPosition, Game.HitType.Body, PlayerState.Side.Right);
            };
    }

    /// <summary>出生后关卡钩子(原作各级 LevelUpdate:全体面向相机;Level1 另有位置/等待修正)</summary>
    protected virtual void OnMonsterBorn(Monster m) => m.FaceCamera();

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

    /// <summary>胜利延迟 2s,单次触发(修原作每帧重复注册 bug);
    /// 原作 Victory 只弹面板回菜单、无任何星级/分数结算(全工程无星级写入点),不写档</summary>
    protected async void TriggerVictory()
    {
        VictoryState = 1;
        GD.Print($"[{LevelKey}] level clear → victory in {VictoryDelay:0.0}s");
        await ToSignal(GetTree().CreateTimer(VictoryDelay), SceneTreeTimer.SignalName.Timeout);
        if (VictoryState != 1)
            return;
        VictoryState = 2;
        GD.Print($"[{LevelKey}] level_victory");
        EmitSignal(SignalName.LevelVictory);
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
