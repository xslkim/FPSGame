extends SceneTree

func _initialize() -> void:
    call_deferred("_run")

func _run() -> void:
    var boss = load("res://scenes/battle/monsters/level2_boss.tscn").instantiate()
    root.add_child(boss)
    var skeleton = boss.find_child("Skeleton3D", true, false)
    var result = {}
    for i in skeleton.get_bone_count():
        var pose = boss.global_transform.affine_inverse() * skeleton.global_transform * skeleton.get_bone_global_rest(i)
        var local = skeleton.get_bone_rest(i)
        result[skeleton.get_bone_name(i)] = {"origin": [pose.origin.x, pose.origin.y, pose.origin.z], "local_origin": [local.origin.x, local.origin.y, local.origin.z], "basis": [[pose.basis.x.x, pose.basis.y.x, pose.basis.z.x], [pose.basis.x.y, pose.basis.y.y, pose.basis.z.y], [pose.basis.x.z, pose.basis.y.z, pose.basis.z.z]]}
    var file = FileAccess.open("G:/FPSGame/temp/level2_boss_godot_rig.json", FileAccess.WRITE)
    file.store_string(JSON.stringify(result, "  "))
    file.close()
    boss.call("Born", Vector3(0, 0, -25), 0, 1000.0)
    boss.set_physics_process(false)
    boss.rotation.y = 0
    boss.get_node("AnimationPlayer").advance(0)
    await process_frame
    for region in boss.find_children("*", "StaticBody3D", true, false):
        if region.has_method("Hit"):
            var shape = region.get_node("HitShape").shape
            print(region.name, " position=", region.global_position, " radius=", shape.radius * region.global_basis.get_scale().x)
    quit()
