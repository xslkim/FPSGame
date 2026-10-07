extends SceneTree

func _initialize():
    var effect = load("res://assets/effects/impact_concrete.tscn").instantiate()
    root.add_child(effect)
    await process_frame
    var rock = effect.get_node("Chips").draw_pass_1
    var puff = effect.get_node("Puff").draw_pass_1.material
    var ok = rock is ArrayMesh and puff.billboard_mode == BaseMaterial3D.BILLBOARD_PARTICLES
    ok = ok and puff.particles_anim_h_frames == 4 and puff.particles_anim_v_frames == 16
    ok = ok and puff.transparency == BaseMaterial3D.TRANSPARENCY_ALPHA
    ok = ok and effect.get_node("Puff").process_material.anim_speed_min == 1.0
    for path in ["impact_dust", "impact_wood"]:
        var dust = load("res://assets/effects/" + path + ".tscn").instantiate()
        root.add_child(dust)
        var particles = dust.get_node("Puff")
        var material = particles.draw_pass_1.material
        ok = ok and material.particles_anim_h_frames == 8 and material.particles_anim_v_frames == 8
    print("[EFFECT-REGRESSION] ", "PASS" if ok else "FAIL", " concrete uses original mesh, smoke uses transparent animated frames")
    for arg in OS.get_cmdline_user_args():
        if arg.begins_with("--effect-shot:"):
            var camera = Camera3D.new()
            root.add_child(camera)
            camera.current = true
            camera.near = 0.01
            effect.position = Vector3(0, -0.06, -0.7)
            var environment = WorldEnvironment.new()
            environment.environment = Environment.new()
            environment.environment.background_mode = Environment.BG_COLOR
            environment.environment.background_color = Color(0.06, 0.11, 0.17)
            environment.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
            environment.environment.ambient_light_energy = 0.7
            root.add_child(environment)
            DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_WINDOWED)
            DisplayServer.window_set_size(Vector2i(1280, 720))
            effect.call("Activate", -1.0)
            await create_timer(0.12).timeout
            await RenderingServer.frame_post_draw
            root.get_texture().get_image().save_png(arg.trim_prefix("--effect-shot:"))
    quit(0 if ok else 1)
