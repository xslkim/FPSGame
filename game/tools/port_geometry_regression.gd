extends SceneTree
var failures = 0
func check(condition, label):
    print("[PORT-GEOMETRY] ", "PASS " if condition else "FAIL ",label)
    if not condition: failures += 1

func _initialize():
    var town = load("res://scenes/levels/env_level2.tscn").instantiate()
    root.add_child(town)
    await physics_frame
    await physics_frame
    var space = town.get_world_3d().direct_space_state
    var origin = Vector3(1.826846,-2.10623,-17.00188)
    for target in [Vector3(23.425,0.16,-35.43), Vector3(18.53,0.01,-35.8)]:
        check(space.intersect_ray(PhysicsRayQueryParameters3D.create(origin,target,1)).is_empty(),"reported right window remains shootable: "+str(target))
    check(not space.intersect_ray(PhysicsRayQueryParameters3D.create(Vector3(22,3,-35),Vector3(22,-20,-35),1)).is_empty(),"town still has physical roof/floor collision")
    town.queue_free()
    await process_frame
    var town_level = load("res://scenes/levels/level2.tscn").instantiate()
    var town_rotations = [Quaternion(-.0024611927,-.97584873,-.010994507,.21815674),Quaternion(-.018756064,.21134768,.004056011,.97722256),Quaternion(-.03198121,.8337852,.048574958,.54901737)]
    for i in 3:
        var q = town_rotations[i].normalized()
        var expected = Basis(Quaternion(q.x,-q.y,-q.z,q.w)) * Basis(Vector3.UP,PI)
        var actual = town_level.get_node("CamPositions/cam_pos_%d" % i).basis
        check((actual.x-expected.x).length()<0.0001 and (actual.y-expected.y).length()<0.0001 and (actual.z-expected.z).length()<0.0001,"L2 camera %d matches all Unity camera axes" % i)
    town_level.free()
    var level = load("res://scenes/levels/level3.tscn").instantiate()
    var quaternions = [Quaternion(-.037342947,.90012144,.078750459,.42683166),Quaternion(-.0085806187,.99135503,.086732367,.098076867),Quaternion(-.014609861,.98209864,.08592254,.1669914),Quaternion(.048167909,.83023242,.072635954,-.55056154),Quaternion(.020420068,.96846642,.084729867,-.23340235),Quaternion(-.027727064,.94443876,.08262773,.3169216),Quaternion(.03683362,.90285902,.078989969,-.42101005)]
    for i in 7:
        var unity_forward = Basis(quaternions[i]).z
        unity_forward.x *= -1
        check((-level.get_node("CamPositions/cam_pos_%d" % i).basis.z-unity_forward).length()<0.0001,"L3 camera %d matches Unity forward" % i)
    var city = level.get_node("Environment")
    var data = JSON.parse_string(FileAccess.get_file_as_string("res://data/env_export/level3.json"))
    var paths = []
    var bad_meshes = []
    var checked = 0
    for item in data.nodes:
        var parent = int(item.parent)
        var node_path = "" if parent < 0 else paths[parent]+"/"+str(item.name)
        paths.append(node_path)
        if not str(item.fbx).ends_with("city.fbx") or item.meshName == "": continue
        var node = city.get_node_or_null(NodePath(node_path.trim_prefix("/")))
        if node == null: bad_meshes.append(node_path); continue
        var visual = node if node is MeshInstance3D else node.get_node_or_null("GeoFix")
        if visual == null: visual = node.get_node_or_null("Geometry")
        var actual = visual.get_aabb()
        if visual != node: actual = visual.transform * actual
        var size = Vector3(item.meshSize[0],item.meshSize[1],item.meshSize[2])
        var center = Vector3(item.meshCenter[0],item.meshCenter[1],item.meshCenter[2])
        if (actual.size-size).length()>0.003 or (actual.get_center()-center).length()>0.003: bad_meshes.append(node_path)
        checked += 1
    check(checked==1325 and bad_meshes.is_empty(),"1325 city meshes match Unity bounds; bad="+str(bad_meshes.slice(0,3)))
    level.free()
    print("[PORT-GEOMETRY] ","PASS" if failures==0 else "FAIL")
    quit(0 if failures==0 else 1)
