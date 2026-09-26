using Godot;

namespace FPSGame;

/// <summary>Unity 烘焙(.kdance.bin)→ Godot AnimationLibrary(.tres)离线转换器:
/// 数学链与 KDancePlayer.Apply 一致(Unity 局部链→全局→镜像 X→父全局⁻¹ 得骨骼局部),
/// 逐帧计算后写成 Animation 轨道(position_3d/rotation_3d,路径 = 骨骼名),
/// 输出 AnimationLibrary 覆盖到怪物场景引用的 tres。
/// 用法: --bin2tres:bull   (读 assets/models/monsters/bull/baked/bull_<clip>.kdance.bin,
///   依据内置剪辑表映射到剪辑名,写 assets/models/monsters/bull/bull_anims.tres)</summary>
public static class BinToTres
{
    private static Node _sceneRoot; // 怪物场景实例根(计算轨道相对路径)
    /// <summary>每怪的烘焙剪辑表:输出剪辑名 → bin 文件名(不含目录/扩展) + 是否循环</summary>
    private static readonly System.Collections.Generic.Dictionary<string, (string Bin, bool Loop, string Out)[]> Jobs = new()
    {
        ["bull"] = new[]
        {
            ("idle", true, "idle"),
            ("walk", true, "locomotion"),   // 原作 Locomotion 混合树 Speed=1 → walk 剪辑
            ("attack_01", false, "attack_01"),
            ("attack_02", false, "attack_02"),
            ("attack_03", false, "attack_03"),
            ("damage", false, "damage"),
            ("die", false, "die"),
        },
    };

    public static void Run(string monsterKey)
    {
        if (!Jobs.TryGetValue(monsterKey, out var clips))
        {
            GD.PrintErr($"[B2T] no job table for {monsterKey}");
            return;
        }
        // 实例化怪物场景取骨架(骨骼名/父链)
        var inst = GD.Load<PackedScene>($"res://scenes/battle/monsters/{monsterKey}.tscn").Instantiate<Node3D>();
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(inst);
        _sceneRoot = inst;
        var sk = inst.FindChild("Skeleton3D", true, false) as Skeleton3D;
        if (sk == null)
        {
            GD.PrintErr("[B2T] no skeleton");
            return;
        }
        var lib = new AnimationLibrary();
        foreach (var (bin, loop, outName) in clips)
        {
            string path = $"G:/FPSGame/tools/bull_bake/{monsterKey}_{bin}.kdance.bin"; // 烘焙产物(不入包,编辑器离线转换用)
            var data = KDanceData.Load(path);
            if (data == null)
            {
                GD.PrintErr($"[B2T] missing {path}");
                continue;
            }
            var anim = Convert(data, sk, outName, loop);
            lib.AddAnimation(outName, anim);
            GD.Print($"[B2T] {outName} ← {path}: frames={data.Frames} len={anim.Length:0.##}s tracks={anim.GetTrackCount()}");
        }
        string outPath = $"res://assets/models/monsters/{monsterKey}/{monsterKey}_anims.tres";
        var err = ResourceSaver.Save(lib, outPath);
        GD.Print($"[B2T] saved {outPath} err={err}");
        inst.QueueFree();
    }

    /// <summary>单个 bin → Animation:骨骼局部姿态逐帧写轨道(复刻 KDancePlayer.Apply 的链数学)</summary>
    private static Animation Convert(KDanceData d, Skeleton3D sk, string name, bool loop)
    {
        int n = d.NodeCount;
        int boneCount = sk.GetBoneCount();
        // 骨骼 → 烘焙节点(末段名匹配,同 KDancePlayer.FindNode)
        var boneNode = new int[boneCount];
        for (int b = 0; b < boneCount; b++)
            boneNode[b] = FindNode(d, sk.GetBoneName(b));
        int matched = 0;
        foreach (var i in boneNode)
            if (i >= 0)
                matched++;
        GD.Print($"[B2T] {name}: bones={boneCount} matched={matched} nodes={n}");
        // 骨骼局部(含根骨):父全局⁻¹×本全局;父链优先骨架父骨,未匹配回退烘焙父链(同 KDancePlayer 第 3 步);
        // 根骨(骨架无父):用烘焙父链全局(bull_king_root 等)→ 相对它取局部,等价骨架父系
        var gU = new Transform3D[n];
        var gG = new Transform3D[n];
        var anim = new Animation { ResourceName = name, LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None };
        double dt = 1.0 / d.Fps;
        anim.Length = (d.Frames - 1) * dt;
        // 轨道相对路径:bull.tscn 里 AnimationPlayer 挂场景根(RootNode=..)→ 场景根到 Skeleton3D 的相对路径
        string skRel = _sceneRoot != null ? _sceneRoot.GetPathTo(sk).ToString() : sk.GetPath().ToString();
        // 为每根有烘焙节点的骨骼建两条轨道
        var posTrack = new int[boneCount];
        var rotTrack = new int[boneCount];
        for (int b = 0; b < boneCount; b++)
        {
            posTrack[b] = rotTrack[b] = -1;
            if (boneNode[b] < 0 || sk.GetBoneParent(b) < 0)
                continue; // 根骨保持 rest(同 KDancePlayer;其轨道值本就≈rest)
            string boneName = sk.GetBoneName(b);
            posTrack[b] = anim.AddTrack(Animation.TrackType.Position3D);
            anim.TrackSetPath(posTrack[b], new NodePath($"{skRel}:{boneName}"));
            rotTrack[b] = anim.AddTrack(Animation.TrackType.Rotation3D);
            anim.TrackSetPath(rotTrack[b], new NodePath($"{skRel}:{boneName}"));
        }
        for (int f = 0; f < d.Frames; f++)
        {
            int o = f * n;
            for (int i = 0; i < n; i++)
            {
                var l = new Transform3D(new Basis(d.Rot[o + i]).Scaled(d.Scl[o + i]), d.Pos[o + i]);
                gU[i] = d.Parents[i] >= 0 ? gU[d.Parents[i]] * l : l;
            }
            for (int i = 0; i < n; i++)
            {
                var t = gU[i];
                var q = t.Basis.GetRotationQuaternion().Normalized();
                // 镜像 X(M·T·M⁻¹):pos(-x,y,z),quat(x,-y,-z,w)
                gG[i] = new Transform3D(
                    new Basis(new Quaternion(q.X, -q.Y, -q.Z, q.W)).Scaled(t.Basis.Scale),
                    new Vector3(-t.Origin.X, t.Origin.Y, t.Origin.Z));
            }
            double tSec = f * dt;
            for (int b = 0; b < boneCount; b++)
            {
                if (posTrack[b] < 0)
                    continue;
                int i = boneNode[b];
                int pb = sk.GetBoneParent(b);
                int pi = pb >= 0 && pb < boneCount && boneNode[pb] >= 0 ? boneNode[pb] : d.Parents[i];
                if (pi < 0)
                    continue;
                var local = gG[pi].AffineInverse() * gG[i];
                anim.PositionTrackInsertKey(posTrack[b], tSec, local.Origin);
                anim.RotationTrackInsertKey(rotTrack[b], tSec, local.Basis.GetRotationQuaternion().Normalized());
            }
        }
        return anim;
    }

    private static int FindNode(KDanceData data, string name)
    {
        for (int i = 0; i < data.NodeCount; i++)
            if (data.Names[i].GetFile() == name)
                return i;
        return -1;
    }
}
