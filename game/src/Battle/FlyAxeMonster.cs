using Godot;

namespace FPSGame;

/// <summary>
/// 飞斧头僵尸:攻击半径 = 7+rand(0,3)(born 时定);面向相机出生。
/// 出生射线参数走 meta born_override(±15°/12m)。
/// </summary>
public partial class FlyAxeMonster : Monster
{
    protected override void OnBorn()
    {
        if (Info.AttackRadiusBase.HasValue)
            Info.AttackRadius = Info.AttackRadiusBase.Value
                + (Info.AttackRadiusRand ?? 0.0f) * GD.Randf();
        FaceCamera();
    }
}
