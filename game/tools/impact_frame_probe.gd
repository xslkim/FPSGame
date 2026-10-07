extends SceneTree

func _initialize():
    root.mode = Window.MODE_WINDOWED
    root.size = Vector2i(1280, 720)
    var scene = load("res://scenes/levels/level1_battle.tscn").instantiate()
    scene.process_mode = Node.PROCESS_MODE_DISABLED
    root.add_child(scene)
    scene.get_node("Environment").visible = true
    var camera = scene.get_node("Camera3D")
    camera.position = Vector3(0.7353256, 1, -10.27063)
    camera.rotation_degrees = Vector3(5.29308, 121.215034, -0.000345)
    camera.fov = 45
    camera.current = true
    for frame in 5: await process_frame
    scene.get_node("Environment").visible = true
    camera.position = Vector3(0.7353256, 1, -10.27063)
    camera.rotation_degrees = Vector3(5.29308, 121.215034, -0.000345)
    camera.fov = 45
    await RenderingServer.frame_post_draw
    root.get_texture().get_image().save_png("G:/FPSGame/temp/impact_environment5.png")
    for mesh in scene.get_node("Environment").find_children("*", "MeshInstance3D", true, false):
        if not mesh.is_visible_in_tree(): continue
        var material = mesh.get_active_material(0)
        if material is StandardMaterial3D and material.transparency != BaseMaterial3D.TRANSPARENCY_DISABLED:
            print("TRANSPARENT ", mesh.get_path(), " ", material.resource_name)
    quit()
