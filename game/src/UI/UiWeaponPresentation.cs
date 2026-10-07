using Godot;

namespace FPSGame;

/// <summary>Render UI weapons above dialogs without also moving the menu monster above them.</summary>
public partial class UiWeaponPresentation : CanvasLayer
{
    public const int WeaponLayer = 25;
    private Camera3D _source = null!;
    private Camera3D _camera = null!;

    public static void Create(Node parent, Camera3D source, params Node3D[] guns)
    {
        var overlay = new UiWeaponPresentation { Name = "WeaponPresentation", Layer = WeaponLayer };
        overlay._source = source;
        var container = new SubViewportContainer { Name = "WeaponContainer", Stretch = true,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        container.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var viewport = new SubViewport { Name = "WeaponViewport", TransparentBg = true,
            HandleInputLocally = false, World3D = source.GetWorld3D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        overlay._camera = new Camera3D { Name = "WeaponCamera", CullMask = 2, Current = true };
        viewport.AddChild(overlay._camera);
        container.AddChild(viewport);
        overlay.AddChild(container);
        parent.AddChild(overlay);
        source.CullMask &= ~2u;
        foreach (var gun in guns)
            foreach (var mesh in gun.FindChildren("*", "GeometryInstance3D", true, false))
                ((GeometryInstance3D)mesh).Layers = 2;
        overlay.SyncCamera();
    }

    private void SyncCamera()
    {
        _camera.GlobalTransform = _source.GlobalTransform;
        _camera.Fov = _source.Fov;
        _camera.Near = _source.Near;
        _camera.Far = _source.Far;
        _camera.KeepAspect = _source.KeepAspect;
    }

    public override void _Process(double delta) => SyncCamera();
}
