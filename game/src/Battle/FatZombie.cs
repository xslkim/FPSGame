using Godot;

namespace FPSGame;

/// <summary>
/// FatZombie(6.2):标准直线追击(基类语义),HP100,速 1+0.5/Lv;
/// 进 attack_radius 且 IsFaceToCamera(±15°) 才出手,CD 未到站桩。
/// 出生射线 ±30°/12m 走 meta born_override。1:1 原作 FatZombie.cs Update。
/// </summary>
public partial class FatZombie : Monster
{
    public const float AttackFacing = 15.0f; // 原作 IsFaceToCamera 15°

    public override void _Ready()
    {
        base._Ready();
        AnimTrackUtil.StripMethodTracks(Anim); // clip 自带 event_attack 方法轨与基类 Tween 重复
    }

    protected override void UpdateActive(float delta)
    {
        // 原作用 AnimatorStateInfo.IsName("Locomotion") 门控
        if (IsPlayingAny(Info.AttackAnims) || (IsCurrentAnim(Info.DamageAnim) && Anim.IsPlaying()))
        {
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
            ApplyGravity(delta);
            MoveAndSlide();
            return;
        }
        var cam = GetViewport().GetCamera3D();
        if (cam == null)
            return;
        Vector3 target = cam.GlobalPosition;
        target.Y = GlobalPosition.Y;
        Vector3 to = target - GlobalPosition;
        float d = to.Length();
        if (d < AttackRadius && IsFaceToCamera(to))
        {
            if (AttackReady())
            {
                DoAttack();
                return;
            }
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f); // 原作 Move(Vector3.down*dt)
            ApplyGravity(delta);
            MoveAndSlide();
            return;
        }
        Vector3 dir = d > 0.0001f ? to / d : Vector3.Zero;
        Vector3 rot = Rotation;
        rot.Y = YawTowards(rot.Y, Mathf.Atan2(-dir.X, -dir.Z), Info.TurnSpeed * delta);
        Rotation = rot;
        float speed = GetMoveSpeed();
        if (d < 3.0f)
            speed = Mathf.Min(speed, 1.0f); // 原作 MonsterBase.GetMoveSpeed 3m 内降速 1
        Velocity = new Vector3(dir.X * speed, Velocity.Y, dir.Z * speed);
        ApplyGravity(delta);
        MoveAndSlide();
        if (!IsCurrentAnim("locomotion") && Anim.HasAnimation("locomotion"))
            Anim.Play("locomotion", 0.2);
    }

    /// <summary>原作 IsFaceToCamera:到相机(y 压平)与 forward 夹角 < 15°</summary>
    private bool IsFaceToCamera(Vector3 flatToCam)
    {
        if (flatToCam.LengthSquared() < 0.0001f)
            return false;
        float desiredYaw = Mathf.Atan2(-flatToCam.X, -flatToCam.Z);
        return Mathf.Abs(Mathf.Wrap(desiredYaw - Rotation.Y, -Mathf.Pi, Mathf.Pi))
            < Mathf.DegToRad(AttackFacing);
    }
}
