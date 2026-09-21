using Godot;

namespace FPSGame;

/// <summary>
/// 怪物对象池:每类型预实例化 N 只(inactive)放池下,取第一个未激活的复用。
/// 对应原作"场景预摆 inactive 复用,不 Instantiate"语义(池化复用行为一致)。
/// </summary>
public partial class MonsterPool : Node3D
{
    private readonly System.Collections.Generic.Dictionary<string, Godot.Collections.Array<Monster>> _pools = new();

    public void RegisterType(string key, PackedScene scene, int size)
    {
        var arr = new Godot.Collections.Array<Monster>();
        for (int i = 0; i < size; i++)
        {
            var m = scene.Instantiate<Monster>();
            m.Name = $"{key}_{i}";
            AddChild(m);
            arr.Add(m);
        }
        _pools[key] = arr;
    }

    public bool HasInactive(string key) => GetMonster(key) != null;

    /// <summary>取该类型第一只未激活的怪;无则返回 null(池满,下 tick 重试)</summary>
    public Monster? GetMonster(string key)
    {
        if (!_pools.TryGetValue(key, out var arr))
            return null;
        foreach (var m in arr)
        {
            if (!m.IsActiveState)
                return m;
        }
        return null;
    }
}
