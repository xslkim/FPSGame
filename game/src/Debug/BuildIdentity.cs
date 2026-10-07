using Godot;
using System.Collections.Generic;
using System.Reflection;

namespace FPSGame;

/// <summary>Compiled identity survives export; reports also identify the running executable.</summary>
public static class BuildIdentity
{
    private static readonly Assembly Assembly = typeof(BuildIdentity).Assembly;
    public static string Id => Assembly.ManifestModule.ModuleVersionId.ToString();
    public static string Utc
    {
        get
        {
            foreach (var attribute in Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
                if (attribute.Key == "BuildUtc") return attribute.Value ?? "unknown";
            return "unknown";
        }
    }

    public static Dictionary<string, object?> Capture() => new()
    {
        ["id"] = Id,
        ["compiled_utc"] = Utc,
        ["executable"] = OS.GetExecutablePath(),
        ["engine"] = Engine.GetVersionInfo()["string"].AsString(),
    };
}
