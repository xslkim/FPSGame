extends SceneTree

# 打印 FBX 实例结构与网格顶点数/包围盒
func _initialize():
    for p in ["res://assets/models/guns/ak47/ak47.fbx", "res://assets/models/guns/m4/m4.fbx"]:
        print("=== ", p)
        var inst = load(p).instantiate()
        _walk(inst, "")
    quit()

func _walk(n: Node, indent: String):
    var info := indent + n.name + " [" + n.get_class() + "]"
    if n is MeshInstance3D:
        var m := (n as MeshInstance3D).mesh
        if m == null:
            info += " mesh=NULL"
        else:
            info += " mesh=" + m.get_class() + " aabb=" + str(m.get_aabb())
            if m is ArrayMesh:
                var arrs := (m as ArrayMesh).surface_get_arrays(0)
                var verts: PackedVector3Array = arrs[ArrayMesh.ARRAY_VERTEX]
                if verts.size() > 0:
                    var mn := verts[0]
                    var mx := verts[0]
                    for v in verts:
                        mn = mn.min(v)
                        mx = mx.max(v)
                    info += " verts=" + str(verts.size()) + " bbox=" + str(mn) + ".." + str(mx)
    if n is Node3D:
        info += " t=" + str((n as Node3D).transform)
    print(info)
    for c in n.get_children():
        _walk(c, indent + "  ")
