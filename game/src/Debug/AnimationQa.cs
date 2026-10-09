using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace FPSGame;

/// <summary>Runs real AnimationPlayer method keys and observes health/projectiles, not synthetic callbacks.</summary>
public partial class AnimationQa : Node3D
{
    private int _failed;
    private Player Right => PlayerState.Instance.PlayerRight;
    public override async void _Ready()
    {
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--animation-selftest") < 0) return;
        try
        {
            if (SaveService.SaveDirectory == "user://") throw new InvalidOperationException("Animation QA needs an isolated save.");
            await Frames(2);
            Game.Instance.SceneState = Game.GameState.Battle;
            Game.Instance.SetPaused(false);
            InputRouter.Instance.SetInputMode(InputRouter.InputMode.Mouse);
            AddChild(new Camera3D { Current = true, Position = new Vector3(0, 2, 0) });
            var ground = new StaticBody3D { Position = new Vector3(0, -.2f, -10), CollisionLayer = 1 };
            ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(100, .4f, 100) } });
            AddChild(ground);
            await Melee("bull", "attack_01", new[] { .76923084 });
            await Melee("bull", "attack_02", new[] { .59818923 });
            await Melee("bull", "attack_03", new[] { .62685847 });
            await Melee("axe_zombie", "Attack1", new[] { .44680333 });
            await Melee("axe_zombie", "Attack2", new[] { .53805929, .94814038 });
            await Melee("wolf", "Attack1", new[] { .34173858, .71545720 });
            await Melee("wolf", "Attack2", new[] { .32268447, .64677477, 1.10106611 });
            await Melee("fat_zombie", "Attack2", new[] { .47258976, .86956519 });
            await Melee("skeleton", "Attack2", new[] { .53953493 });
            await Melee("wolf_blue", "BiteAttack", new[] { .73706377 });
            await Melee("magma_demon", "attack01", new[] { .67962575 });
            await InterruptChecks();
            await ToonChecks("toon", 0);
            await ToonChecks("toon", 2);
            await ToonChecks("toon_alien", 1);
            await AxeChecks();
            foreach (var type in new[] { "dragon_red", "dragon_blue", "dragon_green" }) await DragonChecks(type);
            await BossChecks();
            AudioService.Instance.StopAll();
            await Frames(3);
            GD.Print($"[ANIMATION-QA] {(_failed == 0 ? "ALL PASS" : "FAILED")}");
            GetTree().Quit(_failed == 0 ? 0 : 1);
        }
        catch (Exception ex) { GD.PushError("[ANIMATION-QA] " + ex); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void Check(bool ok, string label) { GD.Print($"[ANIMATION-QA] {(ok ? "PASS" : "FAIL")}: {label}"); if (!ok) _failed++; }

    private async Task<Monster> Spawn(string type, int level = 0)
    {
        Right.Born(); Right.Hp = 1000;
        var actor = GD.Load<PackedScene>($"res://scenes/battle/monsters/{type}.tscn").Instantiate<Monster>();
        AddChild(actor);
        actor.Born(new Vector3(0, 0, -12), level, 0);
        actor._PhysicsProcess(0); // enter Active without moving the attack rig
        actor.SetPhysicsProcess(false);
        var anim = actor.GetNode<AnimationPlayer>("AnimationPlayer");
        anim.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
        await Frames(1);
        return actor;
    }
    private AnimationPlayer Animation(Monster actor) => actor.GetNode<AnimationPlayer>("AnimationPlayer");
    private async Task Remove(Monster actor) { actor.QueueFree(); await Frames(2); }

    private async Task Melee(string type, string clip, double[] times)
    {
        var actor = await Spawn(type);
        var anim = Animation(actor);
        anim.SpeedScale = 1;
        anim.Play(clip, 0); anim.Advance(0);
        double position = 0;
        for (int i = 0; i < times.Length; i++)
        {
            anim.Advance(times[i] - .005 - position);
            Check(actor.CombatEventCount == i && Mathf.IsEqualApprox(Right.Hp, 1000 - i * actor.GetAttack()), $"{type}/{clip} does not hit before Unity key {i + 1}");
            anim.Advance(.01); position = times[i] + .005;
            Check(actor.CombatEventCount == i + 1 && Mathf.IsEqualApprox(Right.Hp, 1000 - (i + 1) * actor.GetAttack()), $"{type}/{clip} key {i + 1} damages exactly once");
        }
        anim.Advance(2);
        Check(actor.CombatEventCount == times.Length, $"{type}/{clip} has no extra fixed timer hit");
        await Remove(actor);
    }

    private async Task InterruptChecks()
    {
        var actor = await Spawn("axe_zombie");
        var anim = Animation(actor);
        anim.Play("Attack2", 0); anim.Advance(.2);
        actor.Hit(1, actor.GlobalPosition, Game.HitType.Body, PlayerState.Side.Right);
        anim.Advance(2);
        Check(actor.CombatEventCount == 0 && Right.Hp == 1000, "hurt cancels pending attack and blended-out keys");
        actor.Born(new Vector3(0, 0, -12), 0, 0); actor._PhysicsProcess(0); actor.SetPhysicsProcess(false);
        anim.Play("Attack1", 0); anim.Advance(0);
        anim.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Idle;
        Game.Instance.SetPaused(true);
        double before = anim.CurrentAnimationPosition;
        await Frames(20);
        Check(actor.CombatEventCount == 0 && anim.CurrentAnimationPosition == before, "pause freezes the pose and attack keys");
        Game.Instance.SetPaused(false);
        anim.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
        anim.Advance(.46);
        Check(actor.CombatEventCount == 1 && Right.Hp < 1000, "resume completes the original attack once");
        await Remove(actor);
    }

    private async Task ToonChecks(string type, int level)
    {
        var actor = await Spawn(type, level);
        var anim = Animation(actor);
        double rate = anim.SpeedScale;
        anim.Play("reload", 0); anim.Advance(0);
        Check(anim.GetAnimation("reload").GetTrackCount() >= 40 && anim.GetAnimation("shoot").GetTrackCount() >= 40, $"{type} uses a complete baked rig for reload and shoot");
        anim.Advance(3.40 / rate);
        Check(actor.CombatEventCount == 0 && anim.CurrentAnimation == "reload", $"{type} Lv{level}: reload never fires a fabricated bullet");
        anim.Advance(.03 / rate);
        anim.Advance(0);
        Check(anim.CurrentAnimation == "shoot", $"{type} Lv{level}: reload exits at Unity controller time");
        int bullets = GetChildren().OfType<ToonBullet>().Count();
        anim.Advance(.23 / rate);
        Check(GetChildren().OfType<ToonBullet>().Count() == bullets, $"{type}: no projectile before shoot key");
        anim.Advance(.02 / rate);
        Check(GetChildren().OfType<ToonBullet>().Count() == bullets + 1, $"{type}: shoot key emits one bullet at the scaled animation time");
        double deadline = Time.GetTicksMsec() / 1000.0 + 1.2;
        while (Right.Hp == 1000 && Time.GetTicksMsec() / 1000.0 < deadline) await Frames(1);
        Check(Right.Hp < 1000, $"{type}: fired bullet settles health damage");
        await Remove(actor);
    }

    private async Task AxeChecks()
    {
        var actor = await Spawn("fly_axe_zombie");
        var anim = Animation(actor);
        int axes = GetChildren().OfType<ProjectileAxe>().Count();
        anim.Play("Skill", 0); anim.Advance(.99);
        Check(actor.CombatEventCount == 1 && GetChildren().OfType<ProjectileAxe>().Count() == axes, "fly axe is held until Unity's actual 1.000629s throw key");
        anim.Advance(.02);
        Check(actor.CombatEventCount == 2 && GetChildren().OfType<ProjectileAxe>().Count() == axes + 1, "fly axe emits one real shootable projectile");
        actor.Born(new Vector3(0, 0, -12), 0, 10);
        actor.SetPhysicsProcess(false); anim.Advance(2);
        Check(actor.CombatEventCount == 0, "recycled axe actor cannot execute an old life's throw");
        await Remove(actor);
    }

    private async Task DragonChecks(string type)
    {
        var actor = await Spawn(type);
        var anim = Animation(actor);
        var breath = actor.FindChild("FireBreath", true, false) as Node3D;
        var particles = breath!.GetChildren().OfType<GpuParticles3D>().ToArray();
        Check(!breath.Visible && particles.All(p => !p.Emitting), $"{type}: birth has no flame, smoke or light");
        var material = (StandardMaterial3D)((QuadMesh)breath.GetNode<GpuParticles3D>("Flames").DrawPass1).Material;
        bool fire = type == "dragon_red";
        Check(material.ParticlesAnimHFrames == (fire ? 4 : 1) && material.ParticlesAnimVFrames == (fire ? 2 : 1)
            && material.AlbedoTexture.ResourcePath.Contains("dragon_breath_"), $"{type}: original texture with matching frame layout");
        anim.Play("FireBreathOnce", 0, .75f); anim.Advance(0);
        anim.Advance(.70 / .75);
        Check(actor.CombatEventCount == 0 && Right.Hp == 1000, $"{type}: breath waits for the original visual key");
        anim.Advance(.02 / .75);
        Check(actor.CombatEventCount == 1, $"{type}: StartFire is restored");
        Check(breath.Visible && particles.All(p => p.Emitting), $"{type}: visual key starts the complete effect");
        anim.Advance(.64 / .75);
        Check(actor.CombatEventCount == 2 && Right.Hp < 1000, $"{type}: original breath key damages the player");
        actor.Hit(1, actor.GlobalPosition, Game.HitType.Body, PlayerState.Side.Right);
        Check(!breath.Visible && particles.All(p => !p.Emitting), $"{type}: hit interrupts flame, smoke and light");
        actor.Born(new Vector3(0, 0, -12), 0, 0);
        Check(!breath.Visible && particles.All(p => !p.Emitting), $"{type}: reuse keeps the effect off until its next key");
        await Remove(actor);
    }

    private async Task BossChecks()
    {
        var actor = (Level2Boss)await Spawn("level2_boss");
        var anim = Animation(actor);
        Check(anim.GetAnimation("Skill1").GetTrackCount() >= 110 && Math.Abs(anim.GetAnimation("Skill1").Length - 1.0 / 3) < .001, "Level2 boss uses complete Unity humanoid poses and original skill duration");
        foreach (double rate in new[] { 1.0, 2.25, 4.0 })
        {
            Right.Hp = 1000;
            actor.SetDifficultyAnimRate(rate);
            anim.Stop();
            anim.SpeedScale = .3f * (float)rate;
            anim.Play("Skill1", 0); anim.Advance(0);
            double wall = 0; int hits = actor.StatSkillCount;
            while (actor.StatSkillCount == hits && wall < 4 / rate)
            {
                anim.Advance(.001); wall += .001;
            }
            Check(Math.Abs(wall * rate - 2.22222) < .015 && Right.Hp < 1000, $"Level2 boss rate {rate}: slow wind-up and actual hit key ({wall:0.000}s)");
        }
        await Remove(actor);
    }
}
