using Godot;

namespace FPSGame;

/// <summary>
/// 可复用特效场景基类:Activate() 重启全部粒子、随机化弹孔朝向/多帧闪电片、
/// 播放自带音效,并按 Lifetime 延迟自隐。FireSystem 与各怪技能特效统一走这个接口。
/// 对应 legacy effect_base.gd(同接口语义)。
/// </summary>
public partial class EffectBase : Node3D
{
    [Export] public float Lifetime = 0.5f;

    private Tween? _hideTween;
    private Node3D? _followTarget;
    private Transform3D _followLocal;

    /// <summary>Keep pooled ownership while following an actor; destroying the actor cannot destroy the pool entry.</summary>
    public void Follow(Node3D target, Vector3 localPosition)
    {
        _followTarget = target;
        _followLocal = new Transform3D(Basis.Identity, localPosition);
        TopLevel = true;
        GlobalTransform = target.GlobalTransform * _followLocal;
    }

    public override void _Process(double delta)
    {
        if (_followTarget == null) return;
        if (!IsInstanceValid(_followTarget) || _followTarget.IsQueuedForDeletion())
        {
            _followTarget = null; // finish at the last valid position
            return;
        }
        GlobalTransform = _followTarget.GlobalTransform * _followLocal;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Pausable;
        foreach (var node in FindChildren("*", "GPUParticles3D", true, false))
        {
            var particles = (GpuParticles3D)node;
            particles.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            if (particles.DrawPass1 is not QuadMesh quad || quad.Material is not StandardMaterial3D source)
                continue;
            string texture = source.AlbedoTexture?.ResourcePath.GetFile() ?? "";
            if (texture == "stone.png")
            {
                // Unity ConcreteImpact/rocks uses Stone1 as a mesh particle.
                // Its RGB rock texture is opaque; drawing it on a billboard
                // creates a solid square that looks like missing transparency.
                var rock = (ArrayMesh)GD.Load<ArrayMesh>("res://assets/effects/models/impact_stone.tres").Duplicate();
                var rockMaterial = (StandardMaterial3D)source.Duplicate();
                rockMaterial.BillboardMode = BaseMaterial3D.BillboardModeEnum.Disabled;
                rockMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel;
                for (int s = 0; s < rock.GetSurfaceCount(); s++)
                    rock.SurfaceSetMaterial(s, rockMaterial);
                particles.DrawPass1 = rock;
                continue;
            }
            if (texture is not ("dust1.png" or "dust2.png"))
                continue;
            // Unity UVModule: Dust1 8x8; Dust2 4x16. Sampling the entire sheet
            // produces a rectangular cloud instead of a single fading puff.
            var draw = (QuadMesh)quad.Duplicate();
            var material = (StandardMaterial3D)source.Duplicate();
            material.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
            material.ParticlesAnimHFrames = texture == "dust2.png" ? 4 : 8;
            material.ParticlesAnimVFrames = texture == "dust2.png" ? 16 : 8;
            material.ParticlesAnimLoop = false;
            material.VertexColorUseAsAlbedo = true;
            draw.Material = material;
            particles.DrawPass1 = draw;
            var process = (ParticleProcessMaterial)particles.ProcessMaterial.Duplicate();
            process.AnimSpeedMin = process.AnimSpeedMax = 1.0f;
            particles.ProcessMaterial = process;
        }
    }

    /// <summary>激活一次特效;overrideLifetime>0 可覆盖自隐时长(血花用 1.2s)</summary>
    public void Activate(float overrideLifetime = -1.0f)
    {
        Visible = true;
        var hole = GetNodeOrNull<Sprite3D>("Hole");
        if (hole != null)
            hole.Rotation = new Vector3(hole.Rotation.X, hole.Rotation.Y, (float)GD.RandRange(0.0, Mathf.Tau));
        // 多帧闪电/闪光片(Bolt1/Bolt2/...):随机选一帧显示
        var bolts = new Godot.Collections.Array<Node3D>();
        foreach (var c in GetChildren())
        {
            if (c is Node3D n && n.Name.ToString().StartsWith("Bolt"))
                bolts.Add(n);
        }
        if (bolts.Count > 0)
        {
            int pick = GD.RandRange(0, bolts.Count - 1);
            for (int i = 0; i < bolts.Count; i++)
                bolts[i].Visible = i == pick;
        }
        foreach (var p in FindChildren("*", "GPUParticles3D", true, false))
        {
            if (p is GpuParticles3D gpu)
                gpu.Restart();
        }
        foreach (var s in FindChildren("*", "AudioStreamPlayer3D", true, false))
        {
            if (s is AudioStreamPlayer3D asp)
                asp.Play();
        }
        _hideTween?.Kill();
        _hideTween = CreateTween();
        _hideTween.TweenInterval(overrideLifetime < 0.0f ? Lifetime : overrideLifetime);
        _hideTween.TweenCallback(Callable.From(() => { Hide(); _followTarget = null; }));
    }
}
