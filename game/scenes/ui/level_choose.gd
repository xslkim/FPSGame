extends Node3D
## LevelChoose(8.1):13 关按钮两页翻页,星/锁/分数/排名,扣币选关,难度面板。
## 解锁:L1 无前置;其余要求前一关 star>0(默认本地全 3 星全解锁,8.3)。

const MENU := "res://scenes/ui/menu.tscn"
const LOADING := "res://scenes/ui/loading.tscn"
const PER_PAGE := 7
const LEVEL_COUNT := 13
const PAGE_OFFSET := 3.2
## 已建关卡映射;未建 → MessageBox "敬请期待"
const SCENE_MAP := {
	0: "res://scenes/levels/level1_story.tscn",
	1: "res://scenes/levels/level2.tscn",
	2: "res://scenes/levels/level3.tscn",
	3: "res://scenes/levels/level4.tscn",
}
const DIFF_NAMES := ["Easy", "Hard", "Hell"]

@export var button_root_path: NodePath = ^"ButtonRoot"

var _page := 0
var _pages_root: Node3D
var _level_buttons: Array = []
var _diff_panel: Node3D
var _coin_label: Label3D
var _page_tween: Tween
var _last_press_time := -99.0
var _pending_level := -1

func _ready() -> void:
	GlobalObject.scene_state = GlobalObject.GameState.UI
	MessageBox.close_current()
	_build()
	refresh()

func _build() -> void:
	var root := get_node(button_root_path)
	_add_label(root, "选择关卡", 88, Vector3(0, 0.95, 0))
	_coin_label = _add_label(root, "", 44, Vector3(0.9, 0.95, 0))
	_pages_root = Node3D.new()
	_pages_root.name = "PagesRoot"
	root.add_child(_pages_root)
	for page in 2:
		var page_node := Node3D.new()
		page_node.name = "Page%d" % page
		page_node.position.x = PAGE_OFFSET * page
		_pages_root.add_child(page_node)
		for i in range(page * PER_PAGE, mini((page + 1) * PER_PAGE, LEVEL_COUNT)):
			var idx: int = i - page * PER_PAGE
			var b := UIButton3D.create("", Vector2(1.0, 0.26), Callable())
			b.name = "BtnLevel%d" % (i + 1)
			b.shortcut = KEY_1 + ((i + 1) % 10)  # 键盘备选:数字键
			b.position = Vector3(0.0, 0.6 - idx * 0.36, 0)
			var level_i := i
			b.on_pressed = func(): select_level(level_i)
			page_node.add_child(b)
			_level_buttons.append(b)
	var prev := UIButton3D.create("< 上页", Vector2(0.55, 0.2), func(): turn_page(-1))
	prev.name = "BtnPrevPage"
	prev.position = Vector3(-0.7, -0.62, 0)
	root.add_child(prev)
	var next := UIButton3D.create("下页 >", Vector2(0.55, 0.2), func(): turn_page(1))
	next.name = "BtnNextPage"
	next.position = Vector3(0.7, -0.62, 0)
	root.add_child(next)
	var back := UIButton3D.create("返回", Vector2(0.55, 0.2), back_to_menu)
	back.name = "BtnBack"
	back.position = Vector3(0, -0.92, 0)
	root.add_child(back)
	_build_diff_panel(root)

func _build_diff_panel(root: Node) -> void:
	_diff_panel = Node3D.new()
	_diff_panel.name = "DifficultyPanel"
	root.add_child(_diff_panel)
	_add_label(_diff_panel, "选择难度", 72, Vector3(0, 0.5, 0.05))
	for d in 3:
		var b := UIButton3D.create(DIFF_NAMES[d], Vector2(0.7, 0.24), Callable())
		b.name = "BtnDiff%d" % d
		b.position = Vector3(0, 0.16 - d * 0.32, 0.05)
		var dd := d
		b.on_pressed = func(): choose_difficulty(dd)
		_diff_panel.add_child(b)
	var cancel := UIButton3D.create("取消", Vector2(0.7, 0.24), func(): _show_diff_panel(false))
	cancel.name = "BtnDiffCancel"
	cancel.set_bg_color(Color(0.45, 0.2, 0.2, 0.95))
	cancel.position = Vector3(0, -0.85, 0.05)
	_diff_panel.add_child(cancel)
	_diff_panel.visible = false

func _add_label(parent: Node, text: String, font_size: int, pos: Vector3) -> Label3D:
	var l := Label3D.new()
	l.text = text
	l.font_size = font_size
	l.pixel_size = 0.0022
	l.position = pos
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	UITheme.apply_label3d(l)
	parent.add_child(l)
	return l

## 星/锁/分数/排名刷新(数据源 DataMgr.level_state)
func refresh() -> void:
	for i in _level_buttons.size():
		var st: Dictionary = DataMgr.level_state[i]
		var star := int(st.get("star", 0))
		var text := "Level %d\n" % (i + 1)
		if _is_unlocked(i):
			var stars := ""
			for s in 3:
				stars += "*" if s < star else "-"
			text += "%s  S:%d R:%d" % [stars, int(st.get("score", 0)), int(st.get("rank", 0))]
			_level_buttons[i].set_bg_color(Color(0.16, 0.35, 0.6, 0.95))
		else:
			text += "LOCK"
			_level_buttons[i].set_bg_color(Color(0.25, 0.25, 0.28, 0.9))
		_level_buttons[i].set_text(text)

func _is_unlocked(i: int) -> bool:
	if i == 0:
		return true
	return int(DataMgr.level_state[i - 1].get("star", 0)) > 0

## 选关:未解锁弹框 → 查币(不足弹框)→ 扣 1 币 → 0.5s 后难度面板;1 秒防抖
func select_level(i: int) -> void:
	var now := Time.get_ticks_msec() / 1000.0
	if now - _last_press_time < 1.0:
		return
	_last_press_time = now
	if not _is_unlocked(i):
		MessageBox.show_box(self, "未解锁", "需要先通关 Level %d。" % i, "确定", "返回")
		return
	if DataMgr.coin < 1:
		MessageBox.show_box(self, "金币不足",
			"游戏币不足,每 3 分钟 +1(上限 10),请稍后再来。", "确定", "返回")
		return
	DataMgr.spend_coin(1)
	_pending_level = i
	await get_tree().create_timer(0.5).timeout
	if _pending_level == i:
		_show_diff_panel(true)

func choose_difficulty(d: int) -> void:
	match d:
		0:
			GlobalObject.difficulty = GlobalObject.Difficulty.Easy
		1:
			GlobalObject.difficulty = GlobalObject.Difficulty.Hard
		_:
			GlobalObject.difficulty = GlobalObject.Difficulty.Hell
	var scene: String = SCENE_MAP.get(_pending_level, "")
	_show_diff_panel(false)
	if scene.is_empty():
		MessageBox.show_box(self, "敬请期待", "Level %d 正在建设中。" % (_pending_level + 1),
			"确定", "返回")
		return
	GlobalObject.next_scene_path = scene
	get_tree().change_scene_to_file(LOADING)

func turn_page(d: int) -> void:
	var new_page: int = clampi(_page + d, 0, 1)
	if new_page == _page:
		return
	_page = new_page
	if _page_tween != null and _page_tween.is_valid():
		_page_tween.kill()
	_page_tween = create_tween()
	_page_tween.tween_property(_pages_root, "position:x", -PAGE_OFFSET * _page, 0.35) \
		.set_trans(Tween.TRANS_SINE)

func back_to_menu() -> void:
	get_tree().change_scene_to_file(MENU)

func _show_diff_panel(v: bool) -> void:
	_diff_panel.visible = v
	_pages_root.visible = not v

func _process(_delta: float) -> void:
	if _coin_label != null:
		_coin_label.text = "金币: %d/%d" % [DataMgr.coin, DataMgr.max_coin]

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo \
			and event.keycode == KEY_ESCAPE:
		if _diff_panel.visible:
			_show_diff_panel(false)
		else:
			back_to_menu()
