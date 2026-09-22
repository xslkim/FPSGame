using Godot;

namespace FPSGame;

/// <summary>
/// Level1 战斗关(school_day 1:1):19 秒开场(BadGroup 三人负重行进 + Tension +
/// vcam1 注视女生 Neck)+ 五波怪(10/10/15/15/20)+ 第 5 波 Baotou Boss(BossMusic)。
/// 原作 Level1.cs:_Direct.time>19 → 停 Timeline、开 FireSystem、清场、StartMonsterGroup(0)。
/// 启动参数:--level1-selftest(headless 加速全流程断言)
/// </summary>
public partial class Level1 : LevelBase
{
    [Export] public NodePath BadGroupPath = "BadGroup";
    [Export] public NodePath StartMusicPlayerPath = "StartMusic";
    [Export] public NodePath EnvPath = "Environment";

    private Node3D _badGroup = null!;
    private AudioStreamPlayer _startMusic = null!;
    private Node3D _env = null!;
    private Tween? _introTween;
    private bool _introPlaying;
    private bool _testFailed;

    protected override void EnterLevel()
    {
        var args = OS.GetCmdlineUserArgs();
        if (System.Array.IndexOf(args, "--level1-selftest") >= 0)
        {
            SelfTest();
            return;
        }
        bool wantIntro = System.Array.IndexOf(args, "--level1-intro") >= 0
            || System.Array.FindIndex(args, a => a.StartsWith("--level1-shot:")) >= 0;
        if (Game.Instance.IsDebug && !wantIntro)
        {
            GD.Print("[L1] debug: 跳过 19s 开场直接开战");
            // 开场对象清理(debug 直开)
            _badGroup = GetNodeOrNull<Node3D>(BadGroupPath)!;
            _env = GetNodeOrNull<Node3D>(EnvPath)!;
            if (_badGroup != null)
                _badGroup.Visible = false;
            if (Camera != null)
                Camera.Fov = 45.0f; // 战斗机位 FOV45(原作 vcam;debug 无开场混合直接设)
            StartBattle();
            foreach (var a in args)
            {
                if (a.StartsWith("--level1-shot-boss:"))
                {
                    // 直跳 G4 Boss 波截图(验证爱心弱点/瞬移)
                    CallDeferred(nameof(DebugJumpBoss), a["--level1-shot-boss:".Length..]);
                }
                else if (a.StartsWith("--level1-shot-battle:"))
                {
                    // 格式 --level1-shot-battle:<path>[:delaySec](从右往左拆,兼容盘符)
                    var rest = a["--level1-shot-battle:".Length..];
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
            return;
        }
        PlayIntro();
        foreach (var a in args)
        {
            if (a.StartsWith("--level1-shot:"))
                TakeShotDelayed(a["--level1-shot:".Length..], 6.0f); // 开场行进中
        }
    }

    /// <summary>延迟截图(真值对比用):窗口模式 --shot-res 设定分辨率</summary>
    private async void TakeShotDelayed(string path, float delay)
    {
        // 激光验证:瞄准可视区中心(光束从枪口到命中点,红点贴面);等一帧确保视口尺寸就绪
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        InputRouter.Instance.MouseGun.SimulateMove(GetViewport().GetVisibleRect().Size / 2.0f);
        await ToSignal(GetTree().CreateTimer(delay, true, true), SceneTreeTimer.SignalName.Timeout);
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(path.Replace('/', '\\'));
        GD.Print($"[L1] shot saved: {path}");
        GetTree().Quit();
    }

    public override void _Ready()
    {
        _badGroup = GetNodeOrNull<Node3D>(BadGroupPath)!;
        _startMusic = GetNodeOrNull<AudioStreamPlayer>(StartMusicPlayerPath)!;
        _env = GetNodeOrNull<Node3D>(EnvPath)!;
        base._Ready(); // _Ready 里 EnterLevel()
    }

    /// <summary>19 秒开场(1:1 SchoolNightTimeline):禁射击 → Tension → BadGroup 演出(根运动+
    /// 尖叫/求救/相机注视)→ 3s 开环境 → 19s 收队 → blend 到 Battle0 开战</summary>
    private void PlayIntro()
    {
        _introPlaying = true;
        InputRouter.Instance.FireEnabled = false;
        if (FireSys != null)
        {
            FireSys.SetProcess(false);
            FireSys.Visible = false;
        }
        if (_badGroup is IntroBadGroup bg)
            bg.Play(Camera);
        PlayMusic(SaveService.Get(Meta, "start_music", "").AsString(),
            _startMusic); // Tension.mp3
        float dur = SaveService.Get(Meta, "intro_duration", 19.0).AsSingle();
        if (Camera != null)
            Camera.Fov = 30.0f;
        GetTree().CreateTimer(3.0).Timeout += () =>
        {
            if (_env != null)
                _env.Visible = true;
            GD.Print("[L1] env active (第3秒开环境)");
        };
        // 演出期间环境隐藏(原作 _Env 隐藏,3s 后 ActiveEnv)
        if (_env != null)
            _env.Visible = false;
        GD.Print($"[L1] intro start: {dur:0}s (Tension)");
        GetTree().CreateTimer(dur).Timeout += () =>
        {
            _introPlaying = false;
            GD.Print("[L1] intro done → battle (Level1Ex)");
            if (_badGroup is IntroBadGroup bg2)
                bg2.Stop();
            // Cinemachine blend → Battle0(FOV 45)
            if (Camera != null && CamPositions.Length > 0 && CamPositions[0] != null)
            {
                var tw = CreateTween();
                tw.TweenProperty(Camera, "global_transform", CamPositions[0].GlobalTransform,
                    CamBlendTime).SetTrans(Tween.TransitionType.Sine);
                tw.Parallel().TweenProperty(Camera, "fov", 45.0f, CamBlendTime);
            }
            StartBattle();
        };
    }

    /// <summary>出生后修正(原作 Level1.LevelUpdate):全体面向相机;
    /// 牛魔王/斧头/小骷髅(原作 _Name 序列化同为 0,同一分支)y+0.2、x<0 时 x+0.3,
    /// 等待时长按难度 Easy rand(3,8)/Hard rand(0,2)/Hell 0;飞斧/宝箱不等待(默认值 0)</summary>
    protected override void OnMonsterBorn(Monster m)
    {
        m.FaceCamera();
        if (m.MetaKey is "bull" or "axe_zombie" or "skeleton")
        {
            var p = m.GlobalPosition;
            p.Y += 0.2f;
            if (p.X < 0.0f)
                p.X += 0.3f;
            m.GlobalPosition = p;
            m.WaittingTime = DifficultyWaittingTime();
        }
    }

    /// <summary>debug:跳 G4 Boss 波并截图(爱心弱点验证)</summary>
    private async void DebugJumpBoss(string path)
    {
        // 等一帧确保视口尺寸就绪,瞄准 Boss 方位(屏幕中心偏左,z=66 方向)
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        InputRouter.Instance.MouseGun.SimulateMove(GetViewport().GetVisibleRect().Size / 2.0f);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        // 直接清场跳到 G4(清掉 G0 已刷的怪,避免堆叠)
        foreach (var m in GetTree().GetNodesInGroup("monster"))
            if (m is Monster mm && mm.IsActiveState)
                mm.Hit(99999.0f, mm.GlobalPosition, Game.HitType.Body, PlayerState.Side.Right);
        StartGroup(4);
        await ToSignal(GetTree().CreateTimer(3.5f, true, true), SceneTreeTimer.SignalName.Timeout);
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(path.Replace('/', '\\'));
        GD.Print($"[L1] boss shot saved: {path}");
        GetTree().Quit();
    }

    private static void PlayMusic(string path, AudioStreamPlayer player)
    {
        if (player == null || path.Length == 0 || !ResourceLoader.Exists(path))
            return;
        player.Stream = GD.Load<AudioStream>(path);
        player.Play();
    }

    // ------------------------------------------------ 自检

    private void Check(bool cond, string label)
    {
        GD.Print((cond ? "[L1-TEST] PASS: " : "[L1-TEST] FAIL: ") + label);
        if (!cond)
            _testFailed = true;
    }

    /// <summary>难度数量断言:Easy 10/10/15/15/20,Hard 18/18/27/27/36,Hell 26/26/39/39/52</summary>
    private void CheckDifficultyCounts()
    {
        var saved = Game.Instance.CurrentDifficulty;
        var cases = new System.Collections.Generic.Dictionary<Game.Difficulty, int[]>
        {
            [Game.Difficulty.Easy] = new[] { 10, 10, 15, 15, 20 },
            [Game.Difficulty.Hard] = new[] { 18, 18, 27, 27, 36 },
            [Game.Difficulty.Hell] = new[] { 26, 26, 39, 39, 52 },
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

    /// <summary>headless 自检:godot --headless --path game scenes/levels/level1_battle.tscn -- --level1-selftest</summary>
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
        var continueArgs = new System.Collections.Generic.List<(bool, int)>();
        OpenContinue += (isOpen, side) => continueArgs.Add((isOpen, side));
        StartBattle();
        // 玩家死亡 → open_continue(false, side)
        while (CurGroup < 1)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        PlayerState.Instance.HitPlayer(999.0f, Game.AttackType.Phy, PlayerState.Side.Right);
        await ToSignal(GetTree().CreateTimer(0.7, true, false, true), SceneTreeTimer.SignalName.Timeout);
        Check(continueArgs.Exists(c => !c.Item1 && c.Item2 == (int)PlayerState.Side.Right),
            "player_died → open_continue(false, side)");
        PlayerState.Instance.PlayerRight.Relife();
        var panel = GetNodeOrNull("Camera3D/InGamePanel");
        if (panel != null && panel.HasMethod("CloseAll"))
            panel.Call("CloseAll");
        PlayerState.Instance.PlayerRight.Hp = 1.0e6f;
        // 等 G4 boss born
        double tWait = Time.GetTicksMsec() / 1000.0;
        while (Boss == null && Time.GetTicksMsec() / 1000.0 - tWait < 90.0)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(Boss != null, "G4: boss (baotou) born");
        Check(StatBossBgm, "G4: boss_bgm switched");
        double bossDeadTime = -1.0;
        if (Boss != null)
            Boss.Died += _ => bossDeadTime = Time.GetTicksMsec() / 1000.0;
        // 等胜利信号
        double t0 = Time.GetTicksMsec() / 1000.0;
        while (victoryCount == 0 && Time.GetTicksMsec() / 1000.0 - t0 < 90.0)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(victoryCount == 1, "level_victory emitted exactly once");
        Check(bossDeadTime > 0.0 && victoryTime - bossDeadTime >= 1.8,
            $"boss dead → ~2s → victory (delay={victoryTime - bossDeadTime:0.00}s)");
        Check(StatGroupsStarted.Count == 5 && string.Join("", StatGroupsStarted) == "01234",
            $"5 groups in order: {string.Join(",", StatGroupsStarted)}");
        Check(StatCamSwitches == 5, $"camera switched 5 times (got {StatCamSwitches})");
        Check(StatFreezes == 5 && SpawnFreezeTime == 2.0f, "spawn freeze 2s × 5 groups");
        Check(StatBoxRolls > 0, $"box probability rolls invoked ({StatBoxRolls} ticks)");
        // HUD 存在性
        Check(PlayerState.Instance.CoinObj != null, "HUD coin exists");
        GD.Print($"[L1-TEST] done, failed={_testFailed}");
        if (BgmPlayer != null)
        {
            BgmPlayer.Stop();
            BgmPlayer.Stream = null;
        }
        GetTree().Quit(_testFailed ? 1 : 0);
    }
}
