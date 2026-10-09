using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace FPSGame;

/// <summary>
/// Normal waves/geometry, real mouse input and barrel rays. No direct Hit, forced
/// victory, altered HP/ammo, cooldown reset or time acceleration. Isolated save only.
/// --playthrough:level2[,level1,...][:easy|hard|hell][:seed]
/// </summary>
public partial class PlaythroughQa : Node
{
    private sealed class Life
    {
        public Monster Actor = null!;
        public int Number;
        public int Group;
        public float InitialHp;
        public float LastHp;
        public double Born;
        public double VisibleSeconds;
        public bool Damaged;
        public bool Closed;
        public bool Killed;
        public string Outcome = "active";
        public string LastPosition = "";
        public string LastAnimation = "";
    }

    private readonly List<Life> _lives = new();
    private readonly Dictionary<ulong, Life> _currentLives = new();
    private readonly HashSet<int> _weaponsFired = new();
    private readonly Dictionary<GunBase, int> _gunShots = new();
    private Monster[] _actors = Array.Empty<Monster>();
    private GunBase[] _guns = Array.Empty<GunBase>();
    private LevelBase? _level;
    private Camera3D _camera = null!;
    private double _start, _lastLog, _lastScan, _lastSwap;
    private double _pauseSince;
    private int _lastCoin, _shots, _continues, _failed;
    private bool _running;
    private string? _visualShotDir;
    private bool _captureBusy;
    private double _nextVisualScan;
    private readonly HashSet<string> _captured = new();
    private static double Now => Time.GetTicksMsec() / 1000.0;
    private InputRouter Router => InputRouter.Instance;
    private Player Right => PlayerState.Instance.PlayerRight;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            if (SaveService.SaveDirectory == "user://")
                throw new InvalidOperationException("Playthrough requires --qa-save-dir; player saves cannot be used.");
            foreach (var option in OS.GetCmdlineUserArgs())
                if (option.StartsWith("--playthrough-shot-dir:"))
                    _visualShotDir = option["--playthrough-shot-dir:".Length..];
            if (_visualShotDir != null) DirAccess.MakeDirRecursiveAbsolute(_visualShotDir);
            // The headless display defaults to a 64x64 window. That square view
            // crops authored side windows and cannot stand in for the game's 16:9 view.
            GetTree().Root.Mode = Window.ModeEnum.Windowed;
            GetTree().Root.Size = new Vector2I(1280, 720);
            // The exported EXE first runs StartupScreen's deferred menu change.
            // Let it finish before requesting Loading, otherwise that change can
            // replace Loading and strand the QA start in Menu.
            await Wait(() => GetTree().CurrentScene?.SceneFilePath == "res://scenes/ui/menu.tscn", 10, "startup menu");
            var arg = OS.GetCmdlineUserArgs().First(a => a.StartsWith("--playthrough:"))[14..].Split(':');
            var levels = arg[0].Split(',');
            if (levels.Any(k => k is not ("level1" or "level2" or "level3" or "level4")))
                throw new InvalidOperationException("Only authored level1-4 are valid playthrough targets.");
            Game.Instance.CurrentDifficulty = arg.Length > 1 ? arg[1] switch
            {
                "hard" => Game.Difficulty.Hard, "hell" => Game.Difficulty.Hell, _ => Game.Difficulty.Easy,
            } : Game.Difficulty.Easy;
            GD.Seed(arg.Length > 2 ? ulong.Parse(arg[2]) : 144006);
            SaveService.Instance.Coin = 50;
            SaveService.Instance.MaxCoin = 50;
            SaveService.Instance.SaveUserData();
            foreach (string key in levels) await RunLevel(key);
            AudioService.Instance.StopAll();
            GD.Print($"[PLAYTHROUGH-QA] {(_failed == 0 ? "ALL PASS" : "FAILED")} failures={_failed}");
            GetTree().Quit(_failed == 0 ? 0 : 1);
        }
        catch (Exception ex)
        {
            if (_running && _level != null) WriteResult(_level.LevelKey);
            _running = false;
            Router.MouseGun.LeftHeld = false;
            GD.PushError("[PLAYTHROUGH-QA] " + ex);
            GetTree().Quit(1);
        }
    }

    private void Check(bool ok, string label)
    {
        GD.Print($"[PLAYTHROUGH-QA] {(ok ? "PASS" : "FAIL")}: {label}");
        if (!ok) _failed++;
    }

    private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async Task Wait(Func<bool> predicate, double seconds, string label)
    {
        double deadline = Now + seconds;
        while (!predicate() && Now < deadline) await Frame();
        if (!predicate()) throw new InvalidOperationException("Timeout: " + label);
    }

    private async Task RunLevel(string key)
    {
        _lives.Clear(); _currentLives.Clear(); _weaponsFired.Clear(); _gunShots.Clear();
        _shots = _continues = 0;
        Router.MouseGun.LeftHeld = false;
        Router.SetInputMode(InputRouter.InputMode.Mouse);
        Router._Notification((int)NotificationApplicationFocusIn);
        Game.Instance.NextScenePath = $"res://scenes/levels/{(key == "level1" ? "level1_battle" : key)}.tscn";
        Game.Instance.ChangeScene("res://scenes/ui/loading.tscn");
        await Wait(() => GetTree().CurrentScene is LevelBase l && l.LevelKey == key
            && InGamePanel.Instance != null && FireSystem.Current != null, 30, key + " loading");
        _level = (LevelBase)GetTree().CurrentScene;
        _camera = _level.GetNode<Camera3D>("Camera3D");
        _actors = _level.FindChildren("*", "", true, false).OfType<Monster>().ToArray();
        _guns = FireSystem.Current!.FindChildren("*", "", true, false).OfType<GunBase>().ToArray();
        _start = _lastLog = _lastSwap = Now; _lastScan = -99; _pauseSince = 0;
        _lastCoin = SaveService.Instance.Coin;
        GD.Print($"[PLAYTHROUGH-QA] START {key} {Game.Instance.CurrentDifficulty} HP={Right.Hp} ammo={Right.Bullet} window={GetTree().Root.Size} viewport={GetViewport().GetVisibleRect().Size}");
        _running = true;
        await Wait(() => (int)_level.CaptureDebugState()["victory_state"]! == 2, 1000, key + " normal victory");
        _running = false;
        Router.MouseGun.LeftHeld = false;
        await Capture(key + "-victory");
        ScanLives();
        foreach (var life in _lives.Where(l => !l.Closed))
        {
            life.Closed = true;
            life.Outcome = "victory_cleanup";
        }
        Check(_lives.Count > 0 && _shots > 0, key + " actual spawns and shots");
        Check(_level.StatGroupsStarted.SequenceEqual(Enumerable.Range(0, _level.StatGroupsStarted.Count)), key + " ordered waves");
        Check(_weaponsFired.Count == 3, key + " fired pistol, AK and M4 during normal waves");
        int expired = _lives.Count(l => l.Outcome == "timeout");
        int hidden = _lives.Count(l => l.Outcome == "timeout" && l.VisibleSeconds < 0.1);
        // A cleared scene must not conceal completely inaccessible targets behind its timeout logic.
        Check(hidden == 0, key + $" no entirely inaccessible spawn (expired={expired}, hidden={hidden})");
        Check(_lives.Any(l => l.Killed), key + " real shots killed enemies");
        int number = int.Parse(key[5..]);
        int star = SaveService.GetInt(SaveService.Instance.LevelState[number - 1], "star", 0);
        Check(star >= (int)Game.Instance.CurrentDifficulty + 1, key + " victory persisted difficulty result");
        WriteResult(key);
        GD.Print($"[PLAYTHROUGH-QA] SUMMARY {key} elapsed={Now-_start:0.0}s lives={_lives.Count} kills={_lives.Count(l=>l.Killed)} shots={_shots} continues={_continues}");
        var button = InGamePanel.Instance!.GetNode<UiButton3D>("VectoryPanel/VectoryPanelBtn");
        await ClickUntil(button, () => GetTree().CurrentScene?.SceneFilePath == "res://scenes/ui/menu.tscn", "victory menu");
        Check(!Game.Instance.IsGamePause && Game.Instance.SceneState == Game.GameState.UI, key + " victory exits to usable menu");
        SaveService.Instance.LoadUserData();
        Check(SaveService.GetInt(SaveService.Instance.LevelState[number - 1], "star", 0) >= star, key + " result survives save reload");
        _level = null;
    }

    public override void _Process(double delta)
    {
        if (!_running || _level == null) return;
        ScanLives();
        ObserveVisuals();
        // Level1 constructs weapons after its 19-second intro, unlike Level2-4.
        if (_guns.Length == 0 && Router.FireEnabled && FireSystem.Current != null)
            _guns = FireSystem.Current.FindChildren("*", "", true, false).OfType<GunBase>().ToArray();
        foreach (var gun in _guns.Where(g => g.Player == Right))
        {
            _gunShots.TryGetValue(gun, out int previous);
            if (gun.ShotsFired > previous)
            {
                _shots += gun.ShotsFired - previous;
                _weaponsFired.Add(gun.GunType);
            }
            _gunShots[gun] = gun.ShotsFired;
        }
        if (SaveService.Instance.Coin < _lastCoin) _continues += _lastCoin - SaveService.Instance.Coin;
        _lastCoin = SaveService.Instance.Coin;
        if (Now - _lastLog > 12)
        {
            _lastLog = Now;
            GD.Print($"[PLAYTHROUGH-QA] { _level.DebugSummary} HP={Right.Hp:0.0} ammo={Right.Bullet} kills={_lives.Count(l=>l.Killed)} shots={_shots} continues={_continues}");
            foreach (var l in _currentLives.Values.Where(l => !l.Closed && Now-l.Born > 12))
            {
                GD.Print($"[PLAYTHROUGH-QA] target {l.Number}/{l.Actor.DebugSummary} pos={l.LastPosition} anim={l.LastAnimation} visible={l.VisibleSeconds:0.0}s damage={l.Damaged}");
                if (l.Actor is ToonMonster toon)
                {
                    var p = _camera.UnprojectPosition(toon.HeadHitPosition);
                    var ray = _camera.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(_camera.ProjectRayOrigin(p),
                        _camera.ProjectRayOrigin(p) + _camera.ProjectRayNormal(p) * FireSystem.RayLength, FireSystem.RayMask));
                    var hitNode = ray.Count > 0 ? ray["collider"].AsGodotObject() as Node : null;
                    GD.Print($"[PLAYTHROUGH-QA] head={toon.HeadHitPosition} screen={p} blocker={hitNode?.GetPath()} desired={toon.GetPath()} camera={_camera.GlobalTransform}");
                }
            }
        }
        if (Game.Instance.IsGamePause)
        {
            var panel = InGamePanel.Instance!;
            var pause = panel.GetNode<Node3D>("PausePanel");
            if (pause.Visible)
            {
                if (_pauseSince == 0) { _pauseSince = Now; Router.MouseGun.LeftHeld = false; }
                var back = pause.GetNode<UiButton3D>("BackGameBtn");
                Router.MouseGun.SimulateMove(_camera.UnprojectPosition(back.GlobalPosition));
                Router.MouseGun.LeftHeld = Now - _pauseSince > 0.12;
                return;
            }
            var container = panel.GetNode<Node3D>("ContinuePanel");
            if (container.Visible)
            {
                // Release for two frames before a new pull; UI only activates on the edge.
                if (_pauseSince == 0) { _pauseSince = Now; Router.MouseGun.LeftHeld = false; }
                var confirm = container.GetNode<UiButton3D>("ContinueGameBtn");
                Router.MouseGun.SimulateMove(_camera.UnprojectPosition(confirm.GlobalPosition));
                Router.MouseGun.LeftHeld = Now - _pauseSince > 0.12;
            }
            else Router.MouseGun.LeftHeld = false;
            return;
        }
        _pauseSince = 0;
        if (!Router.FireEnabled) { Router.MouseGun.LeftHeld = false; return; }
        if (Now - _lastSwap > 15)
        {
            _lastSwap = Now;
            Router.MouseGun.SimulateSwitch();
        }
        if (Now - _lastScan < 0.035) return;
        double sampleDelta = _lastScan < 0 ? 0 : Now - _lastScan;
        _lastScan = Now;
        Vector2? target = null;
        // Sampling observes camera rays; damage is still exclusively produced by FireSystem's barrel ray.
        foreach (var life in _currentLives.Values.Where(l => !l.Closed && !l.Actor.IsDead && l.Actor.Hp > 0)
            .OrderBy(l => l.Actor is BoxMonster ? 0 : l.Actor.IsBoss ? 2 : 1)
            .ThenBy(l => l.Actor.GlobalPosition.DistanceSquaredTo(_camera.GlobalPosition)))
        {
            var point = VisiblePoint(life.Actor);
            if (point != null) life.VisibleSeconds += Math.Min(sampleDelta, 0.15);
            target ??= point;
        }
        if (target is Vector2 aim)
        {
            Router.MouseGun.SimulateMove(aim);
            Router.MouseGun.LeftHeld = true;
        }
        else Router.MouseGun.LeftHeld = false;
    }

    private void ObserveVisuals()
    {
        if (_visualShotDir == null || _captureBusy || _level == null || Now < _nextVisualScan) return;
        _nextVisualScan = Now + 0.1;
        string key = _level.LevelKey;
        if (Game.Instance.IsGamePause)
        {
            if (InGamePanel.Instance!.GetNode<Node3D>("ContinuePanel").Visible)
                _ = Capture(key + "-continue");
            else if (InGamePanel.Instance!.GetNode<Node3D>("PausePanel").Visible)
                _ = Capture(key + "-pause");
            return;
        }
        int group = (int)_level.CaptureDebugState()["group"]!;
        foreach (var life in _currentLives.Values.Where(l => !l.Closed && !l.Actor.IsDead && l.Actor.Hp > 0))
        {
            string type = life.Actor.MetaKey;
            string animation = life.LastAnimation;
            string name = !_captured.Contains($"{key}-wave{group}") ? $"{key}-wave{group}"
                : !_captured.Contains($"{key}-{type}") ? $"{key}-{type}"
                : animation.Contains("attack", StringComparison.OrdinalIgnoreCase)
                    || animation.Contains("skill", StringComparison.OrdinalIgnoreCase)
                    || animation.Contains("atk", StringComparison.OrdinalIgnoreCase)
                    || animation.Contains("breath", StringComparison.OrdinalIgnoreCase)
                    ? $"{key}-{type}-{animation.Replace('/', '-') }" : "";
            if (name.Length == 0 || _captured.Contains(name) || VisiblePoint(life.Actor) == null) continue;
            _ = Capture(name);
            return;
        }
        foreach (var effect in _level.FindChildren("*", "", true, false)
            .OfType<Node3D>().Where(n => n is EffectBase or Fireball))
        {
            string name = key + "-effect-" + (effect is Fireball ? "fireball" : effect.SceneFilePath.GetFile().GetBaseName());
            if (_captured.Contains(name) || !effect.IsVisibleInTree() || _camera.IsPositionBehind(effect.GlobalPosition)) continue;
            if (!GetViewport().GetVisibleRect().HasPoint(_camera.UnprojectPosition(effect.GlobalPosition))) continue;
            _ = Capture(name);
            return;
        }
    }

    private async Task Capture(string name)
    {
        if (_visualShotDir == null || _captured.Contains(name)) return;
        while (_captureBusy) await Frame();
        _captureBusy = true;
        try
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string path = System.IO.Path.Combine(_visualShotDir, name + ".png");
            Check(GetViewport().GetTexture().GetImage().SavePng(path) == Error.Ok, "render saved: " + path);
            _captured.Add(name);
        }
        catch (Exception ex)
        {
            Check(false, "render capture: " + ex.Message);
        }
        finally { _captureBusy = false; }
    }

    private void ScanLives()
    {
        foreach (var actor in _actors)
        {
            if (!GodotObject.IsInstanceValid(actor)) continue;
            ulong id = actor.GetInstanceId();
            _currentLives.TryGetValue(id, out var life);
            if (actor.IsActiveState && !actor.IsDead && actor.Hp > 0 && (life == null || life.Closed))
            {
                life = new Life { Actor = actor, Number = _lives.Count+1,
                    Group = (int)_level!.CaptureDebugState()["group"]!, InitialHp = actor.Hp, LastHp = actor.Hp, Born = Now };
                _lives.Add(life); _currentLives[id] = life;
            }
            if (life == null || life.Closed) continue;
            life.Damaged |= actor.Hp < life.LastHp;
            life.LastHp = actor.Hp;
            life.LastPosition = actor.GlobalPosition.ToString();
            life.LastAnimation = actor.GetNode<AnimationPlayer>("AnimationPlayer").CurrentAnimation;
            if (actor.IsDead || !actor.IsActiveState || actor.Hp <= 0) CloseLife(life);
        }
    }

    private void CloseLife(Life life)
    {
        life.Closed = true;
        life.Killed = life.Actor.IsDead; // Box expiration also sets HP=0; HP alone is not a kill.
        life.Outcome = life.Killed ? "killed" : "timeout";
        if (!life.Killed)
            GD.Print($"[PLAYTHROUGH-QA] EXPIRED {life.Number}/{life.Actor.MetaKey} g={life.Group} hp={life.LastHp} pos={life.LastPosition} visible={life.VisibleSeconds:0.0}s damage={life.Damaged}");
    }

    private Vector2? VisiblePoint(Monster actor) => FindVisiblePoint(actor, _camera);

    internal static Vector2? FindVisiblePoint(Monster actor, Camera3D camera)
    {
        CollisionObject3D? desired = actor is Level2Boss boss ? boss.WeakSpot
            : actor is BaotouMonster ? actor.FindChild("BossHeart", true, false) as CollisionObject3D : actor;
        if (desired == null) return null;
        var points = new List<Vector3>();
        if (actor is ToonMonster toon) points.Add(toon.HeadHitPosition);
        foreach (var shape in desired.GetChildren().OfType<CollisionShape3D>().Where(c => !c.Disabled))
        {
            points.Add(shape.GlobalPosition);
            float r = shape.Shape switch
            {
                SphereShape3D sphere => sphere.Radius,
                CapsuleShape3D capsule => capsule.Radius,
                BoxShape3D box => Mathf.Min(box.Size.X, box.Size.Y) / 2,
                _ => 0.25f,
            } * shape.GlobalBasis.Scale.X;
            float vertical = shape.Shape switch
            {
                CapsuleShape3D capsule => capsule.Height / 2,
                BoxShape3D box => box.Size.Y / 2,
                _ => r / shape.GlobalBasis.Scale.X,
            } * shape.GlobalBasis.Scale.Y;
            int range = actor.IsBoss ? 5 : 3;
            for (int x = -range; x <= range; x++)
            for (int y = -range; y <= range; y++)
            {
                var offset = new Vector2(x, y) / range;
                if (offset.LengthSquared() > 0.95f) continue;
                points.Add(shape.GlobalPosition + (camera.GlobalBasis.X * offset.X * r + camera.GlobalBasis.Y * offset.Y * vertical) * 0.9f);
            }
        }
        Vector2 size = camera.GetViewport().GetVisibleRect().Size;
        foreach (var p in points)
        {
            if (camera.IsPositionBehind(p)) continue;
            Vector2 screen = camera.UnprojectPosition(p);
            if (screen.X < 8 || screen.Y < 8 || screen.X > size.X-8 || screen.Y > size.Y-8) continue;
            Vector3 from = camera.ProjectRayOrigin(screen);
            var ray = camera.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from,
                from + camera.ProjectRayNormal(screen) * FireSystem.RayLength, FireSystem.RayMask));
            if (ray.Count > 0 && ray["collider"].AsGodotObject() == desired) return screen;
        }
        return null;
    }

    private async Task ClickUntil(UiButton3D button, Func<bool> complete, string label)
    {
        double deadline = Now + 10;
        while (!complete() && Now < deadline)
        {
            Router.MouseGun.LeftHeld = false;
            for (int i = 0; i < 8; i++) await Frame();
            if (complete()) break;
            Router.MouseGun.SimulateMove(_camera.UnprojectPosition(button.GlobalPosition));
            for (int i = 0; i < 5; i++) await Frame();
            Router.MouseGun.LeftHeld = true;
            double release = Now + 0.2;
            while (!complete() && Now < release) await Frame();
        }
        Router.MouseGun.LeftHeld = false;
        Check(complete(), label + " actual gun button");
        if (!complete()) throw new InvalidOperationException("Timeout: " + label);
    }

    private void WriteResult(string key)
    {
        var data = new
        {
            build = BuildIdentity.Capture(),
            level = key, difficulty = Game.Instance.CurrentDifficulty.ToString(), seconds = Now-_start,
            shots = _shots, continues = _continues, weapons = _weaponsFired.ToArray(),
            groups = _level!.StatGroupsStarted,
            lives = _lives.Select(l => new { id = l.Number, type = l.Actor.MetaKey, group = l.Group,
                hp_start = l.InitialHp, hp_end = l.LastHp, closed = l.Closed, outcome = l.Outcome, killed = l.Killed, damaged = l.Damaged,
                visible_seconds = l.VisibleSeconds, position = l.LastPosition, animation = l.LastAnimation }).ToArray(),
        };
        System.IO.File.WriteAllText(System.IO.Path.Combine(SaveService.SaveDirectory, key + "-playthrough.json"),
            System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
