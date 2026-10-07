extends SceneTree

func _initialize():
    for file in ["SHW_Add_effect_r_01_01", "SHW_Add_effect_r_01_02"]:
        var model = load("res://assets/models/school_hallway/Models/Add_effect/" + file + ".fbx").instantiate()
        for mesh in model.find_children("*", "MeshInstance3D", true, false):
            var arr = mesh.mesh.surface_get_arrays(0)
            print("FBX ", file, " ", mesh.name, " ", mesh.transform, " bounds ", mesh.get_aabb(), " uv=", arr[Mesh.ARRAY_TEX_UV].size())
        model.free()
    var env = load("res://scenes/levels/env_school_hallway.tscn").instantiate()
    for mesh in env.find_children("SHW_Add_effect*", "MeshInstance3D", true, false):
        if not mesh.visible: continue
        var arr = mesh.mesh.surface_get_arrays(0)
        print("EXPORTED ", mesh.name, " bounds ", mesh.get_aabb(), " uv=", 0 if arr[Mesh.ARRAY_TEX_UV] == null else arr[Mesh.ARRAY_TEX_UV].size())
        break
    env.free()
    quit()
