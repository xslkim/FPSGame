using Godot;

namespace FPSGame;

/// <summary>怪物渲染检视(不进游戏流程):
/// --monster-view:bull:3     实例化 scenes/battle/monsters/bull.tscn 播 locomotion,距 3m 截图
/// 输出 G:/FPSGame/tools/screenshots/fix3/mon_<key>.png</summary>
public partial class MonsterView : Node3D
{
    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string key = "bull";
        float dist = 3.0f;
        string animName = "";
        int gunType = -1;
        foreach (var a in args)
        {
            if (a.StartsWith("--monster-view:"))
            {
                var parts = a["--monster-view:".Length..].Split(':');
                key = parts[0];
                if (parts.Length > 1)
                    float.TryParse(parts[1], out dist);
                if (parts.Length > 2)
                    animName = parts[2];
            }
            else if (a.StartsWith("--gun-view:"))
            {
                // 枪检视:--gun-view:<type>:<dist> 从枪口前方回看(验证朝向/缩放)
                var parts = a["--gun-view:".Length..].Split(':');
                int.TryParse(parts[0], out gunType);
                if (parts.Length > 1)
                    float.TryParse(parts[1], out dist);
            }
        }
        // 简单灰环境 + 相机 + 光
        var env = new WorldEnvironment { Environment = new Environment { BackgroundMode = Environment.BGMode.Color, BackgroundColor = new Color(0.5f, 0.55f, 0.6f), AmbientLightSource = Environment.AmbientSource.Color, AmbientLightColor = new Color(0.8f, 0.8f, 0.8f), AmbientLightEnergy = 0.7f } };
        AddChild(env);
        var light = new DirectionalLight3D { Rotation = new Vector3(-0.8f, 0.6f, 0.0f) };
        AddChild(light);
        var floor = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(20, 20) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.6f, 0.58f) } };
        AddChild(floor);
        var cam = new Camera3D { Position = new Vector3(0, 1.3f, dist), Current = true, Fov = 45.0f };
        AddChild(cam);
        cam.LookAt(new Vector3(0, 0.9f, 0), Vector3.Up);

        if (gunType >= 0)
        {
            // 与 FireSystem.BindSide 相同的装配方式(仅模型部分),放在原点,-Z 朝前
            var gunModel = GD.Load<PackedScene>(FireSystem.GunModelPaths[gunType]).Instantiate<Node3D>();
            gunModel.Name = "Model";
            if (gunType == 0)
                gunModel.Rotation = new Vector3(0, -Mathf.Pi / 2.0f, 0);
            gunModel.Scale = Vector3.One * (gunType == 0 ? 0.4f : 1.0f) * FireSystem.GunModelScale;
            AddChild(gunModel);
            _measureNode = gunModel;
            _measureLabel = $"gun{gunType}";
            // 相机在 -Z 前方回看枪身(看到枪口面);--gun-view:t:d:back 则从 +Z 看枪尾
            bool back = System.Array.IndexOf(args, "--back") >= 0;
            cam.Position = back ? new Vector3(0.05f, 0.05f, dist) : new Vector3(0.05f, 0.05f, -dist);
            cam.LookAt(Vector3.Zero, Vector3.Up);
            ShotAsync($"gun{gunType}{(back ? "_back" : "")}");
            return;
        }
        // --dump-tree:<fbx>:打印导入场景树(节点类型/父链),排查挂点
        int dumpIdx = System.Array.FindIndex(args, a => a.StartsWith("--dump-tree:"));
        if (dumpIdx >= 0)
        {
            var inst = GD.Load<PackedScene>(args[dumpIdx]["--dump-tree:".Length..]).Instantiate<Node3D>();
            AddChild(inst);
            DumpTree(inst, 0);
            GetTree().Quit();
            return;
        }
        // --fbx-view:<anim>:<dist>:<fbx路径放最后含盘符冒号>:原始 FBX 导入场景直接播内嵌动画(对照 .tres 库)
        int fbxIdx = System.Array.FindIndex(args, a => a.StartsWith("--fbx-view:"));
        if (fbxIdx >= 0)
        {
            var rest = args[fbxIdx]["--fbx-view:".Length..];
            int c1 = rest.IndexOf(':');
            int c2 = rest.IndexOf(':', c1 + 1);
            string animPick = c1 > 0 ? rest[..c1] : "";
            string fbxPath = c2 > 0 ? rest[(c2 + 1)..] : "res://assets/models/monsters/bull/bull_king.FBX";
            if (c1 > 0 && c2 > 0)
                float.TryParse(rest[(c1 + 1)..c2], out dist);
            var packed = GD.Load<PackedScene>(fbxPath);
            if (packed == null)
            {
                GD.Print("[MVIEW] load failed: " + fbxPath);
                GetTree().Quit(1);
                return;
            }
            var inst = packed.Instantiate<Node3D>();
            AddChild(inst);
            var ap = inst.FindChild("AnimationPlayer", true, false) as AnimationPlayer;
            if (ap != null)
            {
                GD.Print($"[MVIEW] fbx anims: {string.Join(",", ap.GetAnimationList())}");
                if (animPick.Length > 0 && ap.HasAnimation(animPick))
                {
                    ap.Play(animPick);
                    ap.Advance(1.0);
                }
            }
            cam.Position = new Vector3(0, 1.3f, dist);
            cam.LookAt(new Vector3(0, 0.9f, 0), Vector3.Up);
            ShotAsync("fbx_" + (animPick.Length > 0 ? animPick : "bind"));
            return;
        }
        // --bone-diff:<monsterKey>:<anim>:<at>:骨骼全局姿态与默认姿态逐骨对比(找异常俯仰骨)
        int bdIdx = System.Array.FindIndex(args, a => a.StartsWith("--bone-diff:"));
        if (bdIdx >= 0)
        {
            var parts = args[bdIdx]["--bone-diff:".Length..].Split(':');
            var m2 = GD.Load<PackedScene>($"res://scenes/battle/monsters/{parts[0]}.tscn").Instantiate<Node3D>();
            AddChild(m2);
            m2.Visible = true;
            var sk2 = m2.FindChild("Skeleton3D", true, false) as Skeleton3D;
            // 记录默认全局姿态
            var defPos = new System.Collections.Generic.Dictionary<int, Transform3D>();
            for (int b = 0; b < sk2.GetBoneCount(); b++)
                defPos[b] = sk2.GetBoneGlobalPose(b);
            var ap2 = m2.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
            ap2.Play(parts[1]);
            ap2.Advance(parts.Length > 2 ? float.Parse(parts[2]) : 0.5f);
            for (int b = 0; b < sk2.GetBoneCount(); b++)
            {
                var now = sk2.GetBoneGlobalPose(b);
                float ang = defPos[b].Basis.GetRotationQuaternion().AngleTo(now.Basis.GetRotationQuaternion());
                float move = defPos[b].Origin.DistanceTo(now.Origin);
                if (ang > 0.26f || move > 0.15f)
                    GD.Print($"[BDIFF] {sk2.GetBoneName(b)} Δang={Mathf.RadToDeg(ang):0.0}° Δpos={move:0.00} defOrg={defPos[b].Origin} nowOrg={now.Origin}");
            }
            GetTree().Quit();
            return;
        }
        // --bin2tres:<monsterKey>:Unity 烘焙 bin → 动画库 tres(离线转换,见 BinToTres)
        int b2tIdx = System.Array.FindIndex(args, a => a.StartsWith("--bin2tres:"));
        if (b2tIdx >= 0)
        {
            BinToTres.Run(args[b2tIdx]["--bin2tres:".Length..]);
            GetTree().Quit();
            return;
        }
        // --fx-view:<effect场景名>:<dist>:特效检视(激活后 ~0.2s 截图)
        int fxIdx = System.Array.FindIndex(args, a => a.StartsWith("--fx-view:"));
        if (fxIdx >= 0)
        {
            var parts = args[fxIdx]["--fx-view:".Length..].Split(':');
            var fx = GD.Load<PackedScene>($"res://assets/effects/{parts[0]}.tscn").Instantiate<EffectBase>();
            AddChild(fx);
            fx.Activate();
            cam.Position = new Vector3(0.4f, 0.6f, parts.Length > 1 && float.TryParse(parts[1], out var d2) ? d2 : 1.5f);
            cam.LookAt(Vector3.Zero, Vector3.Up);
            ShotAsync("fx_" + parts[0]);
            return;
        }
        var scene = GD.Load<PackedScene>($"res://scenes/battle/monsters/{key}.tscn");
        var m = scene.Instantiate<Node3D>();
        AddChild(m);        m.Visible = true; // Monster._Ready 会 Deactivate 自隐,检视强制可见
        if (m is Monster mon)
            mon.SetPhysicsProcess(false);
        if (System.Array.IndexOf(args, "--mat-audit") >= 0)
        {
            foreach (var node in m.FindChildren("*", "MeshInstance3D", true, false))
            {
                if (node is not MeshInstance3D mi || mi.Mesh == null)
                    continue;
                var am = mi.GetActiveMaterial(0);
                string tex = "null";
                if (am is BaseMaterial3D bm && bm.AlbedoTexture != null)
                    tex = bm.AlbedoTexture.ResourcePath.Length > 0 ? bm.AlbedoTexture.ResourcePath : "(built-in)";
                GD.Print($"[MAT] {mi.GetPath()} override={(mi.GetSurfaceOverrideMaterial(0) != null ? "y" : "n")} active={(am != null ? am.ResourceName : "null")} tex={tex}");
            }
        }
        var anim = m.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
        if (anim != null)
        {
            var list = anim.GetAnimationList();
            string pick = animName;
            if (pick.Length == 0)
            {
                pick = list.Length > 0 ? list[0] : "";
                foreach (var n in list)
                    if (n.ToString().Contains("locomotion")) { pick = n; break; }
            }
            if (pick.Length > 0 && anim.HasAnimation(pick))
            {
                anim.Play(pick);
                // --at:<秒>:定点;缺省 1.0;支持逗号多帧
                float at = 1.0f;
                int atIdx = System.Array.FindIndex(args, a => a.StartsWith("--at:"));
                if (atIdx >= 0)
                    float.TryParse(args[atIdx]["--at:".Length..], out at);
                anim.Advance(at);
                GD.Print($"[MVIEW] {key} playing {pick} at {at}");
            }
        }
        ShotAsync(key);
    }

    private static void DumpTree(Node n, int depth)
    {
        string extra = n is MeshInstance3D mi2 && mi2.Mesh != null ? $" aabb={mi2.GetAabb()}" : "";
        if (n is BoneAttachment3D ba)
            extra += $" bone=\"{ba.BoneName}\" idx={ba.BoneIdx}";
        GD.Print($"[TREE] {new string(' ', depth * 2)}{n.Name} <{n.GetType().Name}>{extra}");
        foreach (var c in n.GetChildren())
            DumpTree(c, depth + 1);
    }

    private Node3D? _measureNode;
    private string _measureLabel = "";

    private async void ShotAsync(string key)
    {
        // 等两帧让渲染落地
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_measureNode != null)
        {
            var gmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var gmax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var node in _measureNode.FindChildren("*", "MeshInstance3D", true, false))
            {
                if (node is not MeshInstance3D mi2 || mi2.Mesh == null)
                    continue;
                var aabb2 = mi2.GetAabb();
                var gt2 = mi2.GlobalTransform;
                for (int i = 0; i < 8; i++)
                {
                    var c = aabb2.Position + new Vector3(
                        (i & 1) == 0 ? 0 : aabb2.Size.X, (i & 2) == 0 ? 0 : aabb2.Size.Y, (i & 4) == 0 ? 0 : aabb2.Size.Z);
                    var w = gt2 * c;
                    gmin = new Vector3(Mathf.Min(gmin.X, w.X), Mathf.Min(gmin.Y, w.Y), Mathf.Min(gmin.Z, w.Z));
                    gmax = new Vector3(Mathf.Max(gmax.X, w.X), Mathf.Max(gmax.Y, w.Y), Mathf.Max(gmax.Z, w.Z));
                }
            }
            GD.Print($"[GVIEW] {_measureLabel} aabb=({gmin.X:0.000},{gmin.Y:0.000},{gmin.Z:0.000})~({gmax.X:0.000},{gmax.Y:0.000},{gmax.Z:0.000})");
        }
        string path = $"G:/FPSGame/tools/screenshots/fix3/mon_{key}.png";
        GetViewport().GetTexture().GetImage().SavePng(path.Replace('/', '\\'));
        GD.Print($"[MVIEW] saved {path}");
        GetTree().Quit();
    }
}
