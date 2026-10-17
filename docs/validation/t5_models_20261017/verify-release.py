"""Read-only package, latest assembly, captures, and regression evidence check."""
import hashlib,json,re,zipfile
from pathlib import Path
here=Path(__file__).resolve().parent;repo=here.parents[2]
name='SpaceFleet-20261017-t5-fleet-Windows-x64';release=repo/'builds'/name;archive=release.with_suffix('.zip')
digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
info=json.loads((release/'build-info.json').read_text(encoding='utf-8-sig'))
sha=digest(archive);assert sha==Path(str(archive)+'.sha256').read_text().split()[0]
with zipfile.ZipFile(archive) as bundle:
    assert bundle.testzip() is None
    entries=len(bundle.infolist())
assembly=release/'data_SpaceFleet_windows_x86_64/SpaceFleet.dll'
assert digest(assembly)==digest(repo/'.godot/mono/temp/bin/ExportRelease/win-x64/SpaceFleet.dll')
assert (assembly.parent/'coreclr.dll').is_file()
assert len(info['checks'])==15
counts=[int(re.search(r'PASS: (\d+)',s)[1]) for s in info['checks']]
simCounts={}
for stem in ('sim-tier','sim-cruisers','sim-progression','sim-hardpoints'):
    log=(here/f'{stem}.log').read_text(encoding='utf-8-sig')
    assert 'PASS:' in log and 'Exception' not in log
    count=int(re.search(r'PASS: (\d+)',log)[1])
    simCounts[stem]=count
ids=tuple(p.stem for p in sorted((repo/'data/variants').glob('*_t5.json')))
portraits=(here/'portraits.log').read_text(encoding='utf-8-sig')
baseline=json.loads((here/'baseline.json').read_text(encoding='utf-8'))
for key in ids:
    assert f'Portrait: {key}' in portraits
    portrait=f'assets/ui/ships/{key}.png'
    assert portrait not in baseline['files'] and (repo/portrait).is_file()
    assert (here/f'{key}-hangar.png').is_file()
    log=(here/f'{key}-hangar.log').read_text(encoding='utf-8-sig')
    assert 'ERROR:' not in log and 'Exception' not in log
report=dict(release=name,exe=str(release/'SpaceFleet.exe'),zip=str(archive),zipBytes=archive.stat().st_size,sha256=sha,
    zipEntries=entries,zipCrc='pass',releaseCheckGroups=len(counts),releaseChecks=sum(counts),simChecks=simCounts,
    checks=info['checks'],portraitIds=list(ids),selfContained=True,assemblySha256=digest(assembly),matchesLatestExportAssembly=True,
    sourceCommit=info['sourceCommit'],localChanges=info['localChanges'],assetSha256={
    path:digest(repo/path) for key in ids for path in (f'art/blender/{key}.blend',f'assets/ships/{key}.glb',f'assets/ui/ships/{key}.png',f'data/variants/{key}.json',f'data/model_layouts/{key}.json')})
(here/'release-verification.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:report[k] for k in ('release','releaseChecks','simChecks','zipCrc','matchesLatestExportAssembly')}))
