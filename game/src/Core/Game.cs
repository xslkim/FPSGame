using Godot;

namespace FPSGame;

/// <summary>
/// Game [autoload]:全局状态与场景流转。对应原作 GlobalObject.cs 的状态部分。
/// 职责划分:音频 → AudioService,存档/数值 → SaveService,输入 → InputRouter。
/// </summary>
public partial class Game : Node, IDebugInspectable
{
    public string DebugSummary => $"{SceneState} {CurrentDifficulty} paused={IsGamePause}";

    public System.Collections.Generic.Dictionary<string, object?> CaptureDebugState() => new()
    {
        ["scene_state"] = SceneState.ToString(),
        ["difficulty"] = CurrentDifficulty.ToString(),
        ["paused"] = IsGamePause,
        ["next_scene"] = NextScenePath,
    };
    public enum GameState { UI, Battle }
    public enum Difficulty { Easy, Hard, Hell }
    /// <summary>怪物攻击类型(受击状态结算用)</summary>
    public enum AttackType { Phy, Ice, Poison }
    /// <summary>命中部位(BoxHead 转发时携带)</summary>
    public enum HitType { Head, Body, Armour }

    public static Game Instance { get; private set; } = null!;

    public GameState SceneState = GameState.UI;
    public Difficulty CurrentDifficulty = Difficulty.Easy;
    public bool IsGamePause;
    public bool IsDebug = false; // 原作发布态:常关(Player.cs:50-57 子弹 120;Level1 播 19s 开场)

    /// <summary>Loading 场景目标(LevelChoose 选定关卡后写入)</summary>
    public string NextScenePath = "";

    public override void _Ready()
    {
        Instance = this;
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--runtime-selftest") >= 0)
            Callable.From(() => AddChild(new RuntimeQa { Name = "RuntimeQa" })).CallDeferred();
        if (System.Array.Exists(OS.GetCmdlineUserArgs(), a => a.StartsWith("--playthrough:")))
            Callable.From(() => AddChild(new PlaythroughQa { Name = "PlaythroughQa" })).CallDeferred();
        // --quick:<场景路径>:跳过菜单流直进场景(成品冒烟/调试用,编辑器与导出包同效)
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--quick:"))
            {
                string path = a["--quick:".Length..];
                GetTree().CreateTimer(0.1).Timeout += () => ChangeScene(path);
            }
        }
    }

    public void ChangeScene(string path)
    {
        if (!ResourceLoader.Exists(path, "PackedScene"))
        {
            GD.PushError($"[SceneContract] {DebugIdentity.SceneId(GetTree().CurrentScene)} cannot open '{path}' (missing scene)");
            return;
        }
        // Signal callbacks can run while Godot is still releasing native references.
        // Leave that stack before loading and destroying an entire scene.
        Callable.From(() => ApplySceneChange(path)).CallDeferred();
    }

    private void ApplySceneChange(string path)
    {
        SetPaused(false);
        InputRouter.Instance?.ReleaseTransientInput();
        Error error = GetTree().ChangeSceneToFile(path);
        if (error != Error.Ok)
            GD.PushError($"[SceneContract] {DebugIdentity.SceneId(GetTree().CurrentScene)} -> '{path}' failed: {error}");
    }

    /// <summary>暂停语义统一入口:原作 timeScale=0 + IsGamePause 同步维护</summary>
    public void SetPaused(bool paused)
    {
        IsGamePause = paused;
        GetTree().Paused = paused;
    }
}
