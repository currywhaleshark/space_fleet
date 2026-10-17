"""Validate the barrage correction without rewriting earlier combat evidence."""
from collections import defaultdict
from pathlib import Path
from statistics import mean
import hashlib
import json
import re

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
PRIOR = HERE.parent / 'component_combat_20261016'
read = lambda p: p.read_text(encoding='utf-8-sig')
load = lambda p: json.loads(read(p))
before = {r['Id']:r['Result'] for r in load(PRIOR/'pd-fixed.json')}
after = load(HERE/'pd-after.json')
assert len(before) == len(after) == 432
changed = [r['Result'] for r in after if r['Result'] != before[r['Id']]]
assert len(changed) == 36
assert all(r['Part'] == 'pd-barrage' and r['Missile'] for r in changed)
assert load(HERE/'stock-definitions.json') == load(PRIOR/'stock-definitions.json')

salvos = [r['Result'] for r in load(HERE/'salvos.json')]
assert len(salvos) == 768 and all(r['Launched'] == 12 and r['Remaining'] == 0 for r in salvos)
assert any(r['Hits'] > 0 for r in salvos), 'Fixture must actually damage ships when missiles get through'
summary = {}
for part in ('stock','barrage-before','pd-barrage','pd-longrange'):
    group = [r for r in salvos if r['Part'] == part]
    summary[part] = {'trials':len(group), 'launched':sum(r['Launched'] for r in group),
                     'intercepted':sum(r['Intercepted'] for r in group),
                     'ship_hits':sum(r['Hits'] for r in group), 'defenders_destroyed':sum(r['Destroyed'] for r in group)}
assert summary['pd-barrage']['intercepted'] > summary['stock']['intercepted'] > summary['barrage-before']['intercepted']

old_log = read(PRIOR/'sim-checks.log').splitlines()
new_log = read(HERE/'sim-checks.log').splitlines()
strip = lambda lines: [x for x in lines if 'replacement system/catalog/research/preset checks' not in x]
assert strip(old_log) == strip(new_log), 'Existing regression results changed'
assert 'PASS: 2033 replacement system/catalog/research/preset checks' in new_log
counts = [int(m[1]) for x in new_log if (m:=re.match(r'PASS: (\d+) ',x))]
assert len(counts) == 61 and sum(counts) == 5677201
warnings = {}
for name in ('batch-build','game-build','scale-build'):
    log = read(HERE/(name+'.log'))
    assert '오류 0개' in log and set(re.findall(r'warning (\w+):',log)) <= {'NU1900'}
    warnings[name] = int(re.findall(r'경고 (\d+)개',log)[-1])

lines = ['# 탄막형 재검증 수치', '', '통제된 근거리 유입 시험이다. 조건과 한계는 README.md를 참고한다.', '',
         '| 형식 | 시험 수 | 투입 | 요격 | 함선 명중 | 방어함 격침 |', '|---|---:|---:|---:|---:|---:|']
for part,r in summary.items():
    lines.append(f"| {part} | {r['trials']} | {r['launched']} | {r['intercepted']} | {r['ship_hits']} | {r['defenders_destroyed']} |")
lines += ['', '함선 명중은 BattleLog에 피해 표적이 기록된 명중이다. 파괴된 함체 등에 무효 폭발한 탄을 포함하지 않아 요격+명중이 투입과 같지 않을 수 있다.', '',
          '## 함체·속도·탄종별 요격 수', '',
          '각 행은 시드 1~8 × 12발 = 96발. 기본형/수정 전/후/원거리형 순서.', '',
          '| 함체 | 탄종 | 속도(m/s) | 기본 | 탄막 이전 | 탄막 수정 | 원거리 |', '|---|---|---:|---:|---:|---:|---:|']
groups = defaultdict(dict)
for r in salvos:
    key = (r['Hull'],r['Threat'],r['Speed'])
    groups[key][r['Part']] = groups[key].get(r['Part'],0)+r['Intercepted']
for (h,t,s),r in sorted(groups.items()):
    lines.append(f"| {h} | {t} | {s:g} | {r['stock']} | {r['barrage-before']} | {r['pd-barrage']} | {r['pd-longrange']} |")
lines += ['', '## 단일 경량 미사일 — 500m 정지 노출, 시드 1~4', '',
          '| 함체 | 형식 | 요격/4 | 평균 완료(초) | 평균 발수 |', '|---|---|---:|---:|---:|']
for hull in ('battleship','escort','interceptor'):
    for part in ('stock','pd-barrage','pd-longrange'):
        r=[x['Result'] for x in after if x['Result']['Hull']==hull and x['Result']['Part']==part
           and x['Result']['Missile'] and x['Result']['Exposure']=='near']
        lines.append(f"| {hull} | {part} | {sum(x['Intercepted'] for x in r)}/4 | {mean(x['KilledAt'] for x in r):.3f} | {mean(x['Shots'] for x in r):.1f} |")
(HERE/'measurements.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
sources = ('data/components/catalog.json','src/Sim/ShipComponents.cs','src/Sim/Missiles.cs',
           'src/Sim/ShipDefinition.cs','src/Sim/SimPointDefense.cs','tests/SimChecks/ComponentChecks.cs',
           'tools/BattleBatch/ComponentTrials.cs','docs/validation/pd_barrage_20261016/verify.py')
report = {'stock_definitions_identical':34,'unchanged_existing_regression_output':True,
          'simulation_groups':len(counts),'simulation_checks':sum(counts),'component_checks':2033,
          'pd_comparison_cases':len(after),'changed_pd_cases':len(changed),
          'changed_only_barrage_vs_missiles':True,'salvo_cases':len(salvos),'summary':summary,'build_warnings':warnings,
          'source_sha256':{p:hashlib.sha256((REPO/p).read_bytes()).hexdigest() for p in sources},
          'evidence_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in HERE.iterdir()
                             if p.suffix in ('.json','.log') and p.name!='verification.json'}}
(HERE/'verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if not k.endswith('sha256')},ensure_ascii=False))
