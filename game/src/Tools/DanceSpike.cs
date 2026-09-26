using Godot;
using System.Collections.Generic;


namespace FPSGame;

/// <summary>
/// 舞蹈烘焙数据(.kdance.bin,Unity BakeKpopDance 落盘):
/// magic"KDAN"/ver/fps/frames/nodes → nodes×[name,parent] → frames×nodes×[pos3 rot4 scale3](frame-major)。
/// </summary>
public sealed class KDanceData
{
    public float Fps;
    public int Frames;
    public string[] Names = System.Array.Empty<string>();
    public int[] Parents = System.Array.Empty<int>();
    public Vector3[] Pos = System.Array.Empty<Vector3>();   // frame*nodes + node
    public Quaternion[] Rot = System.Array.Empty<Quaternion>();
    public Vector3[] Scl = System.Array.Empty<Vector3>();

    public int NodeCount => Names.Length;

    public static KDanceData? Load(string path)
    {
        // 用 Godot FileAccess(编辑器读文件系统、导出包读 pck 内嵌,与 SaveService 同一约定)
        if (!FileAccess.FileExists(path))
            return null;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null)
            return null;
        if (f.Get32() != 0x4E41444B)
            return null;
        f.Get32(); // version
        var d = new KDanceData { Fps = f.GetFloat(), Frames = (int)f.Get32() };
        int nodes = (int)f.Get32();
        d.Names = new string[nodes];
        d.Parents = new int[nodes];
        for (int i = 0; i < nodes; i++)
        {
            ushort len = f.Get16();
            d.Names[i] = System.Text.Encoding.UTF8.GetString(f.GetBuffer(len));
            d.Parents[i] = (int)f.Get32();
        }
        int n = d.Frames * nodes;
        d.Pos = new Vector3[n];
        d.Rot = new Quaternion[n];
        d.Scl = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            d.Pos[i] = new Vector3(f.GetFloat(), f.GetFloat(), f.GetFloat());
            d.Rot[i] = new Quaternion(f.GetFloat(), f.GetFloat(), f.GetFloat(), f.GetFloat());
            d.Scl[i] = new Vector3(f.GetFloat(), f.GetFloat(), f.GetFloat());
        }
        return d;
    }
}

/// <summary>
/// 舞蹈 spike/回放验证工具(不进游戏流程):
///   --dance-dump:<fbx>            只打印骨架骨骼名/rest 局部 TRS
///   --dance-bin:&lt;bin&gt; --dance-model:&lt;fbx&gt; --dance-frame:N --dance-conv:M --shot:&lt;png&gt;
///     应用第 N 帧骨骼姿态(转换模式 M),摆好相机灯光截图退出。
/// 转换模式:0=原样 1=镜像Z(pos z 取反,quat z,w 取反) 2=镜像X(pos x 取反,quat x,w 取反) 3=镜像Y。
/// </summary>
public partial class DanceSpike : Node3D
{
    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string bin = GetArg(args, "--dance-bin:");
        string model = GetArg(args, "--dance-model:");
        string dump = GetArg(args, "--dance-dump:");
        if (dump.Length > 0)
        {
            DumpSkeleton(dump);
            GetTree().Quit();
            return;
        }
        if (bin.Length == 0 || model.Length == 0)
        {
            GD.Print("[DanceSpike] need --dance-bin + --dance-model");
            GetTree().Quit(1);
            return;
        }
        int frame = int.TryParse(GetArg(args, "--dance-frame:"), out var f) ? f : 0;
        int conv = int.TryParse(GetArg(args, "--dance-conv:"), out var c) ? c : 0;
        string shot = GetArg(args, "--shot:");
        SpikeShot(bin, model, frame, conv, shot);
    }

    private static string GetArg(string[] args, string prefix)
    {
        foreach (var a in args)
            if (a.StartsWith(prefix))
                return a[prefix.Length..];
        return "";
    }

    private void DumpSkeleton(string fbx)
    {
        var inst = GD.Load<PackedScene>(fbx).Instantiate<Node3D>();
        AddChild(inst);
        var sk = FindSkeleton(inst);
        if (sk == null)
        {
            GD.Print("[DanceSpike] no Skeleton3D in " + fbx);
            return;
        }
        GD.Print($"[DanceSpike] {fbx}: {sk.GetBoneCount()} bones");
        for (int i = 0; i < sk.GetBoneCount(); i++)
        {
            int p = sk.GetBoneParent(i);
            var rest = sk.GetBoneRest(i);
            GD.Print($"[DanceSpike] bone[{i}] \"{sk.GetBoneName(i)}\" parent={p} " +
                $"restPos={rest.Origin} restQuat={rest.Basis.GetRotationQuaternion()}");
        }
    }

    private static Skeleton3D? FindSkeleton(Node root) =>
        root.FindChildren("*", "Skeleton3D", true, false).Count > 0
            ? root.FindChild("Skeleton3D", true, false) as Skeleton3D
            : null;

    private async void SpikeShot(string binPath, string modelPath, int frame, int conv, string shotPath)
    {
        var data = KDanceData.Load(binPath);
        var inst = GD.Load<PackedScene>(modelPath).Instantiate<Node3D>();
        AddChild(inst);
        var sk = FindSkeleton(inst);
        if (data == null || sk == null)
        {
            GD.PrintErr("[DanceSpike] load failed");
            GetTree().Quit(1);
            return;
        }
        // 材质接线(同 IntroBadGroup/StoryStart 惯例:FBX 丢贴图,用同名 png)
        var texPath = modelPath.Replace(".fbx", ".png").Replace(".FBX", ".png");
        if (ResourceLoader.Exists(texPath))
        {
            var mat = new StandardMaterial3D { AlbedoTexture = GD.Load<Texture2D>(texPath) };
            foreach (var n in inst.FindChildren("*", "MeshInstance3D", true, false))
                if (n is MeshInstance3D mi)
                    mi.MaterialOverride = mat;
        }
        // 根运动:烘焙 node[0]=模型根(applyRootMotion 累积位移),镜像后应用到模型根节点
        int convRoot = conv switch { 1 or 5 => 1, 2 or 4 => 2, 3 => 3, _ => 0 };
        inst.Position = ConvPos(data.Pos[frame * data.NodeCount], convRoot);
        inst.Quaternion = ConvRot(data.Rot[frame * data.NodeCount], convRoot);
        ApplyFrame(sk, data, frame, conv);
        // 关键骨骼世界坐标(与 Unity 侧 [BakeKpop] POSE 数值对比)
        foreach (var bn in new[] { "Hips", "Head", "LeftHand", "RightHand", "LeftFoot", "RightFoot" })
        {
            int b = sk.FindBone(bn);
            if (b >= 0)
            {
                var w = sk.GlobalTransform * sk.GetBoneGlobalPose(b);
                GD.Print($"[DanceSpike] POSE t={(frame / 30.0):0.00} {bn} world=({w.Origin.X:0.####},{w.Origin.Y:0.####},{w.Origin.Z:0.####})");
            }
        }
        // 相机/灯光(与 Unity RenderPose 同机位)
        var cam = new Camera3D { Name = "Cam", Current = true };
        AddChild(cam);
        cam.Position = new Vector3(0, 1.1f, 2.6f);
        cam.LookAt(new Vector3(0, 0.9f, 0), Vector3.Up);
        AddChild(new DirectionalLight3D { Rotation = new Vector3(-0.8f, 0.4f, 0) });
        AddChild(new WorldEnvironment { Environment = new Environment { BackgroundMode = Environment.BGMode.Color, BackgroundColor = new Color(0.2f, 0.2f, 0.25f) } });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (shotPath.Length > 0)
        {
            GetViewport().GetTexture().GetImage().SavePng(shotPath.Replace('/', '\\'));
            GD.Print($"[DanceSpike] shot saved: {shotPath} frame={frame} conv={conv}");
        }
        GetTree().Quit();
    }

    /// <summary>应用烘焙帧到骨架。conv:0~3=局部直换(原样/镜像Z/镜像X/镜像Y);
    /// 4=全局链镜像X(局部链→Unity全局→镜像→Godot 局部,鲁棒于逐骨轴差异);
    /// 5=全局链镜像Z。本引擎 bone pose = 绝对局部变换(引擎源码 skeleton_3d.cpp 已核)。</summary>
    private void ApplyFrame(Skeleton3D sk, KDanceData data, int frame, int conv)
    {
        frame = Mathf.Clamp(frame, 0, data.Frames - 1);
        if (conv < 4)
        {
            ApplyFrameLocal(sk, data, frame, conv);
            return;
        }
        bool mirrorX = conv == 4;
        int n = data.NodeCount;
        int ofs = frame * n;
        // 1) Unity 局部链 → Unity 全局(烘焙为先序,父先于子)
        var gU = new Transform3D[n];
        for (int i = 0; i < n; i++)
        {
            var l = new Transform3D(new Basis(data.Rot[ofs + i]).Scaled(data.Scl[ofs + i]), data.Pos[ofs + i]);
            gU[i] = data.Parents[i] >= 0 ? gU[data.Parents[i]] * l : l;
        }
        // 2) 镜像到 Godot 全局(手性映射 M·T·M⁻¹;M=diag(-1,1,1) 或 (1,1,-1))
        var gG = new Transform3D[n];
        for (int i = 0; i < n; i++)
            gG[i] = MirrorXform(gU[i], mirrorX);
        // 3) 骨骼:Godot 局部 = 父全局⁻¹ × 本全局(父骨无烘焙数据时用 Godot rest 全局兜底)
        int matched = 0, missed = 0;
        for (int b = 0; b < sk.GetBoneCount(); b++)
        {
            int i = FindNode(data, sk.GetBoneName(b));
            if (i < 0)
            {
                missed++;
                continue;
            }
            matched++;
            Transform3D parentG;
            int pb = sk.GetBoneParent(b);
            int pi = pb >= 0 ? FindNode(data, sk.GetBoneName(pb)) : data.Parents[i];
            if (pi >= 0)
                parentG = gG[pi];
            else
                parentG = sk.GlobalTransform; // 骨架根之外:骨架节点全局(模型原点)
            var local = parentG.AffineInverse() * gG[i];
            sk.SetBonePosePosition(b, local.Origin);
            sk.SetBonePoseRotation(b, local.Basis.GetRotationQuaternion().Normalized());
            sk.SetBonePoseScale(b, local.Basis.Scale);
        }
        GD.Print($"[DanceSpike] apply frame {frame} conv={conv}: matched={matched} missed={missed}");
    }

    private void ApplyFrameLocal(Skeleton3D sk, KDanceData data, int frame, int conv)
    {
        int matched = 0, missed = 0;
        for (int i = 0; i < data.NodeCount; i++)
        {
            string boneName = data.Names[i].GetFile(); // 末段名(Unity 路径末段=骨骼名)
            int b = sk.FindBone(boneName);
            if (b < 0)
            {
                missed++;
                continue;
            }
            matched++;
            int k = frame * data.NodeCount + i;
            sk.SetBonePosePosition(b, ConvPos(data.Pos[k], conv));
            sk.SetBonePoseRotation(b, ConvRot(data.Rot[k], conv));
            sk.SetBonePoseScale(b, data.Scl[k]);
        }
        GD.Print($"[DanceSpike] apply frame {frame} conv={conv} (local): matched={matched} missed={missed}");
    }

    private static int FindNode(KDanceData data, string name)
    {
        for (int i = 0; i < data.NodeCount; i++)
            if (data.Names[i].GetFile() == name)
                return i;
        return -1;
    }

    /// <summary>手性镜像:M·T·M⁻¹。mirrorX: pos(-x,y,z),quat(x,-y,-z,w);否则镜像Z: pos(x,y,-z),quat(-x,-y,z,w)</summary>
    private static Transform3D MirrorXform(Transform3D t, bool mirrorX)
    {
        var q = t.Basis.GetRotationQuaternion().Normalized();
        var s = t.Basis.Scale;
        if (mirrorX)
            return new Transform3D(new Basis(new Quaternion(q.X, -q.Y, -q.Z, q.W)).Scaled(s),
                new Vector3(-t.Origin.X, t.Origin.Y, t.Origin.Z));
        return new Transform3D(new Basis(new Quaternion(-q.X, -q.Y, q.Z, q.W)).Scaled(s),
            new Vector3(t.Origin.X, t.Origin.Y, -t.Origin.Z));
    }

    public static Vector3 ConvPos(Vector3 v, int conv) => conv switch
    {
        1 => new Vector3(v.X, v.Y, -v.Z),
        2 => new Vector3(-v.X, v.Y, v.Z),
        3 => new Vector3(v.X, -v.Y, v.Z),
        _ => v,
    };

    public static Quaternion ConvRot(Quaternion q, int conv) => conv switch
    {
        1 => new Quaternion(q.X, q.Y, -q.Z, -q.W),  // 镜像 Z(GunMath NegZw)
        2 => new Quaternion(-q.X, q.Y, q.Z, -q.W),  // 镜像 X(GunMath NegXw)
        3 => new Quaternion(q.X, -q.Y, q.Z, -q.W),  // 镜像 Y
        _ => q,
    };
}
