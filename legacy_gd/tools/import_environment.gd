# Rebuilds a Unity-exported environment subtree (JSON from SceneExporter.cs)
# as a Godot scene: Node3D hierarchy, meshes from imported FBX (ufbx),
# StandardMaterial3D materials, lights, and collision.
#
# Run (editor binary, game mode — FBX remap works after a first --import):
#   godot --headless -s tools/import_environment.gd -- --json school_day --out env_school_hallway
# (Requires assets to be imported once first: godot --headless --editor --quit)
#
# Options:
#   --map "Unity/Prefix/=res://assets/models/xxx/,..."   asset path mapping(s)
#   --force-active true          treat inactive Unity nodes as active
#   --skip-regex "LOD_?[12]"     skip whole subtrees whose node name matches
#   --max-active-lights N        keep only top-N lights by energy visible
#   --sky-panorama <res path>    add WorldEnvironment with PanoramaSky
#   --sky-cubemap <res path>     add WorldEnvironment with CubemapSky
extends SceneTree

const JSON_DIR := "res://data/env_export/"
const OUT_DIR := "res://scenes/levels/"

# Node names matching this pattern get a box-shaped proxy collision body
# (the hallway pack only ships dedicated collision meshes for doors/stairs).
const PROXY_COLLISION_PATTERN := "(?i)(wall|floor|ceiling|pillar)"

var _mesh_cache := {} # res fbx path -> {mesh name -> ArrayMesh}
var _mat_cache := {} # material name -> StandardMaterial3D
var _mat_defs := {} # material name -> JSON dict
var _proxy_re: RegEx
var _with_proxy_collision := true
var _maps: Array = [] # [[unity_prefix, res_root], ...]
var _force_active := false
var _skip_re: RegEx = null
var _max_active_lights := 0
var _lights_made: Array = [] # [Light3D] for top-N selection
var _fx_nodes: Array = [] # names of ParticleSystem placeholder nodes
var _sky_panorama := ""
var _sky_cubemap := ""
var _stats := {
	"nodes": 0, "mesh_instances": 0, "lights": 0, "inactive": 0,
	"collision_bodies": 0, "proxy_collision_bodies": 0,
	"missing_meshes": [], "missing_textures": [], "missing_fbx": [],
	"materials_built": 0, "fbx_materials_kept": 0,
	"skipped_nodes": 0, "skipped_meshes": 0, "skipped_lights": 0,
	"lights_deactivated": 0, "fx_placeholders": 0, "geo_fixed": 0,
}


func _init() -> void:
	_run()
	quit(0)


func _run() -> void:
	var args := _parse_args()
	var json_name: String = args.get("json", "school_day")
	var out_name: String = args.get("out", json_name.replace("school_", "env_school_"))
	_with_proxy_collision = args.get("proxy-collision", "true") == "true"
	_force_active = args.get("force-active", "false") == "true"
	_max_active_lights = int(args.get("max-active-lights", "0"))
	_sky_panorama = args.get("sky-panorama", "")
	_sky_cubemap = args.get("sky-cubemap", "")
	_proxy_re = RegEx.new()
	_proxy_re.compile(String(args.get("proxy-regex", PROXY_COLLISION_PATTERN)))
	_parse_maps(args.get("map", ""))
	var skip_pattern := String(args.get("skip-regex", ""))
	if skip_pattern != "":
		_skip_re = RegEx.new()
		if _skip_re.compile(skip_pattern) != OK:
			push_error("bad skip-regex: " + skip_pattern)
			_skip_re = null

	var json_path := JSON_DIR + json_name + ".json"
	var f := FileAccess.open(json_path, FileAccess.READ)
	if f == null:
		push_error("Cannot open " + json_path)
		return
	var data: Dictionary = JSON.parse_string(f.get_as_text())
	f.close()
	for md in data.get("materials", []):
		_mat_defs[md["name"]] = md

	var nodes: Array = data["nodes"]
	# Mark skipped subtrees (node + all descendants).
	var skip := {}
	if _skip_re != null:
		var children := {}
		for i in nodes.size():
			children.get_or_add(int(nodes[i]["parent"]), []).append(i)
		for i in nodes.size():
			if _skip_re.search(String(nodes[i]["name"])) != null:
				_mark_skip(i, children, nodes, skip)

	var created := {} # json node index -> Node3D (robust to skipped subtrees)
	var root: Node3D = null

	for i in nodes.size():
		if skip.has(i):
			continue
		var nd: Dictionary = nodes[i]
		var n3 := _make_node(nd)
		n3.name = String(nd["name"]).validate_node_name()
		if int(nd["parent"]) == -1:
			root = n3
		else:
			created[int(nd["parent"])].add_child(n3, true)
		n3.position = Vector3(nd["pos"][0], nd["pos"][1], nd["pos"][2])
		n3.quaternion = Quaternion(nd["rot"][0], nd["rot"][1], nd["rot"][2], nd["rot"][3])
		n3.scale = Vector3(nd["scale"][0], nd["scale"][1], nd["scale"][2])
		if nd.get("particles", false):
			n3.set_meta("fx", true)
			_fx_nodes.append(_node_label(nodes, i))
			_stats["fx_placeholders"] += 1
		if not nd.get("active", true) and not _force_active:
			_stats["inactive"] += 1
			n3.set_meta("unity_active", false)
			if n3 is VisualInstance3D or n3 is Light3D:
				n3.visible = false
			# Inactive subtrees (2F/3F/evening variants) must not collide either:
			# spawn/ground rays would otherwise land on hidden floors.
			_disable_collision_recursive(n3)
		created[i] = n3
		_stats["nodes"] += 1

	if root == null:
		push_error("No root node in JSON")
		return

	_apply_light_budget()
	_add_sky(root)

	# Pack and save.
	_set_owner_recursive(root, root)
	var out_path := OUT_DIR + out_name + ".tscn"
	var packed := PackedScene.new()
	var err := packed.pack(root)
	if err != OK:
		push_error("pack failed: %s" % err)
		return
	err = ResourceSaver.save(packed, out_path)
	if err != OK:
		push_error("save failed: %s" % err)
		return

	# Import report for reconciliation.
	var report := {
		"source_json": json_name,
		"output": out_path,
		"json_nodes": nodes.size(),
		"json_mesh_nodes": nodes.filter(func(n): return n.get("meshName", "") != "").size(),
		"json_lights": nodes.filter(func(n): return n.get("light") != null and String(n["light"].get("type", "")) != "").size(),
		"stats": _stats_filtered(),
		"fx_placeholders": _fx_nodes,
		"sky": _sky_panorama if _sky_panorama != "" else _sky_cubemap,
	}
	var rf := FileAccess.open(JSON_DIR + json_name + "_import_report.json", FileAccess.WRITE)
	rf.store_string(JSON.stringify(report, "  "))
	rf.close()

	print("[import] === %s -> %s ===" % [json_name, out_path])
	print("[import] nodes=%d (inactive %d, skipped %d) meshes=%d lights=%d (deactivated %d) collision=%d proxy=%d materials=%d fbx_mats_kept=%d fx=%d geo_fixed=%d" % [
		_stats["nodes"], _stats["inactive"], _stats["skipped_nodes"], _stats["mesh_instances"],
		_stats["lights"], _stats["lights_deactivated"],
		_stats["collision_bodies"], _stats["proxy_collision_bodies"],
		_stats["materials_built"], _stats["fbx_materials_kept"], _stats["fx_placeholders"],
		_stats["geo_fixed"]])
	for k in ["missing_meshes", "missing_textures", "missing_fbx"]:
		if not _stats[k].is_empty():
			print("[import] %s (%d): %s" % [k, _stats[k].size(), str(_stats[k].slice(0, 10))])


func _stats_filtered() -> Dictionary:
	var d := _stats.duplicate()
	return d


func _parse_maps(map_arg: String) -> void:
	_maps.clear()
	if map_arg == "":
		_maps.append(["Assets/AssetTools/SceneRes/Assets_School_Hallway/", "res://assets/models/school_hallway/"])
		return
	for pair in map_arg.split(",", false):
		var kv := pair.split("=", true, 1)
		if kv.size() == 2:
			var src := kv[0]
			if not src.ends_with("/"):
				src += "/"
			var dst := kv[1]
			if not dst.ends_with("/"):
				dst += "/"
			_maps.append([src, dst])


func _mark_skip(i: int, children: Dictionary, nodes: Array, skip: Dictionary) -> void:
	if skip.has(i):
		return
	skip[i] = true
	_stats["skipped_nodes"] += 1
	if String(nodes[i].get("meshName", "")) != "":
		_stats["skipped_meshes"] += 1
	var l = nodes[i].get("light")
	if l != null and String(l.get("type", "")) != "":
		_stats["skipped_lights"] += 1
	for c in children.get(i, []):
		_mark_skip(c, children, nodes, skip)


func _node_label(nodes: Array, i: int) -> String:
	# name with parent chain for FX report readability
	var parts := [String(nodes[i]["name"])]
	var p := int(nodes[i]["parent"])
	var depth := 0
	while p >= 0 and depth < 4:
		parts.push_front(String(nodes[p]["name"]))
		p = int(nodes[p]["parent"])
		depth += 1
	return "/".join(parts)


func _apply_light_budget() -> void:
	if _max_active_lights <= 0 or _lights_made.size() <= _max_active_lights:
		return
	_lights_made.sort_custom(func(a, b): return a.light_energy > b.light_energy)
	for i in range(_max_active_lights, _lights_made.size()):
		_lights_made[i].visible = false
		_lights_made[i].set_meta("deactivated_by_budget", true)
		_stats["lights_deactivated"] += 1


func _add_sky(root: Node3D) -> void:
	if _sky_panorama == "" and _sky_cubemap == "":
		return
	var we := WorldEnvironment.new()
	we.name = "WorldEnvironment"
	var env := Environment.new()
	env.background_mode = Environment.BG_SKY
	var sky := Sky.new()
	if _sky_panorama != "" and ResourceLoader.exists(_sky_panorama):
		var mat: PanoramaSkyMaterial = PanoramaSkyMaterial.new()
		mat.panorama = load(_sky_panorama)
		sky.sky_material = mat
	else:
		push_warning("[import] sky texture not found, sky skipped")
	env.sky = sky
	env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	env.ambient_light_energy = 0.6
	we.environment = env
	root.add_child(we, true)
	we.owner = root
	print("[import] sky added: ", _sky_panorama if _sky_panorama != "" else _sky_cubemap)


func _parse_args() -> Dictionary:
	var out := {}
	var args := OS.get_cmdline_user_args()
	var i := 0
	while i < args.size():
		if args[i].begins_with("--") and i + 1 < args.size():
			out[args[i].trim_prefix("--")] = args[i + 1]
			i += 2
		else:
			i += 1
	return out


func _unity_to_res(unity_path: String) -> String:
	if unity_path == "":
		return ""
	for m in _maps:
		if unity_path.begins_with(m[0]):
			return m[1] + unity_path.substr(m[0].length())
	return ""


# "…/SHW_Exitlight_01.png" -> "…/SHW_Exitlight_emi_01.png" if that file exists.
func _emi_variant(tex_res: String) -> String:
	var base := tex_res.get_basename() # strips .png
	var dir := tex_res.get_base_dir()
	var file := base.get_file()
	var dot := file.rfind("_")
	if dot == -1:
		return ""
	var cand := "%s/%s_emi%s.png" % [dir, file.substr(0, dot), file.substr(dot)]
	if ResourceLoader.exists(cand):
		return cand
	return ""


func _make_node(nd: Dictionary) -> Node3D:
	var light = nd.get("light")
	if light != null and String(light.get("type", "")) != "":
		return _make_light(light)
	var mesh_name: String = nd.get("meshName", "")
	var n3: Node3D
	var has_visual_mesh := false
	var proxy_aabb := AABB()
	if mesh_name == "":
		n3 = Node3D.new()
	else:
		var fbx_unity: String = nd.get("fbx", "")
		# Dedicated collision meshes: trimesh StaticBody3D, no visual.
		if fbx_unity.contains("/Collision/"):
			n3 = _make_collision_node(nd, mesh_name, fbx_unity)
		else:
			var mesh := _get_mesh(fbx_unity, mesh_name)
			if mesh == null:
				_stats["missing_meshes"].append("%s::%s" % [fbx_unity.get_file(), mesh_name])
				n3 = MeshInstance3D.new()
			else:
				# ufbx drops geometry/unit transforms for shared meshes; Unity's
				# exported mesh bounds are authoritative — wrap a corrective node
				# when the imported mesh's AABB disagrees.
				var fix: Variant = _geo_fix(mesh.get_aabb(), nd.get("meshSize"), nd.get("meshCenter"))
				var mi := MeshInstance3D.new()
				mi.mesh = mesh
				_stats["mesh_instances"] += 1
				_apply_materials(mi, mesh, nd.get("materials", []))
				has_visual_mesh = true
				if fix == null:
					n3 = mi
					proxy_aabb = mesh.get_aabb()
				else:
					n3 = Node3D.new()
					mi.name = "GeoFix"
					mi.transform = fix
					n3.add_child(mi, true)
					_stats["geo_fixed"] += 1
					var us: Array = nd["meshSize"]
					var uc: Array = nd["meshCenter"]
					proxy_aabb = AABB(Vector3(uc[0], uc[1], uc[2]) - Vector3(us[0], us[1], us[2]) * 0.5,
						Vector3(us[0], us[1], us[2]))
	# Colliders exported from Unity components.
	var has_collider := false
	var c_mesh := String(nd.get("colliderMesh", ""))
	if c_mesh != "":
		var cm := _get_mesh(String(nd.get("colliderFbx", "")), c_mesh)
		if cm != null:
			var cfix: Variant = _geo_fix(cm.get_aabb(), nd.get("colSize"), nd.get("colCenter"))
			_add_trimesh_body(n3, cm, cfix)
			has_collider = true
		else:
			_stats["missing_meshes"].append("%s::%s" % [String(nd.get("colliderFbx", "")).get_file(), c_mesh])
	elif nd.get("boxSize") is Array and nd["boxSize"].size() == 3:
		var bs: Array = nd["boxSize"]
		var bc: Array = nd["boxCenter"]
		_add_box_body(n3, Vector3(bs[0], bs[1], bs[2]), Vector3(bc[0], bc[1], bc[2]))
		has_collider = true
	# Proxy collision for structural pieces without any collider of their own.
	if not has_collider and _with_proxy_collision and has_visual_mesh \
			and _proxy_re.search(mesh_name) != null:
		_add_proxy_collision(n3, proxy_aabb)
	return n3


func _add_trimesh_body(parent: Node3D, mesh: Mesh, fix: Variant = null) -> void:
	var body := StaticBody3D.new()
	body.name = "Collision"
	body.collision_layer = 1
	body.collision_mask = 0
	var cs := CollisionShape3D.new()
	cs.shape = mesh.create_trimesh_shape()
	if fix is Transform3D:
		cs.transform = fix
	body.add_child(cs, true)
	parent.add_child(body, true)
	_stats["collision_bodies"] += 1


# Returns null when the imported mesh AABB matches Unity's exported bounds
# (within 1.5x per axis); otherwise a corrective transform that maps the
# imported mesh's AABB onto Unity's (size + center, mirror-X already applied
# at export time).
func _geo_fix(g: AABB, size_arr: Variant, center_arr: Variant) -> Variant:
	if not (size_arr is Array) or not (center_arr is Array):
		return null
	if size_arr.size() != 3 or center_arr.size() != 3:
		return null
	var us := Vector3(size_arr[0], size_arr[1], size_arr[2])
	var uc := Vector3(center_arr[0], center_arr[1], center_arr[2])
	if us.x <= 0.0 or us.y <= 0.0 or us.z <= 0.0:
		return null
	var gs := g.size
	if gs.x <= 0.0 or gs.y <= 0.0 or gs.z <= 0.0:
		return null
	var ratio := Vector3(us.x / gs.x, us.y / gs.y, us.z / gs.z)
	var worst: float = maxf(ratio.x, maxf(ratio.y, ratio.z))
	var best: float = minf(ratio.x, minf(ratio.y, ratio.z))
	if worst <= 1.5 and best >= 1.0 / 1.5:
		return null
	var t := Transform3D.IDENTITY
	t = t.scaled(ratio)
	t.origin = uc - ratio * g.get_center()
	return t


func _add_box_body(parent: Node3D, size: Vector3, center: Vector3) -> void:
	var body := StaticBody3D.new()
	body.name = "Collision"
	body.collision_layer = 1
	body.collision_mask = 0
	var cs := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = size
	cs.shape = box
	cs.position = center
	body.add_child(cs, true)
	parent.add_child(body, true)
	_stats["collision_bodies"] += 1


func _make_light(ld: Dictionary) -> Node3D:
	var col: Array = ld.get("color", [1, 1, 1, 1])
	var c := Color(col[0], col[1], col[2])
	var shadows_on: bool = ld.get("shadows", "None") != "None"
	var l: Light3D
	match String(ld.get("type", "Point")):
		"Point":
			var o := OmniLight3D.new()
			o.omni_range = float(ld.get("range", 10.0))
			l = o
		"Spot":
			var s := SpotLight3D.new()
			s.spot_range = float(ld.get("range", 10.0))
			# Unity spotAngle is the full cone angle; Godot spot_angle is the half-angle.
			s.spot_angle = clampf(float(ld.get("spotAngle", 30.0)) * 0.5, 1.0, 89.0)
			l = s
		"Directional":
			l = DirectionalLight3D.new()
		_:
			return Node3D.new() # Area lights are bake-only in Unity; skip.
	l.light_color = c
	l.light_energy = float(ld.get("intensity", 1.0))
	l.shadow_enabled = shadows_on
	_stats["lights"] += 1
	_lights_made.append(l)
	return l


func _make_collision_node(nd: Dictionary, mesh_name: String, fbx_unity: String) -> Node3D:
	var body := StaticBody3D.new()
	body.collision_layer = 1
	body.collision_mask = 0
	var mesh := _get_mesh(fbx_unity, mesh_name)
	if mesh != null:
		var cs := CollisionShape3D.new()
		cs.shape = mesh.create_trimesh_shape()
		body.add_child(cs, true)
		cs.owner = null # ownership fixed later
		_stats["collision_bodies"] += 1
	else:
		_stats["missing_meshes"].append("%s::%s" % [fbx_unity.get_file(), mesh_name])
	return body


func _add_proxy_collision(parent: Node3D, aabb: AABB) -> void:
	if aabb.size.length() < 0.01:
		return
	var body := StaticBody3D.new()
	body.name = "ProxyCollision"
	body.collision_layer = 1
	body.collision_mask = 0
	var cs := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = aabb.size
	cs.shape = box
	cs.position = aabb.get_center()
	body.add_child(cs, true)
	parent.add_child(body, true)
	_stats["proxy_collision_bodies"] += 1


func _get_mesh(fbx_unity: String, mesh_name: String) -> Mesh:
	# Unity built-in meshes (no asset file): substitute Godot primitives.
	if fbx_unity.contains("unity default resources"):
		if mesh_name == "Plane":
			var pm := PlaneMesh.new()
			pm.size = Vector2(10, 10)
			return pm
		return null
	var res_path := _unity_to_res(fbx_unity)
	if res_path == "":
		if fbx_unity != "" and not _stats["missing_fbx"].has(fbx_unity):
			_stats["missing_fbx"].append(fbx_unity)
		return null
	if not _mesh_cache.has(res_path):
		var d := {}
		if ResourceLoader.exists(res_path):
			var res: Resource = load(res_path)
			if res is PackedScene:
				var inst: Node = res.instantiate()
				_collect_meshes(inst, d)
				inst.free()
			elif res is Mesh:
				# .obj etc. import as a bare mesh resource
				d[mesh_name] = res
		else:
			_stats["missing_fbx"].append(res_path)
		_mesh_cache[res_path] = d
	var d: Dictionary = _mesh_cache[res_path]
	if d.has(mesh_name):
		return d[mesh_name]
	if d.size() == 1:
		return d.values()[0]
	return null


func _collect_meshes(n: Node, out: Dictionary, owner_name := "") -> void:
	# Key meshes by their owning FBX node name (helper nodes from
	# fbx/allow_geometry_helper_nodes are transparent to naming).
	var cur_owner: String = owner_name
	if n is Node3D and not _is_helper_name(n.name):
		cur_owner = n.name
	if n is MeshInstance3D and n.mesh != null:
		var key: String = cur_owner if cur_owner != "" else n.name
		if not out.has(key):
			out[key] = n.mesh
		if n.mesh.resource_name != "" and not out.has(n.mesh.resource_name):
			out[n.mesh.resource_name] = n.mesh
	for c in n.get_children():
		_collect_meshes(c, out, cur_owner)


func _is_helper_name(nm: String) -> bool:
	return nm.begins_with("GeometryTransformHelper") or nm.begins_with("ScaleHelper")


func _apply_materials(mi: MeshInstance3D, mesh: Mesh, mat_names: Array) -> void:
	for s in mini(mesh.get_surface_count(), mat_names.size()):
		var mat_name := String(mat_names[s])
		if mat_name == "":
			continue
		# Glass panes: ufbx imports them opaque black; force a transparent
		# material (Unity uses SHW_Glass_* transparent materials).
		if "glass" in mat_name.to_lower():
			var gm := _build_glass_material(mat_name, mi.get_active_material(s))
			mi.set_surface_override_material(s, gm)
			continue
		# Prefer the material the FBX importer produced, if it carries a texture.
		var existing := mi.get_active_material(s)
		if existing is StandardMaterial3D and existing.albedo_texture != null:
			_stats["fbx_materials_kept"] += 1
			continue
		var m := _build_material(mat_name)
		if m != null:
			mi.set_surface_override_material(s, m)


func _build_glass_material(mat_name: String, base: Material) -> StandardMaterial3D:
	var key := "__glass__" + mat_name
	if _mat_cache.has(key):
		return _mat_cache[key]
	var m := StandardMaterial3D.new()
	m.resource_name = mat_name + "_transparent"
	m.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	m.albedo_color = Color(0.85, 0.9, 0.95, 0.25)
	if base is StandardMaterial3D and base.albedo_texture != null:
		m.albedo_texture = base.albedo_texture
		m.albedo_color.a = 0.35
	m.roughness = 0.05
	_mat_cache[key] = m
	_stats["materials_built"] += 1
	return m


func _build_material(mat_name: String) -> StandardMaterial3D:
	if _mat_cache.has(mat_name):
		return _mat_cache[mat_name]
	var def: Dictionary = _mat_defs.get(mat_name, {})
	if def.is_empty():
		return null
	var m := StandardMaterial3D.new()
	m.resource_name = mat_name
	var shader := String(def.get("shader", "")).to_lower()
	var mode := String(def.get("renderMode", ""))
	var col: Array = def.get("color", [1, 1, 1, 1])
	m.albedo_color = Color(col[0], col[1], col[2], col[3])

	var tex_res := _unity_to_res(String(def.get("mainTex", "")))
	if tex_res != "":
		if ResourceLoader.exists(tex_res):
			m.albedo_texture = load(tex_res)
		elif not _stats["missing_textures"].has(tex_res):
			_stats["missing_textures"].append(tex_res)
	var st: Array = def.get("mainTexST", [])
	if st.size() == 4 and not (st[0] == 1.0 and st[1] == 1.0 and st[2] == 0.0 and st[3] == 0.0):
		m.uv1_scale = Vector3(st[0], st[1], 1.0)
		m.uv1_offset = Vector3(st[2], st[3], 0.0)

	if def.get("emissionEnabled", false):
		m.emission_enabled = true
		var ec: Array = def.get("emissionColor", [0, 0, 0, 1])
		var emax: float = maxf(ec[0], maxf(ec[1], ec[2]))
		m.emission = Color(ec[0], ec[1], ec[2]) / emax if emax > 1.0 else Color(ec[0], ec[1], ec[2])
		m.emission_energy_multiplier = maxf(emax, 1.0)
		var etex := _unity_to_res(String(def.get("emissionTex", "")))
		if etex != "" and ResourceLoader.exists(etex):
			m.emission_texture = load(etex)
		elif m.albedo_texture != null:
			m.emission_texture = m.albedo_texture
	elif tex_res != "":
		# The pack ships companion "_emi" maps (e.g. SHW_Exitlight_01.png ->
		# SHW_Exitlight_emi_01.png); Unity's day scene has emission disabled on
		# the materials, so wire them up by naming convention.
		var emi := _emi_variant(tex_res)
		if emi != "":
			m.emission_enabled = true
			m.emission = Color(1, 1, 1)
			m.emission_texture = load(emi)

	# Unity day scene uses Mobile/Unlit everywhere (lighting came from baked
	# lightmaps); unshaded reproduces that look until lightmaps are baked.
	if shader.contains("unlit"):
		m.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED

	if shader.contains("additive") or shader.contains("particle"):
		m.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
		m.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		m.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		m.cull_mode = BaseMaterial3D.CULL_DISABLED
		if not m.emission_enabled and m.albedo_texture != null:
			m.emission_enabled = true
			m.emission_texture = m.albedo_texture
			m.emission = Color(1, 1, 1)
	elif mode in ["Fade", "Transparent"] or shader.contains("transparent") or int(def.get("renderQueue", 0)) >= 3000:
		m.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA

	_mat_cache[mat_name] = m
	_stats["materials_built"] += 1
	return m


func _set_owner_recursive(n: Node, root: Node) -> void:
	for c in n.get_children():
		c.owner = root
		_set_owner_recursive(c, root)


func _disable_collision_recursive(n: Node) -> void:
	if n is CollisionObject3D:
		n.collision_layer = 0
	for c in n.get_children():
		_disable_collision_recursive(c)
