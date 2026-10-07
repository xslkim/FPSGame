using System;
using System.Threading.Tasks;
using Godot;

namespace FPSGame;

/// <summary>Exercises real UDP input, scene transitions and arcade controls with an isolated save.</summary>
public partial class RuntimeQa : Node
{
    private int _failures;
    private InputRouter Router => InputRouter.Instance;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            if (SaveService.SaveDirectory == "user://")
                throw new InvalidOperationException("Runtime QA requires --qa-save-dir; player saves must remain isolated.");
            await Frames(2);
            await Wait(() => GetTree().CurrentScene?.SceneFilePath == "res://scenes/debug/runtime_qa.tscn", 10, "QA start scene");
            await InputChecks();
            await SessionChecks();
            SaveChecks();
            AudioService.Instance.StopAll();
            await Frames(3);
            GD.Print($"[RUNTIME-QA] {(_failures == 0 ? "ALL PASS" : "FAILED")}");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
        catch (Exception ex)
        {
            GD.PushError("[RUNTIME-QA] " + ex);
            GetTree().Quit(1);
        }
    }

    private void Check(bool ok, string label)
    {
        GD.Print($"[RUNTIME-QA] {(ok ? "PASS" : "FAIL")}: {label}");
        if (!ok) _failures++;
    }

    private void SaveChecks()
    {
        var save = SaveService.Instance;
        string path = ProjectSettings.GlobalizePath(SaveService.SavePath);
        save.Coin = 17;
        save.SaveUserData();
        save.SaveUserData(); // backup now contains the same known valid state
        System.IO.File.WriteAllText(path, "{truncated");
        save.Coin = 0;
        save.LoadUserData();
        Check(save.Coin == 17, "damaged save recovers the last valid backup");
        System.IO.File.WriteAllText(path,
            "{\"coin\":-2,\"max_coin\":-1,\"add_coin_time\":0,\"max_bullet\":0,\"box_bullet\":-5,"
            + "\"level_state\":[null,{\"star\":2}],\"last_add_coin_time\":1}");
        save.LoadUserData();
        Check(save.Coin == 0 && save.MaxCoin >= 1 && save.AddCoinTime >= 1 && save.MaxBullet >= 1 && save.BoxBullet >= 1,
            "invalid save values cannot create negative credits or zero timers");
        Check(save.LevelState.Count == SaveService.LevelCount && SaveService.GetInt(save.LevelState[1], "star", 0) == 2,
            "damaged level entry preserves the following level's index");
        save.TickCoinRegen();
        Check(save.Coin == save.MaxCoin, "long offline credit interval resolves without a frame-blocking loop");
        int coins = save.Coin;
        Check(!save.SpendCoin(-1) && save.Coin == coins, "negative spending cannot mint credits");
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Wait(Func<bool> condition, double seconds, string label)
    {
        double deadline = Time.GetTicksMsec() / 1000.0 + seconds;
        while (!condition() && Time.GetTicksMsec() / 1000.0 < deadline) await Frames(1);
        if (!condition()) throw new InvalidOperationException("Timeout: " + label);
    }

    private static byte[] Packet(char side, bool fire = false, bool swap = false, bool legacy = false)
    {
        var data = new byte[legacy ? 22 : 23];
        data[0] = (byte)side;
        BitConverter.GetBytes(1f).CopyTo(data, 13); // unit quaternion (0,0,0,1)
        BitConverter.GetBytes(1234).CopyTo(data, 17);
        data[21] = fire ? (byte)1 : (byte)0;
        if (!legacy) data[22] = swap ? (byte)1 : (byte)0;
        return data;
    }

    private async Task InputChecks()
    {
        Router._Notification((int)NotificationApplicationFocusIn);
        Game.Instance.SceneState = Game.GameState.Battle;
        Router.SetInputMode(InputRouter.InputMode.RightAndLeft);
        int rightEdges = 0, leftEdges = 0, rightSwaps = 0, leftSwaps = 0;
        void Right() => rightEdges++;
        void Left() => leftEdges++;
        void RightSwap() => rightSwaps++;
        void LeftSwap() => leftSwaps++;
        Router.TriggerRight += Right;
        Router.TriggerLeft += Left;
        Router.SwitchGunRight += RightSwap;
        Router.SwitchGunLeft += LeftSwap;
        int port = 18981;
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--qa-udp-port:")) port = int.Parse(arg["--qa-udp-port:".Length..]);
        using var sender = new PacketPeerUdp();
        sender.SetDestAddress("127.0.0.1", port);
        sender.PutPacket(Packet('R', fire: true, legacy: true));
        sender.PutPacket(Packet('L', fire: true, swap: true));
        await Wait(() => Router.IsRingPhyConnected() && Router.LegConnected, 3, "real UDP peers");
        await Frames(2);
        Check(Router.GetCurKeyRing() && Router.GetCurKeyLeg(), "both UDP triggers held independently");
        Check(rightEdges == 1 && leftEdges == 1 && leftSwaps == 1, "legacy 22-byte and 23-byte packets emit one edge");
        Check(Router.RingPressure == 1234 && Router.LegPressure == 1234, "pressure decoded for both devices");
        Check(!Router.GetRightAim().IsScreenPoint && !Router.GetLeftAim().IsScreenPoint, "hardware owns both aim sources");
        Router.MouseGun.SimulateSwitch();
        Check(rightSwaps == 0, "mouse cannot swap a gun owned by a physical device");
        sender.PutPacket(Packet('R'));
        await Frames(5);
        Check(!Router.GetCurKeyRing() && Router.GetCurKeyLeg(), "right release does not release left trigger");
        sender.PutPacket(Packet('R', swap: true));
        await Frames(5);
        Check(rightSwaps == 1, "right hardware swap is independent of left");
        var corrupt = Packet('R');
        BitConverter.GetBytes(float.NaN).CopyTo(corrupt, 1);
        Check(!Router.AcceptDevicePacket(corrupt) && !Router.AcceptDevicePacket(new byte[4]), "nonfinite and truncated packets rejected");
        corrupt = new byte[23]; corrupt[0] = (byte)'R';
        Check(!Router.AcceptDevicePacket(corrupt), "zero quaternion rejected");
        Game.Instance.SetPaused(true);
        sender.PutPacket(Packet('L'));
        await Frames(5);
        Check(!Router.GetCurKeyLeg(), "hardware release is processed while paused");
        await Wait(() => !Router.IsRingPhyConnected() && !Router.LegConnected, 4, "disconnect watchdog");
        Check(!Router.GetCurKeyRing() && !Router.GetCurKeyLeg(), "disconnect clears stale fire levels");
        Game.Instance.SetPaused(false);
        Router.SetInputMode(InputRouter.InputMode.Mouse);
        Router.MouseGun.HandleInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(640, 360) });
        await Frames(2);
        Check(Router.GetCurKeyRing(), "mouse mode fires continuously while held");
        Router.MouseGun.HandleInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        await Frames(2);
        Check(!Router.GetCurKeyRing(), "mouse release stops firing in mouse mode");
        Router.SetInputMode(InputRouter.InputMode.OnlyLeft);
        Router.MouseGun.HandleInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(800, 450) });
        await Frames(2);
        Check(Router.GetLeftAim().IsScreenPoint && Router.GetCurKeyLeg() && !Router.GetCurKeyRing(), "left-only mouse fallback uses left aim and trigger");
        Router._Notification((int)NotificationApplicationFocusOut);
        await Frames(2);
        Check(!Router.GetCurKeyLeg(), "focus loss releases mouse fire");
        Router._Notification((int)NotificationApplicationFocusIn);
        Router.TriggerRight -= Right;
        Router.TriggerLeft -= Left;
        Router.SwitchGunRight -= RightSwap;
        Router.SwitchGunLeft -= LeftSwap;
        Game.Instance.SceneState = Game.GameState.UI;
    }

    private async Task SessionChecks()
    {
        Router.SetInputMode(InputRouter.InputMode.RightAndLeft);
        Game.Instance.NextScenePath = "res://scenes/levels/level2.tscn";
        Game.Instance.ChangeScene("res://scenes/ui/loading.tscn");
        await Wait(() => GetTree().CurrentScene is Level2 && InGamePanel.Instance != null, 15, "coop level load");
        await Frames(3);
        var level = (Level2)GetTree().CurrentScene;
        level.SetProcess(false); // controlled lifecycle checks; wave gameplay has its own full-flow suite
        Check(Router.Mode == InputRouter.InputMode.RightAndLeft
            && PlayerState.Instance.PlayerRight.Active && PlayerState.Instance.PlayerLeft.Active,
            "level entry preserves coop and both players");
        var state = FireSystem.Current!.CaptureDebugState();
        var sides = (System.Collections.Generic.Dictionary<string, object?>)state["sides"]!;
        var rightGun = (System.Collections.Generic.Dictionary<string, object?>)sides["Right"]!;
        var leftGun = (System.Collections.Generic.Dictionary<string, object?>)sides["Left"]!;
        Check((string?)rightGun["gun_type"] != "none" && (string?)leftGun["gun_type"] != "none",
            "battle constructs both usable weapons");
        await MonsterChecks(level);
        var panel = InGamePanel.Instance!;
        SaveService.Instance.Coin = 5;
        PlayerState.Instance.HitPlayer(999, Game.AttackType.Phy, PlayerState.Side.Left);
        await Wait(() => Game.Instance.IsGamePause, 2, "left death notification");
        Check(Game.Instance.IsGamePause && GetTree().Paused, "left death opens responsive continue panel");
        panel.GetNode<UiButton3D>("ContinuePanel/ContinueGameBtn").Trigger();
        Check(!Game.Instance.IsGamePause && PlayerState.Instance.PlayerLeft.Hp == Player.MaxHp
            && SaveService.Instance.Coin == 4, "continue revives the correct player and spends exactly one coin");
        PlayerState.Instance.PlayerRight.Bullet = 0;
        panel.OpenContinue(true, (int)PlayerState.Side.Right);
        panel.GetNode<UiButton3D>("ContinuePanel/ContinueGameBtn").Trigger();
        Check(PlayerState.Instance.PlayerRight.Bullet == SaveService.Instance.MaxBullet && SaveService.Instance.Coin == 3,
            "ammo exchange restores bullets once");
        SaveService.Instance.Coin = 5;
        PlayerState.Instance.HitPlayer(999, Game.AttackType.Phy, PlayerState.Side.Right);
        PlayerState.Instance.HitPlayer(999, Game.AttackType.Phy, PlayerState.Side.Left);
        await Wait(() => Game.Instance.IsGamePause, 2, "simultaneous coop deaths");
        await Frames(4);
        var confirm = panel.GetNode<UiButton3D>("ContinuePanel/ContinueGameBtn");
        Router.MouseGun.SimulateMove(panel.GetViewport().GetCamera3D().UnprojectPosition(confirm.GlobalPosition));
        Router.MouseGun.LeftHeld = true;
        await Wait(() => SaveService.Instance.Coin == 4, 2, "shot ray activates first continue");
        await Frames(5);
        Check(Game.Instance.IsGamePause && SaveService.Instance.Coin == 4, "simultaneous deaths queue the second player");
        Check(SaveService.Instance.Coin == 4, "held trigger does not spend coins through a second panel");
        Router.MouseGun.LeftHeld = false;
        await Frames(2);
        Router.MouseGun.LeftHeld = true;
        await Wait(() => !Game.Instance.IsGamePause, 2, "second deliberate continue shot");
        Router.MouseGun.LeftHeld = false;
        Check(!Game.Instance.IsGamePause && SaveService.Instance.Coin == 3
            && PlayerState.Instance.PlayerRight.Hp == Player.MaxHp && PlayerState.Instance.PlayerLeft.Hp == Player.MaxHp,
            "both coop players can continue independently");
        PlayerState.Instance.HitPlayer(1, Game.AttackType.Ice, PlayerState.Side.Right);
        PlayerState.Instance.HitPlayer(1, Game.AttackType.Phy, PlayerState.Side.Right);
        await Wait(() => PlayerState.Instance.PlayerRight.Status == Player.HurtState.Normal, 4, "ice expiry after physical hit");
        Check(PlayerState.Instance.PlayerRight.Hp == 98, "ordinary damage does not permanently freeze the player");
        PlayerState.Instance.HitPlayer(10, Game.AttackType.Poison, PlayerState.Side.Right);
        PlayerState.Instance.PlayerRight.Relife();
        await ToSignal(GetTree().CreateTimer(2.1), SceneTreeTimer.SignalName.Timeout);
        Check(PlayerState.Instance.PlayerRight.Hp == Player.MaxHp, "poison from a prior life cannot damage the revived player");
        SaveService.Instance.Coin = 0;
        panel.OpenContinue(false, (int)PlayerState.Side.Right);
        panel.GetNode<UiButton3D>("ContinuePanel/ContinueGameBtn").Trigger();
        Check(Game.Instance.IsGamePause && SaveService.Instance.Coin == 0, "no-credit continue cannot make coins negative");
        panel.CloseAll();
        SaveService.Instance.LevelState[1]["star"] = 0;
        Game.Instance.CurrentDifficulty = Game.Difficulty.Hard;
        level.TriggerVictory();
        await Wait(() => SaveService.GetInt(SaveService.Instance.LevelState[1], "star", 0) == 2, 5, "victory progression");
        Check(Game.Instance.IsGamePause && panel.GetNode<Node3D>("VectoryPanel").Visible, "victory opens panel and records earned stars");
        SaveService.Instance.LoadUserData();
        Check(SaveService.GetInt(SaveService.Instance.LevelState[1], "star", 0) == 2
            && SaveService.Instance.LevelState.Count == SaveService.LevelCount, "progression reload is persisted without duplicating level entries");
        for (int round = 0; round < 2; round++)
        {
            Game.Instance.ChangeScene("res://scenes/ui/menu.tscn");
            await Wait(() => GetTree().CurrentScene is MenuScreen, 10, "return menu");
            await Frames(3);
            Check(!GetTree().Paused && InGamePanel.Instance == null && FireSystem.Current == null, "leaving paused battle clears scene services");
            Router.SetInputMode(InputRouter.InputMode.RightAndLeft);
            Game.Instance.ChangeScene("res://scenes/levels/level2.tscn");
            await Wait(() => GetTree().CurrentScene is Level2 && InGamePanel.Instance != null, 15, "reenter level");
            await Frames(2);
            ((Level2)GetTree().CurrentScene).SetProcess(false);
            PlayerState.Instance.HitPlayer(999, Game.AttackType.Phy, PlayerState.Side.Right);
            await Wait(() => Game.Instance.IsGamePause, 2, "reentry death notification");
            Check(Game.Instance.IsGamePause && InGamePanel.Instance!.GetNode<Node3D>("ContinuePanel").Visible,
                "death after scene reentry reaches only live panel");
            InGamePanel.Instance!.CloseAll();
        }
        Game.Instance.NextScenePath = "res://scenes/levels/missing.tscn";
        Game.Instance.ChangeScene("res://scenes/ui/loading.tscn");
        await Wait(() => GetTree().CurrentScene is LoadingScreen && MessageBox.IsOpen(), 5, "loading error prompt");
        MessageBox.Current!.PressOk();
        await Wait(() => GetTree().CurrentScene is MenuScreen, 5, "loading error recovery");
        Check(!GetTree().Paused && Game.Instance.SceneState == Game.GameState.UI, "missing scene recovers to menu without a soft lock");
    }

    private async Task MonsterChecks(Level2 level)
    {
        int before = PlayerState.CurAliveMonster;
        var box = GD.Load<PackedScene>("res://scenes/battle/monsters/box_monster.tscn").Instantiate<BoxMonster>();
        level.AddChild(box);
        Vector3 spawn = level.GetViewport().GetCamera3D().GlobalPosition + Vector3.Right * 3;
        box.Info.LifeActiveTime = 0.05f;
        box.Born(spawn, 0, 20);
        await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
        box.Info.LifeActiveTime = 100;
        box.Born(spawn, 0, 20);
        await ToSignal(GetTree().CreateTimer(1.8), SceneTreeTimer.SignalName.Timeout);
        Check(box.IsActiveState && PlayerState.CurAliveMonster == before + 1,
            "old box expiration cannot recycle a new life or duplicate the alive count");
        box.Hit(999, box.GlobalPosition, Game.HitType.Body, PlayerState.Side.Right);
        Game.Instance.SetPaused(true);
        await ToSignal(GetTree().CreateTimer(1.8), SceneTreeTimer.SignalName.Timeout);
        Check(box.IsActiveState && box.IsDead, "dead enemy recycling pauses with gameplay");
        Game.Instance.SetPaused(false);
        await Wait(() => !box.IsActiveState, 2, "dead box recycle after resume");
        Check(PlayerState.CurAliveMonster == before, "enemy returns to its pool exactly once");
        box.QueueFree();
        await Frames(2);
    }
}
