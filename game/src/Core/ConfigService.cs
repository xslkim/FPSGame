using Godot;

namespace FPSGame;

/// <summary>
/// ConfigService:远程配置拉取(原作 MenuController.GetConfig;离线容错,5s 超时)。
/// 成功后写入 SaveService.RemoteConfig。
/// </summary>
public static class ConfigService
{
    public const string ConfigUrl = "http://pc.oceanfitness.xyz/data/FPSGameConfig.json";

    public static void FetchRemoteConfig(Node parent)
    {
        var req = new HttpRequest { Timeout = 5.0 };
        parent.AddChild(req);
        req.RequestCompleted += (_, code, _, body) =>
        {
            if (code == 200)
            {
                var json = new Json();
                // A server can return an HTML error page with HTTP 200. Keep offline
                // defaults without reporting that response as a game engine error.
                if (json.Parse(body.GetStringFromUtf8()) == Error.Ok
                    && json.Data.VariantType == Variant.Type.Dictionary)
                    SaveService.Instance.RemoteConfig = json.Data.AsGodotDictionary();
            }
            req.QueueFree();
        };
        req.Request(ConfigUrl);
    }
}
