using Godot;

namespace FPSGame;

/// <summary>
/// Wolf / WolfBlue 共用(WolfBlue 继承 WolfMonster 语义,meta key 区分数值与动画clip)。
/// 1:1 原作 WolfMonster.cs(6.3):born 时按相机方向预计算蛇形路径点
/// (距离减 4,segment=(int)(d/6),第 i 点前进 i/(segment+1)*d,
/// 横向 8*(0.2+(1-i/segment)*0.8) 交替 ±,起始方向 50% 随机);
/// 到点(0.5m)或超时 3s 走下一点;走完进攻击阶段:半径内且面向相机 ±15° 放攻击。
/// 多段距离降速逐行保真原作独立 if 链(后写覆盖先写,见 GetWolfSpeed)。
/// </summary>
public partial class WolfMonster : Monster
{
    public const float SegmentLength = 6.0f;     // 原作 segmentLength
    public const float ApproachTrim = 4.0f;      // 到相机方向距离减 4
    public const float LateralBase = 8.0f;       // 原作 rightOffsetLength
    public const float PointTimeout = 3.0f;      // 原作 maxTargetPosMoveTime
    public const float PointArrive = 0.5f;       // 原作 MoveToTarget errorValue
    public const float AttackFacing = 15.0f;     // 原作 IsFaceToCamera 15°

    /// <summary>M7 颜色变体材质(blue/green .tres),wolf 本族不配置</summary>
    [Export] public Material? BodyMaterial;

    private readonly System.Collections.Generic.List<Vector3> _path = new();
    private int _pathIdx;
    private double _pointStartTime;

    public override void _Ready()
    {
        base._Ready();
        AnimTrackUtil.StripMethodTracks(Anim); // clip 自带 event_attack 方法轨与基类 Tween 重复
        if (BodyMaterial != null)
        {
            foreach (var n in BodyNode.FindChildren("*", "MeshInstance3D", true, false))
                if (n is MeshInstance3D mi)
                    mi.SetSurfaceOverrideMaterial(0, BodyMaterial);
        }
    }

    protected override void OnBorn()
    {
        BuildPath();
        _pointStartTime = Time.GetTicksMsec() / 1000.0;
    }

    /// <summary>原作 born():相机方向距离减 4 后分段,横向偏移递减交替</summary>
    private void BuildPath()
    {
        _path.Clear();
        _pathIdx = 0;
        var cam = GetViewport().GetCamera3D();
        if (cam == null)
            return;
        Vector3 diff = cam.GlobalPosition - GlobalPosition;
        diff.Y = 0.0f;
        if (diff.Length() < 0.001f)
            return;
        Vector3 dir = diff.Normalized();
        float distance = (diff - dir * ApproachTrim).Length();
        int segment = (int)(distance / SegmentLength);
        Vector3 rightDir = dir.Cross(Vector3.Up).Normalized();
        bool right = GD.RandRange(0, 100) > 50;   // 起始方向 50% 随机
        int startSignal = right ? 1 : 0;
        for (int i = 1; i <= segment; ++i)
        {
            float curLength = (float)i / (segment + 1) * distance;
            Vector3 curTarget = GlobalPosition + dir * curLength;
            float curSegment = (startSignal % 2 == 0) ? 1.0f : -1.0f;
            startSignal++;
            float curRightRate = 1.0f - (float)i / segment;
            float curRightOffset = LateralBase * (0.2f + curRightRate * 0.8f);
            curTarget += rightDir * curRightOffset * curSegment;
            _path.Add(curTarget);
        }
    }

    protected override void UpdateActive(float delta)
    {
        // 原作用 AnimatorStateInfo.IsName("Locomotion") 门控攻击/走位全程
        if (IsPlayingAny(Info.AttackAnims) || (IsCurrentAnim(Info.DamageAnim) && Anim.IsPlaying()))
        {
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
            ApplyGravity(delta);
            MoveAndSlide();
            return;
        }
        if (_pathIdx < _path.Count)
            UpdateSerpentine(delta);
        else
            UpdateAttackPhase(delta);
    }

    /// <summary>蛇形走位:朝当前路径点移动(到点 0.5m 或超时 3s 跳下一点)</summary>
    private void UpdateSerpentine(float delta)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        Vector3 target = _path[_pathIdx];
        Vector3 to = target - GlobalPosition;
        to.Y = 0.0f;
        float d = to.Length();
        if (d < PointArrive || now - _pointStartTime > PointTimeout)
        {
            _pathIdx += 1;
            _pointStartTime = now;
            return;
        }
        Vector3 dir = to / d;
        Vector3 rot = Rotation;
        rot.Y = YawTowards(rot.Y, Mathf.Atan2(-dir.X, -dir.Z), Info.TurnSpeed * delta);
        Rotation = rot;
        float sqe = d * d;
        float speed = GetWolfSpeed(sqe);
        Velocity = new Vector3(dir.X * speed, Velocity.Y, dir.Z * speed);
        ApplyGravity(delta);
        MoveAndSlide();
        if (!IsCurrentAnim("locomotion") && Anim.HasAnimation("locomotion"))
            Anim.Play("locomotion", 0.2);
    }

    /// <summary>攻击阶段:半径内且面向相机 → CD 到放攻击;否则继续逼近(3m 内降速 1)</summary>
    private void UpdateAttackPhase(float delta)
    {
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
                DoAttack();
            else
            {
                Velocity = new Vector3(0.0f, Velocity.Y, 0.0f); // 原作 Move(Vector3.down*dt)
                ApplyGravity(delta);
                MoveAndSlide();
            }
            return;
        }
        Vector3 dir = d > 0.0001f ? to / d : Vector3.Zero;
        Vector3 rot = Rotation;
        rot.Y = YawTowards(rot.Y, Mathf.Atan2(-dir.X, -dir.Z), Info.TurnSpeed * delta);
        Rotation = rot;
        float speed = GetWolfSpeed(d * d);
        Velocity = new Vector3(dir.X * speed, Velocity.Y, dir.Z * speed);
        ApplyGravity(delta);
        MoveAndSlide();
        if (!IsCurrentAnim("locomotion") && Anim.HasAnimation("locomotion"))
            Anim.Play("locomotion", 0.2);
    }

    /// <summary>原作 WolfMonster.GetMoveSpeed 逐行保真:两段独立 if/elseif 链,后者覆盖前者</summary>
    private float GetWolfSpeed(float sqe)
    {
        float speed = GetMoveSpeed();
        if (_pathIdx < _path.Count)
        {
            if (_pathIdx > 0)
            {
                float sl = (GlobalPosition - _path[_pathIdx - 1]).LengthSquared();
                if (sl < 1.0f)
                    speed = 1.0f;
                else if (sl < 4.0f)
                    speed = 2.0f;
                if (sl < 9.0f)
                    speed = 3.0f;
                else if (sl < 16.0f)
                    speed = 4.0f;
            }
            if (sqe < 1.0f)
                speed = 1.0f;
            else if (sqe < 4.0f)
                speed = 2.0f;
            if (sqe < 9.0f)
                speed = 3.0f;
            else if (sqe < 16.0f)
                speed = 4.0f;
            return speed;
        }
        if (sqe < 9.0f && speed > 1.0f)
            speed = 1.0f;
        return speed;
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
