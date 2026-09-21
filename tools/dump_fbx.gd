extends SceneTree

func _init() -> void:
    for p in [
        "res://assets/models/monsters/toon/ToonSoldiers_Militias.FBX",
        "res://assets/models/monsters/toon/weapon_ak47.FBX",
        "res://assets/models/monsters/toon_alien/stardudes_suit_mk1.fbx",
        "res://assets/models/monsters/toon_alien/helmet_m_alpha.fbx",
        "res://assets/models/monsters/toon_alien/rifle_battlerifle.fbx",
        "res://assets/models/monsters/level2_boss/Modle.FBX",
        "res://assets/models/monsters/level2_boss/Weapon.FBX",
    ]:
        var sc := load(p)
        print("==== ", p)
        if sc == null:
            print("  LOAD FAILED")
            continue
        var inst = sc.instantiate()
        _dump(inst, 0)
        inst.free()
    quit()

func _dump(n: Node, d: int) -> void:
    var extra := ""
    if n is MeshInstance3D:
        extra = " [Mesh surfaces=%d]" % (n as MeshInstance3D).mesh.get_surface_count()
    if n is Skeleton3D:
        extra = " [Skeleton bones=%d]" % (n as Skeleton3D).get_bone_count()
    print("  ".repeat(d) + n.name + " <" + n.get_class() + ">" + extra)
    for c in n.get_children():
        _dump(c, d + 1)
