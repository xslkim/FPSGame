using Godot;

namespace FPSGame;

/// <summary>
/// 枪基类:CD / 耗弹 / 开火表现(原作 GunBase + firemetajson 数值)。
/// 模型 = 真实 FBX(assets/models/guns/<name>),枪口火光 = MuzzleFlash(1:1 FPS Pack)。
/// </summary>
public partial class GunBase : Node3D
{
    public int GunType;              // 0=AK47 / 1=M4 / 2=HandGun
    public string GunName = "";
    public float FireCd = 0.5f;
    public float Attack = 10.0f;
    public int BulletCost = 1;
    public double LastFireTime = -99.0;

    public Player Player = null!;
    public bool IsLeft;

    public Node3D Muzzle => GetNode<Node3D>("Muzzle");
    private MuzzleFlash _flash = null!;
    private AudioStreamPlayer3D _audio = null!;

    public void Setup(int type, bool isLeft)
    {
        GunType = type;
        IsLeft = isLeft;
        var info = SaveService.Instance.GetGunInfo(type);
        GunName = SaveService.Get(info, "name", $"Gun{type}").AsString();
        FireCd = (float)SaveService.Get(info, "fire_cd", 0.5).AsDouble();
        Attack = (float)SaveService.Get(info, "attack", 10.0).AsDouble();
        BulletCost = SaveService.Get(info, "bullet", 1).AsInt32();
        string soundPath = SaveService.Get(info, "fire_sound", "").AsString();
        _audio = new AudioStreamPlayer3D { Name = "FireAS" };
        if (soundPath.Length > 0 && ResourceLoader.Exists(soundPath))
            _audio.Stream = GD.Load<AudioStream>(soundPath);
        AddChild(_audio);
        _flash = new MuzzleFlash { Name = "MuzzleFlash" };
        Muzzle.AddChild(_flash);
    }

    /// <summary>开火:CD 到 + 弹够才成功。返回 (success, bulletEnough)</summary>
    public (bool Success, bool BulletEnough) Fire()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (now - LastFireTime < FireCd)
            return (false, true);
        if (!Player.Active || Player.Bullet < BulletCost)
            return (false, false);
        LastFireTime = now;
        Player.UseBullet(BulletCost);
        _flash.Fire();
        PlayFireAnimation();
        if (_audio.Stream != null)
            _audio.Play();
        return (true, true);
    }

    private AnimationPlayer? _ani;
    private bool _aniSearched;
    private Node3D? _model;
    private Vector3 _modelBasePos;
    private Tween? _recoilTween;

    /// <summary>开火后座:原作 Fire 时 _Ani.Play() 播枪 FBX legacy "Shoot" 动画
    /// (HandGun.cs:36/AKGun.cs:50/M4Gun.cs:36;三枪 FBX 同名 take,Unity 切帧 1-6≈0.2s)。
    /// 模型带 AnimationPlayer 则整段播放其唯一 take(FBX 仅这一个 take,内容即 Shoot);
    /// 否则(ak47 导入后无 AnimationPlayer)用等效 tween:枪体沿 +Z 快速后移 0.012m、0.2s 回弹。</summary>
    private void PlayFireAnimation()
    {
        if (!_aniSearched)
        {
            _aniSearched = true;
            _ani = FindChild("AnimationPlayer", true, false) as AnimationPlayer;
            _model = GetNodeOrNull<Node3D>("Model");
            if (_model != null)
                _modelBasePos = _model.Position;
        }
        if (_ani != null)
        {
            var list = _ani.GetAnimationList();
            if (list.Length > 0)
            {
                _ani.Stop();
                _ani.Play(list[0]);
                return;
            }
        }
        if (_model == null)
            return;
        _recoilTween?.Kill();
        _model.Position = _modelBasePos + new Vector3(0, 0, 0.012f);
        _recoilTween = CreateTween();
        _recoilTween.TweenProperty(_model, "position", _modelBasePos, 0.2)
            .SetEase(Tween.EaseType.Out);
    }
}
