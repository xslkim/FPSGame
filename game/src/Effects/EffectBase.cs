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
        _hideTween.TweenCallback(Callable.From(Hide));
    }
}
