using Godot;

namespace FPSGame;

/// <summary>
/// MouseGunSource:鼠标模拟光枪(无实体体感枪时的 PC/开发输入)。
/// 移动 = 枪口瞄准点(窗口像素);左键 = 扳机;右键 = 换枪。
/// 实体枪(UDP)连接后自动让位(见 InputRouter.GetRightAim)。
/// </summary>
public sealed class MouseGunSource
{
    public bool Enabled = true;

    /// <summary>最新鼠标位置(窗口像素)</summary>
    public Vector2 AimPos;

    public event System.Action? Triggered;
    public event System.Action? SwitchGun;

    public void HandleInput(InputEvent e)
    {
        if (!Enabled)
            return;
        switch (e)
        {
            case InputEventMouseMotion m:
                AimPos = m.Position;
                break;
            case InputEventMouseButton b when b.Pressed:
                if (b.ButtonIndex == MouseButton.Left)
                    Triggered?.Invoke();
                else if (b.ButtonIndex == MouseButton.Right)
                    SwitchGun?.Invoke();
                break;
        }
    }

    /// <summary>鼠标是否当前担当右路瞄准源(模式含右玩家且实体枪未连)</summary>
    public bool IsActiveForRight(InputRouter router) =>
        Enabled && !router.IsRingPhyConnected() &&
        router.Mode is InputRouter.InputMode.Menu
            or InputRouter.InputMode.OnlyRight
            or InputRouter.InputMode.ControllerOrRight
            or InputRouter.InputMode.RightAndLeft;
}
