extends SceneTree

func _initialize():
    call_deferred("run")

func run():
    root.size = Vector2i(1280,720)
    var level = load("res://scenes/levels/level2.tscn").instantiate()
    root.add_child(level)
    current_scene = level
    await process_frame
    level.set_process(false)
    for tween in get_processed_tweens(): tween.kill()
    var camera = level.get_node("Camera3D")
    var space = camera.get_world_3d().direct_space_state
    for g in 3:
        camera.global_transform = level.get_node("CamPositions/cam_pos_%d" % g).global_transform
        for window in level.get_node("Windows/group%d" % g).get_children():
            var enemy = load("res://scenes/battle/monsters/toon.tscn").instantiate()
            level.add_child(enemy)
            enemy.set("FireWindow",window)
            enemy.call("Born",window.call("GetSrcPosition"),0,0.0)
            for i in 180: await physics_frame
            print("[WINDOW-PROBE] G",g,"/",window.name," root=",enemy.global_position," head=",enemy.get("HeadHitPosition")," floor=",enemy.is_on_floor())
            for point in [enemy.get("HeadHitPosition"),enemy.global_position+Vector3.UP*.75,enemy.global_position+Vector3.UP*1.45]:
                var screen = camera.unproject_position(point)
                var hit = space.intersect_ray(PhysicsRayQueryParameters3D.create(camera.project_ray_origin(screen),camera.project_ray_origin(screen)+camera.project_ray_normal(screen)*200,7))
                print("[WINDOW-PROBE] point=",point," screen=",screen," blocker=",hit.get("collider")," pos=",hit.get("position"))
            enemy.queue_free()
            await process_frame
    quit()
