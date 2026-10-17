"""Verify the modeling pass preserves existing assets and non-geometric balance data."""
import copy
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
from branch_model_layout import resolve, without_authored_layout
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'docs/validation/cruiser_models_20261017'
KEYS=('earth_gun_cruiser','earth_air_defense_cruiser','mars_gun_cruiser','mars_missile_cruiser')
baseline=json.loads((OUT/'baseline.json').read_text(encoding='utf-8'))
allowed={'assets/ships/manifest.json',*(f'data/variants/{k}.json' for k in KEYS)}
changed=[p for p,h in baseline['files'].items() if hashlib.sha256((ROOT/p).read_bytes()).hexdigest()!=h]
assert not set(changed)-allowed,changed
def balance(d):
    d=copy.deepcopy(d);d.pop('modelId');d.pop('hullSections');d['pointDefense'].pop('mounts')
    return d
for key in KEYS:
    assert balance(resolve(ROOT,key))==balance(baseline['definitions'][key]),key+' balance changed'
    future=resolve(ROOT,key)
    future['hullSections'][0]['armor']['thicknessMm']+=7
    restored=without_authored_layout(ROOT,future,key)
    assert restored['hullSections'][0]['armor']==future['hullSections'][0]['armor'],'Geometry restore discarded future armor tuning'
scratch=ROOT/'shots/cruiser-generator-check';scratch.mkdir(parents=True,exist_ok=True)
for folder in ('data/ships','data/tree','data/variants','data/model_layouts'):
    target=scratch/folder;target.mkdir(parents=True,exist_ok=True)
    for p in (ROOT/folder).glob('*.json'):shutil.copy2(p,target/p.name)
(scratch/'tools').mkdir(exist_ok=True)
for name in ('generate-tier-variants.py','branch_model_layout.py'):shutil.copy2(ROOT/'tools'/name,scratch/'tools'/name)
subprocess.run([sys.executable,'-X','utf8',str(scratch/'tools/generate-tier-variants.py')],check=True,capture_output=True)
checked=[]
for key in baseline['definitions']:
    if '_cruiser_t' not in key:continue
    before=resolve(ROOT,key);after=resolve(scratch,key)
    assert before==after,key+' upper tier regeneration changed'
    checked.append(key)
manifest=json.loads((ROOT/'assets/ships/manifest.json').read_text(encoding='utf-8'))
stats=[s for s in manifest['ships'] if s['id'] in KEYS]
report=dict(preservedFiles=len(baseline['files'])-len(changed),changedBaselineFiles=changed,
    unchangedBalanceHullIds=list(KEYS),unchangedRegeneratedUpperTiers=checked,
    modelCosts=[{k:s[k] for k in ('id','triangles','editableParts','meshGroups')} for s in stats])
(OUT/'preservation-audit.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report,indent=2))
