using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace FPSGame;

/// <summary>Reproduce the October 8 reports at fixed cameras/intro times, with optional rendered evidence.</summary>
public partial class ReportedVisualQa : Node3D
{
    private int _failures;
    private string? _shotDir;
    private Node? _scene;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            if (SaveService.SaveDirectory == "user://")
                throw new InvalidOperationException("Visual QA requires an isolated save.");
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--visual-shot-dir:")) _shotDir = arg["--visual-shot-dir:".Length..];
            if (_shotDir != null) DirAccess.MakeDirRecursiveAbsolute(_shotDir);
            await Resize(new Vector2I(1280, 720));
            await MenuChecks();
            await LevelChecks();
            ClearScene();
            AudioService.Instance.StopAll();
            await Frames(2);
            GD.Print($"[REPORTED-VISUAL-QA] {(_failures == 0 ? "ALL PASS" : "FAILED")}");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
        catch (Exception ex)
        {
            GD.PushError("[REPORTED-VISUAL-QA] " + ex);
            GetTree().Quit(1);
        }
    }

    private void Check(bool ok, string message)
    {
        GD.Print($"[REPORTED-VISUAL-QA] {(ok ? "PASS" : "FAIL")}: {message}");
        if (!ok) _failures++;
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Resize(Vector2I size)
    {
        if (_shotDir != null)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(size);
        }
        else GetTree().Root.Size = size;
        await Frames(3);
    }

    private T LoadScene<T>(string path) where T : Node
    {
        ClearScene();
        var scene = GD.Load<PackedScene>(path).Instantiate<T>();
        GetTree().Root.AddChild(scene);
        GetTree().CurrentScene = scene;
        _scene = scene;
        return scene;
    }

    private void ClearScene()
    {
        Game.Instance.SetPaused(false);
        if (_scene == null) return;
        GetTree().CurrentScene = this;
        GetTree().Root.RemoveChild(_scene);
        _scene.Free();
        _scene = null;
    }

    private async Task Shot(string name)
    {
        if (_shotDir == null) return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = _shotDir + "/" + name + ".png";
        Check(GetViewport().GetTexture().GetImage().SavePng(path) == Error.Ok, "render saved: " + path);
    }

    private async Task MenuChecks()
    {
        LoadScene<MenuScreen>("res://scenes/ui/menu.tscn");
        SaveService.Instance.Coin = 60;
        SaveService.Instance.MaxCoin = 60;
        foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080), new Vector2I(1280, 960) })
        {
            await Resize(size);
            foreach (int count in new[] { 0, 9, 60, 999 })
            {
                SaveService.Instance.Coin = count;
                PlayerState.Instance.RefreshHud();
                await Frames(3);
                var row = PlayerState.Instance.CoinObj;
                var icon = row.GetNode<Control>("CoinIcon").GetGlobalRect();
                var x = row.GetNode<Control>("X").GetGlobalRect();
                var number = row.GetNode<Control>("CoinText").GetGlobalRect();
                Check(row.GetNode<Label>("CoinText").Text == count.ToString(), "coin text displays " + count);
                var bounds = GetViewport().GetVisibleRect();
                Check(icon.End.X <= x.Position.X && x.End.X <= number.Position.X
                    && number.End.Y <= bounds.End.Y - 4 && icon.End.Y <= bounds.End.Y - 4,
                    $"coin {count} fits without overlap/clipping at {size}");
                Check(Mathf.Abs(icon.GetCenter().Y - number.GetCenter().Y) < 0.5f,
                    $"coin icon/text share vertical center at {size}");
                if (count == 60) await Shot($"menu-{size.X}x{size.Y}");
            }
        }
        SaveService.Instance.Coin = 60;
        await Resize(new Vector2I(1280, 720));
    }

    private async Task LevelChecks()
    {
        Game.Instance.IsDebug = true; // skip the real-time intro timers; evaluate its fixed times below
        var level = LoadScene<Level1>("res://scenes/levels/level1_battle.tscn");
        level.SetProcess(false);
        level.SetPhysicsProcess(false);
        var camera = level.GetNode<Camera3D>("Camera3D");
        var intro = level.GetNode<IntroBadGroup>("BadGroup");
        intro.SetProcess(false);
        level.GetNode<Node3D>("Environment").Show();
        await Frames(3);
        await ToSignal(GetTree().CreateTimer(2.1), SceneTreeTimer.SignalName.Timeout);
        level.GetNode<FireSystem>("Camera3D/FireSystem").SetProcess(false);
        level.GetNode<FireSystem>("Camera3D/FireSystem").Hide();
        foreach (var node in level.GetNode<Node3D>("Environment").FindChildren("*", "Light3D", true, false))
            if (node is Light3D light && light.LightEnergy > 0.5f) light.LightEnergy = 0.2f;
        intro.Play(camera);
        PlayerState.Instance.UpdateUiMode("Level1Story");
        foreach (double time in new[] { 5.720701, 8.0, 10.0, 15.0 })
        {
            intro.Seek(time);
            await Frames(2); // allow BoneAttachment3D to follow the evaluated skeleton
            intro.Seek(time);
            var blade = intro.GetNode<Node3D>("SoldierBad/Model/Skeleton3D/Carry_Bip001 L UpperArm/Carried");
            var sk = blade.FindChild("Skeleton3D", true, false) as Skeleton3D;
            Vector3 Bone(string name) => (sk!.GlobalTransform * sk.GetBoneGlobalPose(sk.FindBone(name))).Origin;
            var bodyAxis = (Bone("Bip001 Head") - Bone("Bip001 Pelvis")).Normalized();
            Check(Mathf.Abs(bodyAxis.Y) < 0.5f, $"girl reclines in carrier's arms at intro t={time}: axis={bodyAxis}");
            Check(blade.GetParent() is BoneAttachment3D, "girl follows the animated carrier bone");
            await Shot("intro-" + time.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        }
        intro.Stop();
        Game.Instance.SceneState = Game.GameState.Battle;
        InputRouter.Instance.SetInputMode(InputRouter.InputMode.Mouse);
        InputRouter.Instance._Notification((int)NotificationApplicationFocusIn);
        InputRouter.Instance.FireEnabled = true;
        PlayerState.Instance.PlayerRight.Born();
        PlayerState.Instance.PlayerLeft.Active = false;
        PlayerState.Instance.UpdateUiMode("Battle");
        PlayerState.Instance.ShowBattleBullets();
        var bulletText = PlayerState.Instance.FindChild("BulletRightText", true, false) as Label;
        Check(bulletText != null && bulletText.GetGlobalRect().End.Y <= GetViewport().GetVisibleRect().End.Y - 4,
            "bullet count also fits above the screen edge");
        var fire = level.GetNode<FireSystem>("Camera3D/FireSystem");
        fire.ProcessMode = ProcessModeEnum.Always;
        fire.SetProcess(true);
        fire.Show();
        var panel = level.GetNode<InGamePanel>("Camera3D/InGamePanel");
        camera.GlobalPosition = new Vector3(0.7353256f, 1, -10.27063f);
        camera.RotationDegrees = new Vector3(5.29308f, 121.215034f, -0.00034525f);
        camera.Fov = 45;
        for (int type = 0; type < 3; type++)
        {
            PlayerState.Instance.PlayerRight.GunType = type;
            fire.BindPlayers();
            InputRouter.Instance.MouseGun.SimulateMove(new Vector2(442.5f, 390.5f));
            await Frames(4);
            CheckBeam(fire, camera, "wall, gun " + type);
            await Shot("laser-wall-gun" + type);
            if (type == 2)
            {
                var gun = fire.FindChildren("*", "", true, false).OfType<GunBase>().First(g => g.Visible);
                gun.Fire(); // verify a beam/glow from the final recoil pose
                await Frames(3);
                CheckBeam(fire, camera, "wall during recoil");
                await Shot("laser-wall-recoil");
            }
            panel.OpenContinue(false, (int)PlayerState.Side.Right);
            var button = panel.GetNode<UiButton3D>("ContinuePanel/ContinueGameBtn");
            foreach (float localX in new[] { -60f, 0f, 60f })
            {
                InputRouter.Instance.MouseGun.SimulateMove(camera.UnprojectPosition(button.GlobalTransform * new Vector3(localX, 0, 0)));
                await Frames(4);
                CheckBeam(fire, camera, $"continue button x={localX}, gun {type}");
                if (localX == 0) await Shot("laser-continue-gun" + type);
            }
            panel.CloseAll();
        }
        fire.SetProcess(false);
        fire.Hide();
        panel.Hide();
        camera.GlobalPosition = new Vector3(0.08512878f, 1.051493f, -3);
        camera.RotationDegrees = new Vector3(0, 180, 0);
        var box = GD.Load<PackedScene>("res://scenes/battle/monsters/box_monster.tscn").Instantiate<BoxMonster>();
        level.AddChild(box);
        var boxPosition = new Vector3(0.92657673f, 0.00012569f, 4.9556246f);
        foreach (var kind in new[] { BoxMonster.BoxKind.Bullet, BoxMonster.BoxKind.GunAK, BoxMonster.BoxKind.Bullet })
        {
            box.Kind = kind;
            box.Born(boxPosition, 0, 20);
            box.ProcessMode = ProcessModeEnum.Disabled;
            bool bullet = kind == BoxMonster.BoxKind.Bullet;
            Check(box.GetNode<Node3D>("AmmoCrate").Visible == bullet && box.BodyNode.Visible != bullet,
                $"pooled box {kind} selects correct model");
            await Frames(2);
            box.Call("Recycle");
        }
        box.Kind = BoxMonster.BoxKind.Bullet;
        box.Born(boxPosition, 0, 20);
        box.ProcessMode = ProcessModeEnum.Disabled;
        await Frames(2);
        await Shot("ammo-crate-report-camera");
        camera.GlobalPosition = boxPosition + new Vector3(-0.8f, 0.75f, -1.5f);
        camera.LookAt(boxPosition + new Vector3(0, 0.2f, 0));
        await Shot("ammo-crate-detail");
        box.Call("Recycle");
        camera.GlobalPosition = new Vector3(0.08512962f, 1.051493f, 60);
        camera.RotationDegrees = new Vector3(0, 180, 0);
        var boss = GD.Load<PackedScene>("res://scenes/battle/monsters/baotou.tscn").Instantiate<BaotouMonster>();
        level.AddChild(boss);
        boss.Born(new Vector3(-0.70559096f, 0.10166716f, 66.75477f), 0, 1000);
        boss.ProcessMode = ProcessModeEnum.Disabled;
        await Frames(3);
        boss.GetNode<AnimationPlayer>("AnimationPlayer").Advance(0.167677);
        var fx = GD.Load<PackedScene>("res://assets/effects/teleport_flash.tscn").Instantiate<EffectBase>();
        level.AddChild(fx);
        fx.ProcessMode = ProcessModeEnum.Pausable;
        fx.GlobalPosition = boss.GlobalPosition + Vector3.Up;
        fx.Activate();
        var core = fx.GetNode<Sprite3D>("Core");
        Check(core.Hframes == 8 && core.Vframes == 8 && core.Frame == 0,
            "boss teleport samples one frame of 8x8 atlas");
        Check(((StandardMaterial3D)core.MaterialOverride).DisableFog, "black atlas background receives no fog");
        var flares = fx.GetNode<GpuParticles3D>("Wisps");
        Check(((StandardMaterial3D)((QuadMesh)flares.DrawPass1).Material).AlbedoTexture.ResourcePath.EndsWith("teleport_flare.png"),
            "teleport flare uses the original Flare6 texture");
        await Shot("boss-teleport-start");
        await ToSignal(GetTree().CreateTimer(0.12), SceneTreeTimer.SignalName.Timeout);
        Check(core.Frame > 0, "boss teleport advances atlas frames");
        await Shot("boss-teleport-mid");
        await ToSignal(GetTree().CreateTimer(0.40), SceneTreeTimer.SignalName.Timeout);
        Check(!IsInstanceValid(fx), "boss teleport is removed after finishing");
        await Shot("boss-teleport-finished");
    }

    private void CheckBeam(FireSystem fire, Camera3D camera, string context)
    {
        var laser = fire.FindChildren("*", "", true, false).OfType<LaserSight>().First(l => l.Visible);
        var flash = fire.GetNode<MeshInstance3D>("FlashRight");
        Check(flash.Visible && camera.UnprojectPosition(laser.EndPoint).DistanceTo(camera.UnprojectPosition(flash.GlobalPosition)) < 0.1f,
            "rendered beam endpoint/glow share screen center: " + context);
        var rays = (System.Collections.Generic.Dictionary<string, object?>)fire.CaptureDebugState()["sides"]!;
        var right = (System.Collections.Generic.Dictionary<string, object?>)rays["Right"]!;
        Check(right["impact_alignment_error_pixels"] is float error && error < 0.1f, "final frame hit/glow alignment: " + context);
        Check(laser.GlobalPosition.DistanceTo(laser.EndPoint) < 30, "beam stops at hit surface: " + context);
        if (context.StartsWith("continue"))
            Check(((string)right["hit_id"]!).EndsWith("ContinueGameBtn"), "actual ray hits continue button: " + context
                + " hit=" + right["hit_id"] + " end=" + camera.UnprojectPosition(laser.EndPoint)
                + " requested=" + InputRouter.Instance.GetRightAim().ScreenPos);
    }
}
