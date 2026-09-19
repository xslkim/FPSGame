using Godot;

namespace FPSGame;

/// <summary>
/// 移植 SCI-FI 包 JustRotate.cs:绕 Z 轴匀速旋转(度/秒),无限循环,无缓动。
/// 挂在 TextureRect 上;加入树时自动把 pivot_offset 设为控件中心。
/// </summary>
public partial class JustRotate : TextureRect
{
    [Export] public float Speed = 20.0f;

    public override void _Ready() => PivotOffset = Size / 2.0f;

    public override void _Process(double delta) =>
        Rotation += Mathf.DegToRad(Speed) * (float)delta;
}
