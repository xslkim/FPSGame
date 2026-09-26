using Godot;

namespace FPSGame;

/// <summary>一次性诊断:实例化怪物场景,打印各网格的世界 AABB(判断是否 FBX 厘米单位未缩放)。
/// 用法: godot --headless --path . scenes/levels/level1_battle.tscn -- --monster-size-audit
/// (挂接在 Level1 EnterLevel 之外,直接由 Game 调度太绕;这里用 SceneTree 脚本入口)</summary>
public static class MonsterSizeAudit
{
    public static async void Run()
    {
        // 等一帧让节点进树、全局变换就绪
        var tree = (SceneTree)Engine.GetMainLoop();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        string[] keys = { "bull", "axe_zombie", "skeleton", "fly_axe_zombie", "baotou", "box_monster" };
        foreach (var key in keys)
        {
            string path = $"res://scenes/battle/monsters/{key}.tscn";
            if (!ResourceLoader.Exists(path))
            {
                GD.Print($"[SIZE] {key}: scene missing");
                continue;
            }
            var root = GD.Load<PackedScene>(path).Instantiate<Node3D>();
            tree.Root.AddChild(root);
            // 播出生动画推进 0.5s 后再量(静止 bind pose 会掩盖骨骼动画单位不匹配)
            var anim = root.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
            string played = "";
            if (anim != null)
            {
                var list = anim.GetAnimationList();
                // 优先 locomotion(战斗中实际播的),否则第一个
                string pick = list.Length > 0 ? list[0] : "";
                foreach (var n in list)
                    if (n.ToString().Contains("locomotion")) { pick = n; break; }
                if (pick.Length > 0)
                {
                    anim.Play(pick);
                    played = pick;
                    anim.Advance(0.5);
                }
            }
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            GD.Print($"[SIZE] {key}: (anim={played})");
            foreach (var node in root.FindChildren("*", "MeshInstance3D", true, false))
            {
                if (node is not MeshInstance3D mi || mi.Mesh == null)
                    continue;
                var aabb = mi.GetAabb();
                var gt = mi.GlobalTransform;
                // 世界 AABB 近似:8 角点变换
                var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                for (int i = 0; i < 8; i++)
                {
                    var c = aabb.Position + new Vector3(
                        (i & 1) == 0 ? 0 : aabb.Size.X,
                        (i & 2) == 0 ? 0 : aabb.Size.Y,
                        (i & 4) == 0 ? 0 : aabb.Size.Z);
                    var w = gt * c;
                    min = new Vector3(Mathf.Min(min.X, w.X), Mathf.Min(min.Y, w.Y), Mathf.Min(min.Z, w.Z));
                    max = new Vector3(Mathf.Max(max.X, w.X), Mathf.Max(max.Y, w.Y), Mathf.Max(max.Z, w.Z));
                }
                GD.Print($"[SIZE]   {mi.Name} gscale={gt.Basis.Scale} localAabb={aabb} worldSize=({max.X - min.X:0.00},{max.Y - min.Y:0.00},{max.Z - min.Z:0.00}) worldY=[{min.Y:0.00},{max.Y:0.00}]");
            }
            foreach (var node in root.FindChildren("*", "Skeleton3D", true, false))
            {
                if (node is not Skeleton3D sk)
                    continue;
                GD.Print($"[SIZE]   skeleton {sk.Name} bones={sk.GetBoneCount()} gscale={sk.GlobalTransform.Basis.Scale}");
                for (int b = 0; b < Mathf.Min(sk.GetBoneCount(), 4); b++)
                {
                    var rest = sk.GetBoneRest(b);
                    var pose = sk.GetBoneGlobalPose(b);
                    GD.Print($"[SIZE]     bone[{b}] {sk.GetBoneName(b)} restOrg={rest.Origin} poseOrg={pose.Origin}");
                }
            }
            root.QueueFree();
        }
        ((SceneTree)Engine.GetMainLoop()).Quit();
    }
}
