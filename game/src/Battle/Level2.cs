using Godot;

namespace FPSGame;

/// <summary>
/// Level2 木镇(7.2 L2 三波 1:1):FireWindow 窗口刷怪 + 怪物等级体系 + G2 开始 15s 后 Boss 出场。
/// 原作 Level2.cs:UpdateMonsterGroup 走 GetFireWindow(空窗随机)+CreateMonster(SrcPosition 出生,
/// 等级=maxLv-ceil(maxLv*left/num)+base 封顶 6);G2 StateStartTime2 起 15s 后 Boss born
/// (boss_level 按难度/SlowAnimSpeed×难度倍率=动画倍率 easy1/hard2.25/hell4)+ 切 BossMusic;
/// boss 死亡不回收,关卡补 CurAliveMonster-1 并 2s 后胜利清理。
/// L2 无开场演出,Start 直接开战。启动参数:
///   (无)                直接开战
///   --level2-selftest   headless 加速全流程断言
///   --level2-shot:<path>[:delay]  延迟截图(真值对比用)
/// </summary>
public partial class Level2 : LevelBase
{
    public const float ShakeStrength = 0.25f; // 出场震屏 h/v_offset 幅度(随时间衰减)

    [Export] public NodePath[] WindowGroupPaths = System.Array.Empty<NodePath>();

    private System.Collections.Generic.List<FireWindow>[] _windowGroups =
        System.Array.Empty<System.Collections.Generic.List<FireWindow>>();
    private readonly System.Collections.Generic.List<FireWindow> _activeWindows = new();
    private int _bossGroup = -1;
    private double _bossDelayLeft = -1.0;
    private double _bossGroupStartTime = -1.0;
    private double _bossSpawnTime = -1.0;
    private float _shakeLeft;
    private float _shakeDuration = 0.8f;
    private bool _bossCleaned;
    private bool _testFailed;

    // 自检统计
    public int StatWindowBlocked;
    public readonly System.Collections.Generic.List<int> StatWindowsUsed = new();
    public bool StatShakeSeen;
    public int StatStaticBoxes;
    public double StatBossAnimRate;
    public int StatBossLevel = -1;
    public double StatBossDeadTime = -1.0;

    // ------------------------------------------------ 进场/元数据

    /// <summary>meta 复制后把 boss_group 置 -1:Boss 不走基类"波开始即出场",本类按 boss_delay 延迟处理</summary>
    private void PatchMeta()
    {
        Meta = (Godot.Collections.Dictionary)Meta.Duplicate();
        _bossGroup = SaveService.Get(Meta, "boss_group", -1).AsInt32();
        Meta["boss_group"] = -1;
    }

    protected override void EnterLevel()
    {
        PatchMeta();
        CollectWindows();
        var args = OS.GetCmdlineUserArgs();
        if (System.Array.IndexOf(args, "--level2-selftest") >= 0)
        {
            SelfTest();
            return;
        }
        StartBattle();
        foreach (var a in args)
        {
            if (a.StartsWith("--level2-shot:"))
            {
                // 格式 --level2-shot:<path>[:delaySec](从右往左拆,兼容盘符)
                var rest = a["--level2-shot:".Length..];
                float delay = 8.0f;
                string path = rest;
                int ci = rest.LastIndexOf(':');
                if (ci > 1 && float.TryParse(rest[(ci + 1)..], out var d))
                {
                    delay = d;
                    path = rest[..ci];
                }
                TakeShotDelayed(path, delay);
            }
        }
    }

    /// <summary>延迟截图(真值对比用):窗口模式 --shot-res 设定分辨率</summary>
    private async void TakeShotDelayed(string path, float delay)
    {
        await ToSignal(GetTree().CreateTimer(delay, true, true), SceneTreeTimer.SignalName.Timeout);
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(path.Replace('/', '\\'));
        GD.Print($"[L2] shot saved: {path}");
        GetTree().Quit();
    }

    private void CollectWindows()
    {
        var groups = new System.Collections.Generic.List<System.Collections.Generic.List<FireWindow>>();
        foreach (var p in WindowGroupPaths)
        {
            var arr = new System.Collections.Generic.List<FireWindow>();
            var n = GetNodeOrNull(p);
            if (n != null)
            {
                foreach (var c in n.GetChildren())
                {
                    if (c is FireWindow w)
                        arr.Add(w);
                }
            }
            groups.Add(arr);
        }
        _windowGroups = groups.ToArray();
    }

    // ------------------------------------------------ 怪型注册 / 波参数

    protected override void RegisterMonsterTypes()
    {
        Pool.RegisterType("toon_shoot",
            GD.Load<PackedScene>("res://scenes/battle/monsters/toon.tscn"), 10);
        Pool.RegisterType("toon_shoot_alien",
            GD.Load<PackedScene>("res://scenes/battle/monsters/toon_alien.tscn"), 6);
        Pool.RegisterType("level2_boss",
            GD.Load<PackedScene>("res://scenes/battle/monsters/level2_boss.tscn"), 1);
        Pool.RegisterType("box",
            GD.Load<PackedScene>("res://scenes/battle/monsters/box_monster.tscn"), 2);
    }

    private int _curBaseLevel;
    private int _curGroupNum = 1; // 本波总数(等级公式分母,基类 CalcGroupParams 非 virtual,自取)

    protected override void StartGroup(int i)
    {
        base.StartGroup(i);
        var g = Groups[i].AsGodotDictionary();
        _curBaseLevel = SaveService.Get(g, "base_level", 0).AsInt32();
        _curGroupNum = Mathf.Max((int)(SaveService.Get(g, "monster_num", 0).AsDouble() * DiffRate), 1);
        _activeWindows.Clear();
        if (i < _windowGroups.Length)
            _activeWindows.AddRange(_windowGroups[i]);
        StatWindowsUsed.Add(_activeWindows.Count);
        if (i == _bossGroup && _bossDelayLeft < 0.0 && Boss == null)
        {
            _bossGroupStartTime = Time.GetTicksMsec() / 1000.0;
            _bossDelayLeft = SaveService.Get(Meta, "boss_delay", 15.0).AsDouble();
            GD.Print($"[{LevelKey}] group {i}: boss in {_bossDelayLeft:0}s");
        }
    }

    public override void _Process(double delta)
    {
        if (BattleActive && !Game.Instance.IsGamePause)
        {
            // G2 开始 15s(真实时间)后出场
            if (_bossDelayLeft > 0.0)
            {
                _bossDelayLeft -= delta;
                if (_bossDelayLeft <= 0.0)
                    SpawnBossDelayed();
            }
            // 出场震屏:h/v_offset 随机偏移,随剩余时间衰减
            if (_shakeLeft > 0.0f)
            {
                _shakeLeft -= (float)delta;
                if (Camera != null)
                {
                    if (_shakeLeft > 0.0f)
                    {
                        float a = ShakeStrength * (_shakeLeft / Mathf.Max(_shakeDuration, 0.01f));
                        Camera.HOffset = (float)GD.RandRange(-a, a);
                        Camera.VOffset = (float)GD.RandRange(-a, a);
                    }
                    else
                    {
                        Camera.HOffset = 0.0f;
                        Camera.VOffset = 0.0f;
                    }
                }
            }
        }
        base._Process(delta);
    }

    // ------------------------------------------------ 刷怪

    /// <summary>刷怪 tick:箱子判定同基类;Toon 怪走窗口分配(无空窗本 tick 不刷,下 tick 重试同只)</summary>
    protected override void SpawnTick()
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
            MonsterLeft = 0; // 空波语义
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
            else if (GD.Randf() < SaveService.Get(Meta, "gun_rate", 0.05).AsSingle())
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
        // Toon 怪先占窗(7.2):无空窗本 tick 不刷,下 tick 重试同一只
        FireWindow? w = null;
        if (key != "box")
        {
            w = FireWindow.PickFree(_activeWindows);
            if (w == null)
            {
                StatWindowBlocked += 1;
                if (boxKind < 0)
                    PoolIdx -= 1;
                return;
            }
        }
        var m = Pool.GetMonster(key);
        if (m == null)
        {
            if (boxKind < 0)
                PoolIdx -= 1; // 池满,下 tick 重试
            return;
        }
        if (m is BoxMonster bm)
        {
            bm.Kind = (BoxMonster.BoxKind)boxKind;
            BornBox(m);
        }
        else if (m is ToonMonster tm)
        {
            int lv = CalcMonsterLevel();
            w!.FireMonster = tm;
            tm.FireWindow = w;
            tm.Born(w.GetSrcPosition(), lv, DifficultyWaittingTime()); // 从 SrcPosition 翻窗爬入
        }
        else
        {
            float fov = SaveService.Get(Meta, "born_max_fov", 33.0).AsSingle();
            float length = SaveService.Get(Meta, "born_max_length", 8.0).AsSingle();
            var prm = m.GetBornParams(fov, length);
            m.Born(m.GetBornPosition(prm.X, prm.Y), 0, DifficultyWaittingTime());
        }
        MonsterLeft -= 1; // 箱子占本波配额
        if (key == "box")
            GD.Print($"[{LevelKey}] box spawned kind={boxKind} (left={MonsterLeft})");
        if (DebugAutoKill)
            GetTree().CreateTimer(0.15).Timeout += () =>
            {
                if (m != null && m.IsActiveState && !m.IsDead)
                    m.Hit(99999.0f, m.GlobalPosition, Game.HitType.Body, PlayerState.Side.Right);
            };
    }

    /// <summary>补给箱出生(box_born 参数,不占窗口);G0 静态(原作 StaticBox:born 后移速清零,G1 起恢复)</summary>
    private void BornBox(Monster m)
    {
        var bb = SaveService.Get(Meta, "box_born", new Godot.Collections.Dictionary()).AsGodotDictionary();
        float fov = (float)SaveService.Get(bb, "max_fov", 33.0).AsDouble();
        float len = (float)SaveService.Get(bb, "max_length", 12.0).AsDouble();
        var prm = m.GetBornParams(fov, len);
        m.Born(m.GetBornPosition(prm.X, prm.Y), 0, DifficultyWaittingTime());
        if (CurGroup == 0)
        {
            m.Info.MoveSpeed = 0.0f; // 静态箱(下一波 born 时恢复 3)
            StatStaticBoxes += 1;
        }
        else
        {
            m.Info.MoveSpeed = 3.0f; // 恢复默认逃跑跳(meta box move_speed)
        }
    }

    // ------------------------------------------------ 等级公式(7.2)

    /// <summary>level = maxLevel - ceil(maxLevel*monsterLeft/groupNum) + baseLevel,封顶 level_cap</summary>
    private int CalcMonsterLevel() =>
        LevelFormula(MonsterMaxLevel(), MonsterLeft, _curGroupNum, _curBaseLevel);

    private int LevelFormula(int maxLv, int left, int num, int baseLv)
    {
        int lv = maxLv - (int)Mathf.Ceil((float)maxLv * left / num) + baseLv;
        return Mathf.Min(lv, SaveService.Get(Meta, "level_cap", 6).AsInt32());
    }

    private int MonsterMaxLevel()
    {
        var d = SaveService.Get(Meta, "monster_max_level",
            new Godot.Collections.Dictionary { ["easy"] = 3, ["hard"] = 4, ["hell"] = 5 })
            .AsGodotDictionary();
        return Game.Instance.CurrentDifficulty switch
        {
            Game.Difficulty.Hard => SaveService.Get(d, "hard", 4).AsInt32(),
            Game.Difficulty.Hell => SaveService.Get(d, "hell", 5).AsInt32(),
            _ => SaveService.Get(d, "easy", 3).AsInt32(),
        };
    }

    // ------------------------------------------------ Boss(easy×1 / hard×2.25 / hell×4)

    /// <summary>Boss 难度动画倍率(6.3):easy 1 / hard 2.25 / hell 4</summary>
    private double BossAnimRate() => Game.Instance.CurrentDifficulty switch
    {
        Game.Difficulty.Hard => 2.25,
        Game.Difficulty.Hell => 4.0,
        _ => 1.0,
    };

    private int BossLevel()
    {
        var d = SaveService.Get(Meta, "boss_level",
            new Godot.Collections.Dictionary { ["easy"] = 0, ["hard"] = 3, ["hell"] = 6 })
            .AsGodotDictionary();
        return Game.Instance.CurrentDifficulty switch
        {
            Game.Difficulty.Hard => SaveService.Get(d, "hard", 3).AsInt32(),
            Game.Difficulty.Hell => SaveService.Get(d, "hell", 6).AsInt32(),
            _ => SaveService.Get(d, "easy", 0).AsInt32(),
        };
    }

    /// <summary>G2 开始 15s 后出场:难度动画倍率 + born(boss_level) + 切 boss_bgm + 震屏</summary>
    private void SpawnBossDelayed()
    {
        string key = SaveService.Get(Meta, "boss", "").AsString();
        Boss = Pool.GetMonster(key);
        if (Boss == null)
        {
            GD.PushError($"[{LevelKey}] boss pool empty: {key}");
            return;
        }
        double rate = BossAnimRate();
        StatBossAnimRate = rate;
        StatBossLevel = BossLevel();
        if (Boss is Level2Boss b)
        {
            b.SetDifficultyAnimRate(rate);
            b.EntranceShake += OnEntranceShake;
        }
        Boss.Died += OnBossDied;
        float fov = SaveService.Get(Meta, "born_max_fov", 33.0).AsSingle();
        float len = SaveService.Get(Meta, "born_max_length", 8.0).AsSingle();
        var prm = Boss.GetBornParams(fov, len);
        Boss.Born(Boss.GetBornPosition(prm.X, prm.Y), StatBossLevel, 0.0f);
        _bossSpawnTime = Time.GetTicksMsec() / 1000.0;
        PlayMusic(SaveService.Get(Meta, "boss_bgm", "").AsString());
        StatBossBgm = true;
        double delay = _bossGroupStartTime > 0.0 ? _bossSpawnTime - _bossGroupStartTime : -1.0;
        GD.Print($"[{LevelKey}] boss {key} born (delay={delay:0.0}s anim_rate={rate:0.00} level={StatBossLevel})");
        if (DebugAutoKill)
        {
            // 等 boss 至少放出一次技能(覆盖移动/转身/攻击事件/雷柱演出路径)再打头击杀
            var t0 = Time.GetTicksMsec() / 1000.0;
            async void KillBossWhenSkilled()
            {
                while (Boss != null && Boss is Level2Boss lb && lb.StatSkillCount == 0
                    && Time.GetTicksMsec() / 1000.0 - t0 < 30.0)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (Boss != null && Boss.IsActiveState && !Boss.IsDead)
                    // L2 Boss 部位判定:自动击杀必须打头(Armour 不掉血)
                    Boss.Hit(99999.0f, Boss.GlobalPosition, Game.HitType.Head, PlayerState.Side.Right);
            }
            KillBossWhenSkilled();
        }
    }

    private void OnEntranceShake(double duration)
    {
        _shakeDuration = (float)duration;
        _shakeLeft = (float)duration;
        StatShakeSeen = true;
    }

    /// <summary>Boss died → 2s 后 level_victory(基类 TriggerVictory 单次延迟);死亡演出播完由关卡清理</summary>
    private void OnBossDied(Monster m)
    {
        if (_bossCleaned)
            return;
        _bossCleaned = true;
        StatBossDeadTime = Time.GetTicksMsec() / 1000.0;
        PlayerState.CurAliveMonster -= 1; // born 时 +1,boss 不回收,关卡补上
        GD.Print($"[{LevelKey}] boss died → victory in {VictoryDelay:0.0}s");
        TriggerVictory();
        _ = CleanupBossAfterVictory();
    }

    private async System.Threading.Tasks.Task CleanupBossAfterVictory()
    {
        await ToSignal(this, SignalName.LevelVictory);
        if (Boss != null && Boss.IsActiveState && Boss is Level2Boss b)
        {
            b.CleanupByLevel();
            GD.Print($"[{LevelKey}] boss cleaned by level");
        }
    }

    // ------------------------------------------------ 自检(--level2-selftest)

    private void Check(bool cond, string label)
    {
        GD.Print((cond ? "[L2-TEST] PASS: " : "[L2-TEST] FAIL: ") + label);
        if (!cond)
            _testFailed = true;
    }

    /// <summary>本波数量(同基类公式:num=(int)(monster_num×DiffRate))</summary>
    private int GroupNum(int i)
    {
        var g = Groups[i].AsGodotDictionary();
        return (int)(SaveService.Get(g, "monster_num", 0).AsDouble() * DiffRate);
    }

    /// <summary>难度数量断言:Easy 30/30/40,Hard 45/45/60,Hell 60/60/80</summary>
    private void CheckDifficultyCounts()
    {
        var saved = Game.Instance.CurrentDifficulty;
        var cases = new System.Collections.Generic.Dictionary<Game.Difficulty, int[]>
        {
            [Game.Difficulty.Easy] = new[] { 30, 30, 40 },
            [Game.Difficulty.Hard] = new[] { 45, 45, 60 },
            [Game.Difficulty.Hell] = new[] { 60, 60, 80 },
        };
        foreach (var kv in cases)
        {
            Game.Instance.CurrentDifficulty = kv.Key;
            LoadMeta();
            PatchMeta();
            var nums = new int[Groups.Count];
            for (int i = 0; i < Groups.Count; i++)
                nums[i] = GroupNum(i);
            Check(System.Linq.Enumerable.SequenceEqual(nums, kv.Value),
                $"difficulty {kv.Key} group nums = {string.Join(",", nums)}");
        }
        Game.Instance.CurrentDifficulty = saved;
        LoadMeta();
        PatchMeta();
    }

    private void CheckLevelFormula()
    {
        Game.Instance.CurrentDifficulty = Game.Difficulty.Easy;
        LoadMeta();
        PatchMeta();
        Check(LevelFormula(3, 30, 30, 0) == 0, "G0 easy first spawn lv=0");
        Check(LevelFormula(3, 30, 30, 1) == 1, "G1 base+1: first spawn lv=1");
        Check(LevelFormula(3, 1, 30, 0) == 2, "G0 ramp last lv=2 (maxLv-1)");
        Check(LevelFormula(4, 45, 45, 0) == 0 && LevelFormula(4, 1, 45, 0) == 3, "hard ramp 0→3");
        Check(LevelFormula(5, 1, 10, 9) == 6 && LevelFormula(6, 1, 10, 6) == 6, "level cap 6");
        Game.Instance.CurrentDifficulty = Game.Difficulty.Hard;
        LoadMeta();
        PatchMeta();
        Check(MonsterMaxLevel() == 4, "hard monster_max_level=4");
        Game.Instance.CurrentDifficulty = Game.Difficulty.Hell;
        LoadMeta();
        PatchMeta();
        Check(MonsterMaxLevel() == 5, "hell monster_max_level=5");
        Game.Instance.CurrentDifficulty = Game.Difficulty.Easy;
        LoadMeta();
        PatchMeta();
    }

    private void CheckBossParams()
    {
        var saved = Game.Instance.CurrentDifficulty;
        Game.Instance.CurrentDifficulty = Game.Difficulty.Easy;
        Check(BossAnimRate() == 1.0 && BossLevel() == 0, "easy: anim_rate=1 boss_lv=0");
        Game.Instance.CurrentDifficulty = Game.Difficulty.Hard;
        Check(BossAnimRate() == 2.25 && BossLevel() == 3, "hard: anim_rate=2.25 boss_lv=3");
        Game.Instance.CurrentDifficulty = Game.Difficulty.Hell;
        Check(BossAnimRate() == 4.0 && BossLevel() == 6, "hell: anim_rate=4 boss_lv=6");
        Game.Instance.CurrentDifficulty = saved;
    }

    /// <summary>窗口分配与占用释放:占满 → pick_free null(不刷);杀光 → 全部释放</summary>
    private async System.Threading.Tasks.Task UnitTestWindows()
    {
        var ws = _windowGroups[0];
        Check(ws.Count == 7 && _windowGroups[1].Count == 5 && _windowGroups[2].Count == 3,
            "window groups 7/5/3");
        var toons = new System.Collections.Generic.List<Monster>();
        foreach (var w in ws)
        {
            var t = Pool.GetMonster("toon_shoot");
            if (t == null)
                break;
            if (t is ToonMonster tm)
                tm.FireWindow = w;
            w.FireMonster = t;
            t.Born(w.GetSrcPosition(), 0, 0.0f);
            toons.Add(t);
        }
        Check(toons.Count == 7, "7 toons born at windows");
        int occupied = 0;
        foreach (var w in ws)
            if (!w.IsFree())
                occupied += 1;
        Check(occupied == 7, "all 7 windows occupied");
        Check(FireWindow.PickFree(ws) == null, "no free window → pick_free null (无空窗不刷)");
        foreach (var t in toons)
            t.Hit(9999.0f, t.GlobalPosition, Game.HitType.Body, PlayerState.Side.Right);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        int freed = 0;
        foreach (var w in ws)
            if (w.IsFree())
                freed += 1;
        Check(freed == 7, $"windows released on death ({freed}/7)");
        Check(FireWindow.PickFree(ws) != null, "pick_free works again after release");
        await ToSignal(GetTree().CreateTimer(1.8), SceneTreeTimer.SignalName.Timeout); // 等 1.5s 回收回池
    }

    /// <summary>headless 自检:godot --headless --path game scenes/levels/level2.tscn -- --level2-selftest</summary>
    private async void SelfTest()
    {
        TimeScaleTest = 0.04f; // 加速刷怪间隔/冻结/相机 Tween;boss 15s 与胜利 2s 保持真实以验证
        DebugAutoKill = true;
        CheckDifficultyCounts();
        CheckLevelFormula();
        CheckBossParams();
        await UnitTestWindows();
        PlayerState.Instance.PlayerRight.Hp = 1.0e6f;
        PlayerState.Instance.PlayerLeft.Hp = 1.0e6f;
        int victoryCount = 0;
        double victoryTime = 0.0;
        LevelVictory += () =>
        {
            victoryCount += 1;
            victoryTime = Time.GetTicksMsec() / 1000.0;
        };
        StartBattle();
        // 等 G2 boss 出场(15s 真实延迟)
        double tWait = Time.GetTicksMsec() / 1000.0;
        while (Boss == null && Time.GetTicksMsec() / 1000.0 - tWait < 90.0)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(Boss != null, "G2: boss (level2_boss) born after 15s");
        double delay = _bossSpawnTime - _bossGroupStartTime;
        Check(Boss != null && delay >= 14.0 && delay <= 20.0,
            $"boss delay ~15s (measured {delay:0.00}s)");
        Check(StatBossBgm, "boss_bgm switched at boss entrance");
        Check(StatBossAnimRate == 1.0, "easy anim rate 1.0 applied to boss");
        Check(StatBossLevel == 0, "easy boss level 0 applied");
        Check(StatShakeSeen, "entrance_shake → camera shake");
        var bossRef = Boss as Level2Boss;
        // 等胜利信号(debug_auto_kill 打头击杀 boss)
        double t0 = Time.GetTicksMsec() / 1000.0;
        while (victoryCount == 0 && Time.GetTicksMsec() / 1000.0 - t0 < 90.0)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(victoryCount == 1, "level_victory emitted exactly once");
        Check(bossRef != null && bossRef.StatSkillCount > 0,
            $"boss skill fired in battle ({bossRef?.StatSkillCount} hits)");
        Check(StatBossDeadTime > 0.0 && victoryTime - StatBossDeadTime >= 1.8,
            $"boss died → ~2s → victory (delay={victoryTime - StatBossDeadTime:0.00}s)");
        Check(StatGroupsStarted.Count == 3 && string.Join("", StatGroupsStarted) == "012",
            $"3 groups in order: {string.Join(",", StatGroupsStarted)}");
        Check(StatCamSwitches == 3, $"camera switched 3 times (got {StatCamSwitches})");
        Check(StatFreezes == 3 && SpawnFreezeTime == 2.0f, "spawn freeze 2s × 3 groups");
        Check(StatWindowsUsed.Count == 3 && StatWindowsUsed[0] == 7
            && StatWindowsUsed[1] == 5 && StatWindowsUsed[2] == 3,
            $"windows per wave 7/5/3: {string.Join(",", StatWindowsUsed)}");
        Check(StatBoxRolls > 0, $"box probability rolls invoked ({StatBoxRolls} ticks)");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(Boss == null || !Boss.IsActiveState, "boss cleaned by level after victory");
        GD.Print($"[L2-TEST] done, failed={_testFailed} (static_boxes={StatStaticBoxes} window_blocked={StatWindowBlocked})");
        if (BgmPlayer != null)
        {
            BgmPlayer.Stop();
            BgmPlayer.Stream = null;
        }
        GetTree().Quit(_testFailed ? 1 : 0);
    }
}
