extends SceneTree
func _initialize():
    var root = Node3D.new()
    get_root().add_child(root)
    var cam = Camera3D.new()
    cam.current = true
    cam.fov = 50.0
    cam.position = Vector3(0, 3, 0)
    cam.rotation_degrees = Vector3(-90, 0, 0)
    root.add_child(cam)
    var light = DirectionalLight3D.new()
    light.rotation_degrees = Vector3(-90, 0, 0)
    root.add_child(light)
    var defs = [
        ["res://assets/models/guns/ak47/ak47.fbx", Vector3(-1.2, 0, -1.0), Vector3(0, 0, 0), 0.4],
        ["res://assets/models/guns/m4/m4.fbx", Vector3(0.0, 0, -1.0), Vector3(0, 0, 0), 1.0],
        ["res://assets/models/guns/handgun/handgun.fbx", Vector3(1.0, 0, -1.0), Vector3(0, 0, 0), 1.0],
    ]
    for d in defs:
        var inst = load(d[0]).instantiate()
        var wrap = Node3D.new()
        wrap.add_child(inst)
        wrap.position = d[1]
        wrap.rotation_degrees = d[2]
        wrap.scale = Vector3.ONE * d[3]
        root.add_child(wrap)
    await process_frame
    await process_frame
    await create_timer(0.3).timeout
    var img = get_root().get_texture().get_image()
    img.save_png("G:/FPSGame/tools/screenshots/gun_probe_top.png")
    print("saved")
    quit()
