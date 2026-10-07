using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FPSGame;

/// <summary>
/// F4: live diagnostics. F5: screenshot + JSON report. F6: pin hovered object.
/// F7: copy the object ID. Runs in exported builds and while the game is paused.
/// </summary>
public partial class DebugOverlay : CanvasLayer
{
    private const double RefreshSeconds = 0.2;
    private PanelContainer _panel = null!;
    private Label _text = null!;
    private Node? _hovered;
    private Node? _pinned;
    private Vector3? _hitPosition;
    private double _refresh;
    private double _pickElapsed;
    private string _notice = "";
    private double _noticeUntil;
    private bool _capturing;

    public override void _Ready()
    {
        Layer = 110;
        ProcessMode = ProcessModeEnum.Always;
        _panel = new PanelContainer
        {
            Name = "DiagnosticsPanel",
            AnchorLeft = 1,
            AnchorRight = 1,
            OffsetLeft = -472,
            OffsetRight = -12,
            OffsetTop = 12,
            CustomMinimumSize = new Vector2(460, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.01f, 0.025f, 0.045f, 0.88f),
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 9,
            ContentMarginBottom = 9,
        });
        _text = new Label
        {
            Name = "DiagnosticsText",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Text = "F4 diagnostics",
            AutowrapMode = TextServer.AutowrapMode.Arbitrary,
            CustomMinimumSize = new Vector2(430, 0),
        };
        _text.AddThemeColorOverride("font_color", new Color(0.84f, 0.96f, 1f));
        _text.AddThemeFontSizeOverride("font_size", 16);
        _panel.AddChild(_text);
        AddChild(_panel);
        Visible = Array.IndexOf(OS.GetCmdlineUserArgs(), "--show-debug") >= 0;
    }

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        switch (key.Keycode)
        {
            case Key.F4:
                Visible = !Visible;
                if (!Visible)
                    _hovered = null;
                _refresh = RefreshSeconds;
                _pickElapsed = 0.10;
                break;
            case Key.F5:
                if (!_capturing)
                    CaptureReport();
                break;
            case Key.F6:
                _pinned = IsValid(_pinned) ? null : IsValid(_hovered) ? _hovered : GetViewport().GuiGetFocusOwner();
                Notice(_pinned == null ? "Pin cleared" : "Pinned " + CurrentObjectId(_pinned));
                break;
            case Key.F7:
                string id = CurrentObjectId(SelectedNode());
                DisplayServer.ClipboardSet(id);
                Notice("Copied ID: " + id);
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Visible)
            return;
        _pickElapsed += delta;
        if (_pickElapsed < 0.10)
            return;
        _pickElapsed = 0;
        _hovered = null;
        _hitPosition = null;
        var viewport = GetViewport();
        Vector2 aim = InputRouter.Instance.MouseGun.AimPos;
        if (aim == Vector2.Zero)
            aim = viewport.GetMousePosition();
        var ui = viewport.GuiGetHoveredControl();
        if (ui is BaseButton && !_panel.IsAncestorOf(ui))
        {
            _hovered = ui;
            return;
        }
        _hovered = FindAimButton(GetTree().CurrentScene, aim);
        if (_hovered != null)
            return;
        if (ui != null && !_panel.IsAncestorOf(ui))
        {
            _hovered = ui;
            return;
        }
        var camera = FindSceneCamera();
        if (camera == null)
            return;
        Vector2 mouse = CameraMousePosition(camera, aim);
        Vector3 from = camera.ProjectRayOrigin(mouse);
        Vector3 to = from + camera.ProjectRayNormal(mouse) * camera.Far;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        var hit = camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.ContainsKey("collider"))
            _hovered = hit["collider"].AsGodotObject() as Node;
        if (hit.ContainsKey("position"))
            _hitPosition = hit["position"].AsVector3();
    }

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        _refresh += delta;
        if (_refresh < RefreshSeconds)
            return;
        _refresh = 0;
        var scene = GetTree().CurrentScene;
        var camera = FindSceneCamera();
        var node = SelectedNode();
        string id = CurrentObjectId(node);
        double processMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        int draws = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        var lines = new StringBuilder();
        lines.AppendLine("DEBUG  F4 hide · F5 report · F6 pin · F7 copy ID");
        lines.AppendLine($"Scene  {DebugIdentity.SceneId(scene)}");
        lines.AppendLine($"File     {scene?.SceneFilePath ?? "(loading)"}");
        lines.AppendLine($"Build   {BuildIdentity.Id[..8]}  {BuildIdentity.Utc}");
        lines.AppendLine($"FPS     {Engine.GetFramesPerSecond():0}   CPU {processMs:0.0} ms   Draw {draws}");
        string source = IsValid(_pinned) ? "  [PIN]" : !IsValid(_hovered) && node != null ? "  [FOCUS]" : "";
        lines.AppendLine("Object " + WrapId(id) + source);
        var focusOwner = GetViewport().GuiGetFocusOwner();
        if (focusOwner != null && focusOwner != node)
            lines.AppendLine("Focus    " + WrapId(CurrentObjectId(focusOwner)));
        if (node is Node3D spatial)
            lines.AppendLine($"World  {Vec(spatial.GlobalPosition)}");
        if (_hitPosition.HasValue && !IsValid(_pinned))
            lines.AppendLine($"Hit       {Vec(_hitPosition.Value)}");
        if (camera != null)
            lines.AppendLine($"Camera {camera.Name}  {Vec(camera.GlobalPosition)}  FOV {camera.Fov:0.0}");
        if (scene is IDebugInspectable sceneInfo)
            lines.AppendLine($"State   {sceneInfo.DebugSummary}");
        Node? owner = DebugIdentity.FindInspectable(node);
        if (owner is IDebugInspectable info && owner != scene)
            lines.AppendLine($"Owner  {CurrentObjectId(owner)}  {info.DebugSummary}");
        if (_notice.Length > 0 && Time.GetTicksMsec() / 1000.0 < _noticeUntil)
            lines.AppendLine(_notice);
        _text.Text = lines.ToString();
    }

    private static bool IsValid(Node? node) => node != null && GodotObject.IsInstanceValid(node);
    private string CurrentObjectId(Node? node) => DebugIdentity.ObjectId(GetTree().CurrentScene, IsValid(node) ? node : null);
    private Node? SelectedNode() => IsValid(_pinned) ? _pinned : IsValid(_hovered) ? _hovered : GetViewport().GuiGetFocusOwner();
    private static string Vec(Vector3 v) => $"({v.X:0.00}, {v.Y:0.00}, {v.Z:0.00})";

    private static Control? FindAimButton(Node? scene, Vector2 mouse)
    {
        if (scene == null)
            return null;
        Control? found = null;
        var stack = new Stack<Node>();
        stack.Push(scene);
        while (stack.Count > 0)
        {
            Node node = stack.Pop();
            if (node is BaseButton button && button.IsVisibleInTree() &&
                button.GetGlobalRect().HasPoint(mouse))
                found = button;
            for (int i = node.GetChildCount() - 1; i >= 0; i--)
                stack.Push(node.GetChild(i));
        }
        return found;
    }

    private static string WrapId(string id)
    {
        if (id.Length <= 58)
            return id;
        var result = new StringBuilder();
        int length = 0;
        foreach (string part in id.Split('/'))
        {
            int added = part.Length + (length == 0 ? 0 : 1);
            if (length > 0 && length + added > 58)
            {
                result.Append("/\n          ");
                length = 0;
            }
            else if (length > 0)
            {
                result.Append('/');
                length++;
            }
            result.Append(part);
            length += part.Length;
        }
        return result.ToString();
    }

    private Camera3D? FindSceneCamera()
    {
        var camera = GetViewport().GetCamera3D();
        if (camera != null)
            return camera;
        var scene = GetTree().CurrentScene;
        if (scene == null)
            return null;
        foreach (Node child in scene.FindChildren("*", "Camera3D", true, false))
            if (child is Camera3D candidate && candidate.Current)
                return candidate;
        return null;
    }

    private Vector2 CameraMousePosition(Camera3D camera, Vector2 mouse)
    {
        if (camera.GetViewport() is not SubViewport subViewport ||
            subViewport.GetParent() is not SubViewportContainer container)
            return mouse;
        Rect2 rect = container.GetGlobalRect();
        if (rect.Size.X <= 0 || rect.Size.Y <= 0)
            return mouse;
        return (mouse - rect.Position) * (Vector2)subViewport.Size / rect.Size;
    }

    private void Notice(string message)
    {
        _notice = message;
        _noticeUntil = Time.GetTicksMsec() / 1000.0 + 4.0;
        GD.Print("[Debug] " + message);
        _refresh = RefreshSeconds;
    }

    public string GetReportDirectory() => ProjectSettings.GlobalizePath(SaveService.SaveDirectory + "debug_reports");

    private async void CaptureReport()
    {
        _capturing = true;
        try
        {
            // Capture rendered pixels and read-only state at the same completed frame.
            // Headless tests have no rendering signal and produce JSON only.
            if (DisplayServer.GetName() != "headless")
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var scene = GetTree().CurrentScene;
            string sceneId = DebugIdentity.SceneId(scene);
            string stem = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{sceneId.Replace('/', '-')}";
            string directory = GetReportDirectory();
            Directory.CreateDirectory(directory);
            string png = Path.Combine(directory, stem + ".png");
            string json = Path.Combine(directory, stem + ".json");
            var camera = FindSceneCamera();
            var focus = SelectedNode();
            var report = new Dictionary<string, object?>
            {
                ["schema"] = 1,
                ["build"] = BuildIdentity.Capture(),
                ["process_frame"] = Engine.GetProcessFrames(),
                ["physics_frame"] = Engine.GetPhysicsFrames(),
                ["captured_local"] = DateTime.Now.ToString("O"),
                ["scene_id"] = sceneId,
                ["scene_path"] = scene?.SceneFilePath,
                ["focused_id"] = CurrentObjectId(focus),
                ["ui_focus_id"] = CurrentObjectId(GetViewport().GuiGetFocusOwner()),
                ["focused_type"] = focus?.GetType().Name,
                ["focused_node_path"] = IsValid(focus) ? focus!.GetPath().ToString() : null,
                ["mouse_screen"] = GetViewport().GetMousePosition().ToString(),
                ["hit_world"] = _hitPosition?.ToString(),
                ["fps"] = Engine.GetFramesPerSecond(),
                ["process_ms"] = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0,
                ["draw_calls"] = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
                ["camera_id"] = CurrentObjectId(camera),
                ["camera_position"] = camera?.GlobalPosition.ToString(),
                ["camera_rotation"] = camera?.GlobalRotationDegrees.ToString(),
                ["camera_fov"] = camera?.Fov,
                ["globals"] = CollectGlobals(),
                ["inspectables"] = CollectInspectables(scene),
            };
            File.WriteAllText(json, JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
            }));
            if (DisplayServer.GetName() != "headless")
            {
                Error save = GetViewport().GetTexture().GetImage().SavePng(png);
                if (save != Error.Ok)
                    throw new IOException($"SavePng failed: {save}");
            }
            DisplayServer.ClipboardSet(json);
            Notice("Report saved (path copied): " + json);
        }
        catch (Exception ex)
        {
            GD.PushError("[Debug] report capture failed: " + ex);
            Notice("Report failed: " + ex.Message);
        }
        finally
        {
            _capturing = false;
        }
    }

    private static List<Dictionary<string, object?>> CollectInspectables(Node? scene)
    {
        var result = new List<Dictionary<string, object?>>();
        if (scene == null)
            return result;
        var stack = new Stack<Node>();
        stack.Push(scene);
        while (stack.Count > 0 && result.Count < 128)
        {
            Node node = stack.Pop();
            if (node is IDebugInspectable inspectable &&
                (node is not Node3D spatial || spatial.Visible))
            {
                result.Add(new Dictionary<string, object?>
                {
                    ["id"] = DebugIdentity.ObjectId(scene, node),
                    ["summary"] = inspectable.DebugSummary,
                    ["state"] = inspectable.CaptureDebugState(),
                });
            }
            foreach (Node child in node.GetChildren())
                stack.Push(child);
        }
        return result;
    }

    private List<Dictionary<string, object?>> CollectGlobals()
    {
        var result = new List<Dictionary<string, object?>>();
        foreach (Node child in GetTree().Root.GetChildren())
            if (child is IDebugInspectable inspectable && child != GetTree().CurrentScene)
                result.Add(new Dictionary<string, object?>
                {
                    ["id"] = DebugIdentity.ObjectId(null, child),
                    ["summary"] = inspectable.DebugSummary,
                    ["state"] = inspectable.CaptureDebugState(),
                });
        return result;
    }
}
