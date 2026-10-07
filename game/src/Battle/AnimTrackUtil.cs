using Godot;

namespace FPSGame;

/// <summary>
/// Immutable clip libraries with Unity event times, executed by AnimationPlayer.
/// </summary>
internal static class AnimTrackUtil
{
    private static readonly System.Collections.Generic.Dictionary<(string, string), AnimationLibrary> Libraries = new();
    private static Godot.Collections.Dictionary? _events;

    public static void InstallCombatEvents(AnimationPlayer player, string monster)
    {
        _events ??= Json.ParseString(FileAccess.GetFileAsString("res://data/combat_animation_events.json")).AsGodotDictionary();
        var clips = _events.ContainsKey(monster) ? _events[monster].AsGodotDictionary() : new Godot.Collections.Dictionary();
        foreach (var libraryName in player.GetAnimationLibraryList())
        {
            var original = player.GetAnimationLibrary(libraryName);
            var cacheKey = (monster, original.ResourcePath.Length > 0 ? original.ResourcePath : original.GetInstanceId().ToString());
            if (!Libraries.TryGetValue(cacheKey, out var library))
            {
                library = new AnimationLibrary();
                foreach (var name in original.GetAnimationList())
                {
                    var animation = (Animation)original.GetAnimation(name).Duplicate();
                    for (int t = animation.GetTrackCount() - 1; t >= 0; t--)
                        if (animation.TrackGetType(t) == Animation.TrackType.Method) animation.RemoveTrack(t);
                    if (clips.ContainsKey(name.ToString()))
                    {
                        var record = clips[name.ToString()].AsGodotDictionary();
                        animation.Length = record["length"].AsDouble();
                        animation.LoopMode = Animation.LoopModeEnum.None;
                        foreach (var item in record["events"].AsGodotArray())
                        {
                            var e = item.AsGodotDictionary();
                            int track = animation.AddTrack(Animation.TrackType.Method);
                            animation.TrackSetPath(track, new NodePath("."));
                            animation.TrackInsertKey(track, e["time"].AsDouble(), new Godot.Collections.Dictionary
                            {
                                ["method"] = nameof(Monster.OnCombatAnimationEvent),
                                ["args"] = new Godot.Collections.Array { name.ToString(), e["name"].AsString(),
                                    e.ContainsKey("string_parameter") ? e["string_parameter"].AsString() : "" }
                            });
                        }
                    }
                    library.AddAnimation(name, animation);
                }
                Libraries.Add(cacheKey, library);
            }
            player.RemoveAnimationLibrary(libraryName);
            player.AddAnimationLibrary(libraryName, library);
        }
        player.CallbackModeMethod = AnimationMixer.AnimationCallbackModeMethod.Immediate;
    }
}
