"""Verify preserved originals, unchanged performance and deterministic T5/progression regeneration."""
import copy,hashlib,json,shutil,subprocess,sys
from pathlib import Path
from branch_model_layout import resolve
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'docs/validation/t5_models_20261017'
KEYS=tuple(p.stem for p in sorted((ROOT/'data/variants').glob('*_t5.json')))
baseline=json.loads((OUT/'baseline.json').read_text(encoding='utf-8'))
digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
allowed={'assets/ships/manifest.json',*(f'{folder}/{key}{suffix}' for key in KEYS
    for folder,suffix in (('data/variants','.json'),('art/blender','.blend'),('assets/ships','.glb'),('assets/ui/ships','.png')))}
changed=[p for p,h in baseline['files'].items() if digest(ROOT/p)!=h]
assert not set(changed)-allowed,changed
def balance(d):
    d=copy.deepcopy(d);d.pop('hullSections');d.pop('modelId')
    for module in d['modules']:module.pop('center')
    for bay in d['bays']:bay.pop('center')
    d['pointDefense'].pop('mounts')
    d['railgun'].pop('muzzle')
    d['missiles'].pop('launchPoint')
    if d.get('antimatter'):d['antimatter']['flight'].pop('launchPoint')
    if d.get('reconProbes'):d['reconProbes'].pop('launchPoints',None)
    return d
for key in KEYS:
    assert balance(resolve(ROOT,key))==balance(baseline['definitions'][key]),'Changed performance: '+key
    layout=json.loads((ROOT/f'data/model_layouts/{key}.json').read_text(encoding='utf-8'))
    old={s['id']:s for s in baseline['definitions'][key]['hullSections']}
    current={s['id']:s for s in resolve(ROOT,key)['hullSections']}
    for s in layout['sections']:
        for field in ('armor','faces'):assert current[s['id']].get(field)==old[s['armorFrom']].get(field),'Changed armor'
scratch=ROOT/'shots/t5-generator-check';scratch.mkdir(parents=True,exist_ok=True)
for folder in ('data/ships','data/tree','data/variants','data/model_layouts'):
    target=scratch/folder;target.mkdir(parents=True,exist_ok=True)
    for p in (ROOT/folder).glob('*.json'):shutil.copy2(p,target/p.name)
(scratch/'tools').mkdir(exist_ok=True)
for name in ('generate-tier-variants.py','generate-progression-data.py','branch_model_layout.py'):
    shutil.copy2(ROOT/'tools'/name,scratch/'tools'/name)
for name in ('generate-tier-variants.py','generate-progression-data.py'):
    subprocess.run([sys.executable,'-X','utf8',str(scratch/'tools'/name)],check=True,capture_output=True)
checked=[]
for folder in ('data/ships','data/variants'):
    for p in (ROOT/folder).glob('*.json'):
        assert resolve(ROOT,p.stem)==resolve(scratch,p.stem),'Regeneration changed '+p.stem
        checked.append(p.stem)
for p in (ROOT/'data/tree').glob('*.json'):
    assert json.loads(p.read_text(encoding='utf-8-sig'))==json.loads((scratch/'data/tree'/p.name).read_text(encoding='utf-8-sig')),'Changed tree: '+p.name
manifest=json.loads((ROOT/'assets/ships/manifest.json').read_text(encoding='utf-8'))
report=dict(preservedFiles=len(baseline['files'])-len(changed),changedBaselineFiles=changed,
    unchangedPerformanceHullIds=list(KEYS),identicalRegeneratedHullCount=len(checked),identicalResearchTrees=True,
    modelCosts=[{k:s[k] for k in ('id','triangles','editableParts','meshGroups')} for s in manifest['ships'] if s['id'] in KEYS])
(OUT/'preservation-audit.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report,indent=2))
