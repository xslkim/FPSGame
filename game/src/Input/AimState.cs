using Godot;

namespace FPSGame;

/// <summary>
/// 统一瞄准状态:屏幕像素点(鼠标模拟)或枪口旋转(体感枪/键盘回落)。
/// 消费方(MenuScreen / GunUiController / 战斗准星)只面对这一种类型。
/// </summary>
public readonly struct AimState
{
    public readonly bool IsScreenPoint;
    public readonly Vector2 ScreenPos;   // IsScreenPoint 时有效:窗口像素
    public readonly Quaternion Rotation; // 否则有效:枪口旋转(相机局部空间约定见 GunMath)

    private AimState(bool isScreenPoint, Vector2 pos, Quaternion rot)
    {
        IsScreenPoint = isScreenPoint;
        ScreenPos = pos;
        Rotation = rot;
    }

    public static AimState Screen(Vector2 pos) => new(true, pos, Quaternion.Identity);
    public static AimState Rot(Quaternion rot) => new(false, Vector2.Zero, rot);
}
