using Godot;
using System;

namespace FPSGame;

/// <summary>Fail with a scene/object ID when a required Godot node contract breaks.</summary>
public static class SceneContract
{
    public static T Require<T>(Node owner, NodePath path) where T : Node
    {
        T? node = owner.GetNodeOrNull<T>(path);
        if (node != null)
            return node;
        Node? scene = owner.SceneFilePath.Length > 0 ? owner : owner.GetTree().CurrentScene;
        string ownerId = DebugIdentity.ObjectId(scene, owner);
        throw new InvalidOperationException(
            $"[SceneContract] {ownerId} requires {typeof(T).Name} at '{path}'. " +
            "Check the .tscn node path before changing gameplay code.");
    }
}
