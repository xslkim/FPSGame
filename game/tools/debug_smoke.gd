extends SceneTree

func _initialize() -> void:
    call_deferred("_run")

func _run() -> void:
    var report_dir: String = root.get_node("DebugOverlay").call("GetReportDirectory")
    DirAccess.make_dir_recursive_absolute(report_dir)
    var before = DirAccess.get_files_at(report_dir)
    var scene = load("res://scenes/ui/menu.tscn").instantiate()
    root.add_child(scene)
    current_scene = scene
    for i in 4:
        await process_frame
    var first_button = scene.find_child("Btn_OnePlayer", true, false)
    if first_button == null:
        push_error("[DEBUG-SMOKE] first menu button missing")
        quit(1)
        return
    var move = InputEventMouseMotion.new()
    move.position = first_button.get_global_rect().get_center()
    Input.parse_input_event(move)
    for i in 2:
        await process_frame
    var panel = root.get_node("DebugOverlay")
    var f4 = InputEventKey.new()
    f4.keycode = KEY_F4
    f4.pressed = true
    Input.parse_input_event(f4)
    for i in 4:
        await process_frame
    if not panel.visible:
        push_error("[DEBUG-SMOKE] F4 did not open diagnostics")
        quit(1)
        return
    var f5 = InputEventKey.new()
    f5.keycode = KEY_F5
    f5.pressed = true
    Input.parse_input_event(f5)
    for i in 8:
        await process_frame
    var files = DirAccess.get_files_at(report_dir)
    var found = false
    for file in files:
        if file.ends_with("UI-Menu.json") and not file in before:
            found = true
            var report = FileAccess.get_file_as_string(report_dir.path_join(file))
            var data = JSON.parse_string(report)
            if data.get("build", {}).get("id", "").is_empty() or data.get("build", {}).get("compiled_utc", "unknown") == "unknown" or data.get("build", {}).get("executable", "").is_empty():
                push_error("[DEBUG-SMOKE] report is missing compiled build identity")
                quit(1)
                return
            if not report.contains('"scene_id": "UI/Menu"') or not report.contains('"focused_id": "UI/Menu/UILayer/Root/Btn_OnePlayer"'):
                push_error("[DEBUG-SMOKE] report has wrong scene/object ID: " + file)
                quit(1)
                return
            print("[DEBUG-SMOKE] PASS: ", file)
    if not found:
        push_error("[DEBUG-SMOKE] F5 did not save a new report")
    quit(0 if found else 1)
