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

    /// <summary>左键按住电平(原作 _MouseFireRing:InputManager.cs:272/290,按住=持续开火)</summary>
    public bool LeftHeld;

    public event System.Action? Triggered;
    public event System.Action? SwitchGun;

    public void HandleInput(InputEvent e)
    {
        if (!Enabled)
            return;
        switch (e)
        {
            case InputEventMouseMotion m:
                SimulateMove(m.Position);
                break;
            case InputEventMouseButton b when b.ButtonIndex == MouseButton.Left:
                LeftHeld = b.Pressed; // 电平:按住持续开火;Pressed 沿另有 Triggered 事件(UI 用)
                if (b.Pressed)
                    SimulateTrigger();
                break;
            case InputEventMouseButton b when b.Pressed && b.ButtonIndex == MouseButton.Right:
                SwitchGun?.Invoke();
                break;
        }
    }

    /// <summary>移动瞄准点(视口坐标)。自动化测试可直接调用以绕过合成事件管线。</summary>
    public void SimulateMove(Vector2 viewportPos) => AimPos = viewportPos;

    /// <summary>扣扳机。自动化测试可直接调用以绕过合成事件管线。</summary>
    public void SimulateTrigger() => Triggered?.Invoke();

    /// <summary>右键换枪。自动化测试可直接调用。</summary>
    public void SimulateSwitch() => SwitchGun?.Invoke();

    /// <summary>鼠标是否当前担当右路瞄准源(模式含右玩家或为鼠标模式,且实体枪未连)</summary>
    public bool IsActiveForRight(InputRouter router) =>
        Enabled && !router.IsRingPhyConnected() &&
        router.Mode is InputRouter.InputMode.Menu
            or InputRouter.InputMode.OnlyRight
            or InputRouter.InputMode.ControllerOrRight
            or InputRouter.InputMode.RightAndLeft
            or InputRouter.InputMode.Mouse;
}
