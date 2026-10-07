extends SceneTree

func _initialize():
    for scene_path in ["res://scenes/ui/menu.tscn", "res://scenes/ui/level_choose.tscn"]:
        var scene = load(scene_path).instantiate()
        root.add_child(scene)
        await process_frame
        for gun_name in ["M4View", "AK47View"]:
            var gun = scene.get_node_or_null("ViewportLayer/SubViewportContainer/SubViewport/Camera3D/" + gun_name)
            if gun == null:
                continue
            gun.quaternion = Quaternion.IDENTITY
            var points: Array[Vector3] = []
            for mesh in gun.get_node("Model").find_children("*", "MeshInstance3D", true, false):
                if not mesh.visible or mesh.mesh == null:
                    continue
                var transform = gun.global_transform.affine_inverse() * mesh.global_transform
                var bounds = transform * mesh.get_aabb()
                print(scene_path, " ", gun_name, " ", mesh.name, " ", bounds)
                for surface in mesh.mesh.get_surface_count():
                    for vertex in mesh.mesh.surface_get_arrays(surface)[Mesh.ARRAY_VERTEX]:
                        points.append(transform * vertex)
            var tip_z = INF
            for vertex in points:
                tip_z = min(tip_z, vertex.z)
            var tip = Vector3.ZERO
            var count = 0
            for vertex in points:
                if vertex.z < tip_z + 0.001:
                    tip += vertex
                    count += 1
            print("MUZZLE ", gun_name, " tip=", tip / max(count, 1), " points=", count)
        scene.queue_free()
    quit()
