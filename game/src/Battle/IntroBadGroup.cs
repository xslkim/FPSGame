using Godot;

namespace FPSGame;

/// <summary>
/// 开场 BadGroup 演出(1:1 SchoolNightTimeline.playable):
/// 2.729s 激活,三人负重行进(RockWarrior 背 casual 女 / 包头僵尸抱校服女 / 士兵坏人抱剑女孩),
/// 根运动 z:-5@2.95s → 10.19@10.05s → 33.69@21s(分段线性,速度≈2.14m/s);
/// 0.09s 尖叫、11.7s 女孩呼救;相机注视校服女 Neck(Cinemachine vcam1 语义)。
/// 19s(Level1)调 Stop() 收场。被抱女生动画为 Unity humanoid 肌肉曲线,无法直接转换,
/// 以绑定姿态近似(README 已记偏差)。
/// </summary>
public partial class IntroBadGroup : Node3D
{
    public const double ActivateTime = 2.729;
    public const double ScreamTime = 0.09;
    public const double HelpTime = 11.7;

    private double _clock;
    private bool _playing;
    private bool _screamPlayed;
    private bool _helpPlayed;
    private Camera3D? _camera;
    private Node3D? _lookTarget; // 校服女 Neck 近似(包头僵尸载体 + 高度)
    private AudioStreamPlayer _screamPlayer = null!;
    private AudioStreamPlayer _helpPlayer = null!;
    private readonly System.Collections.Generic.List<AnimationPlayer> _animPlayers = new();

    public override void _Ready()
    {
        Visible = false;
        BuildNpcs();
        _screamPlayer = MakePlayer("res://assets/audio/story/尖叫4.mp3");
        _helpPlayer = MakePlayer("res://assets/audio/story/救救我.wav");
    }

    private AudioStreamPlayer MakePlayer(string path)
    {
        var p = new AudioStreamPlayer { Name = "Audio" };
        if (ResourceLoader.Exists(path))
            p.Stream = GD.Load<AudioStream>(path);
        AddChild(p);
        return p;
    }

    public void Play(Camera3D camera)
    {
        _camera = camera;
        _clock = 0.0;
        _playing = true;
        _screamPlayed = false;
        _helpPlayed = false;
        Visible = true;
        foreach (var ap in _animPlayers)
            ap.Play("locomotion", 0.2);
    }

    public void Stop()
    {
        _playing = false;
        Visible = false;
        foreach (var ap in _animPlayers)
            ap.Stop();
    }

    public override void _Process(double delta)
    {
        if (!_playing)
            return;
        _clock += delta;
        // 音频
        if (!_screamPlayed && _clock >= ScreamTime)
        {
            _screamPlayed = true;
            _screamPlayer.Play();
        }
        if (!_helpPlayed && _clock >= HelpTime)
        {
            _helpPlayed = true;
            _helpPlayer.Play();
        }
        // 2.729s 激活后:根运动(分段线性,Unity 曲线关键帧 2.95/-5 → 10.05/10.19 → 21/33.69)
        if (_clock >= ActivateTime)
        {
            float z = _clock switch
            {
                <= 10.05 => -5.0f + 15.194186f * (float)((_clock - 2.95) / 7.10),
                <= 21.0 => 10.194186f + 23.49973f * (float)((_clock - 10.05) / 10.95),
                _ => 33.693916f,
            };
            Position = new Vector3(0.0f, 0.0f, -4.0f + z);
            // 相机注视校服女 Neck(Cinemachine LookAt 语义)
            if (_camera != null && _lookTarget != null)
            {
                Vector3 target = _lookTarget.GlobalPosition + new Vector3(0.0f, 1.35f, 0.0f);
                _camera.LookAt(target, Vector3.Up);
            }
        }
    }

    // ------------------------------------------------ NPC 组装

    private void BuildNpcs()
    {
        // RockWarrior(0.5,0,15.957) 背 casual_dressed_girl(背部)
        var rock = BuildWalker("RockWarrior",
            "res://assets/models/monsters/rock_warrior/rock_warrior_anims.tres",
            "res://assets/models/monsters/rock_warrior/RockWarrior.FBX",
            "res://assets/models/monsters/rock_warrior/rock_warrior_mat.tres",
            new Vector3(0.5f, 0.0f, 15.957f), "locomotion");
        AttachGirl(rock,
            "res://assets/models/actors/casual_dressed_girl/casual_dressed_girl.FBX",
            new Vector3(0.0f, 1.7f, 0.55f), 4.0f);
        _lookTarget = rock; // 注视目标近似(校服女 Neck)

        // 包头僵尸(-0.252,0,14.238) 抱 f05 校服女(胸前)(1s 报人循环→idle 近似)
        var bao = BuildWalker("BaotouNPC",
            "res://assets/models/monsters/baotou/baotou_anims.tres",
            "res://assets/models/monsters/baotou/Chr_Zcharacter_01.FBX",
            "res://assets/models/monsters/baotou/baotou_mat.tres",
            new Vector3(-0.252f, 0.0f, 14.238f), "anim_idle");
        bao.Scale = Vector3.One * 1.2f;
        AttachGirl(bao,
            "res://assets/models/actors/f05_schoolwear/f05_schoolwear_200_m.fbx",
            new Vector3(0.0f, 1.15f, -0.55f), 4.0f);

        // 士兵坏人(-0.674,0,18.752) 抱剑女孩(左臂前托)(Body_A/Legs_B/head_C/rpg_rocket)
        var soldier = BuildSoldier(new Vector3(-0.674f, 0.0f, 18.752f));
        AttachGirl(soldier,
            "res://assets/models/actors/blade_girl/blade_girl.FBX",
            new Vector3(-0.35f, 0.95f, -0.4f), 4.0f);
    }

    private Node3D BuildWalker(string name, string animsPath, string modelPath,
        string matPath, Vector3 pos, string animName)
    {
        var root = new Node3D { Name = name, Position = pos };
        AddChild(root);
        var model = GD.Load<PackedScene>(modelPath).Instantiate<Node3D>();
        model.Name = "Model";
        model.Rotation = new Vector3(0, Mathf.Pi, 0); // Unity +Z 前 → Godot -Z 前
        root.AddChild(model);
        OverrideMaterial(model, matPath);
        var ap = new AnimationPlayer { Name = "AnimationPlayer" };
        root.AddChild(ap);
        var lib = GD.Load<AnimationLibrary>(animsPath);
        ap.AddAnimationLibrary("", lib);
        ap.Play(animName); // 先播,Play() 后由 PlayIntro 统一重播
        _animPlayers.Add(ap);
        return root;
    }

    private Node3D BuildSoldier(Vector3 pos)
    {
        var root = new Node3D { Name = "SoldierBad", Position = pos };
        AddChild(root);
        var model = GD.Load<PackedScene>(
            "res://assets/models/monsters/toon/ToonSoldiers_Militias.FBX").Instantiate<Node3D>();
        model.Name = "Model";
        model.Rotation = new Vector3(0, Mathf.Pi, 0);
        root.AddChild(model);
        // 模块化部件:只留 Body_A / Legs_B / head_C / weapon_rpg_rocket(照 school_day 激活表)
        foreach (var n in model.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (n is not MeshInstance3D mi)
                continue;
            string nm = mi.Name.ToString();
            bool isPart = nm.StartsWith("Body_") || nm.StartsWith("Legs_")
                || nm.StartsWith("head_") || nm.StartsWith("weapon_") || nm.StartsWith("extra_");
            if (!isPart)
                continue;
            bool keep = nm is "Body_A" or "Legs_B" or "head_C" or "weapon_rpg_rocket";
            mi.Visible = keep;
        }
        OverrideMaterial(model, "res://assets/models/monsters/toon/toon_mat.tres");
        var ap = new AnimationPlayer { Name = "AnimationPlayer" };
        root.AddChild(ap);
        ap.AddAnimationLibrary("", GD.Load<AnimationLibrary>(
            "res://assets/models/monsters/toon/toon_anims.tres"));
        ap.Play("locomotion", 0.2);
        _animPlayers.Add(ap);
        return root;
    }

    /// <summary>把女生模型挂到载体节点下(视觉调位;被抱动画为 humanoid 肌肉曲线无法转换,
    /// 绑定姿态近似,README 已记偏差)</summary>
    private static void AttachGirl(Node3D carrier, string girlScene,
        Vector3 localPos, float scale)
    {
        var girl = GD.Load<PackedScene>(girlScene).Instantiate<Node3D>();
        girl.Name = "CarriedGirl";
        carrier.AddChild(girl);
        girl.Position = localPos;
        girl.Scale = Vector3.One * scale;
    }

    private static Node? FindDescendant(Node root, string name)
    {
        if (root.Name == name)
            return root;
        foreach (var c in root.GetChildren())
        {
            var hit = FindDescendant(c, name);
            if (hit != null)
                return hit;
        }
        return null;
    }

    private static void OverrideMaterial(Node node, string matPath)
    {
        if (!ResourceLoader.Exists(matPath))
            return;
        var mat = GD.Load<Material>(matPath);
        foreach (var mi in node.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (mi is MeshInstance3D m && m.Visible)
                m.MaterialOverride = mat;
        }
    }
}
