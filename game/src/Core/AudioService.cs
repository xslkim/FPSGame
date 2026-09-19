using Godot;

namespace FPSGame;

/// <summary>
/// AudioService [autoload]:全局音乐/音效(对应原作 GlobalObject 的音频部分)。
/// 菜单音乐 MenuUIAS(UI.mp3 循环):Menu 起播,战斗场景加载出来才停
/// (近似原作 LoadingScene 播完才停;loading.tscn 进入战斗前仍属 UI 场景)。
/// UI 音效 _Sound(Zapper_3p_02.wav,经 GetGunSound()/Utils.PlayMenuSound 播放)。
/// </summary>
public partial class AudioService : Node
{
    public const string MenuMusicPath = "res://assets/audio/ui/UI.mp3";
    public const string UiSoundPath = "res://assets/audio/ui/Zapper_3p_02.wav";

    /// <summary>菜单音乐存活的 UI 场景文件名</summary>
    public static readonly string[] MenuMusicScenes =
        { "menu.tscn", "level_choose.tscn", "device_connection.tscn", "loading.tscn" };

    public static AudioService Instance { get; private set; } = null!;

    private AudioStreamPlayer _menuMusic = null!;
    private AudioStreamPlayer _uiSound = null!;

    public override void _Ready()
    {
        Instance = this;
        _menuMusic = new AudioStreamPlayer { Name = "MenuUIAS" };
        var ms = GD.Load<AudioStreamMP3>(MenuMusicPath);
        if (ms != null)
        {
            ms.Loop = true;
            _menuMusic.Stream = ms;
        }
        AddChild(_menuMusic);
        _uiSound = new AudioStreamPlayer { Name = "GunSound", Stream = GD.Load<AudioStream>(UiSoundPath) };
        AddChild(_uiSound);
    }

    public void PlayMenuMusic()
    {
        if (!_menuMusic.Playing)
            _menuMusic.Play();
    }

    /// <summary>Utils.PlayMenuSound():整段播放,重按打断重播</summary>
    public void PlayUiSound() => _uiSound.Play();

    /// <summary>战斗场景加载出来后停菜单音乐</summary>
    public override void _Process(double delta)
    {
        var scene = GetTree().CurrentScene;
        if (scene == null)
            return;
        bool want = System.Array.IndexOf(MenuMusicScenes, scene.SceneFilePath.GetFile()) >= 0;
        if (!want && _menuMusic.Playing)
            _menuMusic.Stop();
    }
}
