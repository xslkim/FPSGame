using Godot;

namespace FPSGame;

/// <summary>
/// 开场 BadGroup 演出(1:1 SchoolNightTimeline.playable,真值 tools/research/battle_audit/intro_spec.md):
/// 0~2.7286s 全员隐藏(Activation Track 1),相机持 vcam1 初始姿态;2.7286s 激活。
/// 演员(局部 rot 全 identity,面朝 +Z 迎相机,FBX 原生朝向不做 Y 旋转):
/// RockWarrior(0.5,0,15.957) 空手跑(背上没有人);包头僵尸(-0.252,0,14.238) scale 1,
/// f05 校服女挂其 Bone_ R UpperArm(真值 local TRS);士兵坏人(-0.674,0,18.752) scale 0.7,
/// 剑女孩挂其 Bip001 L UpperArm(scale 1.4286=1/0.7 抵消父缩放),右手 Bip001 R Hand 持沙漠之鹰。
/// 根运动 z=-4+clip(t)(clip:≤2.95s Hold -5 → 10.05s 10.194186 → 21s 33.693916,pre/post Hold);
/// 0.09s 尖叫4、11.7s 救救我;相机=vcam1:机位固定 (1.35,1.86,22.44) FOV30,每帧硬盯 f05 Neck
/// (近似点=组根+(0.1475,1.3899,14.0886),Composer 无 damping)。19s(Level1)调 Stop() 收场。
/// humanoid 肌肉曲线(报人/换人抱/黄头发被抱)无法直转:僵尸 anim_idle、士兵/石头人 locomotion、
/// f05 dance 挣扎近似(README 已记偏差)。
/// </summary>
public partial class IntroBadGroup : Node3D
{
    public const double ActivateTime = 2.7286;
    public const double ScreamTime = 0.09;
    public const double HelpTime = 11.7;

    /// <summary>vcam 切镜表条目(Cinemachine shot,无 blend,切镜即瞬切)</summary>
    private readonly struct VcamShot
    {
        public readonly double Time;     // 生效时刻(s)
        public readonly Vector3 Pos;     // 机位(vcam1 Follow=0 → 全程固定)
        public readonly Quaternion Quat; // 朝向(TrackNeck=false 阶段用)
        public readonly float Fov;
        public readonly bool TrackNeck;  // true=每帧硬盯 Neck(Composer 默认硬盯,无平滑)

        public VcamShot(double time, Vector3 pos, Quaternion quat, float fov, bool trackNeck)
        {
            Time = time;
            Pos = pos;
            Quat = quat;
            Fov = fov;
            TrackNeck = trackNeck;
        }
    }

    // vcam1 真值(intro_spec §2.1):pos (1.35,1.86,22.44);初始朝向 Unity
    // (-0.0009182,0.9986439,-0.0189072,-0.0484978) 按 (x,y,z,w)→(z,w,-x,-y) 换算;FOV 30
    private static readonly Vector3 VcamPos = new(1.35f, 1.86f, 22.44f);
    private static readonly Quaternion VcamInitQuat = new(-0.0189072f, -0.0484978f, 0.0009182f, -0.9986439f);

    // SchoolNightTimeline 运镜轨(intro_spec §1.1 轨8):全程仅一个 shot(CM vcam1,2.7286→21.05s)
    private static readonly VcamShot[] VcamShots =
    {
        new(0.0, VcamPos, VcamInitQuat, 30.0f, false),          // Cinemachine 轨开始前:初始姿态
        new(ActivateTime, VcamPos, VcamInitQuat, 30.0f, true),  // CM vcam1:每帧硬盯 Neck
    };

    // f05 Neck 相对组根的偏移(intro_spec §5.2;组无旋转,全局位置=组原点+此偏移)
    private static readonly Vector3 NeckLocalOffset = new(0.1475f, 1.3899f, 14.0886f);

    private double _clock;
    private bool _playing;
    private bool _activated;
    private bool _screamPlayed;
    private bool _helpPlayed;
    private Camera3D? _camera;
    private Node3D _lookTarget = null!;
    private AudioStreamPlayer _screamPlayer = null!;
    private AudioStreamPlayer _helpPlayer = null!;
    private readonly System.Collections.Generic.List<(AnimationPlayer Player, string Clip)> _animPlayers = new();

    public override void _Ready()
    {
        Visible = false;
        _lookTarget = new Node3D { Name = "NeckLookTarget", Position = NeckLocalOffset };
        AddChild(_lookTarget);
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
        _activated = false;
        _screamPlayed = false;
        _helpPlayed = false;
        Visible = false; // 真值:Activation Track 2.7286s 才激活,0~2.729s 隐藏
        Position = new Vector3(0.0f, 0.0f, -9.0f); // clip pre-Hold(-5) + infinite clip offset(-4)
        ApplyCamera(); // 首帧 vcam 初始姿态
    }

    public void Stop()
    {
        _playing = false;
        Visible = false;
        foreach (var (ap, _) in _animPlayers)
            ap.Stop();
    }

    public override void _Process(double delta)
    {
        if (!_playing)
            return;
        _clock += delta;
        // 音频(轨道3:尖叫4 @0.0902s;轨道10:救救我 @11.7s)
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
        // 根运动(轨道7 Recorded infinite clip,offset (0,0,-4),pre/post extrapolation=Hold)
        float clipZ = _clock switch
        {
            <= 2.95 => -5.0f,
            <= 10.05 => -5.0f + 15.194186f * (float)((_clock - 2.95) / 7.10),
            <= 21.0 => 10.194186f + 23.49973f * (float)((_clock - 10.05) / 10.95),
            _ => 33.693916f,
        };
        Position = new Vector3(0.0f, 0.0f, -4.0f + clipZ);
        // Activation Track 1:2.7286s 激活;轨道4/5/6(跑/报人/换人抱)与 f05 自播同刻起播
        if (!_activated && _clock >= ActivateTime)
        {
            _activated = true;
            Visible = true;
            foreach (var (ap, clip) in _animPlayers)
                ap.Play(clip, 0.2);
        }
        ApplyCamera();
    }

    /// <summary>vcam 切镜表:取当前时刻生效的 shot 瞬切;TrackNeck 阶段每帧硬盯 f05 Neck</summary>
    private void ApplyCamera()
    {
        if (_camera == null)
            return;
        var shot = VcamShots[0];
        for (int i = 1; i < VcamShots.Length; i++)
            if (_clock >= VcamShots[i].Time)
                shot = VcamShots[i];
        _camera.GlobalPosition = shot.Pos;
        if (!Mathf.IsEqualApprox(_camera.Fov, shot.Fov))
            _camera.Fov = shot.Fov;
        if (shot.TrackNeck)
            _camera.LookAt(_lookTarget.GlobalPosition, Vector3.Up);
        else
            _camera.Quaternion = shot.Quat; // 相机挂在关卡根下(identity),局部=全局
    }

    // ------------------------------------------------ NPC 组装

    private void BuildNpcs()
    {
        // RockWarrior(0.5,0,15.957):run,空手——背上/手上没有任何人(真值 §4.1)
        BuildWalker("RockWarrior",
            "res://assets/models/monsters/rock_warrior/rock_warrior_anims.tres",
            "res://assets/models/monsters/rock_warrior/RockWarrior.FBX",
            "res://assets/models/monsters/rock_warrior/rock_warrior_mat.tres",
            new Vector3(0.5f, 0.0f, 15.957f), "locomotion");

        // 包头僵尸(-0.252,0,14.238) scale 1;报人(弯腰抱人)humanoid 无法直转 → anim_idle 近似
        var bao = BuildWalker("BaotouNPC",
            "res://assets/models/monsters/baotou/baotou_anims.tres",
            "res://assets/models/monsters/baotou/Chr_Zcharacter_01.FBX",
            "res://assets/models/monsters/baotou/baotou_mat.tres",
            new Vector3(-0.252f, 0.0f, 14.238f), "anim_idle");
        // f05 校服女挂 Bone_ R UpperArm(真值 local TRS,school_day.unity:122170-178)
        var f05 = AttachToBone(bao, "Bone_ R UpperArm",
            "res://assets/models/actors/f05_schoolwear/f05_schoolwear_200_m.fbx",
            new Vector3(0.235f, -0.051f, 1.099f),
            new Quaternion(-0.0737453f, -0.6580442f, 0.6833673f, 0.3074877f), 1.0f);
        if (f05 != null)
            SetupF05(f05);

        // 士兵坏人(-0.674,0,18.752) scale 0.7(:113016)
        var soldier = BuildSoldier(new Vector3(-0.674f, 0.0f, 18.752f));
        // 剑女孩挂 Bip001 L UpperArm(真值 local TRS;scale 1.4286=1/0.7 抵消父缩放)
        var blade = AttachToBone(soldier, "Bip001 L UpperArm",
            "res://assets/models/actors/blade_girl/blade_girl.FBX",
            new Vector3(-0.273f, 0.635f, -0.953f),
            new Quaternion(-0.8526303f, -0.1896241f, -0.2744347f, -0.4021813f), 1.4286f);
        if (blade != null)
            SetupBladeGirl(blade);
    }

    private Node3D BuildWalker(string name, string animsPath, string modelPath,
        string matPath, Vector3 pos, string animName)
    {
        var root = new Node3D { Name = name, Position = pos };
        AddChild(root);
        var model = GD.Load<PackedScene>(modelPath).Instantiate<Node3D>();
        model.Name = "Model";
        root.AddChild(model); // FBX 原生朝向(面朝 +Z),不旋转(真值局部 rot=identity)
        OverrideMaterial(model, matPath);
        var ap = new AnimationPlayer { Name = "AnimationPlayer" };
        root.AddChild(ap);
        ap.AddAnimationLibrary("", GD.Load<AnimationLibrary>(animsPath));
        _animPlayers.Add((ap, animName)); // 2.7286s 激活时统一从 0 起播(Timeline clip 起点)
        return root;
    }

    private Node3D BuildSoldier(Vector3 pos)
    {
        var root = new Node3D { Name = "SoldierBad", Position = pos, Scale = Vector3.One * 0.7f };
        AddChild(root);
        var model = GD.Load<PackedScene>(
            "res://assets/models/monsters/toon/ToonSoldiers_Militias.FBX").Instantiate<Node3D>();
        model.Name = "Model";
        root.AddChild(model);
        // 模块化部件:只留 Body_A / Legs_B / head_C(真值激活表,其余部件全 inactive);
        // 身体贴图 Militias_B.tga(TS_militia_B.mat)
        var bodyMat = GD.Load<Material>("res://assets/models/monsters/toon/toon_militia_b_mat.tres");
        foreach (var n in model.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (n is not MeshInstance3D mi)
                continue;
            bool keep = mi.Name.ToString() is "Body_A" or "Legs_B" or "head_C";
            mi.Visible = keep;
            if (keep)
                mi.SetSurfaceOverrideMaterial(0, bodyMat);
        }
        // 右手沙漠之鹰:Bip001 R Hand → WeaponContainer(真值 local TRS) → weapon_deserteagle
        // scale 0.42284146;武器贴图 militia_weapons_texture(TS_militia_weapons.mat),与身体分开盖
        var sk = model.FindChild("Skeleton3D", true, false) as Skeleton3D;
        const string weaponPath = "res://assets/models/monsters/toon/weapon_deserteagle.FBX";
        if (sk != null && ResourceLoader.Exists(weaponPath))
        {
            var hand = new BoneAttachment3D { Name = "WeaponHand", BoneName = "Bip001 R Hand" };
            sk.AddChild(hand);
            var container = new Node3D
            {
                Name = "WeaponContainer",
                Position = new Vector3(-0.22053517f, 0.024589777f, 0.14772432f),
                Quaternion = new Quaternion(-0.079513475f, 0.663656f, 0.74313986f, -0.031328555f),
            };
            hand.AddChild(container);
            var weapon = GD.Load<PackedScene>(weaponPath).Instantiate<Node3D>();
            weapon.Name = "weapon_deserteagle";
            weapon.Scale = Vector3.One * 0.42284146f;
            container.AddChild(weapon);
            OverrideMaterial(weapon, "res://assets/models/monsters/toon/toon_weapon_mat.tres");
        }
        var ap = new AnimationPlayer { Name = "AnimationPlayer" };
        root.AddChild(ap);
        ap.AddAnimationLibrary("", GD.Load<AnimationLibrary>(
            "res://assets/models/monsters/toon/toon_anims.tres"));
        _animPlayers.Add((ap, "locomotion")); // 换人抱.anim(humanoid)无法直转 → locomotion 近似
        return root;
    }

    /// <summary>把被抱者挂到骨骼上(BoneAttachment3D),local TRS 照抄 Unity 场景序列化值</summary>
    private static Node3D? AttachToBone(Node3D carrier, string bone, string scene,
        Vector3 localPos, Quaternion localRot, float scale)
    {
        var sk = carrier.FindChild("Skeleton3D", true, false) as Skeleton3D;
        if (sk == null || !ResourceLoader.Exists(scene))
        {
            GD.PushWarning($"[IntroBadGroup] 挂骨失败: bone={bone} scene={scene}");
            return null;
        }
        var attach = new BoneAttachment3D { Name = "Carry_" + bone, BoneName = bone };
        sk.AddChild(attach);
        var carried = GD.Load<PackedScene>(scene).Instantiate<Node3D>();
        carried.Name = "Carried";
        attach.AddChild(carried);
        carried.Position = localPos;
        carried.Quaternion = localRot;
        carried.Scale = Vector3.One * scale;
        return carried;
    }

    /// <summary>f05 材质(FBX 导入丢贴图;真值 4 槽全 Unlit/Texture:槽0/1 校服、槽2/3 脸/发,
    /// 按 surface 名匹配)+ 黄头发被抱.anim 无法直转 → dance 近似挣扎</summary>
    private void SetupF05(Node3D f05)
    {
        var clothMat = MakeUnlitMat("res://assets/models/actors/f05_schoolwear/f05_schoolwear_200_m.png");
        var faceMat = MakeUnlitMat("res://assets/models/actors/f05_schoolwear/f05_face_00_m.png");
        foreach (var n in f05.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (n is not MeshInstance3D mi || mi.Mesh is not ArrayMesh mesh)
                continue;
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                string sn = mesh.SurfaceGetName(s).ToString();
                mi.SetSurfaceOverrideMaterial(s,
                    sn.Contains("face") || sn.Contains("hair") ? faceMat : clothMat);
            }
        }
        var ap = f05.FindChild("AnimationPlayer", true, false) as AnimationPlayer;
        const string animsPath = "res://assets/models/actors/f05_schoolwear/f05_schoolwear_anims.tres";
        if (ap != null && ResourceLoader.Exists(animsPath))
        {
            if (ap.HasAnimationLibrary(""))
                ap.RemoveAnimationLibrary("");
            ap.AddAnimationLibrary("", GD.Load<AnimationLibrary>(animsPath));
            _animPlayers.Add((ap, "dance"));
        }
    }

    /// <summary>剑女孩:只留 headusOBJexport008(身体);headusOBJexport009(武器)/Object001
    /// 真值 inactive=0;Blade_Girl_Ex.mat 为 Unlit/Texture → Unshaded 贴图覆盖;无 Animator(绑定姿态)</summary>
    private void SetupBladeGirl(Node3D blade)
    {
        var mat = MakeUnlitMat("res://assets/models/actors/blade_girl/blade_girl_base.png");
        foreach (var n in blade.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (n is not MeshInstance3D mi || mi.Mesh == null)
                continue;
            bool keep = mi.Name == "headusOBJexport008";
            mi.Visible = keep;
            if (keep)
                for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                    mi.SetSurfaceOverrideMaterial(s, mat);
        }
    }

    /// <summary>Unlit/Texture(fileID 14)等价:Unshaded + albedo 贴图(雾照常生效)</summary>
    private static StandardMaterial3D MakeUnlitMat(string texPath)
    {
        return new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoTexture = GD.Load<Texture2D>(texPath),
        };
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
