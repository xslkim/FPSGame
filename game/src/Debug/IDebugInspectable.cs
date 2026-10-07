using System.Collections.Generic;

namespace FPSGame;

/// <summary>
/// A gameplay node can expose a small, read-only snapshot to the global debug overlay.
/// The overlay never needs to know the concrete gameplay type.
/// </summary>
public interface IDebugInspectable
{
    string DebugSummary { get; }
    Dictionary<string, object?> CaptureDebugState();
}
