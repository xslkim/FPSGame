extends SceneTree

# Replace coarse building boxes with the visible mesh, so windows remain shootable.
func _initialize():
    var path = "res://scenes/levels/env_level2.tscn"
    var scene = load(path).instantiate()
    var repaired = 0
    for mesh in scene.find_children("*", "MeshInstance3D", true, false):
        var body = mesh.get_node_or_null("ProxyCollision")
        if body == null or mesh.mesh == null:
            continue
        for child in body.get_children():
            if child is CollisionShape3D and child.shape is BoxShape3D:
                child.shape = mesh.mesh.create_trimesh_shape()
                child.transform = Transform3D.IDENTITY
                repaired += 1
    var packed = PackedScene.new()
    var result = packed.pack(scene)
    if result == OK:
        result = ResourceSaver.save(packed, path)
    print("[COLLISION-REPAIR] meshes=", repaired, " save=", result)
    scene.free()
    quit(0 if result == OK else 1)
