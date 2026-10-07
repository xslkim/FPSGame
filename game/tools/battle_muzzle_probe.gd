extends SceneTree

func _initialize():
    for type in 3:
        var paths = ["ak47/ak47.fbx", "m4/m4.fbx", "handgun/handgun.fbx"]
        var model = load("res://assets/models/guns/" + paths[type]).instantiate()
        model.rotation.y = -PI / 2 if type == 0 else PI
        model.scale = Vector3.ONE * (0.08 if type == 0 else 0.725)
        root.add_child(model)
        await process_frame
        for mesh in model.find_children("*", "MeshInstance3D", true, false):
            if not mesh.visible: continue
            print("SKIN ", mesh.name, " ", mesh.skin, " skel ", mesh.skeleton)
            var points: Array[Vector3] = []
            for s in mesh.mesh.get_surface_count():
                for vertex in mesh.mesh.surface_get_arrays(s)[Mesh.ARRAY_VERTEX]:
                    points.append(mesh.global_transform * vertex)
            var min_z = INF
            for p in points: min_z = min(min_z, p.z)
            var front: Array[Vector3] = []
            for p in points:
                if p.z < min_z + 0.001: front.append(p)
            print(type, " ", mesh.name, " bounds ", mesh.global_transform * mesh.get_aabb(), " FRONT ", front)
            if type == 0:
                var barrel: Array[Vector3] = []
                for p in points:
                    if p.z < -0.08: barrel.append(p)
                print("AK BARREL ", barrel)
        model.free()
    quit()
