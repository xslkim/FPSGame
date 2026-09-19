using Godot;

namespace FPSGame;

/// <summary>
/// Loading(8.1):最少 4 秒 + 随机 Tips + 真实进度条(修原作只显示 100% 的 bug)。
/// 激活瞬间:SceneState=Battle、CurAliveMonster=0、重置玩家。
/// </summary>
public partial class LoadingScreen : CanvasLayer
{
    private const double MinTime = 4.0;

    private string _path = "";
    private double _elapsed;
    private bool _done;

    private ProgressBar _bar = null!;
    private Label _tipLabel = null!;
    private Label _infoLabel = null!;

    public override void _Ready()
    {
        _bar = GetNode<ProgressBar>("Center/ProgressBar");
        _tipLabel = GetNode<Label>("Center/TipLabel");
        _infoLabel = GetNode<Label>("Center/InfoLabel");
        _path = Game.Instance.NextScenePath;
        if (_path.Length == 0)
            _path = "res://scenes/levels/level1_battle.tscn";
        var tips = SaveService.Instance.GetTips();
        if (tips.Count > 0)
            _tipLabel.Text = "Tips: " + tips[GD.RandRange(0, tips.Count - 1)].AsString();
        _infoLabel.Text = "Loading: " + _path;
        var err = ResourceLoader.LoadThreadedRequest(_path);
        if (err != Error.Ok)
            GD.PushError($"[Loading] threaded request failed: {_path} (err {err})");
    }

    public override void _Process(double delta)
    {
        if (_done)
            return;
        _elapsed += delta;
        var progress = new Godot.Collections.Array();
        var status = ResourceLoader.LoadThreadedGetStatus(_path, progress);
        // 真实进度:加载进度与最小耗时取小(修原作恒 100%)
        _bar.Value = 100.0 * Mathf.Min(progress.Count > 0 ? progress[0].AsDouble() : 0.0, _elapsed / MinTime);
        if (status == ResourceLoader.ThreadLoadStatus.Loaded && _elapsed >= MinTime)
        {
            _done = true;
            var packed = ResourceLoader.LoadThreadedGet(_path) as PackedScene;
            if (packed == null)
            {
                GD.PushError("[Loading] threaded get failed: " + _path);
                return;
            }
            // 激活瞬间重置战斗状态(8.1)
            Game.Instance.SceneState = Game.GameState.Battle;
            PlayerState.CurAliveMonster = 0;
            PlayerState.Instance.SetupPlayers(InputRouter.Instance.Mode);
            GetTree().ChangeSceneToPacked(packed);
        }
    }
}
