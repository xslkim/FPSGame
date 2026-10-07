extends SceneTree

# Run tools/export_city_geometry.py first. Raw FBX geometry is matched against
# Unity's exported bounds, which disambiguate repeated Maya mesh names.
func _initialize():
    var input = ProjectSettings.globalize_path("res://").path_join("../temp/city_geometry.json").simplify_path()
    var source = JSON.parse_string(FileAccess.get_file_as_string(input))
    var data = JSON.parse_string(FileAccess.get_file_as_string("res://data/env_export/level3.json"))
    var by_name = {}
    for geometry in source:
        var vertices = geometry.vertices
        var bounds = AABB(Vector3(vertices[0],vertices[1],vertices[2])*0.01,Vector3.ZERO)
        for i in range(0,vertices.size(),3):
            bounds = bounds.expand(Vector3(vertices[i],vertices[i+1],vertices[i+2])*0.01)
        geometry["bounds"] = bounds
        for mesh_name in geometry.names:
            if not by_name.has(mesh_name): by_name[mesh_name] = []
            by_name[mesh_name].append(geometry)
    var materials = {}
    for definition in data.materials:
        var mat = StandardMaterial3D.new()
        mat.resource_name = definition.name
        var color = definition.color
        mat.albedo_color = Color(color[0],color[1],color[2],1)
        var texture = str(definition.mainTex).replace("Assets/AssetTools/MobilePostProcess/", "res://assets/models/mobile_pp/")
        if ResourceLoader.exists(texture): mat.albedo_texture = load(texture)
        mat.roughness = 1.0
        mat.vertex_color_use_as_albedo = true
        materials[definition.name] = mat
    var path = "res://scenes/levels/env_level3.tscn"
    var scene = load(path).instantiate()
    var paths = []
    var matched = 0
    var failed = 0
    for item in data.nodes:
        var parent = int(item.parent)
        var node_path = "" if parent < 0 else paths[parent] + "/" + str(item.name)
        paths.append(node_path)
        if not str(item.fbx).ends_with("city.fbx") or item.meshName == "": continue
        var node = scene.get_node_or_null(NodePath(node_path.trim_prefix("/")))
        if node == null: continue
        var size = Vector3(item.meshSize[0],item.meshSize[1],item.meshSize[2])
        var center = Vector3(item.meshCenter[0],item.meshCenter[1],item.meshCenter[2])
        var geometry = null
        var best_error = INF
        for candidate in by_name.get(item.meshName,[]):
            var error = (candidate.bounds.size-size).length()
            if error < best_error:
                best_error = error
                geometry = candidate
        if geometry == null or best_error > 0.003:
            print("UNMATCHED ",node_path," error=",best_error)
            failed += 1
            continue
        var mesh = build_mesh(geometry,center)
        if mesh == null:
            failed += 1
            continue
        var visual = node if node is MeshInstance3D else node.get_node_or_null("GeoFix")
        if visual == null:
            visual = MeshInstance3D.new()
            visual.name = "Geometry"
            node.add_child(visual)
            visual.owner = scene
        visual.mesh = mesh
        if visual != node: visual.transform = Transform3D.IDENTITY
        for i in mesh.get_surface_count():
            var material_index = int(mesh.get_meta("material_%d" % i))
            if material_index < item.materials.size() and materials.has(item.materials[material_index]):
                visual.set_surface_override_material(i,materials[item.materials[material_index]])
        var collision = node.get_node_or_null("Collision/CollisionShape3D")
        if collision != null:
            collision.shape = mesh.create_trimesh_shape()
            collision.transform = Transform3D.IDENTITY
        matched += 1
    var packed = PackedScene.new()
    var result = packed.pack(scene)
    if result == OK and failed == 0: result = ResourceSaver.save(packed,path)
    print("[CITY-REPAIR] exact_meshes=",matched," unmatched=",failed," save=",result)
    scene.free()
    quit(0 if result == OK and failed == 0 else 1)

func attribute(layer, key, index_key, corner, control_point, polygon):
    var index = corner
    match layer.get("MappingInformationType", "ByPolygonVertex"):
        "ByVertice", "ByVertex": index = control_point
        "ByPolygon": index = polygon
        "AllSame": index = 0
    if layer.get("ReferenceInformationType", "Direct") == "IndexToDirect" and layer.has(index_key):
        index = int(layer[index_key][index])
    return index

func build_mesh(geometry,center):
    var layers = geometry.layers
    var normal_layer = layers.get("LayerElementNormal",{})
    var uv_layer = layers.get("LayerElementUV",{})
    var material_layer = layers.get("LayerElementMaterial",{})
    var color_layer = layers.get("LayerElementColor",{})
    var old_center = geometry.bounds.get_center()
    var shift = center-old_center
    var groups = {}
    var corners = []
    var polygon = 0
    for corner in geometry.polygons.size():
        var raw_index = int(geometry.polygons[corner])
        var control_point = raw_index if raw_index >= 0 else -raw_index-1
        corners.append([corner,control_point])
        if raw_index >= 0: continue
        var material_index = polygon if material_layer.get("MappingInformationType", "AllSame") == "ByPolygon" else 0
        var material = int(material_layer.get("Materials",[0])[material_index])
        if not groups.has(material): groups[material] = [PackedVector3Array(),PackedVector3Array(),PackedVector2Array(),PackedColorArray()]
        var arrays = groups[material]
        var face = PackedVector3Array()
        for c in corners:
            var cp = int(c[1])*3
            face.append(Vector3(geometry.vertices[cp],geometry.vertices[cp+1],geometry.vertices[cp+2]))
        var face_normal = Vector3.ZERO
        for i in face.size():
            face_normal += face[i].cross(face[(i+1)%face.size()])
        var axis = face_normal.abs().max_axis_index()
        var projected = PackedVector2Array()
        for point in face:
            match axis:
                0: projected.append(Vector2(point.y,point.z))
                1: projected.append(Vector2(point.x,point.z))
                2: projected.append(Vector2(point.x,point.y))
        var triangles = Geometry2D.triangulate_polygon(projected)
        if triangles.is_empty():
            # FBX occasionally includes zero-area polygons; they have no visible surface.
            if face_normal.length_squared() > 0.000001:
                push_error("Cannot triangulate city polygon")
                return null
        for triangle in range(0,triangles.size(),3):
            var indices = [triangles[triangle],triangles[triangle+1],triangles[triangle+2]]
            if (face[indices[1]]-face[indices[0]]).cross(face[indices[2]]-face[indices[0]]).dot(face_normal)>0:
                var swap = indices[1]
                indices[1] = indices[2]
                indices[2] = swap
            for index in indices:
                var c = corners[index]
                var cp = int(c[1])*3
                arrays[0].append(Vector3(geometry.vertices[cp],geometry.vertices[cp+1],geometry.vertices[cp+2])*0.01+shift)
                var ni = attribute(normal_layer,"Normals","NormalsIndex",c[0],c[1],polygon)*3
                var normals = normal_layer.get("Normals",[])
                arrays[1].append(Vector3(normals[ni],normals[ni+1],normals[ni+2]).normalized() if normals.size()>ni+2 else Vector3.UP)
                var ui = attribute(uv_layer,"UV","UVIndex",c[0],c[1],polygon)*2
                var uvs = uv_layer.get("UV",[])
                arrays[2].append(Vector2(uvs[ui],1-uvs[ui+1]) if uvs.size()>ui+1 else Vector2.ZERO)
                var ci = attribute(color_layer,"Colors","ColorIndex",c[0],c[1],polygon)*4
                var colors = color_layer.get("Colors",[])
                arrays[3].append(Color(colors[ci],colors[ci+1],colors[ci+2],colors[ci+3]) if colors.size()>ci+3 else Color.WHITE)
        corners.clear()
        polygon += 1
    var mesh = ArrayMesh.new()
    for material in groups:
        var arrays = []
        arrays.resize(Mesh.ARRAY_MAX)
        arrays[Mesh.ARRAY_VERTEX] = groups[material][0]
        arrays[Mesh.ARRAY_NORMAL] = groups[material][1]
        arrays[Mesh.ARRAY_TEX_UV] = groups[material][2]
        arrays[Mesh.ARRAY_COLOR] = groups[material][3]
        mesh.set_meta("material_%d" % mesh.get_surface_count(),material)
        mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES,arrays)
    return mesh
