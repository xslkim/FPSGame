# M7b: 特效贴图 + 音效资产复制/转换(TGA/PSD→PNG,其余直拷)
# 源:G:\test\FPSGame\Assets(只读) → G:\FPSGame\game\assets\effects\textures 与 assets\audio
import os, shutil, sys
from PIL import Image

SRC = r"G:\test\FPSGame\Assets"
TEX_OUT = r"G:\FPSGame\game\assets\effects\textures"
AUD_OUT = r"G:\FPSGame\game\assets\audio"

os.makedirs(TEX_OUT, exist_ok=True)
os.makedirs(os.path.join(AUD_OUT, "monsters"), exist_ok=True)
os.makedirs(os.path.join(AUD_OUT, "ui"), exist_ok=True)
os.makedirs(os.path.join(AUD_OUT, "effects"), exist_ok=True)

WARFX = os.path.join(SRC, "Effect", "JMO Assets", "WarFX", "Desktop", "Textures")
FPSP = os.path.join(SRC, "AssetTools", "FPS Pack", "Textures", "Effects")
KFX = os.path.join(SRC, "AssetTools", "KriptoFX", "Realistic Effects Pack v4", "Effects")
POLY = os.path.join(SRC, "Effect", "PolygonParticles", "Textures")
RPG = os.path.join(SRC, "AssetTools", "RPG VFX pack", "Textures")
CARTOON = os.path.join(SRC, "Effect", "Fantastic cartoon VFX", "Textures")
ZH = os.path.join(SRC, "Sound", "ZombieHorrorPackageFree", "MP3")
SND = os.path.join(SRC, "Sound")

def tex(src, dst_name):
    dst = os.path.join(TEX_OUT, dst_name)
    if src.lower().endswith(".png"):
        shutil.copyfile(src, dst)
    else:  # TGA / PSD → PNG
        img = Image.open(src)
        if img.mode not in ("RGBA", "RGB", "LA", "L"):
            img = img.convert("RGBA")
        img.save(dst)

def aud(src_rel, dst_sub, dst_name):
    src = os.path.join(SND, src_rel)
    dst_dir = os.path.join(AUD_OUT, dst_sub)
    os.makedirs(dst_dir, exist_ok=True)
    shutil.copyfile(src, os.path.join(dst_dir, dst_name))

errors = []
def T(src, name):
    try: tex(src, name)
    except Exception as e: errors.append(f"TEX {name}: {e}")
def A(src_rel, sub, name):
    try: aud(src_rel, sub, name)
    except Exception as e: errors.append(f"AUD {name}: {e}")

# ---- 命中/弹孔 ----
T(os.path.join(WARFX, "Bullet Holes", "WFX_T_BulletHoles Wood.tga"), "hole_wood.png")
T(os.path.join(WARFX, "Bullet Holes", "WFX_T_BulletHoles Metal.tga"), "hole_metal.png")
T(os.path.join(WARFX, "Bullet Holes", "WFX_T_BulletHoles Concrete.tga"), "hole_concrete.png")
T(os.path.join(WARFX, "Bullet Holes", "WFX_T_BulletHoles Generic.tga"), "hole_generic.png")
T(os.path.join(WARFX, "Misc", "WFX_T_Sparks Metal A8.png"), "sparks_metal.png")
T(os.path.join(WARFX, "Misc", "WFX_T_GlowCircle A8.png"), "glow_circle.png")
T(os.path.join(WARFX, "Flames", "WFX_T_FlamesSmall 4frm.tga"), "flame_4frm.png")
T(os.path.join(FPSP, "Impact", "WoodChipTexture.png"), "wood_chip.png")
T(os.path.join(FPSP, "Impact", "Stone.png"), "stone.png")
T(os.path.join(FPSP, "Impact", "Dust1.png"), "dust1.png")
T(os.path.join(FPSP, "Impact", "Dust2.png"), "dust2.png")
T(os.path.join(FPSP, "Impact", "Smoke.png"), "smoke.png")
T(os.path.join(FPSP, "Impact", "Particle.png"), "particle.png")
T(os.path.join(FPSP, "Impact", "Lazer.png"), "lazer.png")
T(os.path.join(FPSP, "Impact", "DirtBulletDecal2.png"), "dirt_decal.png")

# ---- 枪口火光(9 帧随机) ----
for i in range(1, 10):
    T(os.path.join(FPSP, "Muzzle", f"MuzzleFlash{i}.png"), f"muzzle{i}.png")
for i in range(1, 5):
    T(os.path.join(FPSP, "Muzzle", f"Flame{i}.png"), f"muzzle_flame{i}.png")

# ---- 血/火/烟/闪电/能量(KriptoFX) ----
KT = os.path.join(KFX, "Textures")
for f, n in [("BloodParticle.png", "blood_particle.png"), ("BloodDecal.png", "blood_decal.png"),
             ("Fire1.png", "fire1.png"), ("Fire2.png", "fire2.png"), ("Fire4.png", "fire4.png"),
             ("Explosion1.png", "explosion1.png"),
             ("Smoke3.png", "smoke3.png"), ("Smoke4.png", "smoke4.png"), ("Smoke3Light.png", "smoke3light.png"),
             ("Trail1.png", "trail1.png"), ("Trail2.png", "trail2.png"),
             ("Core1.png", "core1.png"), ("EnergyBall.png", "energy_ball.png"),
             ("Lightning1.png", "lightning1.png"), ("Lightning2.png", "lightning2.png"),
             ("TeleportParticles.png", "teleport1.png"), ("TeleportParticles2.png", "teleport2.png"),
             ("SnowParticles.png", "snow_particle.png")]:
    T(os.path.join(KT, f), n)
FB1 = os.path.join(SRC, "Effect", "Realistic Effects Pack", "Materials", "Projectiles", "Fireball1")
T(os.path.join(FB1, "EnergyBall3.png"), "fireball_core.png")
T(os.path.join(FB1, "TrailBall3.png"), "fireball_trail.png")
T(os.path.join(FB1, "Explosion.png"), "fireball_explosion.png")

# ---- PolygonParticles / RPG VFX / 卡通心形 ----
T(os.path.join(POLY, "PolygonParticles_Lightning_02.png"), "lightning_p.png")
T(os.path.join(POLY, "PolygonParticles_Sparkle.png"), "sparkle.png")
T(os.path.join(POLY, "PolygonParticles_Soft_Spot.png"), "soft_spot.png")
T(os.path.join(POLY, "PolygonParticles_Circle_01.png"), "circle_01.png")
T(os.path.join(POLY, "PolygonParticles_Smoke_01.png"), "poly_smoke.png")
T(os.path.join(RPG, "Point19.png"), "flash_point19.png")
T(os.path.join(RPG, "Point5.png"), "flash_point5.png")
T(os.path.join(CARTOON, "Heart1.png"), "heart.png")

# ---- 怪物音效(每怪 hurt/dead 1-2 个,接口按 meta_key 子目录) ----
MON = "monsters"
A(os.path.join("..", "") + "Sound/ZombieHorrorPackageFree/MP3/VO/Zombie01/Zombie001_Hurt_A_001.mp3", f"{MON}/axe_zombie", "hurt.mp3")
A("ZombieHorrorPackageFree/MP3/BodyFall/Foley_BodyFall_001.mp3", f"{MON}/axe_zombie", "dead.mp3")
A("ZombieHorrorPackageFree/MP3/VO/Zombie01/Zombie001_Hurt_A_002.mp3", f"{MON}/fat_zombie", "hurt.mp3")
A("ZombieHorrorPackageFree/MP3/BodyFall/Foley_BodyFall_002.mp3", f"{MON}/fat_zombie", "dead.mp3")
A("ZombieHorrorPackageFree/MP3/VO/Zombie03/Zombie003_Hurt_A_001.mp3", f"{MON}/baotou", "hurt.mp3")
A("ZombieHorrorPackageFree/MP3/BodyFall/Foley_BodyFall_003.mp3", f"{MON}/baotou", "dead.mp3")
A("ZombieHorrorPackageFree/MP3/VO/Zombie03/Zombie003_Hurt_A_002.mp3", f"{MON}/skeleton", "hurt.mp3")
A("ZombieHorrorPackageFree/MP3/BodyFall/Foley_BodyFall_001.mp3", f"{MON}/skeleton", "dead.mp3")
A("DefaultHurt.ogg", f"{MON}/bull", "hurt.ogg")
A("DefaultDeath.ogg", f"{MON}/bull", "dead.ogg")
A("DefaultHurt.ogg", f"{MON}/box", "hurt.ogg")
A("DefaultDeath.ogg", f"{MON}/box", "dead.ogg")
A("ZombieHorrorPackageFree/MP3/VO/Zombie01/Zombie001_Hurt_A_003.mp3", f"{MON}/fly_axe_zombie", "hurt.mp3")
A("ZombieHorrorPackageFree/MP3/BodyFall/Foley_BodyFall_002.mp3", f"{MON}/fly_axe_zombie", "dead.mp3")
A("ToonShoot/Male_Hurt_01_Clifford.ogg", f"{MON}/toon_shoot", "hurt.ogg")
A("ToonShoot/Male_Death_01_Clifford.ogg", f"{MON}/toon_shoot", "dead.ogg")
A("ToonShoot/Male_Hurt_02_Clifford.ogg", f"{MON}/toon_shoot_alien", "hurt.ogg")
A("ToonShoot/Male_Death_02_Clifford.ogg", f"{MON}/toon_shoot_alien", "dead.ogg")
A("Monster/Level2BossHurt.ogg", f"{MON}/level2_boss", "hurt.ogg")
A("Monster/Level2BossDeath.ogg", f"{MON}/level2_boss", "dead.ogg")
A("Monster/狼人.wav", f"{MON}/wolf", "hurt.wav")
A("Monster/狼人.wav", f"{MON}/wolf", "dead.wav")
A("Monster/狼人.wav", f"{MON}/wolf_blue", "hurt.wav")
A("Monster/狼人.wav", f"{MON}/wolf_blue", "dead.wav")
A("Monster/狼人.wav", f"{MON}/wolf_green", "hurt.wav")
A("Monster/狼人.wav", f"{MON}/wolf_green", "dead.wav")
A("Dragon/Dragon_Growl_00.mp3", f"{MON}/dragon_red", "hurt.mp3")
A("Dragon/Dragon_Growl_01.mp3", f"{MON}/dragon_red", "dead.mp3")
A("Dragon/Dragon_Growl_00.mp3", f"{MON}/dragon_green", "hurt.mp3")
A("Dragon/Dragon_Growl_01.mp3", f"{MON}/dragon_green", "dead.mp3")
A("_flight/F_Ice_polish.wav", f"{MON}/dragon_blue", "hurt.wav")
A("KriptoFX/../AssetTools_dummy", f"{MON}/_dummy", "x")  # placeholder removed below
errors = [e for e in errors if "_dummy" not in e]
KA = os.path.join(SRC, "AssetTools", "KriptoFX", "Realistic Effects Pack v4", "Effects", "Audio")
def K(src, sub, name):
    d = os.path.join(AUD_OUT, sub)
    os.makedirs(d, exist_ok=True)
    shutil.copyfile(src, os.path.join(d, name))
K(os.path.join(KA, "shout_frostbreath_006.wav"), f"{MON}/dragon_blue", "dead.wav")
K(os.path.join(KA, "FireIn.wav"), f"{MON}/magma_demon", "hurt.wav")
K(os.path.join(KA, "RockExplosion.wav"), f"{MON}/magma_demon", "dead.wav")
K(os.path.join(KA, "RockImpact.wav"), f"{MON}/rock_warrior", "hurt.wav")
K(os.path.join(KA, "RockExplosion.wav"), f"{MON}/rock_warrior", "dead.wav")
K(os.path.join(SND, "Impacts", "Ricochet_01_SFX.wav"), f"{MON}/level2_boss", "metal_1.wav")
K(os.path.join(SND, "Impacts", "Ricochet_02_SFX.wav"), f"{MON}/level2_boss", "metal_2.wav")
K(os.path.join(KA, "RockImpact.wav"), f"{MON}/level2_boss", "metal_3.wav")

# ---- UI / 系统音效 ----
A("UI.mp3", "ui", "click.mp3")
A("Jingle_Win_00.mp3", "ui", "win.mp3")
A("PostApocalypseGunsDemo/Pistols/Zapper_3p_02.wav", "ui", "shot.wav")
shutil.copyfile(os.path.join(KA, "FreezeLoop.wav"), os.path.join(AUD_OUT, "effects", "freeze_loop.wav"))
shutil.copyfile(os.path.join(SND, "_flight", "F_Ball_lightfire.wav"), os.path.join(AUD_OUT, "effects", "fireball_launch.wav"))
shutil.copyfile(os.path.join(KA, "FireExplosion.wav"), os.path.join(AUD_OUT, "effects", "fireball_hit.wav"))
shutil.copyfile(os.path.join(SND, "_flight", "F_Ball_rock.wav"), os.path.join(AUD_OUT, "effects", "fireball_rock.wav"))
shutil.copyfile(os.path.join(SND, "_flight", "F_little_Thunder.wav"), os.path.join(AUD_OUT, "effects", "lightning.wav"))
shutil.copyfile(os.path.join(KA, "LightningImpact1.wav"), os.path.join(AUD_OUT, "effects", "lightning_impact.wav"))
shutil.copyfile(os.path.join(SND, "_flight", "F_flash_steam.wav"), os.path.join(AUD_OUT, "effects", "teleport.wav"))
shutil.copyfile(os.path.join(KA, "Flame1.wav"), os.path.join(AUD_OUT, "effects", "breath_fire.wav"))
shutil.copyfile(os.path.join(KA, "shout_frostbreath_006.wav"), os.path.join(AUD_OUT, "effects", "breath_ice.wav"))
shutil.copyfile(os.path.join(SND, "_flight", "F_wind_shine.wav"), os.path.join(AUD_OUT, "effects", "pickup.wav"))

if errors:
    print("ERRORS:")
    print("\n".join(errors))
    sys.exit(1)
print("OK: textures=%d" % len(os.listdir(TEX_OUT)))
print("OK audio dirs:", sorted(os.listdir(os.path.join(AUD_OUT, "monsters"))))
