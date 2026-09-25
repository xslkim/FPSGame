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
        // 截图/跳波挂接显式走 debug 直开,与 IsDebug 开关解耦(正常游戏流程应播 19s 开场)
        bool wantDebugJump = System.Array.FindIndex(args, a => a.StartsWith("--level1-shot-battle:")
            || a.StartsWith("--level1-shot-boss:") || a.StartsWith("--level1-shot-group:")) >= 0;
        if ((Game.Instance.IsDebug || wantDebugJump) && !wantIntro)
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
                    var (path, delay) = SplitShotArg(a["--level1-shot-battle:".Length..], 4.0f);
                    TakeShotDelayed(path, delay);
                }
                else if (a.StartsWith("--level1-shot-group:"))
                {
                    // 直跳第 g 波截图(原作 Level1Shot L1_SHOT_GROUPS:强制 StartMonsterGroup(g) 2.5s 后截)
                    var (path, g) = SplitShotArg(a["--level1-shot-group:".Length..], 0);
                    CallDeferred(nameof(DebugJumpGroup), path, (int)g);
                }
            }
            return;
        }
        PlayIntro();
        foreach (var a in args)
        {
            if (a.StartsWith("--level1-shot:"))
            {
                var (path, delay) = SplitShotArg(a["--level1-shot:".Length..], 6.0f);
                TakeShotDelayed(path, delay); // 开场行进中(可指定秒,对位真值时间戳)
            }
        }
    }

    /// <summary>延迟截图(真值对比用):窗口模式 --shot-res 设定分辨率</summary>
    private async void TakeShotDelayed(string path, float delay)
    {
        ApplyShotRes();
        // 激光验证:瞄准可视区中心(光束从枪口到命中点,红点贴面);等一帧确保视口尺寸就绪
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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
    /// 等待时长按难度 Easy rand(3,8)/Hard rand(0,2)/Hell 0;飞斧/宝箱不等待(默认值 0)。
    /// 注意:本场景环境整体 X 镜像,x 微调随之翻转(原作 x<0→+0.3 ⇒ 本侧 x>0→−0.3,
    /// 裁决见 tools/research/battle_audit/battle0_mirror_verdict.md)</summary>
    protected override void OnMonsterBorn(Monster m)
    {
        m.FaceCamera();
        if (m.MetaKey is "bull" or "axe_zombie" or "skeleton")
        {
            var p = m.GlobalPosition;
            p.Y += 0.2f;
            if (p.X > 0.0f)
                p.X -= 0.3f;
            m.GlobalPosition = p;
            m.WaittingTime = DifficultyWaittingTime();
        }
    }

    /// <summary>debug:跳 G4 Boss 波并截图(爱心弱点验证)</summary>
    private async void DebugJumpBoss(string path)
    {
        ApplyShotRes();
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

    /// <summary>debug:强制切到第 g 波并截图(对位原作 Level1Shot L1_SHOT_GROUPS:强制 StartMonsterGroup(g) 后 2.5s 截,含 1s blend 落定)</summary>
    private async void DebugJumpGroup(string path, int g)
    {
        ApplyShotRes();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        InputRouter.Instance.MouseGun.SimulateMove(GetViewport().GetVisibleRect().Size / 2.0f);
        // 清场(避免 G0 已刷的怪堆叠,同 DebugJumpBoss)
        foreach (var m in GetTree().GetNodesInGroup("monster"))
            if (m is Monster mm && mm.IsActiveState)
                mm.Hit(99999.0f, mm.GlobalPosition, Game.HitType.Body, PlayerState.Side.Right);
        StartGroup(g);
        GD.Print($"[L1] jump group {g}: cam pos={Camera?.GlobalPosition} quat={Camera?.Quaternion} fov={Camera?.Fov}");
        await ToSignal(GetTree().CreateTimer(2.5f, true, true), SceneTreeTimer.SignalName.Timeout);
        GD.Print($"[L1] group{g} cam pos={Camera?.GlobalPosition} quat={Camera?.Quaternion} fov={Camera?.Fov}");
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(path.Replace('/', '\\'));
        GD.Print($"[L1] group{g} shot saved: {path}");
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
        // 怪物真值断言(wave1 修复):宝箱等待/舔舐间隔按 kind(school_day 场景序列化 20/20/20、Idle02Time 3/3/5);
        // 飞斧命中率的难度倍率走本关 diff_rate(1.8/2.6);近战 max_distance 6 / 飞斧 20
        Check(BoxMonster.WaittingTimeOf(BoxMonster.BoxKind.Bullet) == 20.0f
            && BoxMonster.WaittingTimeOf(BoxMonster.BoxKind.GunAK) == 20.0f
            && BoxMonster.WaittingTimeOf(BoxMonster.BoxKind.GunM4) == 20.0f,
            "box waitting_time 20/20/20 (school_day serialized)");
        Check(BoxMonster.Idle2IntervalOf(BoxMonster.BoxKind.Bullet) == 3.0f
            && BoxMonster.Idle2IntervalOf(BoxMonster.BoxKind.GunAK) == 3.0f
            && BoxMonster.Idle2IntervalOf(BoxMonster.BoxKind.GunM4) == 5.0f,
            "box idle2_interval 3/3/5 (prefab Idle02Time)");
        var flyInfo = MonsterInfo.Load("fly_axe_zombie");
        Check(flyInfo.HitRate == 0.15f && flyInfo.MaxDistance == 20.0f,
            "fly axe hit_rate 0.15 / max_distance 20");
        Check(MonsterInfo.Load("skeleton").MaxDistance == 6.0f
            && MonsterInfo.Load("axe_zombie").MaxDistance == 6.0f
            && MonsterInfo.Load("bull").MaxDistance == 6.0f,
            "melee max_distance 6 (SK/Axe/Nmw)");
        Check(MonsterInfo.Load("bull").Idle2Anim.Length == 0,
            "bull no Idle02 state (nmw.controller)");
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
