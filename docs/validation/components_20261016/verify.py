"""Compare unchanged stock behavior and validate the six-system integration evidence."""
from pathlib import Path
import hashlib
import json
import re

root = Path(__file__).resolve().parent
project = root.parents[2]
prior = root.parent / 'hardpoints_20261016'
read = lambda p: p.read_text(encoding='utf-8-sig')
before = json.loads(read(prior / 'after-definitions.json'))
after = json.loads(read(root / 'stock-definitions.json'))
assert len(after) == 34 and before == after, 'Stock definitions/loadout IDs changed'
old = read(prior / 'after-sim-checks.log').splitlines()
new = read(root / 'sim-checks.log').splitlines()
added = [s for s in new if 'replacement system/catalog/research/preset checks' in s]
assert added == ['PASS: 1880 replacement system/catalog/research/preset checks']
assert [s for s in new if s not in added] == old, 'Existing regression output changed'
counts = [int(m[1]) for s in new if (m := re.match(r'PASS: (\d+) ', s))]
assert len(counts) == 61 and sum(counts) == 5677048
render = {'model-checks': 3560, 'hangar-flow': 32, 'progression-flow': 30}
for name, count in render.items():
    log = read(root / (name + '.log'))
    assert f'PASS: {count} ' in log and 'ERROR:' not in log, name
build = read(root / 'game-build.log')
assert '경고 0개' in build and '오류 0개' in build
for name in ('hangar-triple', 'hangar-fixed', 'hangar-generator', 'hangar-shield'):
    assert (root / (name + '.png')).stat().st_size > 10000
    assert 'ERROR:' not in read(root / (name + '.log'))
sources = ('src/Sim/ShipComponents.cs', 'data/components/catalog.json', 'src/Sim/ShipDefinition.cs',
           'src/Sim/ShipLoadouts.cs', 'src/Sim/FleetLoadouts.cs', 'src/Sim/ResearchTree.cs', 'src/Sim/TierMatchmaking.cs',
           'src/Game/ResearchProgress.cs', 'src/Game/HangarProfile.cs', 'src/Game/HangarView.cs',
           'src/Game/Main.cs', 'src/Game/ShipModelChecks.cs', 'src/Game/HangarFlowChecks.cs',
           'src/View/PrimaryWeaponModels.cs', 'src/View/ImportedShipModels.cs', 'tests/SimChecks/ComponentChecks.cs',
           'tests/SimChecks/FleetLoadoutChecks.cs', 'tests/SimChecks/ProgressionChecks.cs', 'tests/SimChecks/Program.cs')
report = {'stock_hulls': 34, 'stock_definitions_and_loadout_ids_identical': True,
          'existing_regression_output_identical': True, 'simulation': {'groups': len(counts), 'checks': sum(counts)},
          'component_checks': 1880, 'render_checks': render, 'game_build_warnings': 0, 'game_build_errors': 0,
          'source_sha256': {name: hashlib.sha256((project / name).read_bytes()).hexdigest() for name in sources}}
(root / 'verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print(json.dumps({k: v for k, v in report.items() if k != 'source_sha256'}, ensure_ascii=False))
