using Godot;

namespace FPSGame;

/// <summary>
/// 单只怪的数值元数据(data/monster_meta.json:defaults + monsters[key] 覆盖)。
/// 照原作 MonsterBase.GetMeta():HP/攻击/CD/半径/移速/转向 + 动画 clip 名 + 命中特效 tag。
/// </summary>
public sealed class MonsterInfo
{
    public readonly string Key;
    public readonly string CnName;
    public readonly string Behavior;

    public float Attack;
    public float AttackLevelRate;
    public float AttackLevelBonus;   // 基类不吃 Level;SKMonster/Bull 重写为 Attack+5×Lv
    public float AttackCd;
    public float AttackCdLevelRate;
    public float AttackRadius;
    public float Hp;
    public float HpLevelRate;
    public float TurnSpeed;
    public float MoveSpeed;
    public float MoveSpeedLevelRate;
    public Game.AttackType AttackType;
    public string Idle2Anim = "Idle02";
    public string[] AttackAnims = { "Attack1", "Attack2" };
    public string? IdleAnim;         // baotou/magma/level2_boss 有专属 idle
    public string DamageAnim = "Damage";
    public string DeadAnim = "Dead";
    public float DeadAnimSpeed = 1.0f;
    public string ImpactTag = "Blood";
    public float LifeActiveTime;     // >0 覆盖 25s 超时(box=20)
    public float? BornMaxFov;        // born_override(飞斧 15/12、胖僵尸 30/12)
    public float? BornMaxLength;
    public float? AttackRadiusBase;  // 飞斧:7+rand(0,3)(原作 int 重载,7/8/9)
    public float? AttackRadiusRand;
    public float HitRate;            // 飞斧命中率(原作 MonsterMeta.HitRate=0.15)
    public float MaxDistance;        // 攻击距离闸门(原作骷髅/斧/牛=6、飞斧=20)
    public string BornAnim = "";     // 出生默认动画(原作 controller 默认态;空→IdleAnim??"locomotion")
    public float Idle2Interval = 5.0f; // 等待期 Idle02 插播间隔(原作 Idle02Time,宝箱 3)
    public Godot.Collections.Dictionary? Kinds; // 宝箱按 kind 的序列化值(waitting_time/idle2_interval)

    public MonsterInfo(string key, Godot.Collections.Dictionary defaults, Godot.Collections.Dictionary over)
    {
        Key = key;
        CnName = GetStr(over, "cn_name", key);
        Behavior = GetStr(over, "behavior", "");

        Attack = GetF(over, "attack", GetF(defaults, "attack", 15.0f));
        AttackLevelRate = GetF(over, "attack_level_rate", GetF(defaults, "attack_level_rate", 5.0f));
        AttackLevelBonus = GetF(over, "attack_level_bonus", GetF(defaults, "attack_level_bonus", 0.0f));
        AttackCd = GetF(over, "attack_cd", GetF(defaults, "attack_cd", 5.0f));
        AttackCdLevelRate = GetF(over, "attack_cd_level_rate", GetF(defaults, "attack_cd_level_rate", 0.5f));
        AttackRadius = GetF(over, "attack_radius", GetF(defaults, "attack_radius", 2.0f));
        Hp = GetF(over, "hp", GetF(defaults, "hp", 30.0f));
        HpLevelRate = GetF(over, "hp_level_rate", GetF(defaults, "hp_level_rate", 5.0f));
        TurnSpeed = GetF(over, "turn_speed", GetF(defaults, "turn_speed", 2.0f));
        MoveSpeed = GetF(over, "move_speed", GetF(defaults, "move_speed", 1.0f));
        MoveSpeedLevelRate = GetF(over, "move_speed_level_rate", GetF(defaults, "move_speed_level_rate", 0.3f));
        AttackType = (Game.AttackType)GetI(over, "attack_type", GetI(defaults, "attack_type", 0));
        Idle2Anim = GetStr(over, "idle2_anim", GetStr(defaults, "idle2_anim", "Idle02"));
        AttackAnims = GetStrArray(over, "attack_anims", GetStrArray(defaults, "attack_anims", new[] { "Attack1", "Attack2" }));
        IdleAnim = over.ContainsKey("idle_anim") ? GetStr(over, "idle_anim", "") : null;
        DamageAnim = GetStr(over, "damage_anim", GetStr(defaults, "damage_anim", "Damage"));
        DeadAnim = GetStr(over, "dead_anim", GetStr(defaults, "dead_anim", "Dead"));
        DeadAnimSpeed = GetF(over, "dead_anim_speed", GetF(defaults, "dead_anim_speed", 1.0f));
        ImpactTag = GetStr(over, "impact_tag", GetStr(defaults, "impact_tag", "Blood"));
        LifeActiveTime = GetF(over, "life_active_time", GetF(defaults, "life_active_time", 0.0f));
        var born = over.ContainsKey("born_override") ? over["born_override"].AsGodotDictionary() : null;
        if (born != null)
        {
            BornMaxFov = (float)born["max_fov"].AsDouble();
            BornMaxLength = (float)born["max_length"].AsDouble();
        }
        AttackRadiusBase = over.ContainsKey("attack_radius_base") ? (float)over["attack_radius_base"].AsDouble() : null;
        AttackRadiusRand = over.ContainsKey("attack_radius_rand") ? (float)over["attack_radius_rand"].AsDouble() : null;
        HitRate = GetF(over, "hit_rate", GetF(defaults, "hit_rate", 0.15f));
        MaxDistance = GetF(over, "max_distance", GetF(defaults, "max_distance", 2000.0f));
        BornAnim = GetStr(over, "born_anim", GetStr(defaults, "born_anim", ""));
        Idle2Interval = GetF(over, "idle2_interval", GetF(defaults, "idle2_interval", 5.0f));
        Kinds = over.ContainsKey("kinds") ? over["kinds"].AsGodotDictionary() : null;
    }

    public static MonsterInfo Load(string key)
    {
        var root = SaveService.Instance.GetMonsterMeta();
        var defaults = SaveService.Get(root, "defaults", new Godot.Collections.Dictionary()).AsGodotDictionary();
        var monsters = SaveService.Get(root, "monsters", new Godot.Collections.Dictionary()).AsGodotDictionary();
        var over = monsters.ContainsKey(key)
            ? monsters[key].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        return new MonsterInfo(key, defaults, over);
    }

    private static float GetF(Godot.Collections.Dictionary d, string k, float def) =>
        d.TryGetValue(k, out var v) ? (float)v.AsDouble() : def;

    private static int GetI(Godot.Collections.Dictionary d, string k, int def) =>
        d.TryGetValue(k, out var v) ? v.AsInt32() : def;

    private static string GetStr(Godot.Collections.Dictionary d, string k, string def) =>
        d.TryGetValue(k, out var v) ? v.AsString() : def;

    private static string[] GetStrArray(Godot.Collections.Dictionary d, string k, string[] def)
    {
        if (!d.TryGetValue(k, out var v))
            return def;
        var arr = v.AsGodotArray();
        var ret = new string[arr.Count];
        for (int i = 0; i < arr.Count; i++)
            ret[i] = arr[i].AsString();
        return ret;
    }
}
