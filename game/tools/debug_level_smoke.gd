extends SceneTree

func _initialize() -> void:
    call_deferred("_run")

func _run() -> void:
    var report_dir: String = root.get_node("DebugOverlay").call("GetReportDirectory")
    DirAccess.make_dir_recursive_absolute(report_dir)
    var before = DirAccess.get_files_at(report_dir)
    var scene = load("res://scenes/levels/level2.tscn").instantiate()
    root.add_child(scene)
    current_scene = scene
    for i in 12:
        await process_frame
    var f5 = InputEventKey.new()
    f5.keycode = KEY_F5
    f5.pressed = true
    Input.parse_input_event(f5)
    for i in 8:
        await process_frame
    for file in DirAccess.get_files_at(report_dir):
        if file.ends_with("L2-Battle.json") and not file in before:
            var report = FileAccess.get_file_as_string(report_dir.path_join(file))
            if report.contains('"scene_id": "L2/Battle"') and report.contains('"sides"') and report.contains('"group"'):
                print("[DEBUG-LEVEL-SMOKE] PASS: ", file)
                quit()
                return
    push_error("[DEBUG-LEVEL-SMOKE] L2 report lacks scene/wave/fire state")
    quit(1)
