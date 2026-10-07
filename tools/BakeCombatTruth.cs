using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BakeCombatTruth
{
    [Serializable] public class ClipEvent { public float time; public string name; public int int_parameter; public string string_parameter; }
    [Serializable] public class ClipRecord { public string path; public string name; public float length; public float frame_rate; public bool human; public List<ClipEvent> events = new List<ClipEvent>(); }
    [Serializable] public class Report { public List<ClipRecord> clips = new List<ClipRecord>(); }
    [Serializable] public class Paths { public string militia_model; public string alien_model; public string infantry_combat_reload; public string infantry_combat_shoot; }

    public static void Run()
    {
        var report = new Report();
        foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var clip = asset as AnimationClip;
                if (clip == null || clip.name.StartsWith("__preview__")) continue;
                var record = new ClipRecord { path=path, name=clip.name, length=clip.length, frame_rate=clip.frameRate, human=clip.humanMotion };
                foreach (var e in AnimationUtility.GetAnimationEvents(clip))
                    record.events.Add(new ClipEvent { time=e.time, name=e.functionName, int_parameter=e.intParameter, string_parameter=e.stringParameter });
                report.clips.Add(record);
            }
        }
        string output = "G:/FPSGame/tools/combat_bake";
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"unity_animation_events.json"),JsonUtility.ToJson(report,true));
        Environment.SetEnvironmentVariable("KPOP_OUT", output);
        Environment.SetEnvironmentVariable("KPOP_ROOTMOTION", "0");
        Environment.SetEnvironmentVariable("KPOP_POSE_T", "");
        const string folder = "Assets/Enemy/金属盔甲武士/LowPoly_Lancer/";
        var jobs = new List<string>();
        foreach (string clip in new[] {"Idle", "Run", "Hit", "Dead", "Skill1", "Skill2"})
        {
            var asset = AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+"Ani/"+clip+".anim");
            jobs.Add(folder+"Ani/"+clip+".anim>"+folder+"FBX/Modle.FBX>level2_boss_"+clip+">"+asset.length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        var paths = JsonUtility.FromJson<Paths>(File.ReadAllText(Path.Combine(Application.dataPath,"Editor/combat_paths.json")));
        foreach (string clipPath in new[] { paths.infantry_combat_reload, paths.infantry_combat_shoot })
        {
            var asset = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            string alias = clipPath.EndsWith("reload.FBX") ? "reload" : "shoot";
            string seconds = asset.length.ToString(System.Globalization.CultureInfo.InvariantCulture);
            jobs.Add(clipPath+">"+paths.militia_model+">toon_"+alias+">"+seconds);
            jobs.Add(clipPath+">"+paths.alien_model+">toon_alien_"+alias+">"+seconds);
        }
        Environment.SetEnvironmentVariable("KPOP_JOBS", string.Join("|", jobs.ToArray()));
        BakeKpopDance.Bake();
        Debug.Log("[COMBAT-TRUTH] ALL DONE clips="+report.clips.Count);
    }
}
