using Godot;

namespace FPSGame;

/// <summary>
/// L2 FireWindow(7.2):Toon 怪的射击窗口节点。
/// 对应 legacy fire_window.gd / 原作 FireWindow.cs:fire_monster=当前占用怪;
/// SrcPosition=翻窗爬入出生点(子节点,缺省用窗口自身位置)。
/// 分配由 Level2.SpawnTick 完成;释放由 ToonMonster 死亡/回收时调用 Release。
/// </summary>
public partial class FireWindow : Node3D
{
    [Export] public NodePath SrcPositionPath = "SrcPosition";

    /// <summary>当前占用窗口的怪(null/已回收 = 空窗)</summary>
    public Monster? FireMonster;

    /// <summary>翻窗爬入出生点(无 SrcPosition 子节点时用窗口自身位置)</summary>
    public Vector3 GetSrcPosition()
    {
        var n = GetNodeOrNull<Node3D>(SrcPositionPath);
        return n != null ? n.GlobalPosition : GlobalPosition;
    }

    /// <summary>窗口是否空闲(占用怪死亡/回收后也算空)</summary>
    public bool IsFree() =>
        FireMonster == null || !IsInstanceValid(FireMonster) || !FireMonster.IsActiveState;

    /// <summary>释放占用(ToonMonster 死亡/回收时调用)</summary>
    public void Release(Monster monster)
    {
        if (FireMonster == monster)
            FireMonster = null;
    }

    /// <summary>7.2 分配策略:收集空窗口均匀随机选一个;无空窗返回 null(关卡下 tick 重试)</summary>
    public static FireWindow? PickFree(System.Collections.Generic.IEnumerable<FireWindow> windows)
    {
        FireWindow? picked = null;
        int count = 0;
        foreach (var w in windows)
        {
            if (!w.IsFree())
                continue;
            count += 1;
            if (GD.Randf() < 1.0f / count)
                picked = w; // reservoir sampling,均匀随机
        }
        return picked;
    }
}
