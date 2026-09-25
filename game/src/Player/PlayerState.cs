using System.Collections.Generic;
using Godot;

namespace FPSGame;

/// <summary>
/// PlayerState [autoload]:左右双玩家状态 / 受击结算 / 存活怪物计数 / 常驻 HUD。
/// 对应原作 PlayerSystem.cs(5.5 换枪、6.4 受击状态、8.2 HUD 数据源)。
/// HUD 对应原作 GlobalObject 下 PlayerSystem.prefab(跨场景 DontDestroyOnLoad):
/// CoinObj(菜单/选关/战斗)+ 战斗 HUD(子弹×2/头像环×2/受击闪×2;HP 条按真值截图隐藏,仅内部追踪)。
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
        BuildBattleHud();
        PlayerState.Instance.UiChanged += RefreshBattleHud;
        PlayerHurt += OnHurtFlash;
    }

    // ------------------------------------------------ 战斗 HUD(PlayerSystem.prefab 1:1)

    private Control _battleRoot = null!;
    private Control _headRight = null!;
    private Control _headLeft = null!;
    private Control _bulletRight = null!;
    private Control _bulletLeft = null!;
    private Label _bulletRightText = null!;
    private Label _bulletLeftText = null!;
    private TextureProgressBar _hpRight = null!;
    private TextureProgressBar _hpLeft = null!;
    private TextureRect _hurtRight = null!;
    private TextureRect _hurtLeft = null!;
    private TextureRect _headLeftIcon = null!; // 左头像图标(左手未激活时藏,只留红环)
    private Tween? _hurtRightTween;
    private Tween? _hurtLeftTween;

    private void BuildBattleHud()
    {
        // 战斗容器:子弹×2(底中)/头像×2(右上/左上,HP 条隐藏)/受击全屏闪×2
        _battleRoot = new Control { Name = "BattleHud", MouseFilter = Control.MouseFilterEnum.Ignore };
        _battleRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hudLayer.AddChild(_battleRoot);

        _bulletRight = MakeBulletHud("BulletRight", new Color(0.0f, 0.7255f, 0.2824f),
            new Color(0.0088f, 0.3113f, 0.1265f), new Vector2(185.12f, 0.0f), 0.0f, out _bulletRightText);
        _bulletLeft = MakeBulletHud("BulletLeft", new Color(0.7062f, 0.7255f, 0.0f),
            new Color(0.3973f, 0.4057f, 0.0f), new Vector2(-258.72f, 31.4f), 0.5f, out _bulletLeftText);
        _headRight = MakeHeadHud("HeadRight", true, out _hpRight);
        _headLeft = MakeHeadHud("HeadLeft", false, out _hpLeft);
        _hurtRight = MakeHurtOverlay("RightHurt", "res://assets/textures/ui/bloodEffectRight.png");
        _hurtLeft = MakeHurtOverlay("LeftHurt", "res://assets/textures/ui/bloodEffect.png");
        _battleRoot.Visible = false;
    }

    private Control MakeBulletHud(string name, Color iconTint, Color xColor, Vector2 offset, float pivotY, out Label countLabel)
    {
        // 64×64(anchor 底中 + offset)。原作 pivot 左右不同:BulletRight pivot(0.5,0) pos (185.12,0)
        // → 图标底贴屏底;BulletLeft pivot(0.5,0.5) pos (-258.72,31.4) → 图标中心在屏底上方 31.4px。
        // Godot 底锚偏移 = pivotY*64 - offset.Y(Unity +y 向上, Godot 偏移向下为正)。
        var root = new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        root.OffsetLeft = offset.X - 32.0f;
        root.OffsetRight = offset.X + 32.0f;
        root.OffsetTop = pivotY * 64.0f - offset.Y - 64.0f;
        root.OffsetBottom = pivotY * 64.0f - offset.Y;
        // AddBulletAni 放大绕原作 pivot:右 (0.5,0)=底中,左 (0.5,0.5)=中心
        root.PivotOffset = new Vector2(32.0f, 64.0f - pivotY * 64.0f);
        _battleRoot.AddChild(root);
        var icon = new TextureRect
        {
            Name = "Icon",
            Texture = GD.Load<Texture2D>("res://assets/textures/ui/Bullet.png"),
            Modulate = iconTint,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        };
        icon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(icon);
        // "X":60×60,中心相对图标中心 (+58.43,+23.58)(原作 (58.43,-23.58) +y 向上 → 图标右下),28 号
        var x = new Label
        {
            Name = "X",
            Text = "X",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        x.AddThemeFontSizeOverride("font_size", 28);
        x.AddThemeColorOverride("font_color", xColor);
        x.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        x.OffsetLeft = 32.0f + 58.43f - 30.0f;
        x.OffsetRight = 32.0f + 58.43f + 30.0f;
        x.OffsetTop = 32.0f + 23.58f - 30.0f;
        x.OffsetBottom = 32.0f + 23.58f + 30.0f;
        root.AddChild(x);
        countLabel = new Label
        {
            Name = name + "Text",
            Text = "120",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        countLabel.AddThemeFontSizeOverride("font_size", 52);
        countLabel.SetAnchorsPreset(Control.LayoutPreset.CenterLeft);
        // 原作 (83.31,-3.1) 左中锚,+y 向上 → 数字中心在图标中心下方 3.1px
        countLabel.Position = new Vector2(83.31f, 3.1f - 30.0f);
        countLabel.Size = new Vector2(120.0f, 60.0f);
        root.AddChild(countLabel);
        return root;
    }

    private Control MakeHeadHud(string name, bool isRight, out TextureProgressBar slider)
    {
        // HdBg 128×128 右上角(右)/左上角(左)+ Head 100×100 + HP 438×16(隐藏,见下)
        var root = new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(isRight ? Control.LayoutPreset.TopRight : Control.LayoutPreset.TopLeft);
        root.OffsetLeft = isRight ? -128.0f : 0.0f;
        root.OffsetRight = isRight ? 0.0f : 128.0f;
        root.OffsetTop = 0.0f;
        root.OffsetBottom = 128.0f;
        _battleRoot.AddChild(root);
        var bg = new TextureRect
        {
            Name = "HdBg",
            Texture = GD.Load<Texture2D>(isRight
                ? "res://assets/textures/ui/HeadBg.png"
                : "res://assets/textures/ui/headBgRed.png"),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(bg);
        var head = new TextureRect
        {
            Name = "Head",
            Texture = GD.Load<Texture2D>("res://assets/textures/ui/heads/Icon1.png"),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        };
        head.SetAnchorsPreset(Control.LayoutPreset.Center);
        head.Size = new Vector2(100.0f, 100.0f);
        head.Position = new Vector2(-50.0f, -50.0f);
        root.AddChild(head);
        if (!isRight)
            _headLeftIcon = head;
        // 真值截图满血/残血均无可见 HP 条(l1u_gun_25s/l1u_battle_45s)→ 隐藏;
        // 保留节点与 RefreshBattleHud 的 Value 刷新(HP 值内部追踪与 API 不变)
        slider = new TextureProgressBar
        {
            Name = "HPSlider",
            TextureUnder = GD.Load<Texture2D>("res://assets/textures/ui/2-Empty.png"),
            TextureProgress = GD.Load<Texture2D>("res://assets/textures/ui/2-AppleGreen.png"),
            MinValue = 0.0,
            MaxValue = 100.0,
            Value = 100.0,
            Visible = false,
        };
        slider.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        slider.Size = new Vector2(438.0f, 16.0f);
        slider.Position = new Vector2(-219.0f + (isRight ? 0.0f : 0.0f), 8.0f);
        root.AddChild(slider);
        return root;
    }

    private TextureRect MakeHurtOverlay(string name, string texPath)
    {
        // prefab 色 (1,0,0,0.392);初始隐藏(原作 HurtEffect 默认 SetActive(false),受击才激活)
        var tr = new TextureRect
        {
            Name = name,
            Texture = GD.Load<Texture2D>(texPath),
            Modulate = new Color(1.0f, 0.0f, 0.0f, 0.392f),
            Visible = false,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        tr.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _battleRoot.AddChild(tr);
        return tr;
    }

    private void RefreshBattleHud()
    {
        if (_battleRoot == null || !_battleRoot.Visible)
            return;
        var pr = PlayerRight;
        var pl = PlayerLeft;
        // 加子弹动画进行中的侧由动画逐帧写文本,此处不覆盖
        if (!_bulletAniRight)
        {
            _bulletRightText.Text = pr.Bullet.ToString();
            _bulletShownRight = pr.Bullet;
        }
        if (!_bulletAniLeft)
        {
            _bulletLeftText.Text = pl.Bullet.ToString();
            _bulletShownLeft = pl.Bullet;
        }
        _hpRight.Value = Mathf.Clamp(pr.Hp, 0.0f, Player.MaxHp);
        _hpLeft.Value = Mathf.Clamp(pl.Hp, 0.0f, Player.MaxHp);
    }

    /// <summary>受击全屏闪(原作 UpdateHurtEffect,PlayerSystem.cs:262-300):
    /// Phy 黄 (1,1,0)/Ice 青 (0,1,1)/Poison 绿 (0,1,0),起始 alpha=1,每 0.1s 减 0.05(≈2s 淡完),
    /// 结束复位 alpha=1 后隐藏;效果进行中再受击 → 颜色保持,仅 alpha 复位 1 重新淡出</summary>
    private void OnHurtFlash(int side, int attackType)
    {
        if (_battleRoot == null)
            return;
        bool right = side == (int)Side.Right;
        var overlay = right ? _hurtRight : _hurtLeft;
        var tween = right ? _hurtRightTween : _hurtLeftTween;
        Color c = overlay.Modulate;
        if (!overlay.Visible)
        {
            c = (Game.AttackType)attackType switch
            {
                Game.AttackType.Ice => new Color(0.0f, 1.0f, 1.0f),
                Game.AttackType.Poison => new Color(0.0f, 1.0f, 0.0f),
                _ => new Color(1.0f, 1.0f, 0.0f),
            };
        }
        c.A = 1.0f;
        overlay.Modulate = c;
        overlay.Visible = true;
        tween?.Kill();
        tween = CreateTween();
        tween.TweenProperty(overlay, "modulate:a", 0.0f, 2.0f);
        tween.TweenCallback(Callable.From(() =>
        {
            var m = overlay.Modulate;
            m.A = 1.0f;
            overlay.Modulate = m;
            overlay.Visible = false;
        }));
        if (right) _hurtRightTween = tween; else _hurtLeftTween = tween;
    }

    // ------------------------------------------------ 加子弹放大动画(原作 AddBulletAni,PlayerSystem.cs:475-516)

    private bool _bulletAniRight, _bulletAniLeft;
    private int _bulletAniSeqRight, _bulletAniSeqLeft;
    private int _bulletShownRight = -1, _bulletShownLeft = -1;

    /// <summary>Player.Bullet 外部增加(宝箱掉落)时由 setter 回调:图标 ×1.5,文本每帧 +1 逐发涨到终值</summary>
    internal void OnBulletAdded(Player p, int from, int to)
    {
        bool right = ReferenceEquals(p, PlayerRight);
        var root = right ? _bulletRight : _bulletLeft;
        if (_battleRoot == null || !_battleRoot.Visible || !root.Visible)
            return; // 非战斗或该侧未显形:RefreshBattleHud 直接落终值
        int shown = right ? _bulletShownRight : _bulletShownLeft;
        if (shown < from || shown > to)
            shown = from;
        AddBulletAni(right, shown, to);
    }

    private async void AddBulletAni(bool right, int from, int to)
    {
        var root = right ? _bulletRight : _bulletLeft;
        var text = right ? _bulletRightText : _bulletLeftText;
        if (right) { _bulletAniRight = true; _bulletAniSeqRight += 1; }
        else { _bulletAniLeft = true; _bulletAniSeqLeft += 1; }
        int seq = right ? _bulletAniSeqRight : _bulletAniSeqLeft;
        root.Scale = new Vector2(1.5f, 1.5f);
        int v = from;
        while (v < to)
        {
            if (seq != (right ? _bulletAniSeqRight : _bulletAniSeqLeft))
                return; // 被新一次加子弹/场景切换接管
            v += 1;
            text.Text = v.ToString();
            if (right) _bulletShownRight = v; else _bulletShownLeft = v;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (seq != (right ? _bulletAniSeqRight : _bulletAniSeqLeft))
            return;
        root.Scale = Vector2.One;
        if (right) _bulletAniRight = false; else _bulletAniLeft = false;
    }

    /// <summary>取消进行中的加子弹动画并复位图标(切场景/显隐切换时,原作协程随 SetActive(false) 中断)</summary>
    private void CancelBulletAni()
    {
        _bulletAniSeqRight += 1;
        _bulletAniSeqLeft += 1;
        _bulletAniRight = _bulletAniLeft = false;
        if (_bulletRight != null)
            _bulletRight.Scale = Vector2.One;
        if (_bulletLeft != null)
            _bulletLeft.Scale = Vector2.One;
    }

    /// <summary>隐藏受击闪(原作 UpdateUIMode 各分支均 HurtEffect.SetActive(false))</summary>
    private void HideHurtOverlays()
    {
        _hurtRightTween?.Kill();
        _hurtLeftTween?.Kill();
        if (_hurtRight != null)
            _hurtRight.Visible = false;
        if (_hurtLeft != null)
            _hurtLeft.Visible = false;
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
        // 原作 UpdateUIMode 任意分支入口:受击闪强制隐藏;进行中的加子弹动画随显隐切换中断
        HideHurtOverlays();
        CancelBulletAni();
        if (sceneName is "Menu" or "LevelChoose")
        {
            _coinObj.Visible = true;
            if (_battleRoot != null)
                _battleRoot.Visible = false;
            Game.Instance.SceneState = Game.GameState.UI;
        }
        else if (sceneName is "DeviceConnection" or "LoadingScene")
        {
            _coinObj.Visible = false;
            if (_battleRoot != null)
                _battleRoot.Visible = false;
            Game.Instance.SceneState = Game.GameState.UI;
        }
        else
        {
            _coinObj.Visible = true;
            Game.Instance.SceneState = Game.GameState.Battle;
            if (_battleRoot != null)
            {
                _battleRoot.Visible = true;
                // 原作战斗 HUD:左侧红环头像常显(即使左手玩家未激活);
                // 子弹 HUD 开战后由 ShowBattleBullets 显形(左弹仅左手活跃时)
                _bulletLeft.Visible = false;
                _headLeft.Visible = true;
                if (_headLeftIcon != null)
                    _headLeftIcon.Visible = PlayerLeft.Active; // 原作:左手未激活只显红环
                _bulletRight.Visible = false;
                _headRight.Visible = true;
            }
        }
        RefreshHud();
        RefreshBattleHud();
    }

    /// <summary>开战后显示子弹 HUD(原作战斗截图:开场只见金币/头像,不见子弹)</summary>
    public void ShowBattleBullets()
    {
        if (_battleRoot == null)
            return;
        _bulletRight.Visible = true;
        _bulletLeft.Visible = PlayerLeft.Active;
        RefreshBattleHud();
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
