extends SceneTree

func _initialize():
    call_deferred("run")

func run():
    var stage = Node3D.new()
    root.add_child(stage)
    var camera = Camera3D.new()
    camera.position = Vector3(0, 6, 0)
    stage.add_child(camera)
    camera.current = true
    var boss = load("res://scenes/battle/monsters/level2_boss.tscn").instantiate()
    stage.add_child(boss)
    boss.call("Born", Vector3(0, 0, -25), 0, 1000.0)
    boss.set_physics_process(false)
    var anim = boss.get_node("AnimationPlayer")
    anim.callback_mode_process = AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
    var weak = boss.get("WeakSpot")
    for angle in [0, 40, 80, 120, 180]:
        boss.basis = (Basis.from_euler(Vector3(0, PI, 0)) * Basis.from_euler(Vector3(deg_to_rad(20), deg_to_rad(-angle), 0))).scaled(Vector3.ONE * 3)
        for clip in ["Idle", "Skill1", "Skill2"]:
            for fraction in [0.0, .5, .9]:
                anim.play(clip, 0)
                anim.seek(anim.get_animation(clip).length * fraction, true)
                await physics_frame
                await physics_frame
                await process_frame
                var hits = {}
                var center = camera.unproject_position(weak.global_position)
                var edge = camera.unproject_position(weak.global_position + Vector3(.45, 0, 0))
                var pixels = center.distance_to(edge)
                for x in range(-8, 9):
                    for y in range(-8, 9):
                        var offset = Vector2(x, y) / 8.0
                        if offset.length_squared() > .99: continue
                        var screen = center + offset * pixels
                        var origin = camera.project_ray_origin(screen)
                        var hit = stage.get_world_3d().direct_space_state.intersect_ray(PhysicsRayQueryParameters3D.create(origin, origin + camera.project_ray_normal(screen) * 100, 2))
                        var key = hit.collider.name if hit else "none"
                        hits[key] = hits.get(key, 0) + 1
                print("[WEAK-PROBE] ", angle, " ", clip, " ", fraction, " ", hits)
    quit()
