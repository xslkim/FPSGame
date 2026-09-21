using Godot;

namespace FPSGame;

/// <summary>
/// 动画 clip 辅助:C# 框架用 DoAttack 的 0.3s Tween 代替原作动画事件
/// (Monster.TriggerAttackEvent)。部分 FBX 导入 clip 自带 Call Method Track
/// (event_attack / rock_attack),运行时会报 "Method not found" 并与 Tween 重复触发。
/// 这里在内存中剥离 method 轨道(只改运行时资源,不动 .tres 共享资产)。
/// </summary>
internal static class AnimTrackUtil
{
    private static readonly System.Collections.Generic.HashSet<Animation> Stripped = new();

    public static void StripMethodTracks(AnimationPlayer anim)
    {
        foreach (var name in anim.GetAnimationList())
        {
            var a = anim.GetAnimation(name);
            if (a == null || !Stripped.Add(a))
                continue;
            for (int t = a.GetTrackCount() - 1; t >= 0; t--)
            {
                if (a.TrackGetType(t) == Animation.TrackType.Method)
                    a.RemoveTrack(t);
            }
        }
    }
}
