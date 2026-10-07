extends SceneTree
func _initialize():
    var model = load("res://assets/models/monsters/toon/infantry_combat_idle.FBX").instantiate()
    root.add_child(model)
    var player = model.find_child("AnimationPlayer", true, false)
    print("IDLE LIST ", player.get_animation_list())
    for name in player.get_animation_list():
        var animation = player.get_animation(name)
        print("IDLE ", name, " tracks=", animation.get_track_count())
        for index in range(animation.get_track_count()):
            print(animation.track_get_path(index))
        var fixed = animation.duplicate(true)
        fixed.loop_mode = Animation.LOOP_LINEAR
        for index in range(fixed.get_track_count()):
            fixed.track_set_path(index, NodePath("Model/" + str(fixed.track_get_path(index))))
        var library = load("res://assets/models/monsters/toon/toon_anims.tres")
        library.remove_animation("locomotion")
        library.add_animation("locomotion", fixed)
        print("SAVE ", ResourceSaver.save(library, "res://assets/models/monsters/toon/toon_anims.tres"))
    quit()
