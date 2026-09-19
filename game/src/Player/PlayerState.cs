using System.Collections.Generic;
using Godot;

namespace FPSGame;

/// <summary>
/// PlayerState [autoload]:左右双玩家状态 / 受击结算 / 存活怪物计数 / 常驻 HUD。
/// 对应原作 PlayerSystem.cs(5.5 换枪、6.4 受击状态、8.2 HUD 数据源)。
/// HUD 对应原作 GlobalObject 下 PlayerSystem.prefab(跨场景 DontDestroyOnLoad),
/// 当前仅移植 CoinObj(菜单/选关/战斗显示);弹药/头像/血条待战斗界面步骤移植。
/// </summary>
public partial class PlayerState : Node
{
    public enum Side { Left, Right, Both }

    [Signal] public delegate void PlayerHurtEventHandler(int side, int attackType);
    [Signal] public delegate void PlayerDiedEventHandler(int side);
    [Signal] public delegate void UiChangedEventHandler();

    public static PlayerState Instance { get; private set; } = null!;

    public static int CurAliveMonster;

    /// <summary>受击音效(按侧随机,文件缺失静默):右手 Male / 左手 Female</summary>
    private static readonly Dictionary<Side, string[]> HurtSounds = new()
    {
        [Side.Right] = new[]
        {
            "res://assets/audio/player/Male_Hurt_01_Edwyn.ogg",
            "res://assets/audio/player/Male_Hurt_02_Edwyn.ogg",
            "res://assets/audio/player/Male_Hurt_03_Edwyn.ogg",
            "res://assets/audio/player/Male_Hurt_04_Edwyn.ogg",
            "res://assets/audio/player/Male_Hurt_05_Edwyn.ogg",
            "res://assets/audio/player/Male_Hurt_06_Edwyn.ogg",
        },
        [Side.Left] = new[]
        {
            "res://assets/audio/player/Female_Hurt_03_Rina.ogg",
            "res://assets/audio/player/Female_Hurt_04_Rina.ogg",
        },
    };

    private const string FreezeLoopPath = "res://assets/audio/effects/freeze_loop.wav";

    public Player PlayerLeft = new();
    public Player PlayerRight = new();

    private readonly Dictionary<Side, AudioStream[]> _hurtStreams = new();
    private readonly Dictionary<Side, AudioStreamPlayer> _hurtPlayers = new();
    private AudioStreamPlayer? _freezePlayer;

    private CanvasLayer _hudLayer = null!;
    private Control _coinObj = null!;
    private Label _coinText = null!;

    /// <summary>金币 HUD 根控件(自检测试用)</summary>
    public Control CoinObj => _coinObj;

    public override void _Ready()
    {
        Instance = this;
        foreach (var side in new[] { Side.Right, Side.Left })
        {
            var streams = new List<AudioStream>();
            foreach (var path in HurtSounds[side])
            {
                if (ResourceLoader.Exists(path))
                    streams.Add(GD.Load<AudioStream>(path));
            }
            _hurtStreams[side] = streams.ToArray();
            var asp = new AudioStreamPlayer { Name = side == Side.Right ? "HurtSoundRight" : "HurtSoundLeft" };
            AddChild(asp);
            _hurtPlayers[side] = asp;
        }
        if (ResourceLoader.Exists(FreezeLoopPath))
        {
            _freezePlayer = new AudioStreamPlayer
            {
                Name = "FreezeLoop",
                Stream = GD.Load<AudioStream>(FreezeLoopPath),
            };
            AddChild(_freezePlayer);
        }
        BuildHud();
        SaveService.Instance.Changed += RefreshHud;
    }

    // ------------------------------------------------ 常驻 HUD

    private void BuildHud()
    {
        _hudLayer = new CanvasLayer { Name = "HUD", Layer = 10 };
        AddChild(_hudLayer);
        // 逻辑分辨率恒为 1280×720(canvas_items+expand),直接全屏锚定,金币吸底中
        var root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hudLayer.AddChild(root);
        // CoinObj:anchor 底中,pivot 底中,pos (-26.02, 0),64×64(原作 PlayerSystem.prefab)
        _coinObj = new Control { Name = "CoinObj", MouseFilter = Control.MouseFilterEnum.Ignore };
        _coinObj.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _coinObj.OffsetLeft = -26.02f - 32.0f;
        _coinObj.OffsetRight = -26.02f + 32.0f;
        _coinObj.OffsetTop = -64.0f;
        _coinObj.OffsetBottom = 0.0f;
        root.AddChild(_coinObj);
        var icon = new TextureRect
        {
            Name = "CoinIcon",
            Texture = GD.Load<Texture2D>("res://assets/textures/ui/coin.png"),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        };
        icon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _coinObj.AddChild(icon);
        // "X":60×60,中心相对金币中心 (+49.31, +5.32),字号 32,暗金黄
        // 偏移量已含 +32(金币中心换算),锚点必须用 TOP_LEFT(若用 CENTER 会重复加 32)
        var xLabel = new Label
        {
            Name = "X",
            Text = "X",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        xLabel.AddThemeFontSizeOverride("font_size", 32);
        xLabel.AddThemeColorOverride("font_color", new Color(0.5188679f, 0.4779218f, 0.0f));
        xLabel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        xLabel.OffsetLeft = 32.0f + 49.31f - 30.0f;
        xLabel.OffsetRight = 32.0f + 49.31f + 30.0f;
        xLabel.OffsetTop = 32.0f + 5.32f - 30.0f;
        xLabel.OffsetBottom = 32.0f + 5.32f + 30.0f;
        _coinObj.AddChild(xLabel);
        // CoinText:60×60,中心 (+81.1, +4.15),字号 52 白,显示金币数
        _coinText = new Label
        {
            Name = "CoinText",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _coinText.AddThemeFontSizeOverride("font_size", 52);
        _coinText.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _coinText.OffsetLeft = 32.0f + 81.1f - 30.0f;
        _coinText.OffsetRight = 32.0f + 81.1f + 30.0f;
        _coinText.OffsetTop = 32.0f + 4.15f - 30.0f;
        _coinText.OffsetBottom = 32.0f + 4.15f + 30.0f;
        _coinObj.AddChild(_coinText);
        _coinObj.Visible = false;
        RefreshHud();
    }

    public void RefreshHud()
    {
        _coinText.Text = SaveService.Instance.Coin.ToString();
    }

    /// <summary>对应原作 PlayerSystem.UpdateUIMode():按场景切 HUD 元素与 SceneState。
    /// Menu/LevelChoose=只显示金币;DeviceConnection/LoadingScene=全隐藏;其余=战斗。</summary>
    public void UpdateUiMode(string sceneName)
    {
        if (sceneName == "StartUp")
            return;
        if (sceneName is "Menu" or "LevelChoose")
        {
            _coinObj.Visible = true;
            Game.Instance.SceneState = Game.GameState.UI;
        }
        else if (sceneName is "DeviceConnection" or "LoadingScene")
        {
            _coinObj.Visible = false;
            Game.Instance.SceneState = Game.GameState.UI;
        }
        else
        {
            _coinObj.Visible = true;
            Game.Instance.SceneState = Game.GameState.Battle;
        }
        RefreshHud();
    }

    // ------------------------------------------------ 玩家管理(4.4)

    public Player GetPlayer(Side side) => side == Side.Right ? PlayerRight : PlayerLeft;

    /// <summary>按输入模式创建/停用左右玩家</summary>
    public void SetupPlayers(InputRouter.InputMode mode)
    {
        bool rightOn = mode is InputRouter.InputMode.OnlyRight
            or InputRouter.InputMode.ControllerOrRight
            or InputRouter.InputMode.RightAndLeft
            or InputRouter.InputMode.Mouse; // 鼠标模式:右玩家由鼠标光枪担当
        bool leftOn = mode is InputRouter.InputMode.OnlyLeft
            or InputRouter.InputMode.RightAndLeft;
        if (rightOn) PlayerRight.Born(); else PlayerRight.Active = false;
        if (leftOn) PlayerLeft.Born(); else PlayerLeft.Active = false;
        EmitSignal(SignalName.UiChanged);
    }

    // ------------------------------------------------ 受击结算(6.1 / 6.4)

    /// <summary>怪物攻击结算。side=Both 双打;目标侧不活跃则转嫁另一侧(6.1 EventAttack 规则)</summary>
    public void HitPlayer(float monsterAttack, Game.AttackType attackType, Side side)
    {
        if (side == Side.Both)
        {
            ApplyHit(PlayerRight, monsterAttack, attackType, Side.Right);
            ApplyHit(PlayerLeft, monsterAttack, attackType, Side.Left);
            return;
        }
        var p = GetPlayer(side);
        var realSide = side;
        if (!p.Active)
        {
            realSide = side == Side.Right ? Side.Left : Side.Right;
            p = GetPlayer(realSide);
            if (!p.Active)
                return;
        }
        ApplyHit(p, monsterAttack, attackType, realSide);
    }

    /// <summary>6.4:立即扣血 + 泛红(HUD 做);Ice 置 Frozen 2 秒;Poison 2 秒后再扣一次等额血</summary>
    private void ApplyHit(Player p, float monsterAttack, Game.AttackType attackType, Side side)
    {
        if (!p.Active)
            return;
        p.Hp -= monsterAttack;
        p.StatusSeq += 1;
        int seq = p.StatusSeq;
        EmitSignal(SignalName.PlayerHurt, (int)side, (int)attackType);
        PlayHurtSound(side);
        if (attackType == Game.AttackType.Ice)
        {
            p.Status = Player.HurtState.Frozen;
            _freezePlayer?.Play();
            GetTree().CreateTimer(2.0).Timeout += () =>
            {
                if (p.StatusSeq == seq && p.Status == Player.HurtState.Frozen)
                {
                    p.Status = Player.HurtState.Normal;
                    _freezePlayer?.Stop();
                    EmitSignal(SignalName.UiChanged);
                }
            };
        }
        else if (attackType == Game.AttackType.Poison)
        {
            GetTree().CreateTimer(2.0).Timeout += () =>
            {
                if (p.Active && p.Hp > 0.0f)
                {
                    p.Hp -= monsterAttack;
                    EmitSignal(SignalName.UiChanged);
                    CheckDead(p, side);
                }
            };
        }
        EmitSignal(SignalName.UiChanged);
        // CheckDead 延迟 0.5s 检查
        GetTree().CreateTimer(0.5).Timeout += () => CheckDead(p, side);
    }

    private void CheckDead(Player p, Side side)
    {
        if (p.Hp <= 0.0f)
        {
            p.Hp = 0.0f;
            EmitSignal(SignalName.UiChanged);
            EmitSignal(SignalName.PlayerDied, (int)side);
        }
    }

    /// <summary>按侧随机播放受击音效;该侧无可用音频文件时静默</summary>
    private void PlayHurtSound(Side side)
    {
        var streams = _hurtStreams.GetValueOrDefault(side, System.Array.Empty<AudioStream>());
        if (streams.Length == 0)
            return;
        var asp = _hurtPlayers[side];
        asp.Stream = streams[GD.RandRange(0, streams.Length - 1)];
        asp.Play();
    }

    public void NotifyUiChanged() => EmitSignal(SignalName.UiChanged);
}
