extends SceneTree

func _initialize():
    var scene = load("res://scenes/ui/level_choose.tscn").instantiate()
    root.add_child(scene)
    await process_frame
    var gun = scene.get_node("ViewportLayer/SubViewportContainer/SubViewport/Camera3D/AK47View")
    gun.quaternion = Quaternion.IDENTITY
    var model = gun.get_node("Model")
    print("MODEL BASIS ", model.basis, " barrel ", -model.basis.x.normalized())
    for mesh in model.find_children("*", "MeshInstance3D", true, false):
        if not mesh.visible: continue
        var tr = gun.global_transform.affine_inverse() * mesh.global_transform
        var points: Array[Vector3] = []
        for surface in mesh.mesh.get_surface_count():
            for p in mesh.mesh.surface_get_arrays(surface)[Mesh.ARRAY_VERTEX]:
                var v = tr * p
                if v.z < -0.18: points.append(v)
        print(mesh.name, " TIP VERTICES ", points)
    quit()
