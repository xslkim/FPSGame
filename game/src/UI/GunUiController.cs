using Godot;

namespace FPSGame;

/// <summary>
/// 射击触发 UI 框架(对应原作 UIController.cs),用于 3D 按钮界面(选关/连接手机)。
/// 挂在 UI 场景的相机下:每帧按 InputRouter 瞄准(UDP 四元数/键盘方向键)算射线方向,
/// 扳机边沿(回车/手机扳机)或鼠标左键 → 射线命中 layer 3 且有 OnShot() 的物体 → 调 OnShot()。
/// MessageBox 打开时只响应名字带 "MessageButton" 前缀的命中(照原作)。
/// 按钮统一用 UiButton3D.Create() 构建;键盘备选 = 按钮 Shortcut 键 / MessageBox 的 Enter。
/// </summary>
public partial class GunUiController : Node3D
{
    public const float RayLength = 100.0f;
    public const uint RayMask = 0b100; // 只打 layer 3 Button

    [Export] public NodePath CameraPath = "..";

    private Camera3D _camera = null!;

    public override void _Ready()
    {
        _camera = GetNodeOrNull<Camera3D>(CameraPath) ?? GetViewport().GetCamera3D();
        InputRouter.Instance.FireEnabled = true;
        InputRouter.Instance.TriggerRight += OnTrigger;
        InputRouter.Instance.TriggerLeft += OnTrigger;
        InputRouter.Instance.MouseGun.Triggered += OnMouseTrigger;
        MakeCrosshair();
    }

    private void OnTrigger()
    {
        if (Game.Instance.IsGamePause || _camera == null)
            return;
        FireRay(_camera.GlobalPosition, AimDir());
    }

    /// <summary>鼠标模拟光枪:射线取鼠标落点(原作鼠标左键路径)</summary>
    private void OnMouseTrigger()
    {
        if (Game.Instance.IsGamePause || _camera == null)
            return;
        var pos = InputRouter.Instance.MouseGun.AimPos;
        FireRay(_camera.ProjectRayOrigin(pos), _camera.ProjectRayNormal(pos));
    }

    /// <summary>瞄准方向 = 相机朝向 × 枪口旋转(4.2 节,与战斗开火同一解算)</summary>
    private Vector3 AimDir() =>
        _camera.GlobalBasis * (GunMath.PhoneToGunRotation(InputRouter.Instance.RawRingRotation) * Vector3.Forward);

    public override void _UnhandledInput(InputEvent e)
    {
        if (Game.Instance.IsGamePause || _camera == null)
            return;
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            foreach (var n in GetTree().GetNodesInGroup("ui_button_3d"))
            {
                if (n is UiButton3D b && b.Shortcut != Key.None && b.Shortcut == k.Keycode)
                {
                    if (MessageBox.IsOpen() && !((string)b.Name).StartsWith("MessageButton"))
                        continue;
                    b.Trigger();
                    return;
                }
            }
        }
    }

    private void FireRay(Vector3 from, Vector3 dir)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, from + dir * RayLength, RayMask);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0)
            HandleHit(hit["collider"].AsGodotObject());
    }

    /// <summary>MessageBox 打开时只响应 MessageButton(返回是否真触发;自检测试用)</summary>
    public bool HandleHit(GodotObject? obj)
    {
        if (obj is not UiButton3D b)
            return false;
        if (MessageBox.IsOpen() && !((string)b.Name).StartsWith("MessageButton"))
            return false;
        b.OnShot();
        return true;
    }

    private void MakeCrosshair()
    {
        var layer = new CanvasLayer { Name = "CrosshairLayer" };
        var label = new Label { Text = "+" };
        label.AddThemeFontSizeOverride("font_size", 32);
        label.SetAnchorsPreset(Control.LayoutPreset.Center);
        layer.AddChild(label);
        AddChild(layer);
    }
}
