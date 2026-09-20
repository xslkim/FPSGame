using System.Collections.Generic;
using Godot;

namespace FPSGame;

/// <summary>
/// SaveService [autoload]:数值配置(data/*.json)+ 用户存档(user://save.json)。
/// 对应原作 DataMgr / UserData(金币经济:UTC 计时,每 180s +1 币,上限 10,变更落盘)。
/// </summary>
public partial class SaveService : Node
{
    public const string SavePath = "user://save.json";
    public const int LevelCount = 13;

    public static SaveService Instance { get; private set; } = null!;

    /// <summary>存档/数值变更(金币、关卡成绩)时触发,PlayerState 的 HUD 据此刷新</summary>
    public event System.Action? Changed;

    public int GunTypeNum = 7;
    public int Coin = 10;
    public int MaxCoin = 10;
    public int AddCoinTime = 180; // 秒
    public int MaxBullet = 120;
    public int BoxBullet = 60;
    public string Udid = "";
    public double LastAddCoinTime;

    /// <summary>FPSGameConfig.json 远程配置(MenuController.GetConfig)</summary>
    public Godot.Collections.Dictionary RemoteConfig = new();

    /// <summary>13 关 × {star, score, rank}</summary>
    public readonly List<Godot.Collections.Dictionary> LevelState = new();

    private Godot.Collections.Dictionary _fireMeta = new();
    private Godot.Collections.Dictionary _monsterMeta = new();
    private Godot.Collections.Dictionary _levelMeta = new();
    private Godot.Collections.Array _tips = new();

    public override void _Ready()
    {
        Instance = this;
        _fireMeta = ReadJson("res://data/fire_meta.json", new Godot.Collections.Dictionary());
        GunTypeNum = GetInt(_fireMeta, "gun_type_num", 7);
        _monsterMeta = ReadJson("res://data/monster_meta.json", new Godot.Collections.Dictionary());
        _levelMeta = ReadJson("res://data/level_meta.json", new Godot.Collections.Dictionary());
        var tipsRoot = ReadJson("res://data/tips.json", new Godot.Collections.Dictionary());
        _tips = Get(tipsRoot, "tips", new Godot.Collections.Array()).AsGodotArray();
        LoadUserData();
    }

    public override void _Process(double delta) => RegenCoins();

    // ------------------------------------------------ 金币经济(8.3)

    /// <summary>全局回币:UTC 秒,180s +1,上限 MaxCoin,落盘</summary>
    private void RegenCoins()
    {
        if (Coin >= MaxCoin)
            return;
        double now = Time.GetUnixTimeFromSystem();
        bool changed = false;
        while (Coin < MaxCoin && LastAddCoinTime + AddCoinTime <= now)
        {
            Coin += 1;
            LastAddCoinTime += AddCoinTime;
            changed = true;
        }
        if (changed)
        {
            SaveUserData();
            Changed?.Invoke();
        }
    }

    /// <summary>扣币(选关 -1 / 续币 -1);不足返回 false。扣币后重新计回币时间</summary>
    public bool SpendCoin(int n = 1)
    {
        if (Coin < n)
            return false;
        Coin -= n;
        LastAddCoinTime = Time.GetUnixTimeFromSystem();
        SaveUserData();
        Changed?.Invoke();
        return true;
    }

    /// <summary>距下一枚回币的秒数(面板倒计时用);满币返回 0</summary>
    public double TimeToNextCoin()
    {
        if (Coin >= MaxCoin)
            return 0.0;
        return System.Math.Max(0.0, LastAddCoinTime + AddCoinTime - Time.GetUnixTimeFromSystem());
    }

    /// <summary>通关结算落盘(8.3):星级/分数取历史最高</summary>
    public void SetLevelResult(int idx, int star, int score, int rank)
    {
        if (idx < 0 || idx >= LevelState.Count)
            return;
        var s = LevelState[idx];
        s["star"] = System.Math.Max(GetInt(s, "star", 0), star);
        s["score"] = System.Math.Max(GetInt(s, "score", 0), score);
        s["rank"] = rank;
        LevelState[idx] = s;
        SaveUserData();
        Changed?.Invoke();
    }

    // ------------------------------------------------ 元数据查询

    /// <summary>枪数值:type 0=AK47 / 1=M4 / 2=HandGun,字段见 data/fire_meta.json</summary>
    public Godot.Collections.Dictionary GetGunInfo(int type)
    {
        foreach (var g in Get(_fireMeta, "guns", new Godot.Collections.Array()).AsGodotArray())
        {
            var gd = g.AsGodotDictionary();
            if (GetInt(gd, "type", -1) == type)
                return gd;
        }
        GD.PushWarning($"SaveService: unknown gun type {type}");
        return new Godot.Collections.Dictionary();
    }

    public Godot.Collections.Dictionary GetMonsterMeta() => _monsterMeta;
    public Godot.Collections.Dictionary GetLevelMeta() => _levelMeta;
    public Godot.Collections.Array GetTips() => _tips;

    // ------------------------------------------------ 存档读写

    public void LoadUserData()
    {
        if (FileAccess.FileExists(SavePath))
        {
            var parsed = Json.ParseString(FileAccess.GetFileAsString(SavePath));
            if (parsed.VariantType == Variant.Type.Dictionary)
            {
                var d = parsed.AsGodotDictionary();
                Coin = GetInt(d, "coin", Coin);
                MaxCoin = GetInt(d, "max_coin", MaxCoin);
                AddCoinTime = GetInt(d, "add_coin_time", AddCoinTime);
                MaxBullet = GetInt(d, "max_bullet", MaxBullet);
                BoxBullet = GetInt(d, "box_bullet", BoxBullet);
                Udid = Get(d, "udid", "").AsString();
                LastAddCoinTime = Get(d, "last_add_coin_time", 0.0).AsDouble();
                var ls = Get(d, "level_state", new Godot.Collections.Array()).AsGodotArray();
                for (int i = 0; i < System.Math.Min(ls.Count, LevelCount); i++)
                {
                    if (ls[i].VariantType == Variant.Type.Dictionary)
                        LevelState.Add(ls[i].AsGodotDictionary());
                }
            }
        }
        if (Udid.Length == 0)
            Udid = $"{(long)Time.GetUnixTimeFromSystem()}_{GD.Randi()}";
        while (LevelState.Count < LevelCount)
            LevelState.Add(DefaultLevelState());
        if (LastAddCoinTime <= 0.0)
            LastAddCoinTime = Time.GetUnixTimeFromSystem();
        SaveUserData();
    }

    /// <summary>服务器离线化:本地默认 13 关全 3 星全解锁(照原作 UserMeta.cs LevelStateData
    /// 默认值 Star=3/Source=3/Rank=1;通关后按 HP+用时 1-3 星改写)</summary>
    private static Godot.Collections.Dictionary DefaultLevelState() =>
        new() { ["star"] = 3, ["score"] = 3, ["rank"] = 1 };

    public void SaveUserData()
    {
        var levelArray = new Godot.Collections.Array();
        foreach (var s in LevelState)
            levelArray.Add(s);
        var d = new Godot.Collections.Dictionary
        {
            ["coin"] = Coin,
            ["max_coin"] = MaxCoin,
            ["add_coin_time"] = AddCoinTime,
            ["max_bullet"] = MaxBullet,
            ["box_bullet"] = BoxBullet,
            ["level_state"] = levelArray,
            ["udid"] = Udid,
            ["last_add_coin_time"] = LastAddCoinTime,
        };
        using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        f?.StoreString(Json.Stringify(d, "  "));
    }

    private static Godot.Collections.Dictionary ReadJson(string path, Godot.Collections.Dictionary fallback)
    {
        if (!FileAccess.FileExists(path))
        {
            GD.PushError("SaveService: missing json " + path);
            return fallback;
        }
        var data = Json.ParseString(FileAccess.GetFileAsString(path));
        if (data.VariantType != Variant.Type.Dictionary)
        {
            GD.PushError("SaveService: bad json " + path);
            return fallback;
        }
        return data.AsGodotDictionary();
    }

    // ------------------------------------------------ Variant 读取辅助

    public static Variant Get(Godot.Collections.Dictionary d, string key, Variant def) =>
        d.TryGetValue(key, out var v) ? v : def;

    public static int GetInt(Godot.Collections.Dictionary d, string key, int def) =>
        d.TryGetValue(key, out var v) ? v.AsInt32() : def;
}
