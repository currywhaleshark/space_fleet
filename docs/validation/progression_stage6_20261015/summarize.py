"""Verify this run and regenerate its summary/hash manifest from repository root."""
import csv
import hashlib
import json
import re
import subprocess
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(__file__).resolve().parent
OLD = OUT.parent / 'progression_stage5_20261015'
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def read(path): return path.read_text(encoding='utf-8-sig')
def dump(path, value): path.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
def rows(name): return list(csv.DictReader((OUT/name).open(encoding='utf-8-sig', newline='')))

log = read(OUT/'simchecks.txt')
total = sum(map(int, re.findall(r'PASS: (\d+)', log)))
assert total == 5607574
def legacy(text):
    return [line for line in text.splitlines() if not any(s in line for s in
        ('research/tree/reward/migration', 'squad loadout/preset', 'tier branch/catalog'))]
assert legacy(log) == legacy(read(OLD/'sim-checks.txt'))
render = {name: sum(map(int, re.findall(r'PASS: (\d+)', read(OUT/(name+'.txt')))))
          for name in ('model-test','progression-test','hangar-test','match-flow-test','flow-test')}
assert sum(render.values()) == 2429
assert all(p.stat().st_size == 0 for p in OUT.glob('*-errors.txt'))

summary = {'trials': json.loads(read(OUT/'branch-trials.json')), 'fleets': {}}
for name in ('branches','recon-symmetric','assault-symmetric','tier5'):
    battles = rows(name+'.csv'); ships = rows(name+'.ships.csv')
    assert all(r['winner'] and float(r['friendlyCollisions']) == 0 for r in battles)
    summary['fleets'][name] = []
    for battle in battles:
        group = [s for s in ships if s['seed'] == battle['seed'] and s['mirror'] == battle['mirror']]
        assert len(group) == (78 if name == 'tier5' else 24)
        roles = {}
        for hull in sorted({s['hullId'] for s in group}):
            members = [s for s in group if s['hullId'] == hull]
            roles[hull] = {'ships': len(members), 'destroyed': sum(int(s['destroyed']) for s in members),
                'torpedoes': sum(int(s['torpedoes']) for s in members),
                'torpedoHits': sum(int(s['torpedoHits']) for s in members),
                'probes': sum(int(s['probesLaunched']) for s in members),
                'probeContacts': sum(int(s['probeContacts']) for s in members),
                'probeIdentifications': sum(int(s['probeIdentifications']) for s in members),
                'moduleDamage': sum(float(s['moduleDamageInflicted']) for s in members),
                'firstAssault': sorted({float(s['firstAssault']) for s in members if s['firstAssault']}),
                'averageSurvival': sum(float(s['survivalSeconds']) for s in members)/len(members)}
        summary['fleets'][name].append({'seed': int(battle['seed']), 'mirror': int(battle['mirror']),
            'winner': battle['winner'], 'reason': battle['reason'], 'seconds': float(battle['outcomeTime']), 'hulls': roles})
dump(OUT/'summary.json', summary)

old = json.loads(read(OLD/'manifest.json'))
preserved = {k: sha(ROOT/k) == v for k, v in old['sourceSha256'].items()
    if k.startswith(('data/ships/','data/variants/','data/modules/','assets/ships/','art/blender/'))}
assert all(preserved.values())
prior_evidence = {k: sha(OLD/k) == v for k, v in old['evidenceSha256'].items()}
assert all(prior_evidence.values())
original_models = [f'{directory}/{hull}.{ext}' for hull in
    ('battleship','escort','interceptor','mars_battleship','mars_escort','mars_interceptor')
    for directory, ext in (('art/blender','blend'),('assets/ships','glb'))]
subprocess.run(['git','diff','--quiet','HEAD','--',*original_models], cwd=ROOT, check=True)
files = set()
for pattern in ('src/**/*.cs','src/**/*.csproj','data/**/*.json','tests/SimChecks/*.cs','tests/SimChecks/*.csproj',
                'tools/BattleBatch/*.cs','tools/BattleBatch/*.csproj','assets/ships/*.glb','art/blender/*.blend'):
    files.update(ROOT.glob(pattern))
files.update(ROOT/p for p in ('SpaceFleet.csproj','project.godot','assets/ships/manifest.json',
    'tools/generate-tier-variants.py','tools/generate-progression-data.py','README.md','CLAUDE.md',
    'docs/DESIGN_progression.md','docs/PLAN.md'))
manifest = {'date':'2026-10-15','stage':6,
    'baseCommit': subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),
    'functionalChecks':total,'branchChecks':224,'researchChecks':478,'squadChecks':799,
    'legacyOutputIdentical':True,'rendererChecks':render,'rendererTotal':sum(render.values()),
    'stage5DataAndModelsUnchanged':preserved,'stage5EvidenceUnchanged':prior_evidence,
    'originalModelsEqualToHEAD':original_models,
    'limits':['Reused models','Existing Mars advantage','No AM launches in seven fleet samples',
        'Small sample; not final balance','No performance claim','No updated standalone EXE'],
    'sourceSha256':{str(p.relative_to(ROOT)).replace('\\','/'):sha(p) for p in sorted(files)},
    'evidenceSha256':{p.name:sha(p) for p in sorted(OUT.iterdir()) if p.is_file() and p.name!='manifest.json'}}
dump(OUT/'manifest.json',manifest)
print(f'PASS: {total} functional, {sum(render.values())} renderer, 7 completed battles; prior data/models/evidence preserved')
