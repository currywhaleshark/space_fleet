"""Preservation, balance-field and reproducible geometry generation checks."""
import copy,hashlib,json,shutil,subprocess,sys
from pathlib import Path
from branch_model_layout import resolve
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'docs/validation/t4_small_models_20261017'
KEYS=('earth_recon_t4','interceptor_t4','mars_assault_t4','mars_interceptor_t4')
baseline=json.loads((OUT/'baseline.json').read_text(encoding='utf-8'))
allowed={'assets/ships/manifest.json',*(f'data/variants/{k}.json' for k in KEYS)}
changed=[p for p,h in baseline['files'].items() if hashlib.sha256((ROOT/p).read_bytes()).hexdigest()!=h]
assert not set(changed)-allowed,changed
def balance(d):
    d=copy.deepcopy(d);d.pop('modelId');d.pop('hullSections')
    for items in ('modules','bays'):
        for x in d[items]:x.pop('center')
    d['railgun'].pop('muzzle');d['missiles'].pop('launchPoint');d['pointDefense'].pop('mounts')
    d['antimatter']['flight'].pop('launchPoint')
    if d.get('reconProbes'):d['reconProbes'].pop('launchPoints',None)
    return d
for key in KEYS:
    assert balance(resolve(ROOT,key))==balance(baseline['definitions'][key]),'Changed performance: '+key
    layout=json.loads((ROOT/f'data/model_layouts/{key}.json').read_text(encoding='utf-8'))
    old={s['id']:s for s in baseline['definitions'][key]['hullSections']}
    current={s['id']:s for s in resolve(ROOT,key)['hullSections']}
    for s in layout['sections']:
        for field in ('armor','faces'):assert current[s['id']].get(field)==old[s['armorFrom']].get(field),'Changed armor'
scratch=ROOT/'shots/t4-small-generator-check';scratch.mkdir(parents=True,exist_ok=True)
for folder in ('data/ships','data/tree','data/variants','data/model_layouts'):
    target=scratch/folder;target.mkdir(parents=True,exist_ok=True)
    for p in (ROOT/folder).glob('*.json'):shutil.copy2(p,target/p.name)
(scratch/'tools').mkdir(exist_ok=True)
for name in ('generate-tier-variants.py','branch_model_layout.py'):shutil.copy2(ROOT/'tools'/name,scratch/'tools'/name)
subprocess.run([sys.executable,'-X','utf8',str(scratch/'tools/generate-tier-variants.py')],check=True,capture_output=True)
checked=[]
for p in (ROOT/'data/variants').glob('*_t*.json'):
    assert resolve(ROOT,p.stem)==resolve(scratch,p.stem),'Regeneration changed '+p.stem
    checked.append(p.stem)
manifest=json.loads((ROOT/'assets/ships/manifest.json').read_text(encoding='utf-8'))
report=dict(preservedFiles=len(baseline['files'])-len(changed),changedBaselineFiles=changed,unchangedPerformanceHullIds=list(KEYS),
    identicalRegeneratedVariants=checked,modelCosts=[{k:s[k] for k in ('id','triangles','editableParts','meshGroups')} for s in manifest['ships'] if s['id'] in KEYS])
(OUT/'preservation-audit.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report,indent=2))
