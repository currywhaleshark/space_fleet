"""Verify recorded combat trials and regenerate compact, reproducible tables."""
from collections import defaultdict
from pathlib import Path
from statistics import mean
import hashlib
import json
import re

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
read = lambda p: p.read_text(encoding='utf-8-sig')
load = lambda name: json.loads(read(HERE / name))
rows = lambda name: [r['Result'] for r in load(name + '.json')]
bench, duels, fleet = rows('bench'), rows('duels'), rows('fleet')
pd, legacy = rows('pd-fixed'), rows('pd-legacy')
assert [len(x) for x in (bench, duels, fleet, pd, legacy)] == [72, 144, 6, 432, 432]
before = {r['Id']: r['Result'] for r in load('pd-legacy.json')}
changed = []
for item in load('pd-fixed.json'):
    r, old = item['Result'], before[item['Id']]
    if {k:v for k,v in r.items() if k != 'Legacy'} != {k:v for k,v in old.items() if k != 'Legacy'}:
        changed.append(r)
assert changed and all(r['Part'] != 'stock' and not r['Missile'] for r in changed)
for r in pd:
    if not r['Missile'] and r['Hits']:
        energy = {'stock':20, 'pd-barrage':18, 'pd-longrange':23}[r['Part']]
        assert abs(r['ShieldDamage'] - r['Hits'] * energy) < .01
assert any(r['Exposure'] == 'outer' and r['Range'] > 5000 and r['ShieldDamage'] > 0 for r in pd)
assert all(r['Shots'] == 0 for r in pd if r['Exposure'] == 'outer' and r['Part'] != 'pd-longrange')

cadence = [r for r in bench if r['Kind'] == 'cadence']
armor = [r for r in bench if r['Kind'] == 'armor']
shield = [r for r in bench if r['Kind'] == 'shield']
for r in cadence:
    stock = next(x for x in cadence if x['Hull'] == r['Hull'] and x['Part'] == 'stock' and x['Priority'] == r['Priority'])
    assert r['Shots'] > 0 and r['Shots'] + r['Ammo'] == stock['Shots'] + stock['Ammo']
assert all(r['Hits'] > 0 and r['Hits'] <= r['Shots'] and r['Remaining'] == 0 for r in shield)
assert all(r['Rays'] >= r['Penetrated'] > 0 and r['ModuleDamagePerRound'] > 0 for r in armor)

# Same stock battle must be reproduced on both equipment-side assignments.
# Otherwise an initialization-order change would masquerade as component balance.
for r in duels:
    if r['Part'] != 'stock' or r['Mirror']:
        continue
    other = next(x for x in duels if x['Part'] == 'stock' and x['Hull'] == r['Hull']
                 and x['Range'] == r['Range'] and x['Seed'] == r['Seed'] and x['Mirror'])
    assert r['Winner'] == other['Winner'] and r['Seconds'] == other['Seconds']
    assert r['Selected'] == other['Stock'] and r['Stock'] == other['Selected']
assert all(r['Selected']['Log']['Rails'] > 0 for r in duels if r['Range'] != 15000)
assert all(r['Ships'] >= 40 and r['Selected'] for r in fleet)
for r in fleet:
    fitted = [s for s in r['Selected'] if s['Hull'] == r['Hull']]
    assert fitted and all((s['Components'] or {}).get('MainGun', 'stock') == r['Part'] for s in fitted)

prior = HERE.parent / 'components_20261016'
assert load('stock-definitions.json') == json.loads(read(prior / 'stock-definitions.json'))
old_log = read(prior / 'sim-checks.log').splitlines()
new_log = read(HERE / 'sim-checks.log').splitlines()
without_components = lambda lines: [x for x in lines if 'replacement system/catalog/research/preset checks' not in x]
assert without_components(new_log) == without_components(old_log)
assert 'PASS: 1893 replacement system/catalog/research/preset checks' in new_log
counts = [int(m[1]) for line in new_log if (m := re.match(r'PASS: (\d+) ', line))]
assert len(counts) == 61 and sum(counts) == 5677061
build_warnings = {}
for name in ('game-build', 'scale-build', 'batch-build'):
    log = read(HERE / (name + '.log'))
    assert '오류 0개' in log, name
    build_warnings[name] = int(re.findall(r'경고 (\d+)개', log)[-1])
    # This offline machine could not query NuGet's advisory service. Preserve
    # that warning in the evidence; it is not a compiler or test failure.
    assert set(re.findall(r'warning (\w+):', log)) <= {'NU1900'}, name

lines = ['# 교체 무장 측정표', '', '자동 생성: `python docs/validation/component_combat_20261016/analyze.py`', '',
         '시험 조건과 해석의 한계는 README.md를 함께 읽는다.', '',
         '## 연속 사격 — 300초, 무장 기본 2 PIP', '',
         '| 함체 | 형식 | 발수 | 발사 에너지 합 | 잔탄 | 소진 시각(초) | 과열(초) |',
         '|---|---|---:|---:|---:|---:|---:|']
num = lambda value: '—' if value is None else f'{value:.1f}'
for r in cadence:
    if not r['Priority']:
        lines.append(f"| {r['Hull']} | {r['Part']} | {r['Shots']} | {r['FiredEnergy']:.0f} | {r['Ammo']} | {num(r['EmptyAt'])} | {r['HotSeconds']:.1f} |")
lines += ['', '## 장갑 — 새 표적의 모듈 중심을 향한 고정 광선, 실드 제외', '',
          '| 함체 | 형식 | 관통 경로/전체 | 발당 모듈 피해 | 포탑 일제 모듈 피해 환산 | 발당 구조 피해 |',
          '|---|---|---:|---:|---:|---:|']
for r in armor:
    lines.append(f"| {r['Hull']} | {r['Part']} | {r['Penetrated']}/{r['Rays']} | {r['ModuleDamagePerRound']:.1f} | {r['ModuleDamagePerMountSalvo']:.1f} | {r['StructurePerRound']:.1f} |")
lines += ['', '## 실드 — 5km, 실제 포탑 하나/고정포', '',
          '| 함체 | 형식 | 첫 발사→실드 소진(초) | 발사/명중 | 실드 흡수 합 |', '|---|---|---:|---:|---:|']
for r in shield:
    lines.append(f"| {r['Hull']} | {r['Part']} | {num(r['TimeFromFirst'])} | {r['Shots']}/{r['Hits']} | {r['Absorbed']:.1f} |")
lines += ['', '## 실제 AI 동종함 1:1 — 시드 1·2, 장착 진영 교환', '',
          '미결은 300초 제한이며 무승부 판정과 다르다. 피해 합은 미사일·부포 등 다른 무장을 포함한다.', '',
          '| 함체 | 시작 거리(km) | 형식 | 승/패/미결 | 주포 발사/명중 | 주포 미발사 판수 | 모듈 피해 합 |',
          '|---|---:|---|---:|---:|---:|---:|']
groups = defaultdict(list)
for r in duels:
    groups[r['Hull'],r['Range'],r['Part']].append(r)
for (h,d,p), group in sorted(groups.items()):
    wins = sum(r['Winner'] == r['SelectedSide'] for r in group)
    losses = sum(r['Winner'] is not None and r['Winner'] != r['SelectedSide'] for r in group)
    pending = sum(r['Winner'] is None for r in group)
    logs = [r['Selected']['Log'] for r in group]
    lines.append(f"| {h} | {d/1000:g} | {p} | {wins}/{losses}/{pending} | {sum(r['Rails'] for r in logs)}/{sum(r['RailHits'] for r in logs)} | {sum(r['SelectedFirstFire'] is None for r in group)} | {sum(r['ModuleDamageInflicted'] for r in logs):.1f} |")
lines += ['', '## 부포 — 6초 노출, 시드 1~4 평균', '',
          '| 함체 | 표적 | 노출 | 형식 | 발수 평균 | 명중 평균 | 실드 피해 평균 | 미사일 요격/4 | 요격 완료 평균(초) |',
          '|---|---|---|---|---:|---:|---:|---:|---:|']
groups.clear()
for r in pd:
    groups[r['Hull'],r['Missile'],r['Exposure'],r['Part']].append(r)
for (h,m,e,p), group in sorted(groups.items()):
    kills = [r['KilledAt'] for r in group if r['Intercepted'] and r['KilledAt'] is not None]
    lines.append(f"| {h} | {'미사일' if m else '요격함'} | {e} | {p} | {mean(r['Shots'] for r in group):.2f} | {mean(r['Hits'] for r in group):.2f} | {mean(r['ShieldDamage'] for r in group):.2f} | {sum(r['Intercepted'] for r in group)}/4 | {num(mean(kills) if kills else None)} |")
lines += ['', '## 10:10 편대 혼성 매칭 — 시드 1, 최대 300초 기능 시험', '',
          '| 대표 함체 | 형식 | 전체 척수 | 종료 시각(초) | 소유 편대 주포 발사/명중 | 잔탄 | 격침 |',
          '|---|---|---:|---:|---:|---:|---:|']
for r in fleet:
    selected = r['Selected']
    lines.append(f"| {r['Hull']} | {r['Part']} | {r['Ships']} | {r['Seconds']:.1f} | {sum(s['Log']['Rails'] for s in selected)}/{sum(s['Log']['RailHits'] for s in selected)} | {sum(s['Ammo'] for s in selected)} | {sum(s['Destroyed'] for s in selected)}/{len(selected)} |")
(HERE / 'measurements.md').write_text('\n'.join(lines)+'\n', encoding='utf-8')
sources = ('src/Sim/Missiles.cs','src/Sim/ShipComponents.cs','src/Sim/ShipDefinition.cs',
           'src/Sim/SimPointDefense.cs','src/Sim/SimMissiles.cs','tests/SimChecks/ComponentChecks.cs',
           'tools/BattleBatch/ComponentTrials.cs','tools/BattleBatch/Program.cs','data/components/catalog.json')
report = {'bench':len(bench),'armor_rays':sum(r['Rays'] for r in armor),'ai_duels':len(duels),
          'mixed_fleet_smoke':len(fleet),'pd_trials_per_version':len(pd),'pd_changed_trials':len(changed),
          'stock_pd_and_missile_pd_unchanged':True,'stock_hulls_identical':34,
          'existing_regression_output_identical':True,'simulation_groups':len(counts),'simulation_checks':sum(counts),
          'build_warnings':build_warnings,
          'source_sha256':{p:hashlib.sha256((REPO/p).read_bytes()).hexdigest() for p in sources},
          'evidence_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in HERE.iterdir()
                             if p.suffix in ('.json','.log') and p.name != 'verification.json'}}
(HERE / 'verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if not k.endswith('sha256')},ensure_ascii=False))
