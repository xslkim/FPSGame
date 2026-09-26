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
            || a.StartsWith("--level1-shot-boss:") || a.StartsWith("--level1-shot-group:")
            || a.StartsWith("--level1-probe:") || a.StartsWith("--level1-fire:")
            || a.StartsWith("--level1-shot-victory:")) >= 0;
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
                else if (a.StartsWith("--level1-probe:"))
                {
                    // 探针:战斗开始后 sec 秒打印相机/怪物/光标位置与屏幕投影(不写文件)
                    var (_, sec) = SplitShotArg(a["--level1-probe:".Length..], 7.0f);
                    ProbeDelayed(sec);
                }
                else if (a.StartsWith("--level1-shot-victory:"))
                {
                    // 胜利面板截图:跳 G4 → 击杀 Boss → 2s 后胜利面板(原作 Victory 流程)
                    CallDeferred(nameof(DebugVictoryShot), a["--level1-shot-victory:".Length..]);
                }
                else if (a.StartsWith("--level1-fire:"))
                {
                    // 开火验证:按住鼠标左键,每帧瞄准第一只活怪胸口,sec 秒后截图(伤害/飘字/击杀链路)
                    var (path, sec) = SplitShotArg(a["--level1-fire:".Length..], 6.0f);
                    FireTest(path, sec);
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
            else if (a.StartsWith("--level1-probe-intro:"))
            {
                var (_, sec) = SplitShotArg(a["--level1-probe-intro:".Length..], 3.0f);
                ProbeDelayed(sec); // 开场模式:探针打印相机视线上的物体(识别异常网格)
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

    /// <summary>探针(debug 直开后 sec 秒):打印相机/存活怪物/命中光标的 world+屏幕投影,验证落位真值</summary>
    private async void ProbeDelayed(float sec)
    {
        ApplyShotRes();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        InputRouter.Instance.MouseGun.SimulateMove(GetViewport().GetVisibleRect().Size / 2.0f);
        await ToSignal(GetTree().CreateTimer(sec, true, true), SceneTreeTimer.SignalName.Timeout);
        var cam = GetViewport().GetCamera3D();
        var vp = GetViewport().GetVisibleRect().Size;
        GD.Print($"[PROBE] cam pos={cam.GlobalPosition} quat={cam.Quaternion} fov={cam.Fov} vp={vp}");
        foreach (var m in Pool.GetChildren())
        {
            if (m is not Monster mm || !mm.IsActiveState)
                continue;
            Vector3 p = mm.GlobalPosition;
            Vector2 sp = cam.UnprojectPosition(p + new Vector3(0, 1.0f, 0.0f));
            GD.Print($"[PROBE] {mm.MetaKey} lv={mm.Level} hp={mm.Hp} pos=({p.X:0.00},{p.Y:0.00},{p.Z:0.00}) " +
                $"dist={p.DistanceTo(cam.GlobalPosition):0.00} scr=({sp.X:0},{sp.Y:0}) behind={cam.IsPositionBehind(p)}");
        }
        // 视口射线:识别指定归一化屏幕点上的物体(异常网格排查)
        var space = GetWorld3D().DirectSpaceState;
        foreach (var uv in new[] { new Vector2(0.50f, 0.50f), new Vector2(0.61f, 0.28f),
            new Vector2(0.55f, 0.20f), new Vector2(0.66f, 0.25f), new Vector2(0.62f, 0.35f), new Vector2(0.70f, 0.15f) })
        {
            var o = cam.ProjectRayOrigin(uv * vp);
            var n = cam.ProjectRayNormal(uv * vp);
            var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(o, o + n * 100.0f));
            if (hit.Count > 0)
            {
                var col = (GodotObject)hit["collider"];
                string name = col is Node cn ? cn.GetPath().ToString() : "?";
                var hp = (Vector3)hit["position"];
                GD.Print($"[PROBE-RAY] uv=({uv.X:0.00},{uv.Y:0.00}) hit {name} at ({hp.X:0.0},{hp.Y:0.0},{hp.Z:0.0}) dist={hp.DistanceTo(o):0.0}");
            }
            else
                GD.Print($"[PROBE-RAY] uv=({uv.X:0.00},{uv.Y:0.00}) no hit");
        }
        // AABB 扫描:列出视线穿过的所有可见网格(含无碰撞体的视觉网格),按距离排序
        Node scanRoot = this; // 从关卡根扫(含 env/怪物/枪/特效一切 MeshInstance3D)
        GD.Print($"[PROBE-SCAN] root={scanRoot.GetPath()} (env null={_env == null})");
        {
            foreach (var uv in new[] { new Vector2(0.61f, 0.28f), new Vector2(0.58f, 0.22f) })
            {
                var o = cam.ProjectRayOrigin(uv * vp);
                var n = cam.ProjectRayNormal(uv * vp).Normalized();
                var hits = new System.Collections.Generic.List<(float T, string Desc)>();
                foreach (var node in scanRoot.FindChildren("*", "MeshInstance3D", true, false))
                {
                    if (node is not MeshInstance3D mi || !mi.IsVisibleInTree() || mi.Mesh == null)
                        continue;
                    var inv = mi.GlobalTransform.AffineInverse();
                    Vector3 lo = inv * o;
                    Vector3 ld = (inv.Basis * n); // 非归一(跟随缩放),t 仅用于排序相对比较
                    var aabb = mi.GetAabb();
                    // slab 法
                    float tmin = 0.0f, tmax = float.MaxValue;
                    bool miss = false;
                    for (int ax = 0; ax < 3 && !miss; ax++)
                    {
                        float oo = lo[ax], dd = ld[ax];
                        float mn = aabb.Position[ax], mx = aabb.Position[ax] + aabb.Size[ax];
                        if (Mathf.Abs(dd) < 1e-8f)
                        {
                            if (oo < mn || oo > mx) miss = true;
                            continue;
                        }
                        float t1 = (mn - oo) / dd, t2 = (mx - oo) / dd;
                        if (t1 > t2) (t1, t2) = (t2, t1);
                        tmin = Mathf.Max(tmin, t1); tmax = Mathf.Min(tmax, t2);
                        if (tmin > tmax) miss = true;
                    }
                    if (!miss && tmax > 0.0f)
                    {
                        string matName = "?";
                        var m0 = mi.GetActiveMaterial(0);
                        if (m0 is BaseMaterial3D bm) matName = $"{m0.ResourceName}(blend={(int)bm.BlendMode},transp={(int)bm.Transparency},shading={(int)bm.ShadingMode})";
                        else if (m0 != null) matName = m0.ResourceName;
                        hits.Add((Mathf.Max(tmin, 0.0f), $"{mi.GetPath()} aabb={aabb} mat={matName}"));
                    }
                }
                hits.Sort((a, b) => a.T.CompareTo(b.T));
                GD.Print($"[PROBE-SCAN] uv=({uv.X:0.00},{uv.Y:0.00}) {hits.Count} meshes along ray:");
                foreach (var h in hits.GetRange(0, Mathf.Min(hits.Count, 8)))
                    GD.Print($"[PROBE-SCAN]   t={h.T:0.00} {h.Desc}");
            }
        }
        GD.Print("[PROBE] done");
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng($"G:/FPSGame/tools/screenshots/check1/probe_{sec:0}s.png".Replace('/', '\\'));
        GetTree().Quit();
    }
    /// <summary>开火验证(debug 直开后):等首怪出生,然后按住左键跟踪瞄准最近活怪,sec 秒后截图退出</summary>
    private async void FireTest(string path, float sec)
    {
        ApplyShotRes();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        // 等冻结期结束首怪出生(2s 冻结 + tick)
        double t0 = Time.GetTicksMsec() / 1000.0;
        Monster? target = null;
        while (Time.GetTicksMsec() / 1000.0 - t0 < 10.0)
        {
            target = FirstActiveMonster();
            if (target != null)
                break;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        GD.Print($"[FIRE] target={(target != null ? target.MetaKey + " hp=" + target.Hp : "none")}");
        InputRouter.Instance.MouseGun.LeftHeld = true;
        double tEnd = Time.GetTicksMsec() / 1000.0 + sec;
        int frame = 0;
        while (Time.GetTicksMsec() / 1000.0 < tEnd)
        {
            var cam = GetViewport().GetCamera3D();
            target = FirstActiveMonster();
            if (target != null && cam != null)
            {
                Vector3 chest = target.GlobalPosition + new Vector3(0, target.AimCenterY, 0); // 瞄胶囊中心(原作 CC 高度各异)
                if (!cam.IsPositionBehind(chest))
                    InputRouter.Instance.MouseGun.SimulateMove(cam.UnprojectPosition(chest));
            }
            if (++frame % 60 == 0)
                GD.Print($"[FIRE] f={frame} keyRing={InputRouter.Instance.GetCurKeyRing()} " +
                    $"leftHeld={InputRouter.Instance.MouseGun.LeftHeld} state={Game.Instance.SceneState} " +
                    $"mode={InputRouter.Instance.Mode} fireEnabled={InputRouter.Instance.FireEnabled}");
            if (frame == 120) // 中段右键换枪:验证 SwitchGun 信号链(原作右键=换枪)
            {
                int before = PlayerState.Instance.PlayerRight.GunType;
                InputRouter.Instance.MouseGun.SimulateSwitch();
                int after = PlayerState.Instance.PlayerRight.GunType;
                GD.Print($"[FIRE] switch gun via mouse right: {before} → {after} (expect change)");
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        InputRouter.Instance.MouseGun.LeftHeld = false;
        GD.Print($"[FIRE] after: target={(target != null ? target.MetaKey + " hp=" + target.Hp + " dead=" + target.IsDead : "none")} " +
            $"alive={PlayerState.CurAliveMonster} bullet={PlayerState.Instance.PlayerRight.Bullet}");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); // 等飘字/特效呈现一帧
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(path.Replace('/', '\\'));
        GD.Print($"[FIRE] shot saved: {path}");
        GetTree().Quit();
    }

    private Monster? FirstActiveMonster()
    {
        foreach (var m in Pool.GetChildren())
            if (m is Monster mm && mm.IsActiveState && !mm.IsDead)
                return mm;
        return null;
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

    /// <summary>debug:跳 G4 杀 Boss,等胜利面板弹出后截图(胜利流程可视化验证)</summary>
    private async void DebugVictoryShot(string path)
    {
        ApplyShotRes();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        StartGroup(4);
        await ToSignal(GetTree().CreateTimer(3.0f, true, true), SceneTreeTimer.SignalName.Timeout);
        if (Boss != null && Boss.IsActiveState)
            Boss.Hit(99999.0f, Boss.GlobalPosition, Game.HitType.Body, PlayerState.Side.Right);
        await ToSignal(GetTree().CreateTimer(4.0f, true, true), SceneTreeTimer.SignalName.Timeout); // 胜利延迟 2s + 面板呈现
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(path.Replace('/', '\\'));
        GD.Print($"[L1] victory shot saved: {path}");
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
        // 开火链路契约回归(2026-09-26 修复:GunBase.Player 未赋值→开火 NRE;FireSystem Call("hit") 小写不匹配)
        Check(FireSys == null || FireSys.AllGunsBound, "fire path: all guns have Player bound (no NRE)");
        {
            var anyMonster = System.Linq.Enumerable.FirstOrDefault(
                System.Linq.Enumerable.OfType<Monster>(Pool.GetChildren()));
            Check(anyMonster != null && anyMonster.HasMethod("Hit"),
                "fire path: monster exposes Hit (FireSystem dispatch name, case-sensitive)");
        }
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
