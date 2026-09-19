using Godot;

namespace FPSGame;

/// <summary>
/// GunMath:Unity(左手系,+Z 向前)→ Godot(右手系,-Z 向前)的四元数镜像与枪口旋转解算。
/// UDP 数据由手机 App 按 Unity 约定发出;Unity 与 Godot 的 Quaternion 都是 (x,y,z,w),
/// 区别在坐标系手性。注意 q 与 -q 表示同一旋转,故"取反 x,w"≡"取反 y,z"(镜像 X 轴)。
/// </summary>
public static class GunMath
{
    public enum QuatMirror
    {
        None,   // 不转换
        NegXw,  // 取反 x,w(镜像 X 轴:x→-x)
        NegYz,  // 取反 y,z(与 NegXw 同旋转,冗余候选)
        NegZw,  // 取反 z,w(镜像 Z 轴:z→-z)
    }

    /// <summary>
    /// 默认 NegZw:标准坐标映射 (x,y,z)_unity → (x,y,-z)_godot 的四元数形式为
    /// (-x,-y,z,w),整体取反(同旋转)即 (x,y,-z,-w) = 取反 z,w。
    /// 可用 project setting fpsgame/input/quat_mirror(int)在真机校准时切换候选。
    /// </summary>
    public const QuatMirror DefaultMirror = QuatMirror.NegZw;
    public const float PhoneMoveRate = 0.5f;

    public static int MirrorMode =>
        ProjectSettings.GetSetting("fpsgame/input/quat_mirror", (int)DefaultMirror).AsInt32();

    /// <summary>坐标系镜像修正。该变换是对合(应用两次即还原)。</summary>
    public static Quaternion ApplyMirror(Quaternion raw) => (QuatMirror)MirrorMode switch
    {
        QuatMirror.NegXw => new Quaternion(-raw.X, raw.Y, raw.Z, -raw.W),
        QuatMirror.NegYz => new Quaternion(raw.X, -raw.Y, -raw.Z, raw.W),
        QuatMirror.NegZw => new Quaternion(raw.X, raw.Y, -raw.Z, -raw.W),
        _ => raw,
    };

    /// <summary>
    /// 手机四元数 → 枪口旋转。
    /// 原作:v = q * Vector3.forward; v.z = PhoneMoveRate; FromToRotation(forward, v)。
    /// Godot FORWARD 为 -Z(Unity 为 +Z),v.z 取 -PhoneMoveRate 保持"朝前";
    /// q 先做手性镜像,v = q' * FORWARD 即标准映射 M(q·v)=(MqM)(Mv)。
    /// </summary>
    public static Quaternion PhoneToGunRotation(Quaternion raw)
    {
        var q = ApplyMirror(raw);
        var v = q * Vector3.Forward;
        v.Z = -PhoneMoveRate;
        return new Quaternion(Vector3.Forward, v);
    }
}
