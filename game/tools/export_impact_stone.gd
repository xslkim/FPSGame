extends SceneTree

func _initialize():
    var model = load("res://assets/effects/models/Stone1.FBX").instantiate()
    var source = model.find_children("*", "MeshInstance3D", true, false)[0]
    var mesh = ArrayMesh.new()
    var bounds = source.mesh.get_aabb()
    var scale_factor = 0.05 / max(bounds.size.x, max(bounds.size.y, bounds.size.z))
    for s in source.mesh.get_surface_count():
        var arrays = source.mesh.surface_get_arrays(s)
        var vertices = arrays[Mesh.ARRAY_VERTEX]
        for v in vertices.size():
            vertices[v] = (vertices[v] - bounds.get_center()) * scale_factor
        arrays[Mesh.ARRAY_VERTEX] = vertices
        mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
    ResourceSaver.save(mesh, "res://assets/effects/models/impact_stone.tres")
    print("[STONE] original Unity mesh normalized to 5cm: ", bounds)
    model.free()
    quit()
