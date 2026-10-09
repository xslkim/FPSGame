using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace FPSGame;

/// <summary>Fixed camera/skill rendering for levels 2-4, complementing normal playthroughs.</summary>
public partial class OtherLevelsVisualQa : Node3D
{
    private string? _shotDir;
    private int _failures;
    private LevelBase? _level;
    private Camera3D _camera = null!;
    private FireSystem _fire = null!;
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Delay(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private void Check(bool ok, string message) { GD.Print($"[OTHER-LEVELS-VISUAL-QA] {(ok ? "PASS" : "FAIL")}: {message}"); if (!ok) _failures++; }

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            if (SaveService.SaveDirectory == "user://") throw new InvalidOperationException("QA requires isolated saves.");
            foreach (string arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--visual-shot-dir:")) _shotDir = arg["--visual-shot-dir:".Length..];
            if (_shotDir != null)
            {
                DirAccess.MakeDirRecursiveAbsolute(_shotDir);
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
            }
            GetTree().Root.Mode = Window.ModeEnum.Windowed;
            GetTree().Root.Size = new Vector2I(1280, 720);
            SaveService.Instance.Coin = 50;
            InputRouter.Instance.SetInputMode(InputRouter.InputMode.Mouse);
            InputRouter.Instance._Notification((int)NotificationApplicationFocusIn);
            await Frames(2);
            foreach (int number in new[] { 2, 3, 4 })
            {
                await Load(number);
                foreach (var marker in _level!.GetNode<Node3D>("CamPositions").GetChildren().OfType<Node3D>())
                {
                    _camera.GlobalTransform = marker.GlobalTransform;
                    await Frames(3);
                    await Shot($"level{number}-{marker.Name}");
                }
                await PanelChecks(number);
                if (number == 2) await LightningChecks();
                if (number == 3) await RockChecks();
                if (number == 4) { await MagmaChecks(); await DragonChecks(); }
            }
            Clear();
            AudioService.Instance.StopAll();
            await Frames(3);
            GD.Print($"[OTHER-LEVELS-VISUAL-QA] {(_failures == 0 ? "ALL PASS" : "FAILED")}");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
        catch (Exception ex) { GD.PushError("[OTHER-LEVELS-VISUAL-QA] " + ex); GetTree().Quit(1); }
    }

    private void Clear()
    {
        Game.Instance.SetPaused(false);
        if (_level == null) return;
        GetTree().CurrentScene = this;
        GetTree().Root.RemoveChild(_level);
        _level.Free();
        _level = null;
    }

    private async Task Load(int number)
    {
        Clear();
        _level = GD.Load<PackedScene>($"res://scenes/levels/level{number}.tscn").Instantiate<LevelBase>();
        GetTree().Root.AddChild(_level);
        GetTree().CurrentScene = _level;
        _level.SetProcess(false);
        _level.SetPhysicsProcess(false);
        _camera = _level.GetNode<Camera3D>("Camera3D");
        _fire = _level.GetNode<FireSystem>("Camera3D/FireSystem");
        foreach (var actor in _level.GetNode<Node3D>("MonsterPool").GetChildren().OfType<Monster>()) actor.Call("Recycle");
        await Delay(2.1);
        _fire.SetProcess(false);
        _fire.Hide();
    }

    private async Task Shot(string name)
    {
        if (_shotDir == null) return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Check(GetViewport().GetTexture().GetImage().SavePng(_shotDir + "/" + name + ".png") == Error.Ok, "render saved: " + name);
    }

    private async Task PanelChecks(int number)
    {
        PlayerState.Instance.PlayerRight.Born();
        PlayerState.Instance.PlayerLeft.Active = false;
        _fire.Show(); _fire.SetProcess(true);
        var panel = _level!.GetNode<InGamePanel>("Camera3D/InGamePanel");
        panel.OpenContinue(false, (int)PlayerState.Side.Right);
        var button = panel.GetNode<UiButton3D>("ContinuePanel/ContinueGameBtn");
        for (int gun = 0; gun < 3; gun++)
        {
            PlayerState.Instance.PlayerRight.GunType = gun;
            _fire.BindPlayers();
            for (int i = 0; i < 6; i++)
            {
                InputRouter.Instance.MouseGun.SimulateMove(_camera.UnprojectPosition(button.GlobalPosition));
                await Frames(1);
            }
            var sides = (System.Collections.Generic.Dictionary<string, object?>)_fire.CaptureDebugState()["sides"]!;
            var right = (System.Collections.Generic.Dictionary<string, object?>)sides["Right"]!;
            Check(((string)right["hit_id"]!).EndsWith("ContinueGameBtn") && right["impact_alignment_error_pixels"] is float error && error < .1f,
                $"level{number} gun{gun}: continue button ray/glow alignment");
            await Shot($"level{number}-continue-gun{gun}");
        }
        panel.CloseAll();
        var mouse = InputRouter.Instance.MouseGun;
        mouse.LeftHeld = false;
        mouse.SimulateMove(new Vector2(1100, 600));
        await Frames(3);
        mouse.LeftHeld = true;
        await Frames(4);
        mouse.SimulateMove(_camera.UnprojectPosition(panel.GetNode<UiButton3D>("PauseBtn").GlobalPosition));
        await Frames(4);
        Check(!Game.Instance.IsGamePause, $"level{number}: sustained fire cannot also press pause");
        mouse.LeftHeld = false; await Frames(2);
        mouse.LeftHeld = true; await Frames(4);
        Check(Game.Instance.IsGamePause && panel.GetNode<Node3D>("PausePanel").Visible, $"level{number}: a fresh pull opens pause");
        await Shot($"level{number}-pause");
        mouse.LeftHeld = false; await Frames(2);
        mouse.SimulateMove(_camera.UnprojectPosition(panel.GetNode<UiButton3D>("PausePanel/BackGameBtn").GlobalPosition));
        await Frames(3);
        mouse.LeftHeld = true; await Frames(4);
        Check(!Game.Instance.IsGamePause, $"level{number}: actual gun resumes pause");
        mouse.LeftHeld = false;
        _fire.SetProcess(false); _fire.Hide();
    }

    private async Task<Monster> Spawn(string type, Vector3 position)
    {
        var actor = GD.Load<PackedScene>($"res://scenes/battle/monsters/{type}.tscn").Instantiate<Monster>();
        _level!.AddChild(actor);
        actor.Born(position, 0, 0);
        actor._PhysicsProcess(0);
        actor.SetPhysicsProcess(false);
        actor.GlobalPosition = position;
        actor.FaceCamera();
        actor.GetNode<AnimationPlayer>("AnimationPlayer").CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
        await Frames(2);
        return actor;
    }

    private async Task LightningChecks()
    {
        var fx = GD.Load<PackedScene>("res://assets/effects/lightning_pillar.tscn").Instantiate<EffectBase>();
        _level!.AddChild(fx);
        fx.GlobalPosition = _camera.GlobalPosition - _camera.GlobalBasis.Z * 4;
        fx.Activate();
        await Delay(.2);
        Check(fx.GetNode<Sprite3D>("Bolt1").Hframes == 8 && fx.GetNode<Sprite3D>("Bolt2").Hframes == 4
            && fx.GetNode<Sprite3D>("Bolt2").Vframes == 8, "lightning samples its 8x8 and 4x8 atlas layouts");
        foreach (string name in new[] { "Bolt1", "Bolt2", "Bolt3" })
        {
            fx.Activate();
            foreach (string other in new[] { "Bolt1", "Bolt2", "Bolt3" }) fx.GetNode<Sprite3D>(other).Visible = other == name;
            await Delay(.2);
            Check(fx.IsVisibleInTree(), name + " remains visible during the lightning interval");
            await Shot("level2-lightning-" + name);
        }
        await Delay(.35);
        Check(!fx.Visible, "lightning finishes after the skill interval");
        fx.QueueFree();
    }

    private async Task RockChecks()
    {
        var root = _camera.GlobalPosition - _camera.GlobalBasis.Z * 25;
        root.Y = 0;
        var actor = (RockWarriorBoss)await Spawn("rock_warrior", root);
        var anim = actor.GetNode<AnimationPlayer>("AnimationPlayer");
        anim.Play("atk01", 0); anim.Advance(0); anim.Advance(.72);
        await Shot("level3-rock-attack");
        await Frames(3);
        Check(actor.LastFireball != null, "rock attack animation creates its fireball");
        if (actor.LastFireball is Fireball fb)
        {
            Check(fb.Scale.IsEqualApprox(Vector3.One * 10), "rock boss preserves its large fireball");
            await Shot("level3-rock-fireball");
            fb.QueueFree();
        }
        actor.QueueFree();
        await Frames(2);
    }

    private async Task MagmaChecks()
    {
        var actor = await Spawn("magma_demon", new Vector3(-26.997597f, 17.57701f, 1.8815998f));
        var center = actor.GetNode<CollisionShape3D>("CollisionShape3D").GlobalPosition;
        var ray = _camera.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(_camera.GlobalPosition, center, FireSystem.RayMask));
        GD.Print($"[OTHER-LEVELS-VISUAL-QA] reported magma center={center} screen={_camera.UnprojectPosition(center)} first={(ray.Count > 0 ? ray["collider"].AsGodotObject() : null)}");
        Check(ray.Count > 0 && ray["collider"].AsGodotObject() is UiButton3D, "reported magma center is covered by the pause button");
        Check(PlaythroughQa.FindVisiblePoint(actor, _camera) != null, "magma remains shootable below the pause button");
        await Shot("level4-magma-reported-position");
        actor.QueueFree(); await Frames(2);
    }

    private async Task DragonChecks()
    {
        foreach (string type in new[] { "dragon_red", "dragon_blue", "dragon_green" })
        {
            var actor = await Spawn(type, _camera.GlobalPosition - _camera.GlobalBasis.Z * 18 - Vector3.Up * 3);
            var breath = (Node3D)actor.FindChild("FireBreath", true, false);
            Check(!breath.Visible, type + " has no premature breath");
            var anim = actor.GetNode<AnimationPlayer>("AnimationPlayer");
            anim.Play("FireBreathOnce", 0, .75f); anim.Advance(.72 / .75);
            Check(breath.Visible, type + " starts breath at the original key");
            await Delay(.3);
            await Shot("level4-" + type + "-breath");
            actor.Hit(1, actor.GlobalPosition, Game.HitType.Body, PlayerState.Side.Right);
            Check(!breath.Visible && breath.GetChildren().OfType<GpuParticles3D>().All(p => !p.Emitting), type + " interruption hides the whole effect");
            await Shot("level4-" + type + "-interrupted");
            actor.QueueFree(); await Frames(2);
        }
    }
}
