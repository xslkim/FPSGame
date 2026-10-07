using Godot;

namespace FPSGame;

/// <summary>A bone-attached shot region forwards its authored damage type to its owner.</summary>
public partial class MonsterHitRegion : StaticBody3D
{
    private Monster _owner = null!;
    private Game.HitType _type;
    private CollisionShape3D _shape = null!;

    public static MonsterHitRegion AttachSphere(Monster owner, Skeleton3D skeleton,
        string name, string bone, Vector3 offset, float radius, Game.HitType type)
    {
        int index = skeleton.FindBone(bone);
        if (index < 0) throw new System.InvalidOperationException($"{owner.Name}: missing hit-region bone {bone}");
        var attachment = new BoneAttachment3D { Name = name + "Attachment", BoneIdx = index };
        skeleton.AddChild(attachment);
        var region = new MonsterHitRegion
        {
            Name = name, _owner = owner, _type = type,
            CollisionLayer = 0, CollisionMask = 0, Position = offset,
        };
        region._shape = new CollisionShape3D
        {
            Name = "HitShape", Shape = new SphereShape3D { Radius = radius }, Disabled = true,
        };
        region.AddChild(region._shape);
        attachment.AddChild(region);
        region.SyncActivation();
        return region;
    }

    public void SyncActivation()
    {
        bool active = _owner.IsActiveState && !_owner.IsDead;
        uint layer = active ? 2u : 0u;
        if (CollisionLayer == layer) return;
        CollisionLayer = layer; // remove dead/pool entries from rays immediately
        _shape.SetDeferred(CollisionShape3D.PropertyName.Disabled, !active);
    }

    public override void _Process(double delta) => SyncActivation();

    public string Hit(float attack, Vector3 point, int ignoredHitType, int side) =>
        _owner.IsActiveState && !_owner.IsDead
            ? _owner.Hit(attack, point, _type, (PlayerState.Side)side) : "Metal";
}
