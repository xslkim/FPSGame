using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace FPSGame;

/// <summary>Real input → barrel ray → damage/drop integration checks, without direct Hit calls.</summary>
public partial class ShotQa : Node3D
{
    private Camera3D _camera = null!;
    private FireSystem _fire = null!;
    private InGamePanel _panel = null!;
    private int _failed;
    private InputRouter Router => InputRouter.Instance;
    private Player Right => PlayerState.Instance.PlayerRight;

    public override async void _Ready()
    {
        bool reportOnly = Array.IndexOf(OS.GetCmdlineUserArgs(), "--report-selftest") >= 0;
        if (!reportOnly && Array.IndexOf(OS.GetCmdlineUserArgs(), "--shot-selftest") < 0) return;
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            if (SaveService.SaveDirectory == "user://")
                throw new InvalidOperationException("Shot QA requires an isolated save.");
            await Frames(2);
            PrepareArena();
            await Frames(3);
            if (!reportOnly)
            {
                await WeaponChecks();
                await EnemyChecks();
                await DropAndProjectileChecks();
            }
            if (reportOnly || Array.IndexOf(OS.GetCmdlineUserArgs(), "--qa-report") >= 0)
                await ReportCheck();
            AudioService.Instance.StopAll();
            GD.Print($"[SHOT-QA] {(_failed == 0 ? "ALL PASS" : "FAILED")}");
            GetTree().Quit(_failed == 0 ? 0 : 1);
        }
        catch (Exception ex)
        {
            GD.PushError("[SHOT-QA] " + ex);
            GetTree().Quit(1);
        }
    }

    private void PrepareArena()
    {
        GetTree().Root.Size = new Vector2I(1280, 720);
        Game.Instance.SetPaused(false);
        Game.Instance.SceneState = Game.GameState.Battle;
        PlayerState.CurAliveMonster = 0;
        Router.SetInputMode(InputRouter.InputMode.Mouse);
        Router._Notification((int)NotificationApplicationFocusIn);
        Router.FireEnabled = true;
        PlayerState.Instance.UpdateUiMode("Battle");
        PlayerState.Instance.ShowBattleBullets();
        _camera = new Camera3D { Name = "Camera3D", Current = true, Position = new Vector3(0, 2.4f, 0), Near = 0.01f, Far = 2000, Fov = 45 };
        AddChild(_camera);
        var floor = new StaticBody3D { Name = "Ground", CollisionLayer = 1, Position = new Vector3(0, -0.2f, -10) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(40, 0.4f, 40) } });
        AddChild(floor);
        _fire = new FireSystem { Name = "FireSystem" };
        _camera.AddChild(_fire);
        _panel = new InGamePanel { Name = "InGamePanel" };
        _camera.AddChild(_panel);
        _fire.OpenContinue += _panel.OpenContinue;
        _fire.BindPlayers();
    }

    private void Check(bool ok, string description)
    {
        GD.Print($"[SHOT-QA] {(ok ? "PASS" : "FAIL")}: {description}");
        if (!ok) _failed++;
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Settle()
    {
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await Frames(2);
    }

    private GunBase CurrentGun() => _fire.FindChildren("*", "", true, false).OfType<GunBase>()
        .First(g => g.Player == Right && g.Visible);

    private async Task<bool> Pull(Vector2 point, Func<bool> completed, double timeout = 0.8)
    {
        Router.MouseGun.LeftHeld = false;
        Router.MouseGun.SimulateMove(point);
        await Settle();
        CurrentGun().LastFireTime = -99;
        Router.MouseGun.LeftHeld = true;
        double deadline = Time.GetTicksMsec() / 1000.0 + timeout;
        while (!completed() && Time.GetTicksMsec() / 1000.0 < deadline) await Frames(1);
        Router.MouseGun.LeftHeld = false;
        bool result = completed();
        await Frames(2);
        return result;
    }

    private async Task<Monster> Spawn(string type, float hp = 100)
    {
        _panel.CloseAll();
        _camera.Position = new Vector3(0, type == "level2_boss" ? 6 : 2.4f, 0);
        var actor = GD.Load<PackedScene>($"res://scenes/battle/monsters/{type}.tscn").Instantiate<Monster>();
        AddChild(actor);
        actor.ProcessMode = ProcessModeEnum.Pausable;
        actor.Info.Hp = hp;
        actor.Info.LifeActiveTime = 1000;
        Vector3 position = new(0, 0, type is "rock_warrior" or "level2_boss" ? -25 : -8);
        actor.Born(position, 0, 1000);
        // Keep the authored collision/rig pose. Suppress chasing only in this stationary hit test.
        actor.GlobalPosition = position; // dragons normally spawn outside the camera
        actor.SetPhysicsProcess(false);
        actor.FaceCamera();
        actor.GetNode<AnimationPlayer>("AnimationPlayer").Advance(0);
        await Settle();
        return actor;
    }

    private Vector2? VisibleRegionPoint(MonsterHitRegion region)
    {
        var center = _camera.UnprojectPosition(region.GlobalPosition);
        float radius = ((SphereShape3D)region.GetNode<CollisionShape3D>("HitShape").Shape).Radius
            * region.GlobalBasis.Scale.X;
        float pixels = center.DistanceTo(_camera.UnprojectPosition(region.GlobalPosition + _camera.GlobalBasis.X * radius));
        var offsets = Enumerable.Range(-5, 11).SelectMany(x => Enumerable.Range(-5, 11).Select(y => new Vector2(x, y) / 5f))
            .Where(v => v.LengthSquared() < 0.9f).OrderBy(v => v.LengthSquared());
        foreach (var offset in offsets)
        {
            Vector2 screen = center + offset * pixels;
            var from = _camera.ProjectRayOrigin(screen);
            var ray = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from,
                from + _camera.ProjectRayNormal(screen) * 200, FireSystem.RayMask));
            if (ray.Count > 0 && ray["collider"].AsGodotObject() == region) return screen;
        }
        return null;
    }

    private async Task Remove(Monster actor)
    {
        actor.QueueFree();
        await Settle();
    }

    private async Task WeaponChecks()
    {
        // Aim into empty sky: a trigger must still produce a shot and consume ammo.
        for (int i = 0; i < 3; i++)
        {
            var gun = CurrentGun();
            int before = Right.Bullet;
            bool fired = await Pull(new Vector2(1150, 70), () => Right.Bullet < before);
            Check(fired && Right.Bullet == before - gun.BulletCost, $"{gun.GunName} fires once into empty space");
            int releasedAmmo = Right.Bullet;
            await ToSignal(GetTree().CreateTimer(0.55), SceneTreeTimer.SignalName.Timeout);
            Check(Right.Bullet == releasedAmmo, $"{gun.GunName} release stops firing");
            Router.MouseGun.SimulateSwitch();
            await ToSignal(GetTree().CreateTimer(0.55), SceneTreeTimer.SignalName.Timeout);
        }
    }

    private async Task ExposeBossWeakPoint(Level2Boss boss)
    {
        // Original Skill1 rotates the actor upward and exposes a small part of the
        // sphere during its follow-through. Idle armour can cover the entire core.
        boss.FaceCamera();
        var t = boss.Transform;
        t.Basis *= Basis.FromEuler(new Vector3(Mathf.DegToRad(20), 0, 0));
        boss.Transform = t;
        var animation = boss.GetNode<AnimationPlayer>("AnimationPlayer");
        animation.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
        animation.Play("Skill1", 0);
        animation.Seek(.3, true);
        await Settle();
    }

    private async Task EnemyChecks()
    {
        string[] types = { "bull", "axe_zombie", "fly_axe_zombie", "skeleton", "fat_zombie", "wolf", "wolf_blue", "wolf_green",
            "magma_demon", "magma_demon_orange", "magma_demon_purple", "magma_demon_green", "toon", "toon_alien", "level2_boss", "rock_warrior",
            "dragon_red", "dragon_blue", "dragon_green", "baotou" };
        foreach (string type in types)
        {
            var actor = await Spawn(type);
            Node3D target = actor;
            Vector3 point = actor.GetNode<CollisionShape3D>("CollisionShape3D").GlobalPosition;
            if (actor is BaotouMonster)
            {
                target = (Node3D)actor.FindChild("BossHeart", true, false);
                point = target.GlobalPosition;
            }
            Vector2? bossTarget = null;
            if (actor is Level2Boss boss)
            {
                int armourAmmo = Right.Bullet;
                float armourHp = boss.Hp;
                bool fired = await Pull(_camera.UnprojectPosition(boss.HeadArmour.GlobalPosition), () => Right.Bullet < armourAmmo);
                Check(fired && boss.Hp == armourHp, "Level2 boss head armour blocks damage but consumes the shot");
                await ExposeBossWeakPoint(boss);
                point = boss.WeakSpot.GlobalPosition;
                // Original overlapping armour covers part of the sphere; aim at its exposed surface.
                bossTarget = VisibleRegionPoint(boss.WeakSpot);
                Check(bossTarget.HasValue, "Level2 boss chest weak point is shootable during the original attack pose");
            }
            float beforeHp = actor.Hp;
            int beforeAmmo = Right.Bullet;
            bool damaged = await Pull(bossTarget ?? _camera.UnprojectPosition(point), () => actor.Hp < beforeHp);
            Check(damaged && Right.Bullet < beforeAmmo, $"{type} actual barrel shot damages its valid target");
            if (!damaged) GD.Print("[SHOT-QA] RAY " + System.Text.Json.JsonSerializer.Serialize(_fire.CaptureDebugState()));
            if (actor is Level2Boss hurtBoss)
            {
                Check(damaged && hurtBoss.LastAttackTime > Time.GetTicksMsec() / 1000.0 + 2.7,
                    "Level2 boss weak-point damage resets attack cooldown to now + 3 seconds");
                float hurtHp = hurtBoss.Hp;
                int hurtAmmo = Right.Bullet;
                var targetPoint = VisibleRegionPoint(hurtBoss.WeakSpot);
                bool shot = await Pull(targetPoint ?? _camera.UnprojectPosition(hurtBoss.WeakSpot.GlobalPosition), () => Right.Bullet < hurtAmmo);
                Check(damaged && shot && hurtBoss.Hp == hurtHp, "Level2 boss damage animation prevents duplicate damage");
                await ExposeBossWeakPoint(hurtBoss);
                hurtBoss.Hp = 1;
                await Settle();
                var exposed = VisibleRegionPoint(hurtBoss.WeakSpot);
                bool killed = await Pull(exposed ?? _camera.UnprojectPosition(hurtBoss.WeakSpot.GlobalPosition), () => hurtBoss.IsDead);
                var regions = hurtBoss.FindChildren("*", "", true, false).OfType<MonsterHitRegion>().ToArray();
                Check(killed && regions.Length == 17 && regions.All(r => r.CollisionLayer == 0),
                    "Level2 boss dies from the real weak-point shot and disables all 17 bone regions");
            }
            if (actor is ToonMonster toon)
            {
                // Let the real bone-following head collider update, then freeze movement again.
                actor.SetPhysicsProcess(true);
                await Settle();
                actor.SetPhysicsProcess(false);
                beforeHp = actor.Hp;
                bool head = await Pull(_camera.UnprojectPosition(toon.HeadHitPosition + Vector3.Up * 0.18f), () => actor.Hp < beforeHp);
                Check(head, $"{type} upper head receives damage through the same barrel ray");
            }
            await Remove(actor);
        }
    }

    private async Task DropAndProjectileChecks()
    {
        foreach (var kind in new[] { BoxMonster.BoxKind.Bullet, BoxMonster.BoxKind.GunAK, BoxMonster.BoxKind.GunM4 })
        {
            var box = (BoxMonster)await Spawn("box_monster", 1);
            box.Kind = kind;
            int before = Right.Bullet;
            Right.Guns = 1 << 2;
            var point = box.GetNode<CollisionShape3D>("CollisionShape3D").GlobalPosition;
            bool destroyed = await Pull(_camera.UnprojectPosition(point), () => box.IsDead);
            Check(destroyed, $"{kind} supply can be shot through its real collision");
            Check(kind == BoxMonster.BoxKind.Bullet ? Right.Bullet == before - CurrentGun().BulletCost + SaveService.Instance.BoxBullet
                : (Right.Guns & (1 << (kind == BoxMonster.BoxKind.GunAK ? 0 : 1))) != 0, $"{kind} shot awards its configured supply");
            await Remove(box);
        }
        Right.Guns = 7;
        ProjectileAxe.Spawn(this, null, new Vector3(0, 2.4f, -8), Vector3.Back, 10, Game.AttackType.Phy);
        var axe = GetChildren().OfType<ProjectileAxe>().Last();
        axe.SetProcess(false);
        await Settle();
        bool intercepted = await Pull(_camera.UnprojectPosition(axe.GlobalPosition), () => !IsInstanceValid(axe) || axe.IsQueuedForDeletion());
        Check(intercepted, "actual shot intercepts a flying axe");
        Right.Bullet = 0;
        bool prompt = await Pull(new Vector2(1150, 70), () => Game.Instance.IsGamePause);
        Check(prompt && _panel.GetNode<Node3D>("ContinuePanel").Visible, "empty gun opens ammo exchange even when aiming at the sky");
        _panel.CloseAll();
    }

    private async Task ReportCheck()
    {
        var overlay = GetTree().Root.GetNode<DebugOverlay>("DebugOverlay");
        string directory = overlay.GetReportDirectory();
        string[] before = System.IO.Directory.Exists(directory)
            ? System.IO.Directory.GetFiles(directory, "*.json") : Array.Empty<string>();
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.F5, Pressed = true });
        await Frames(12);
        string[] files = System.IO.Directory.Exists(directory)
            ? System.IO.Directory.GetFiles(directory, "*.json").Except(before).ToArray() : Array.Empty<string>();
        Check(files.Length == 1, "F5 writes one isolated report from the running executable");
        if (files.Length != 1) return;
        using var report = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(files[0]));
        var build = report.RootElement.GetProperty("build");
        Check(build.GetProperty("id").GetString() == BuildIdentity.Id
            && build.GetProperty("executable").GetString() == OS.GetExecutablePath()
            && build.GetProperty("compiled_utc").GetString() == BuildIdentity.Utc,
            "report identifies the actual compiled build and executable path");
        if (DisplayServer.GetName() != "headless")
        {
            string png = System.IO.Path.ChangeExtension(files[0], ".png");
            Check(System.IO.File.Exists(png), "graphical F5 report includes its same-frame PNG");
            if (System.IO.File.Exists(png))
            {
                using var picture = Image.LoadFromFile(png);
                Check(picture.GetWidth() == 1280 && picture.GetHeight() == 720, "F5 PNG uses the actual 1280x720 viewport");
            }
        }
    }
}
