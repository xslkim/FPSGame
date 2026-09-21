using Godot;

namespace FPSGame;

/// <summary>
/// Level4 战斗关(MEP 浮岛月夜,7.2 L4 七波,照 legacy level4.gd):
/// G0 空波即过 / G1 红龙×1(force_pool:只许 dragon_red,箱子 roll 中即放弃该 tick,
/// 照原作 groupId==1 非 DragonRed 返回 null)/ G2 三色龙×5 / G3 斧×2 / G4 骷髅×2 /
/// G5 龙+Magma×15 / G6 空池清场即胜(2s 延迟)。出生固定正前方 6m(meta born_max_fov=0 /
/// born_max_length=6;龙四点巡回、Magma 相机前 10m 四向,均忽略传入点自算)。无 boss。
/// 启动参数:
///   --level4-selftest            headless 加速全流程断言
///   --level4-shot:&lt;path&gt;[:delay] 延迟截图(默认 4s)
/// </summary>
public partial class Level4 : LevelBase
{
    public readonly System.Collections.Generic.List<string> StatG1Keys = new();
    public int StatG6LeftAtStart { get; private set; } = -1; // G6 开始时的 MonsterLeft(空池应为 0)
    public double StatG6StartTime { get; private set; } = -1.0; // G6 开始时刻(验证 2s 胜利延迟)

    private bool _testFailed;

    protected override void RegisterMonsterTypes()
    {
        base.RegisterMonsterTypes();
        Pool.RegisterType("dragon_red",
            GD.Load<PackedScene>("res://scenes/battle/monsters/dragon_red.tscn"), 4);
        Pool.RegisterType("dragon_green",
            GD.Load<PackedScene>("res://scenes/battle/monsters/dragon_green.tscn"), 4);
        Pool.RegisterType("dragon_blue",
            GD.Load<PackedScene>("res://scenes/battle/monsters/dragon_blue.tscn"), 4);
        Pool.RegisterType("magma_demon",
            GD.Load<PackedScene>("res://scenes/battle/monsters/magma_demon.tscn"), 5);
    }

    protected override void EnterLevel()
    {
        var args = OS.GetCmdlineUserArgs();
        if (System.Array.IndexOf(args, "--level4-selftest") >= 0)
        {
            SelfTest();
            return;
        }
        StartBattle();
        foreach (var a in args)
        {
            if (a.StartsWith("--level4-shot:"))
            {
                // 格式 --level4-shot:<path>[:delaySec](从右往左拆,兼容盘符)
                var rest = a["--level4-shot:".Length..];
                float delay = 4.0f;
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
        GD.Print($"[L4] shot saved: {path}");
        GetTree().Quit();
    }

    protected override void StartGroup(int i)
    {
        base.StartGroup(i);
        // 空池波(L4 G0/G6):不等刷怪 tick,直接视为刷完(清场即推进/胜利)
        var poolArr = SaveService.Get(Groups[i].AsGodotDictionary(), "pool",
            new Godot.Collections.Array()).AsGodotArray();
        if (poolArr.Count == 0)
            MonsterLeft = 0;
        if (i == 6)
        {
            StatG6LeftAtStart = MonsterLeft;
            StatG6StartTime = Time.GetTicksMsec() / 1000.0;
        }
    }

    /// <summary>force_pool 波(G1):跳过箱子 spawn,箱子 roll 中即放弃该 tick(配额不扣,下 tick 重试);
    /// 其余波走基类(meta born_max_fov=0 / born_max_length=6,基类出生点即正前 6m)</summary>
    protected override void SpawnTick()
    {
        var g = Groups[CurGroup].AsGodotDictionary();
        if (!SaveService.Get(g, "force_pool", false).AsBool())
        {
            base.SpawnTick();
            return;
        }
        if (MonsterLeft <= 0)
            return;
        if (SaveService.Get(Cur, "max_alive", 0).AsInt32() <= 0)
            return;
        if (PlayerState.CurAliveMonster >= SaveService.Get(Cur, "max_alive", 0).AsInt32())
            return;
        var poolArr = SaveService.Get(Cur, "pool", new Godot.Collections.Array()).AsGodotArray();
        if (poolArr.Count == 0)
        {
            MonsterLeft = 0;
            return;
        }
        StatBoxRolls += 1;
        if (Pool.HasInactive("box") && ((float)GD.Randf() < SaveService.Get(Cur, "bullet_box_rate", 0.05).AsSingle()
                || (float)GD.Randf() < SaveService.Get(Meta, "gun_rate", 0.02).AsSingle()))
            return; // 抽到补给箱(非本波 pool 怪)→ 放弃该 tick,照原作
        string key = poolArr[PoolIdx % poolArr.Count].AsString();
        PoolIdx += 1;
        var m = Pool.GetMonster(key);
        if (m == null)
        {
            PoolIdx -= 1; // 池满,下 tick 重试同一只
            return;
        }
        BornAhead(m);
        MonsterLeft -= 1;
        if (CurGroup == 1)
            StatG1Keys.Add(key);
        if (DebugAutoKill)
            GetTree().CreateTimer(0.15).Timeout += () =>
            {
                if (m != null && m.IsActiveState && !m.IsDead)
                    m.Hit(99999.0f, m.GlobalPosition, Game.HitType.Body, PlayerState.Side.Right);
            };
    }

    /// <summary>出生:meta 固定 fov 0 / 正前方 6m(与基类 SpawnTick 同规则,含 born_length_override)</summary>
    private void BornAhead(Monster m)
    {
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
    }

    // ------------------------------------------------ 自检

    private void Check(bool cond, string label)
    {
        GD.Print((cond ? "[L4-TEST] PASS: " : "[L4-TEST] FAIL: ") + label);
        if (!cond)
            _testFailed = true;
    }

    /// <summary>难度数量断言(纯计算):Easy 0/1/5/2/2/15/15,Hard 0/1/7/3/3/22/22,Hell 0/2/10/4/4/30/30</summary>
    private void CheckDifficultyCounts()
    {
        var saved = Game.Instance.CurrentDifficulty;
        var cases = new System.Collections.Generic.Dictionary<Game.Difficulty, int[]>
        {
            [Game.Difficulty.Easy] = new[] { 0, 1, 5, 2, 2, 15, 15 },
            [Game.Difficulty.Hard] = new[] { 0, 1, 7, 3, 3, 22, 22 },
            [Game.Difficulty.Hell] = new[] { 0, 2, 10, 4, 4, 30, 30 },
        };
        foreach (var kv in cases)
        {
            Game.Instance.CurrentDifficulty = kv.Key;
            LoadMeta();
            var nums = new int[Groups.Count];
            for (int i = 0; i < Groups.Count; i++)
                nums[i] = SaveService.Get(CalcGroupParams(i), "num", 0).AsInt32();
            Check(System.Linq.Enumerable.SequenceEqual(nums, kv.Value),
                $"difficulty {kv.Key} group nums = {string.Join(",", nums)}");
        }
        Game.Instance.CurrentDifficulty = saved;
        LoadMeta();
    }

    /// <summary>headless 自检:godot --headless --path game scenes/levels/level4.tscn -- --level4-selftest</summary>
    private async void SelfTest()
    {
        TimeScaleTest = 0.04f;
        DebugAutoKill = true;
        CheckDifficultyCounts();
        int victoryCount = 0;
        double victoryTime = 0.0;
        LevelVictory += () =>
        {
            victoryCount += 1;
            victoryTime = Time.GetTicksMsec() / 1000.0;
        };
        // 防测试期玩家被打死弹出续币面板暂停流程
        foreach (var side in new[] { PlayerState.Side.Left, PlayerState.Side.Right })
        {
            var p = PlayerState.Instance.GetPlayer(side);
            if (p.Active)
                p.Hp = 1.0e6f;
        }
        OpenContinue += (_, _) =>
        {
            var panel = GetNodeOrNull("Camera3D/InGamePanel");
            if (panel != null && panel.HasMethod("CloseAll"))
                panel.Call("CloseAll");
        };
        var battleT0 = Time.GetTicksMsec() / 1000.0;
        StartBattle();
        // G0 空波:应不经刷怪等待立即推进到 G1
        while (CurGroup < 1 && Time.GetTicksMsec() / 1000.0 - battleT0 < 5.0)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var g1Delay = Time.GetTicksMsec() / 1000.0 - battleT0;
        Check(CurGroup == 1 && g1Delay < 1.0, $"G0 empty wave skipped immediately (G1 at {g1Delay:0.00}s)");
        // G1 force_pool:只出红龙
        double t1 = Time.GetTicksMsec() / 1000.0;
        while (CurGroup < 2 && Time.GetTicksMsec() / 1000.0 - t1 < 90.0)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(StatG1Keys.Count > 0 && StatG1Keys.TrueForAll(k => k == "dragon_red"),
            $"G1 force_pool spawned only dragon_red: {string.Join(",", StatG1Keys)}");
        // 等胜利信号(G6 空池清场即胜)
        double t0 = Time.GetTicksMsec() / 1000.0;
        while (victoryCount == 0 && Time.GetTicksMsec() / 1000.0 - t0 < 120.0)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(victoryCount == 1, "level_victory emitted exactly once");
        Check(StatG6LeftAtStart == 0,
            $"G6 empty pool: monster_left=0 at group start (got {StatG6LeftAtStart})");
        Check(StatG6StartTime > 0.0 && victoryTime - StatG6StartTime >= 1.8,
            $"G6 clear → ~2s → victory (delay={victoryTime - StatG6StartTime:0.00}s)");
        Check(StatGroupsStarted.Count == 7 && string.Join("", StatGroupsStarted) == "0123456",
            $"7 groups in order: {string.Join(",", StatGroupsStarted)}");
        Check(StatCamSwitches == 7, $"camera switched 7 times (G6 reuses cam_pos_5, got {StatCamSwitches})");
        Check(StatFreezes == 7 && SpawnFreezeTime == 2.0f, "spawn freeze 2s × 7 groups");
        Check(StatBoxRolls > 0, $"box probability rolls invoked ({StatBoxRolls} ticks)");
        Check(Boss == null && !StatBossBgm, "no boss / no boss_bgm");
        GD.Print($"[L4-TEST] done, failed={_testFailed}");
        if (BgmPlayer != null)
        {
            BgmPlayer.Stop();
            BgmPlayer.Stream = null;
        }
        GetTree().Quit(_testFailed ? 1 : 0);
    }
}
