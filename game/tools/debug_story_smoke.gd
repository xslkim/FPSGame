extends SceneTree

func _initialize() -> void:
    call_deferred("_run")

func _run() -> void:
    var report_dir: String = root.get_node("DebugOverlay").call("GetReportDirectory")
    DirAccess.make_dir_recursive_absolute(report_dir)
    var before = DirAccess.get_files_at(report_dir)
    Engine.time_scale = 6.0
    var scene = load("res://scenes/levels/level1_story.tscn").instantiate()
    root.add_child(scene)
    current_scene = scene
    while scene.get("_clock") < 4.0:
        await process_frame
    var f5 = InputEventKey.new()
    f5.keycode = KEY_F5
    f5.pressed = true
    Input.parse_input_event(f5)
    for i in 8:
        await process_frame
    for file in DirAccess.get_files_at(report_dir):
        if file.ends_with("L1-Story.json") and not file in before:
            var report = FileAccess.get_file_as_string(report_dir.path_join(file))
            if report.contains('"scene_id": "L1/Story"') and report.contains('"left_sole_y"') and report.contains('"cut_marker"'):
                print("[DEBUG-STORY-SMOKE] PASS: ", file)
                quit()
                return
    push_error("[DEBUG-STORY-SMOKE] story report lacks camera/dancer state")
    quit(1)
