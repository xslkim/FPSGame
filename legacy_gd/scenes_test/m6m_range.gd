extends Node3D
## M6 怪物批次(L2-L4)试验场 + headless 断言自检。
## 启动参数:
##   (无)              占位(键盘观察用,刷怪逻辑在关卡侧)
##   --m6m-selftest    headless 逐怪断言自检
## 用法: godot --headless --path game scenes/test/m6m_range.tscn -- --m6m-selftest

const MONSTER_SCENES := {
	"toon_shoot": preload("res://gameplay/monsters/toon.tscn"),
	"toon_shoot_alien": preload("res://gameplay/monsters/toon_alien.tscn"),
	"wolf": preload("res://gameplay/monsters/wolf.tscn"),
	"wolf_blue": preload("res://gameplay/monsters/wolf_blue.tscn"),
	"fat_zombie": preload("res://gameplay/monsters/fat_zombie.tscn"),
	"dragon_red": preload("res://gameplay/monsters/dragon_red.tscn"),
	"dragon_blue": preload("res://gameplay/monsters/dragon_blue.tscn"),
	"dragon_green": preload("res://gameplay/monsters/dragon_green.tscn"),
	"magma_demon": preload("res://gameplay/monsters/magma_demon.tscn"),
	"rock_warrior": preload("res://gameplay/monsters/rock_warrior.tscn"),
	"level2_boss": preload("res://gameplay/monsters/level2_boss.tscn"),
}

@onready var _pool: MonsterPool = $MonsterPool

var _test_failed := false

func _ready() -> void:
	GlobalObject.scene_state = GlobalObject.GameState.Battle
	InputManager.set_input_mode(InputManager.InputMode.RightAndLeft)
	for key in MONSTER_SCENES:
		_pool.register_type(key, MONSTER_SCENES[key], 2)
	if OS.get_cmdline_user_args().has("--m6m-selftest"):
		_self_test()

func _check(cond: bool, label: String) -> void:
	print(("[M6M-TEST] PASS: " if cond else "[M6M-TEST] FAIL: ") + label)
	if not cond:
		_test_failed = true

## 轮询等待 cond 成立(每物理帧),超时返回 false
func _wait_until(cond: Callable, timeout: float) -> bool:
	var t0 := Time.get_ticks_msec() / 1000.0
	while not cond.call():
		if Time.get_ticks_msec() / 1000.0 - t0 > timeout:
			return false
		await get_tree().physics_frame
	return true

func _self_test() -> void:
	await get_tree().process_frame
	await get_tree().physics_frame
	var rp := PlayerSystem.player_right
	var lp := PlayerSystem.player_left
	var cam := get_viewport().get_camera_3d()

	# ---- fat_zombie:出生固定 get_born_position(30,12)(meta born_override)----
	var fz := _pool.get_monster("fat_zombie")
	_check(fz.get_born_params(33.0, 8.0) == Vector2(30.0, 12.0),
		"fat_zombie: born_override = (30, 12)")

	# ---- FireWindow:占用/pick_free ----
	var w1 := FireWindow.new()
	add_child(w1)
	w1.global_position = Vector3(0, 0, -4)
	var w2 := FireWindow.new()
	add_child(w2)
	w2.global_position = Vector3(3, 0, -4)

	# ---- magma_demon:出生偏移(相机前 10m + 四向 2+6=8)+ 恒速 8 ----
	var mg := _pool.get_monster("magma_demon") as MagmaDemonMonster
	mg.born(Vector3(99, 99, 99), 3, 99.0)  # 传入点被忽略,_on_born 重算
	var base_p := cam.global_position + (-cam.global_basis.z) * 10.0
	var off: Vector3 = mg.global_position - base_p
	_check(absf(off.length() - 8.0) < 0.01, "magma 出生偏移 = 8 (Right/Up 2 + OutOffset 6)")
	_check((absf(absf(off.x) - 8.0) < 0.01) != (absf(absf(off.y) - 8.0) < 0.01)
		and absf(off.z) < 0.01, "magma 出生方向 ∈ {右,左,上,下} 之一")
	_check(mg.get_move_speed() == 8.0, "magma get_move_speed 恒 8 (level=3 不加成)")
	mg._recycle()

	# ---- toon:走窗 + 动画倍速 + 定时命中 + 释放窗口 ----
	var toon := _pool.get_monster("toon_shoot") as ToonMonster
	toon.fire_window = w1
	w1.fire_monster = toon
	toon.born(w1.global_position + Vector3(3, 0, 0), 2, 0.0)
	_check(is_equal_approx(toon._anim.speed_scale, 2.0), "toon 动画速度 = 1 + 0.5*level(2)")
	_check(toon.get_node("Appearance").get_child_count() == 6, "toon 换装:6 组部件")
	_check(FireWindow.pick_free([w1, w2]) == w2, "fire_window: pick_free 跳过占用窗")
	var arrived := await _wait_until(func():
		return toon.global_position.distance_to(w1.global_position) <= 0.15, 5.0)
	_check(arrived, "toon 走向分配窗口(速度 3,到位 ≤0.1)")
	var hp_r0 := rp.hp
	var hp_l0 := lp.hp
	var toon_hit := await _wait_until(func():
		return rp.hp < hp_r0 or lp.hp < hp_l0, 4.0)
	_check(toon_hit and (hp_r0 - rp.hp) + (hp_l0 - lp.hp) == 15.0,
		"toon_shoot: 0.5s 定时命中单体 15")
	toon.hit(999.0, toon.global_position, GlobalObject.HitType.Body, PlayerSystem.Side.Right)
	_check(w1.fire_monster == null and toon.fire_window == null, "toon 死亡释放窗口占用")
	await get_tree().create_timer(1.7).timeout
	_check(not toon.is_active(), "toon 1.5s 后回收")

	# ---- wolf:蛇形路径点数量 + 降速保真 + 超时跳点 ----
	var wolf := _pool.get_monster("wolf") as WolfMonster
	wolf.born(Vector3(0, 0, -14), 0, 0.0)  # 相机 (0,1.6,6),平距 20 → (20-4)/6 → 2 段
	_check(wolf._path.size() == 2, "wolf 路径点 = int((20-4)/6) = 2 (got %d)" % wolf._path.size())
	_check(wolf._speed_for_dist(0.5) == 1.0 and wolf._speed_for_dist(1.5) == 2.0
		and wolf._speed_for_dist(2.5) == 3.0 and wolf._speed_for_dist(3.5) == 4.0
		and wolf._speed_for_dist(5.0) == 5.0, "wolf 多段降速 <1/2/3/4m → 1/2/3/4 保真")
	await get_tree().physics_frame
	await get_tree().physics_frame
	var idx0: int = wolf._path_idx
	wolf._point_time = 99.0  # 模拟单点超时 3s
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(wolf._path_idx == idx0 + 1, "wolf 单点超时 3s 跳下一点")
	wolf._recycle()

	# ---- wolf_blue:标准近战动画集 ----
	var wb := _pool.get_monster("wolf_blue")
	wb.born(Vector3(5, 0, -14), 0, 99.0)
	_check(wb.attack_anims == [&"BiteAttack", &"ClawAttack"], "wolf_blue attack_anims Bite/Claw")
	_check(wb.get_max_hp() == 20.0 and wb.get_move_speed() == 8.0, "wolf_blue meta 数值 hp20/speed8")
	wb._recycle()

	# ---- dragon:四段巡回循环 + 吐息 + TakeDamage 无敌 + 坠落回收 ----
	var dr := _pool.get_monster("dragon_red") as DragonMonster
	dr.born(Vector3(0, 5, -10), 0, 0.0)
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(dr._segment == DragonMonster.FlySeg.FarWay, "dragon 起始段 FarWay")
	var cycle_ok := true
	for s in [DragonMonster.FlySeg.InCamera, DragonMonster.FlySeg.Attack,
			DragonMonster.FlySeg.CamOffset, DragonMonster.FlySeg.FarWay]:
		dr.global_position = dr._target
		await get_tree().physics_frame
		await get_tree().physics_frame
		if dr._segment != s:
			cycle_ok = false
	_check(cycle_ok, "dragon 四段循环 FarWay→InCamera→Attack→CamOffset→重随机")
	# 进到 Attack 段并置于相机 75m 内 → 吐息
	dr.global_position = dr._target
	await get_tree().physics_frame
	await get_tree().physics_frame
	dr.global_position = dr._target
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(dr._segment == DragonMonster.FlySeg.Attack, "dragon 进入 Attack 段")
	dr.global_position = cam.global_position + Vector3(0, 0, -10)
	var breathing := await _wait_until(func():
		return dr.is_current_anim(&"FireBreathOnce"), 2.0)
	_check(breathing, "dragon Attack 段 <75m 播 FireBreathOnce")
	_check(dr._breath_fx != null and dr._breath_fx.emitting, "dragon 吐息特效(占位粒子)激活")
	# TakeDamage 无敌
	dr._anim.play(dr.damage_anim)
	var dr_hp := dr.hp
	_check(dr.hit(10.0, dr.global_position, GlobalObject.HitType.Body, PlayerSystem.Side.Right) == ""
		and dr.hp == dr_hp, "dragon TakeDamage 状态中无敌(hit 返回空)")
	# 死亡坠落 y<-3 回收
	dr._anim.play(dr.idle2_anim)
	dr.global_position.y = 1.0
	dr.hit(999.0, dr.global_position, GlobalObject.HitType.Body, PlayerSystem.Side.Right)
	var dr_recycled := await _wait_until(func(): return not dr.is_active(), 4.0)
	_check(dr_recycled, "dragon 死亡坠落 y<-3 回收")

	# ---- magma:飞入屏幕点 → idle → attack01/02;受击半速;坠落回收 ----
	var mg2 := _pool.get_monster("magma_demon") as MagmaDemonMonster
	mg2.born(Vector3(0, 0, 0), 0, 0.0)
	var in_combat := await _wait_until(func():
		return mg2._phase == MagmaDemonMonster.Phase.Combat, 4.0)
	_check(in_combat, "magma 飞到屏幕内随机点 → Combat(LookAt 相机进 idle)")
	var atk_played := await _wait_until(func():
		return mg2._is_playing_any(mg2.attack_anims), 3.0)
	_check(atk_played, "magma idle → CD → attack01/02")
	await get_tree().create_timer(1.0).timeout  # 等攻击 clip 播完(event_attack 已结算)
	mg2.hit(10.0, mg2.global_position, GlobalObject.HitType.Body, PlayerSystem.Side.Right)
	_check(mg2.is_current_anim(&"takedamage"), "magma 受击播 takedamage(半速)")
	mg2.hit(999.0, mg2.global_position, GlobalObject.HitType.Body, PlayerSystem.Side.Right)
	var mg_recycled := await _wait_until(func(): return not mg2.is_active(), 4.0)
	_check(mg_recycled, "magma 死亡坠落 y<-3 回收")

	# ---- rock_warrior:硬化皮肤 + atk01 全伤 + 大火球 ----
	var rw := _pool.get_monster("rock_warrior") as RockWarriorMonster
	rw.born(Vector3(0, 0, 60), 0, 99.0)  # 远离 attack_radius(36),不主动攻击
	_check(rw.hp == 300.0 and rw.is_boss, "rock_warrior born: hp=300, is_boss")
	rw.hit(10.0, rw.global_position, GlobalObject.HitType.Body, PlayerSystem.Side.Right)
	_check(rw.hp == 299.0, "rock 硬化皮肤:非 atk01 状态只掉 1 血")
	_check(not rw.is_current_anim(rw.damage_anim), "rock 硬化皮肤:不播受击动画")
	rw._anim.play(&"atk01")
	await get_tree().process_frame
	rw.hit(10.0, rw.global_position, GlobalObject.HitType.Body, PlayerSystem.Side.Right)
	_check(rw.hp == 289.0, "rock atk01 状态中全额掉血 10")
	var hp_r1 := rp.hp
	var hp_l1 := lp.hp
	rw.rock_attack()
	_check(rw.last_fireball != null and rw.last_fireball.scale == Vector3.ONE * 10.0,
		"rock_attack: 火球 localScale ×10")
	var rock_hit := await _wait_until(func():
		return rp.hp < hp_r1 and lp.hp < hp_l1, 3.0)
	_check(rock_hit, "rock 火球定时命中 Both (Poison)")
	rw._recycle()

	# ---- level2_boss:头部判定 / Armour Metal / 罚 CD / 无敌 / 技能 / 死亡不回收 ----
	var b := _pool.get_monster("level2_boss") as Level2BossMonster
	var shake := [0.0]
	b.entrance_shake.connect(func(d: float): shake[0] = d)
	b.born(Vector3(0, 0, 20), 0, 99.0)  # waitting 99:静止,逐项直接断言
	_check(is_equal_approx(shake[0], 0.8), "L2Boss 出场 0.8s 震屏信号")
	_check(b.hit(10.0, b.global_position, GlobalObject.HitType.Body, PlayerSystem.Side.Right) == "Metal"
		and b.hp == 100.0, "L2Boss Body 不掉血,返回 Metal")
	_check(b.hit(10.0, b.global_position, GlobalObject.HitType.Armour, PlayerSystem.Side.Right) == "Metal"
		and b.hp == 100.0, "L2Boss Armour 不掉血,返回 Metal(随机金属音)")
	var lat0: float = b.last_attack_time
	b.hit(10.0, b.global_position, GlobalObject.HitType.Head, PlayerSystem.Side.Right)
	_check(b.hp == 90.0, "L2Boss 只 HitType.Head 掉血 100→90")
	_check(is_equal_approx(b.last_attack_time, lat0 + 3.0), "L2Boss 受击罚 3 秒 CD")
	var b_hp := b.hp
	_check(b.hit(10.0, b.global_position, GlobalObject.HitType.Head, PlayerSystem.Side.Right) == ""
		and b.hp == b_hp, "L2Boss Damage02 状态中无敌")
	# Skill1:物理单体 15(两级 50% 选边)
	var hp_r2 := rp.hp
	var hp_l2 := lp.hp
	b.hert_player_skill1()
	_check((hp_r2 - rp.hp) + (hp_l2 - lp.hp) == 15.0, "L2Boss Skill1 物理单体 15")
	# Skill2:闪电特效 + 0.5s 后 Both 伤害×0.5(7.5)+ Ice
	hp_r2 = rp.hp
	hp_l2 = lp.hp
	b.hert_player_skill2()
	_check(b._lightning_fx != null and b._lightning_fx.visible, "L2Boss Skill2 闪电特效占位激活")
	await get_tree().create_timer(0.8).timeout
	_check(is_equal_approx(hp_r2 - rp.hp, 7.5) and is_equal_approx(hp_l2 - lp.hp, 7.5),
		"L2Boss Skill2 0.5s 后 Both 伤害×0.5 (7.5)")
	_check(rp.hurt_state == PlayerSystem.Player.HurtState.Frozen, "L2Boss Skill2 Ice → Frozen")
	b.set_difficulty_anim_rate(2.25)
	_check(is_equal_approx(b.anim_speed_rate, 2.25), "L2Boss 难度倍率由关卡设置(hard ×2.25)")
	# 死亡不调回收(关卡处理)
	b._anim.play(&"Idle")
	b.hit(9999.0, b.global_position, GlobalObject.HitType.Head, PlayerSystem.Side.Right)
	_check(b.is_dead() and b.is_active(), "L2Boss 死亡不调回收(died 信号由关卡处理)")

	print("[M6M-TEST] done, failed=%s" % _test_failed)
	get_tree().quit(1 if _test_failed else 0)
