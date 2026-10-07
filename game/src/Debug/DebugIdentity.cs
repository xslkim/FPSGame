using Godot;
using System.Collections.Generic;

namespace FPSGame;

/// <summary>Human-readable IDs derived from a saved scene and a node path.</summary>
public static class DebugIdentity
{
    private static readonly Dictionary<string, string> SceneNames = new()
    {
        ["res://scenes/ui/startup.tscn"] = "UI/Startup",
        ["res://scenes/ui/menu.tscn"] = "UI/Menu",
        ["res://scenes/ui/level_choose.tscn"] = "UI/LevelChoose",
        ["res://scenes/ui/device_connection.tscn"] = "UI/DeviceConnection",
        ["res://scenes/ui/loading.tscn"] = "UI/Loading",
        ["res://scenes/levels/level1_story.tscn"] = "L1/Story",
        ["res://scenes/levels/level1_battle.tscn"] = "L1/Battle",
        ["res://scenes/levels/level2.tscn"] = "L2/Battle",
        ["res://scenes/levels/level3.tscn"] = "L3/Battle",
        ["res://scenes/levels/level4.tscn"] = "L4/Battle",
    };

    public static string SceneId(Node? scene)
    {
        if (scene == null)
            return "Scene/Loading";
        if (SceneNames.TryGetValue(scene.SceneFilePath, out string? name))
            return name;
        return scene.SceneFilePath.Length > 0
            ? scene.SceneFilePath.TrimPrefix("res://").TrimSuffix(".tscn")
            : $"Scene/{scene.Name}";
    }

    public static string ObjectId(Node? scene, Node? node)
    {
        if (node == null)
            return "(none)";
        if (node.HasMeta("debug_id"))
            return node.GetMeta("debug_id").AsString();
        if (scene == node)
            return $"{SceneId(scene)}/Root";
        if (scene == null || !scene.IsAncestorOf(node))
            return $"Global{node.GetPath()}";
        string path = scene.GetPathTo(node).ToString();
        return $"{SceneId(scene)}/{path}";
    }

    public static Node? FindInspectable(Node? node)
    {
        for (Node? current = node; current != null; current = current.GetParent())
            if (current is IDebugInspectable)
                return current;
        return null;
    }
}
