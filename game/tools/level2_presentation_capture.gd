extends SceneTree

# Fixed rendered comparison of the reported Level2 camera and waiting enemy poses.
# Run with --qa-save-dir:<isolated-dir> --qa-udp-port:<unused-port>.
func _initialize() -> void:
    call_deferred("_run")

func _run() -> void:
    root.size = Vector2i(1280, 720)
    var scene = load("res://scenes/levels/level2.tscn").instantiate()
    root.add_child(scene)
    current_scene = scene
    await process_frame
    scene.set_process(false)
    var camera = scene.get_node("Camera3D")
    camera.global_transform = scene.get_node("CamPositions/cam_pos_0").global_transform
    for monster in scene.get_node("MonsterPool").get_children():
        monster.hide()
        monster.set_physics_process(false)
    var offset = 0
    var positions = [Vector3(8.258181, -6.3289895, -32.787052), Vector3(21.536856, -6.0757537, -31.788906)]
    for type in ["toon", "toon_alien"]:
        var enemy = load("res://scenes/battle/monsters/" + type + ".tscn").instantiate()
        scene.add_child(enemy)
        enemy.call("Born", positions[offset], 0, 30.0)
        enemy.call("FaceCamera")
        offset += 1
    for i in 12:
        await physics_frame
    await RenderingServer.frame_post_draw
    var destination = "G:/FPSGame/temp/level2-waiting-pose.png"
    var error = root.get_texture().get_image().save_png(destination)
    print("[L2-CAPTURE] ", destination, " status=", error)
    quit(0 if error == OK else 1)
