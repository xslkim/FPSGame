using Godot;

namespace FPSGame;

/// <summary>
/// K-POP 舞蹈烘焙回放器:Unity 侧 BakeKpopDance.cs 把 humanoid 肌肉剪辑逐帧烘成全骨架局部 TRS
/// (.kdance.bin),这里按帧插值回放。坐标链:烘焙局部链 → Unity 全局 → 镜像 X(项目 FBX 导入
/// 与 Unity 的既定手性差)→ 父全局⁻¹ 得 Godot 局部(本引擎 bone pose=绝对局部变换)。
/// 数值校验:f05 t=5s 六骨骼世界坐标与 Unity 逐位一致(tools/dance_bake/ 对比记录)。
/// 根运动(node[0]=模型根)镜像后叠加到 motionRoot(保持放置朝向的基准变换 × 运动)。
/// </summary>
public partial class KDancePlayer : Node
{
    public double StartTime = 2.9667; // 原作 StoryStartTimeline 舞蹈轨起始(2.9667/2.9833/3.0333 错落)
    public double Phase;              // 舞者相位偏移
    /// <summary>循环播放(开场背负等短循环剪辑);LoopTime<=0 时按数据时长循环</summary>
    public bool Loop;
    public double LoopTime;
    /// <summary>应用 node[0] 根运动到 motionRoot(剧情舞蹈=true;开场背负=false,姿态全在骨骼里)</summary>
    public bool ApplyRootMotion = true;
    public double Duration => _data != null ? (_data.Frames - 1) / (double)_data.Fps : 0.0;

    private KDanceData _data = null!;
    private Skeleton3D _sk = null!;
    private Node3D _motionRoot = null!;
    private Transform3D _motionBase;
    private int[] _boneNode = System.Array.Empty<int>(); // 骨骼 → 烘焙节点
    private Transform3D[] _gU = System.Array.Empty<Transform3D>();
    private Transform3D[] _gG = System.Array.Empty<Transform3D>();
    private double _selfClock;
    private bool _selfPlaying;

    /// <summary>挂载到角色:model=FBX 实例根(含 Skeleton3D);motionRoot=接收根运动的节点(=model);
    /// motionBase=motionRoot 的基准局部变换(放置朝向);缺数据返回 null(回退旧动画)。</summary>
    public static KDancePlayer? TryCreate(Node3D model, string binPath, Transform3D motionBase)
    {
        var data = KDanceData.Load(binPath); // res:// 路径(FileAccess 内部处理,导出包读 pck)
        if (data == null)
        {
            GD.PushWarning($"[KDance] 无烘焙数据 {binPath},回退循环动画");
            return null;
        }
        var sk = model.FindChild("Skeleton3D", true, false) as Skeleton3D;
        if (sk == null)
        {
            GD.PushWarning($"[KDance] {model.Name} 无 Skeleton3D");
            return null;
        }
        var p = new KDancePlayer { Name = "KDancePlayer", _data = data, _sk = sk };
        p._motionRoot = model;
        p._motionBase = motionBase;
        p._gU = new Transform3D[data.NodeCount];
        p._gG = new Transform3D[data.NodeCount];
        // 骨骼名 → 烘焙节点(末段名匹配)
        var bones = new System.Collections.Generic.List<int>();
        for (int b = 0; b < sk.GetBoneCount(); b++)
            bones.Add(FindNode(data, sk.GetBoneName(b)));
        p._boneNode = bones.ToArray();
        int matched = 0;
        foreach (var i in bones)
            if (i >= 0)
                matched++;
        GD.Print($"[KDance] {model.Name}: bones={sk.GetBoneCount()} matched={matched} frames={data.Frames}");
        model.AddChild(p);
        return p;
    }

    /// <summary>剧情时钟驱动:clock 为场景时钟(秒),内部换算成舞蹈时间并应用</summary>
    public void SetStoryTime(double clock)
    {
        if (_data == null)
            return;
        double t = clock - StartTime + Phase;
        if (Loop)
        {
            double lt = LoopTime > 0.0 ? LoopTime : Duration;
            t = t > 0.0 ? t % lt : 0.0;
        }
        Apply(System.Math.Clamp(t, 0.0, Duration));
    }

    /// <summary>自走时模式(开场背负):Play 起内部时钟推进,循环播放</summary>
    public void Play()
    {
        _selfClock = 0.0;
        _selfPlaying = true;
    }

    public void Stop() => _selfPlaying = false;

    public override void _Process(double delta)
    {
        if (!_selfPlaying || _data == null)
            return;
        _selfClock += delta;
        double t = _selfClock;
        if (Loop)
        {
            double lt = LoopTime > 0.0 ? LoopTime : Duration;
            t %= lt;
        }
        Apply(System.Math.Clamp(t, 0.0, Duration));
    }

    private void Apply(double time)
    {
        var d = _data;
        float ft = (float)time * d.Fps;
        int f0 = Mathf.Clamp((int)ft, 0, d.Frames - 1);
        int f1 = Mathf.Min(f0 + 1, d.Frames - 1);
        float alpha = Mathf.Clamp(ft - f0, 0.0f, 1.0f);
        int n = d.NodeCount;
        int o0 = f0 * n, o1 = f1 * n;
        // 1) 插值出 Unity 局部 → 链乘 Unity 全局
        for (int i = 0; i < n; i++)
        {
            var pos = d.Pos[o0 + i].Lerp(d.Pos[o1 + i], alpha);
            var rot = d.Rot[o0 + i].Normalized().Slerp(d.Rot[o1 + i].Normalized(), alpha);
            var scl = d.Scl[o0 + i].Lerp(d.Scl[o1 + i], alpha);
            var l = new Transform3D(new Basis(rot).Scaled(scl), pos);
            _gU[i] = d.Parents[i] >= 0 ? _gU[d.Parents[i]] * l : l;
        }
        // 2) 镜像 X 到 Godot(M·T·M⁻¹:pos(-x,y,z),quat(x,-y,-z,w))
        for (int i = 0; i < n; i++)
        {
            var t = _gU[i];
            var q = t.Basis.GetRotationQuaternion().Normalized();
            _gG[i] = new Transform3D(
                new Basis(new Quaternion(q.X, -q.Y, -q.Z, q.W)).Scaled(t.Basis.Scale),
                new Vector3(-t.Origin.X, t.Origin.Y, t.Origin.Z));
        }
        // 3) 骨骼局部 = 父全局⁻¹ × 本全局(父骨无烘焙节点时回退烘焙父链)
        for (int b = 0; b < _boneNode.Length; b++)
        {
            int i = _boneNode[b];
            if (i < 0)
                continue;
            int pb = _sk.GetBoneParent(b);
            int pi = pb >= 0 && pb < _boneNode.Length && _boneNode[pb] >= 0 ? _boneNode[pb] : d.Parents[i];
            if (pi < 0)
                continue; // 根骨无父:保持 rest(根运动由 motionRoot 承担)
            var local = _gG[pi].AffineInverse() * _gG[i];
            _sk.SetBonePosePosition(b, local.Origin);
            _sk.SetBonePoseRotation(b, local.Basis.GetRotationQuaternion().Normalized());
            _sk.SetBonePoseScale(b, local.Basis.Scale);
        }
        // 4) 根运动:node[0] Unity 全局(=其局部)镜像 × 基准放置
        if (ApplyRootMotion)
        {
            var m = _gG[0];
            _motionRoot.Transform = _motionBase * new Transform3D(m.Basis, m.Origin);
        }
    }

    private static int FindNode(KDanceData data, string name)
    {
        for (int i = 0; i < data.NodeCount; i++)
            if (data.Names[i].GetFile() == name)
                return i;
        return -1;
    }
}
