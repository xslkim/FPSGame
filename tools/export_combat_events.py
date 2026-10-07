"""Map Unity API event output to Godot clip names; FBX meta time is normalized."""
import json
from pathlib import Path
records=json.loads(Path('tools/combat_bake/unity_animation_events.json').read_text(encoding='utf-8'))['clips']
mapping={
 'bull': [('bull_king@attack_01.FBX','attack_01'),('bull_king@attack_02.FBX','attack_02'),('bull_king@attack_03.FBX','attack_03')],
 'axe_zombie': [('MonsterAZ@Attack01.FBX','Attack1'),('MonsterAZ@Attack02.FBX','Attack2'),('MonsterAZ@AttackL.FBX','AttackL'),('MonsterAZ@AttackR.FBX','AttackR')],
 'fly_axe_zombie': [('MonsterAZ@Skill.FBX','Skill')],
 'baotou': [('Anim_ATTACK_01.FBX','anim_attack')],
 'magma_demon': [('Magma Demon@attack01.FBX','attack01'),('Magma Demon@attack02.FBX','attack02')],
 'rock_warrior': [('RockWarrior@atk01.FBX','atk01')],
 'wolf': [('MonsterW@Attack01.FBX','Attack1'),('MonsterW@Attack02.FBX','Attack2'),('MonsterW@Skill01.FBX','Skill1'),('MonsterW@Skill02.FBX','Skill2')],
 'wolf_blue': [('Demon Wolf@BiteAttack.FBX','BiteAttack'),('Demon Wolf@ClawAttack.FBX','ClawAttack')],
 'fat_zombie': [('MonsterFZ@Attack01.FBX','Attack1'),('MonsterFZ@Attack02.FBX','Attack2')],
 'skeleton': [('MonsterS@Attack01.FBX','Attack1'),('MonsterS@Attack02.FBX','Attack2')],
 'level2_boss': [('Skill1.anim','Skill1'),('Skill2.anim','Skill2')],
}
for key in ['dragon_red','dragon_blue','dragon_green']:
    mapping[key]=[('Fantasy Dragon@FireBreathOnce.FBX','FireBreathOnce')]
for key in ['toon_shoot','toon_shoot_alien']:
    mapping[key]=[('infantry_combat_reload.FBX','reload'),('infantry_combat_shoot.FBX','shoot')]
result={}
for monster,clips in mapping.items():
    result[monster]={}
    for filename,name in clips:
        found=[r for r in records if r['path'].endswith('/'+filename)]
        assert len(found)==1,(monster,filename,found)
        record=found[0]
        result[monster][name]={'source':record['path'],'length':record['length'],'events':record['events']}
        if name=='reload':
            result[monster][name]['events']=[{'time':record['length']*.9318182,'name':'BeginShoot','string_parameter':''}]
            result[monster][name]['transition_source']='Assets/Monster/ToonSoldiers/InfantryGun.controller: exitTime=0.9318182, blend=0.25s'
Path('game/data/combat_animation_events.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Combat event map:',len(result),'actor types')
