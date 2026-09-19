extends Node
## DataMgr:数值配置(data/*.json)+ 用户存档(user://save.json)。
## 对应原作 DataMgr / UserData(8.3 节经济语义)。

const SAVE_PATH := "user://save.json"
const LEVEL_COUNT := 13

var gun_type_num := 7

var _fire_meta: Dictionary = {}
var _monster_meta: Dictionary = {}
var _level_meta: Dictionary = {}
var _tips: Array = []

# ---- 用户数据 ----
var coin := 10
var max_coin := 10
var add_coin_time := 180  # 秒,每 180s +1 币(UTC 计时,M5 实现再生)
var max_bullet := 120
var box_bullet := 60
var level_state: Array = []  # 13 × {star, score, rank}
var udid := ""
var last_add_coin_time := 0.0

func _ready() -> void:
	_fire_meta = _read_json("res://data/fire_meta.json", {})
	gun_type_num = int(_fire_meta.get("gun_type_num", 7))
	_monster_meta = _read_json("res://data/monster_meta.json", {})
	_level_meta = _read_json("res://data/level_meta.json", {})
	_tips = _read_json("res://data/tips.json", {}).get("tips", [])
	load_user_data()

func _process(_delta: float) -> void:
	_regen_coins()

# ---- 金币经济(8.3):全局回币,UTC 秒,180s +1 上限 10,落盘 ----

func _regen_coins() -> void:
	if coin >= max_coin:
		return
	var now := Time.get_unix_time_from_system()
	var changed := false
	while coin < max_coin and last_add_coin_time + add_coin_time <= now:
		coin += 1
		last_add_coin_time += add_coin_time
		changed = true
	if changed:
		save_user_data()
		PlayerSystem.notify_ui_changed()

## 扣币(选关 -1 / 续币 -1);不足返回 false。扣币后重新计回币时间
func spend_coin(n := 1) -> bool:
	if coin < n:
		return false
	coin -= n
	last_add_coin_time = Time.get_unix_time_from_system()
	save_user_data()
	PlayerSystem.notify_ui_changed()
	return true

## 距下一枚回币的秒数(面板倒计时 mm:ss 用);满币返回 0
func time_to_next_coin() -> float:
	if coin >= max_coin:
		return 0.0
	return maxf(0.0, last_add_coin_time + add_coin_time - Time.get_unix_time_from_system())

## 通关结算落盘(8.3 本地化):星级/分数取历史最高
func set_level_result(idx: int, star: int, score: int, rank: int) -> void:
	if idx < 0 or idx >= level_state.size():
		return
	var s: Dictionary = level_state[idx]
	s["star"] = maxi(int(s.get("star", 0)), star)
	s["score"] = maxi(int(s.get("score", 0)), score)
	s["rank"] = rank
	level_state[idx] = s
	save_user_data()

func _read_json(path: String, fallback: Variant) -> Variant:
	if not FileAccess.file_exists(path):
		push_error("DataMgr: missing json " + path)
		return fallback
	var data: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
	if data == null:
		push_error("DataMgr: bad json " + path)
		return fallback
	return data

## 枪数值:type 0=AK47 / 1=M4 / 2=HandGun,字段见 data/fire_meta.json
func get_gun_info(type: int) -> Dictionary:
	for g in _fire_meta.get("guns", []):
		if int(g.get("type", -1)) == type:
			return g
	push_warning("DataMgr: unknown gun type %d" % type)
	return {}

func get_monster_meta() -> Dictionary:
	return _monster_meta

func get_level_meta() -> Dictionary:
	return _level_meta

func get_tips() -> Array:
	return _tips

# ---- 存档 ----

func load_user_data() -> void:
	if FileAccess.file_exists(SAVE_PATH):
		var d: Variant = JSON.parse_string(FileAccess.get_file_as_string(SAVE_PATH))
		if d is Dictionary:
			coin = int(d.get("coin", coin))
			max_coin = int(d.get("max_coin", max_coin))
			add_coin_time = int(d.get("add_coin_time", add_coin_time))
			max_bullet = int(d.get("max_bullet", max_bullet))
			box_bullet = int(d.get("box_bullet", box_bullet))
			udid = str(d.get("udid", ""))
			last_add_coin_time = float(d.get("last_add_coin_time", 0.0))
			var ls: Array = d.get("level_state", [])
			for i in mini(ls.size(), LEVEL_COUNT):
				if ls[i] is Dictionary:
					level_state.append(ls[i])
	if udid.is_empty():
		udid = "%d_%d" % [int(Time.get_unix_time_from_system()), randi()]
	while level_state.size() < LEVEL_COUNT:
		level_state.append(_default_level_state())
	if last_add_coin_time <= 0.0:
		last_add_coin_time = Time.get_unix_time_from_system()
	save_user_data()

func _default_level_state() -> Dictionary:
	# 服务器离线化:本地默认 13 关全 3 星全解锁(M5 通关按 HP+用时 1-3 星本地结算后改写)
	return {"star": 3, "score": 0, "rank": 0}

func save_user_data() -> void:
	var d := {
		"coin": coin,
		"max_coin": max_coin,
		"add_coin_time": add_coin_time,
		"max_bullet": max_bullet,
		"box_bullet": box_bullet,
		"level_state": level_state,
		"udid": udid,
		"last_add_coin_time": last_add_coin_time,
	}
	var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	if f:
		f.store_string(JSON.stringify(d, "  "))
		f.close()
