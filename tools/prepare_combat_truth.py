"""Copy only combat animation assets into an isolated Unity research project."""
from pathlib import Path
import re, shutil, json, hashlib

source=Path('G:/test/FPSGame')
project=Path('G:/FPSGame/temp/unity_combat_truth')
project.mkdir(parents=True,exist_ok=True)
metas=list((source/'Assets').rglob('*.meta'))
guid_paths={}
selected=[]
events=re.compile(r'functionName: (EventAttack|EventSkill|ToonShoot|BaotouSkill|RockAttack|StartFire)\b')
for meta in metas:
    text=meta.read_text(encoding='utf-8-sig',errors='replace')
    match=re.search(r'^guid: ([a-f0-9]{32})',text,re.M)
    if match: guid_paths[match[1]]=meta.with_suffix('')
    if events.search(text) and meta.with_suffix('').suffix.lower()=='.fbx': selected.append(meta.with_suffix(''))
boss=source/'Assets/Enemy/金属盔甲武士/LowPoly_Lancer'
selected.extend([boss/'FBX/Modle.FBX',*list((boss/'Ani').glob('*.anim'))])
models={}
for key,name,game_file in [('militia_model','ToonSoldiers_Militias.FBX','toon/ToonSoldiers_Militias.FBX'),('alien_model','stardudes_suit_mk1.fbx','toon_alien/stardudes_suit_mk1.fbx')]:
    digest=hashlib.sha256((Path('G:/FPSGame/game/assets/models/monsters')/game_file).read_bytes()).digest()
    candidates=[p for p in guid_paths.values() if p.name.lower()==name.lower()]
    matches=[p for p in candidates if hashlib.sha256(p.read_bytes()).digest()==digest]
    if not matches and len(candidates)!=1:
        raise RuntimeError(f'Ambiguous original model for {key}: {candidates}')
    model=matches[0] if matches else candidates[0]
    if not matches: print('Model was converted in Godot; baking on original Unity rig:',model.relative_to(source))
    selected.append(model)
    models[key]=model.relative_to(source).as_posix()
for name in ['infantry_combat_reload.FBX','infantry_combat_shoot.FBX']:
    clip=next(p for p in guid_paths.values() if p.name==name)
    selected.append(clip)
    models[name.split('.')[0]]=clip.relative_to(source).as_posix()
copied=set()
def copy_asset(asset):
    if asset in copied: return
    copied.add(asset)
    target=project/asset.relative_to(source)
    target.parent.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(asset,target)
    meta=asset.with_name(asset.name+'.meta')
    if meta.exists():
        shutil.copyfile(meta,target.with_name(target.name+'.meta'))
        data=meta.read_text(encoding='utf-8-sig',errors='replace')
        for avatar_guid in re.findall(r'(?:sourceAvatar|lastHumanDescriptionAvatarSource):[^\n]*guid: ([a-f0-9]{32})',data):
            if avatar_guid in guid_paths: copy_asset(guid_paths[avatar_guid])
for asset in selected: copy_asset(asset)
(project/'ProjectSettings').mkdir(exist_ok=True)
shutil.copyfile(source/'ProjectSettings/ProjectVersion.txt',project/'ProjectSettings/ProjectVersion.txt')
(project/'Packages').mkdir(exist_ok=True)
modules=['animation','jsonserialize','imageconversion','physics','audio']
(project/'Packages/manifest.json').write_text(json.dumps({'dependencies':{'com.unity.modules.'+m:'1.0.0' for m in modules}},indent=2),encoding='utf-8')
editor=project/'Assets/Editor';editor.mkdir(exist_ok=True)
baker=(source/'Assets/Editor/AITools/BakeKpopDance.cs').read_text(encoding='utf-8-sig')
baker=baker.replace('graph.Play();','graph.Play();\n        graph.Evaluate(0f); // sample clip zero before recording the first visible pose')
(editor/'BakeKpopDance.cs').write_text(baker,encoding='utf-8')
(editor/'combat_paths.json').write_text(json.dumps(models,ensure_ascii=False),encoding='utf-8')
shutil.copyfile(Path('G:/FPSGame/tools/BakeCombatTruth.cs'),editor/'BakeCombatTruth.cs')
print('Isolated Unity combat project:',project,'assets:',len(copied))
for controller in (source/'Assets').rglob('*.controller'):
    text=controller.read_text(encoding='utf-8-sig',errors='replace')
    for block in re.split(r'^--- ',text,flags=re.M):
        if re.search(r'^  m_Name: infantry_combat_reload$',block,re.M):
            motion=re.search(r'^  m_Motion: .*guid: ([a-f0-9]{32})',block,re.M)
            if motion: print('Reload motion:',controller.relative_to(source),guid_paths.get(motion[1]))
