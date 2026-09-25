using Godot;

namespace FPSGame;

/// <summary>
/// Level3 战斗关(城市街区,7.2 L3 七波 15×7;G6 Boss rock_warrior + BossMusic)。
/// 坐标系:环境烘焙为 Unity 镜像 X(SceneExporter mirror_x 约定),机位/方向光
/// 由 Unity Level3.unity vcam 测绘换算 pos=(-x,y,z)、quat=(x,-y,-z,w)。
/// 启动参数:
///   (无)                    直接开战
///   --level3-selftest       headless 加速全流程断言
///   --level3-shot:<path>[:delaySec]       延迟截图(真值对比用),可多次传入
///   --level3-camshot:<idx>:<path>         直接机位 idx 截图(相机/环境自查),可多次传入
///   --level3-freeshot:<x>:<y>:<z>:<lx>:<ly>:<lz>:<path>[:delay]  自由机位(delay 可选,0=立即)
/// </summary>
public partial class Level3 : LevelBase
{
    private bool _testFailed;
    private readonly System.Collections.Generic.List<(string Path, float Delay)> _shots = new();
    private readonly System.Collections.Generic.List<(int Idx, string Path)> _camShots = new();
    private readonly System.Collections.Generic.List<((float X, float Y, float Z) Pos,
        (float X, float Y, float Z) Look, string Path, float Delay)> _freeShots = new();

    /// <summary>L3 怪池:wolf 系新场景 + fat_zombie/rock_warrior;fly_axe/skeleton/box 复用基类注册</summary>
    protected override void RegisterMonsterTypes()
    {
        base.RegisterMonsterTypes();
        Pool.RegisterType("wolf",
            GD.Load<PackedScene>("res://scenes/battle/monsters/wolf.tscn"), 10);
        Pool.RegisterType("wolf_blue",
            GD.Load<PackedScene>("res://scenes/battle/monsters/wolf_blue.tscn"), 8);
        Pool.RegisterType("wolf_green",
            GD.Load<PackedScene>("res://scenes/battle/monsters/wolf_green.tscn"), 8);
        Pool.RegisterType("fat_zombie",
            GD.Load<PackedScene>("res://scenes/battle/monsters/fat_zombie.tscn"), 8);
        Pool.RegisterType("toon_shoot_alien",
            GD.Load<PackedScene>("res://scenes/battle/monsters/toon_alien.tscn"), 8);
        Pool.RegisterType("rock_warrior",
            GD.Load<PackedScene>("res://scenes/battle/monsters/rock_warrior.tscn"), 1);
    }

    protected override void EnterLevel()
    {
        var args = OS.GetCmdlineUserArgs();
        if (System.Array.IndexOf(args, "--level3-selftest") >= 0)
        {
            SelfTest();
            return;
        }
        StartBattle();
        foreach (var a in args)
        {
            if (a.StartsWith("--level3-shot:"))
            {
                // 格式 --level3-shot:<path>[:delaySec](从右往左拆,兼容盘符)
                var rest = a["--level3-shot:".Length..];
                float delay = 4.0f;
                string path = rest;
                int ci = rest.LastIndexOf(':');
                if (ci > 1 && float.TryParse(rest[(ci + 1)..], out var d))
                {
                    delay = d;
                    path = rest[..ci];
                }
                _shots.Add((path, delay));
            }
            else if (a.StartsWith("--level3-camshot:"))
            {
                // 格式 --level3-camshot:<idx>:<path>(机位截图,相机/环境自查)
                var rest = a["--level3-camshot:".Length..];
                int ci = rest.IndexOf(':');
                if (ci > 0 && int.TryParse(rest[..ci], out var idx))
                    _camShots.Add((idx, rest[(ci + 1)..]));
            }
            else if (a.StartsWith("--level3-freeshot:"))
            {
                // 格式 --level3-freeshot:<x>:<y>:<z>:<lookX>:<lookY>:<lookZ>:<path>[:delaySec]
                // (自由机位,调试;delay 可选——相对路径无冒号时可用,0=立即,wave2_i)
                var rest = a["--level3-freeshot:".Length..];
                var vals = new float[6];
                int idx = -1;
                bool ok = true;
                for (int k = 0; k < 6 && ok; k++)
                {
                    int ni = rest.IndexOf(':', idx + 1);
                    if (ni < 0 || !float.TryParse(rest[(idx + 1)..ni], out vals[k]))
                        ok = false;
                    idx = ni;
                }
                if (ok && idx + 1 < rest.Length)
                {
                    string path = rest[(idx + 1)..];
                    float delay = 0.0f;
                    int ci = path.LastIndexOf(':');
                    if (ci > 0 && float.TryParse(path[(ci + 1)..], out var d))
                    {
                        delay = d;
                        path = path[..ci];
                    }
                    _freeShots.Add(((vals[0], vals[1], vals[2]), (vals[3], vals[4], vals[5]), path, delay));
                }
            }
        }
        if (_shots.Count > 0 || _camShots.Count > 0 || _freeShots.Count > 0)
        {
            ShotGuardLoop(); // 截图模式:防玩家死亡→续币面板暂停打断序列
            ShotsSequence();
        }
    }

    /// <summary>截图模式防护:外设重连会重置玩家 HP(Player.Born),轮询补无敌并关掉续币面板</summary>
    private async void ShotGuardLoop()
    {
        while (true)
        {
            PlayerState.Instance.PlayerRight.Hp = 1.0e6f;
            PlayerState.Instance.PlayerLeft.Hp = 1.0e6f;
            if (Game.Instance.IsGamePause)
            {
                PlayerState.Instance.PlayerRight.Relife();
                PlayerState.Instance.PlayerLeft.Relife();
                var panel = GetNodeOrNull("Camera3D/InGamePanel");
                if (panel != null && panel.HasMethod("CloseAll"))
                    panel.Call("CloseAll");
            }
            await ToSignal(GetTree().CreateTimer(0.3, true, false, true), SceneTreeTimer.SignalName.Timeout);
        }
    }

    /// <summary>G6 InitBoss:born 后面向相机(原作 SpawnBoss LookAt 相机),与本波小怪同刷</summary>
    protected override void StartGroup(int i)
    {
        base.StartGroup(i);
        if (i == SaveService.Get(Meta, "boss_group", -1).AsInt32()
            && SaveService.Get(Meta, "boss", "").AsString() != ""
            && Boss != null)
            Boss.FaceCamera();
    }

    /// <summary>统一截图序列:等 G0 blend 完成 → 机位 → 自由机位 → 延迟截图(时刻=开战起算),拍完退出</summary>
    private async void ShotsSequence()
    {
        double t0 = Time.GetTicksMsec() / 1000.0;
        await ToSignal(GetTree().CreateTimer(2.0, true, false, true), SceneTreeTimer.SignalName.Timeout);
        KillCameraTweens();
        foreach (var (idx, path) in _camShots)
        {
            if (Camera != null && idx >= 0 && idx < CamPositions.Length && CamPositions[idx] != null)
                Camera.GlobalTransform = CamPositions[idx].GlobalTransform;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            SaveShot(path, $"camshot {idx}");
        }
        foreach (var (pos, look, path, delay) in _freeShots)
        {
            double remain = delay - (Time.GetTicksMsec() / 1000.0 - t0);
            if (remain > 0.0)
                await ToSignal(GetTree().CreateTimer(remain, true, false, true), SceneTreeTimer.SignalName.Timeout);
            if (Camera != null)
            {
                Camera.GlobalPosition = new Vector3(pos.X, pos.Y, pos.Z);
                Camera.LookAt(new Vector3(look.X, look.Y, look.Z));
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            SaveShot(path, "freeshot");
        }
        foreach (var (path, delay) in _shots)
        {
            double remain = delay - (Time.GetTicksMsec() / 1000.0 - t0);
            if (remain > 0.0)
                await ToSignal(GetTree().CreateTimer(remain, true, false, true), SceneTreeTimer.SignalName.Timeout);
            SaveShot(path, "shot");
        }
        GetTree().Quit();
    }

    private void SaveShot(string path, string tag)
    {
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(path.Replace('/', '\\'));
        GD.Print($"[L3] {tag} saved: {path}");
    }

    /// <summary>杀掉所有活动 Tween(截图模式防 G0 blend 覆写摆好的机位)</summary>
    private void KillCameraTweens()
    {
        foreach (var tw in GetTree().GetProcessedTweens())
            tw.Kill();
    }

    // ------------------------------------------------ 自检

    private void Check(bool cond, string label)
    {
        GD.Print((cond ? "[L3-TEST] PASS: " : "[L3-TEST] FAIL: ") + label);
        if (!cond)
            _testFailed = true;
    }

    /// <summary>难度数量断言:Easy 15×7,Hard (int)(15×1.5)=22×7,Hell 30×7(int 截断)</summary>
    private void CheckDifficultyCounts()
    {
        var saved = Game.Instance.CurrentDifficulty;
        var cases = new System.Collections.Generic.Dictionary<Game.Difficulty, int[]>
        {
            [Game.Difficulty.Easy] = new[] { 15, 15, 15, 15, 15, 15, 15 },
            [Game.Difficulty.Hard] = new[] { 22, 22, 22, 22, 22, 22, 22 },
            [Game.Difficulty.Hell] = new[] { 30, 30, 30, 30, 30, 30, 30 },
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

    /// <summary>headless 自检:godot --headless --path game scenes/levels/level3.tscn -- --level3-selftest</summary>
    private async void SelfTest()
    {
        TimeScaleTest = 0.04f; // 加速刷怪间隔/冻结/相机 Tween;胜利延迟保持真实 2s 以验证
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
        PlayerState.Instance.PlayerRight.Hp = 1.0e6f; // 防止测试期再次被打死触发面板暂停
        // 等 G6 boss born
        double tWait = Time.GetTicksMsec() / 1000.0;
        while (Boss == null && Time.GetTicksMsec() / 1000.0 - tWait < 180.0)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(Boss != null, "G6: boss (rock_warrior) born");
        // L3-1:boss 出生点 = meta boss_pos(Level3.unity RockWarrior (83.83,-0.02,-101.3) mirror-X)
        var expectBossPos = new Vector3(-83.83f, -0.02f, -101.3f);
        Check(Boss != null && Boss.GlobalPosition.DistanceTo(expectBossPos) < 1.0f,
            $"boss born at meta boss_pos (got {Boss?.GlobalPosition}, expect ≈{expectBossPos})");
        Check(CurGroup == 6, $"boss born at G6 (cur_group={CurGroup})");
        Check(StatBossBgm, "G6: boss_bgm switched");
        if (BgmPlayer != null && BgmPlayer.Stream != null)
            Check(BgmPlayer.Stream.ResourcePath.EndsWith("Level3Boss.mp3"),
                $"G6: bgm stream = {BgmPlayer.Stream.ResourcePath}");
        double bossDeadTime = -1.0;
        if (Boss != null)
        {
            Boss.Died += _ => bossDeadTime = Time.GetTicksMsec() / 1000.0;
            // rock_warrior 硬化皮肤:非 atk01 状态单发只掉 1 血,压血线补刀
            Boss.Hp = 1.0f;
            Boss.Hit(99999.0f, Boss.GlobalPosition, Game.HitType.Body, PlayerState.Side.Right);
        }
        // 等胜利信号
        double t0 = Time.GetTicksMsec() / 1000.0;
        while (victoryCount == 0 && Time.GetTicksMsec() / 1000.0 - t0 < 90.0)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(victoryCount == 1, "level_victory emitted exactly once");
        Check(bossDeadTime > 0.0 && victoryTime - bossDeadTime >= 1.8,
            $"boss dead → ~2s → victory (delay={victoryTime - bossDeadTime:0.00}s)");
        Check(StatGroupsStarted.Count == 7 && string.Join("", StatGroupsStarted) == "0123456",
            $"7 groups in order: {string.Join(",", StatGroupsStarted)}");
        Check(StatCamSwitches == 7, $"camera switched 7 times (got {StatCamSwitches})");
        Check(StatFreezes == 7 && SpawnFreezeTime == 2.0f, "spawn freeze 2s × 7 groups");
        Check(StatBoxRolls > 0, $"box probability rolls invoked ({StatBoxRolls} ticks)");
        // HUD 存在性
        Check(PlayerState.Instance.CoinObj != null, "HUD coin exists");
        GD.Print($"[L3-TEST] done, failed={_testFailed}");
        if (BgmPlayer != null)
        {
            BgmPlayer.Stop();
            BgmPlayer.Stream = null;
        }
        GetTree().Quit(_testFailed ? 1 : 0);
    }
}
