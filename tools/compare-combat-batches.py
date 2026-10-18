"""Compare two BattleBatch --tier-hull runs (same seeds, same code, different ship data).
python tools/compare-combat-batches.py BASE_DIR AFTER_DIR OUT.md [CHANGED_IDS_JSON]
Each directory holds <anchor>.ships.csv / <anchor>.losses.json files written by run pairs."""
import csv, json, statistics, sys
from collections import defaultdict
from pathlib import Path

base, after, out = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
changed = set(json.loads(Path(sys.argv[4]).read_text(encoding='utf-8'))) if len(sys.argv) > 4 else set()
anchors = sorted(p.name[:-len('.ships.csv')] for p in base.glob('*.ships.csv'))


def ships(d, anchor):
    with open(d / f'{anchor}.ships.csv', encoding='utf-8') as f: return list(csv.DictReader(f))


def battles(d, anchor):
    return json.loads((d / f'{anchor}.losses.json').read_text(encoding='utf-8'))


def mean(xs): return statistics.fmean(xs) if xs else float('nan')


lines = ['# 전투 전후 비교 (자동 생성)', '']
lines += ['## 판별 결과', '', '| 기준 함급 | 판 | 승자 일치 | 기준 평균 격침 | 변경 평균 격침 | 기준 평균 종료(초) | 변경 평균 종료(초) |', '|---|---|---|---|---|---|---|']
pooled = {'base': defaultdict(list), 'after': defaultdict(list)}
design = {'base': defaultdict(list), 'after': defaultdict(list)}
bb = {'base': [], 'after': []}
for anchor in anchors:
    b, a = battles(base, anchor), battles(after, anchor)
    lineup_b = {(r['seed'], r['mirror'], r['callsign'], r['hullId']) for r in ships(base, anchor)}
    lineup_a = {(r['seed'], r['mirror'], r['callsign'], r['hullId']) for r in ships(after, anchor)}
    assert lineup_b == lineup_a, (anchor, 'lineups differ')
    same = sum(x['Outcome'] and y['Outcome'] and x['Outcome']['Winner'] == y['Outcome']['Winner'] for x, y in zip(b, a))
    t = lambda rs: mean([r['Outcome']['Time'] for r in rs if r['Outcome']])
    lines.append(f"| {anchor} | {len(b)} | {same}/{len(b)} | {mean([r['Destroyed'] for r in b]):.1f} | {mean([r['Destroyed'] for r in a]):.1f} | {t(b):.0f} | {t(a):.0f} |")
    for tag, d in (('base', base), ('after', after)):
        for r in ships(d, anchor):
            pooled[tag][r['hullId']].append(r); design[tag][r['design']].append(r)
            if r['kind'] == 'Battleship': bb[tag].append(r)


def row(rs):
    received = [float(r['shieldDamageReceived']) + float(r['moduleDamageReceived']) for r in rs]
    return (len(rs), mean([int(r['destroyed']) for r in rs]), mean([float(r['survivalSeconds']) for r in rs]), mean(received),
            mean([int(r['railHits']) / int(r['rails']) for r in rs if int(r['rails'])]))


lines += ['', '## 함급별 (모든 기준 함급·양 진영 합산)', '',
          '바뀐 함급은 **굵게** 표시했다. 받은 피해는 실드와 모듈 피해의 합이다. 명중률은 그 함선이 쏜 주포의 명중률이다.', '',
          '| 함급 | 척 | 격침률 전→후 | 평균 생존(초) 전→후 | 받은 피해 전→후 | 주포 명중률 전→후 |', '|---|---|---|---|---|---|']
for hull in sorted(pooled['base'], key=lambda h: (h not in changed, h)):
    b, a = row(pooled['base'][hull]), row(pooled['after'][hull])
    name = f'**{hull}**' if hull in changed else hull
    lines.append(f'| {name} | {b[0]} | {b[1]:.2f}→{a[1]:.2f} | {b[2]:.0f}→{a[2]:.0f} | {b[3]:.0f}→{a[3]:.0f} | {b[4]:.3f}→{a[4]:.3f} |')
lines += ['', '## 설계 계통별', '', '| 계통 | 척 | 격침률 전→후 | 평균 생존(초) 전→후 | 받은 피해 전→후 |', '|---|---|---|---|---|']
for d in sorted(design['base']):
    b, a = row(design['base'][d]), row(design['after'][d])
    lines.append(f'| {d} | {b[0]} | {b[1]:.2f}→{a[1]:.2f} | {b[2]:.0f}→{a[2]:.0f} | {b[3]:.0f}→{a[3]:.0f} |')
lines += ['', '## 전함 기능', '', '| 구분 | 척 | 주포 0발 | 최초 무력화까지(초, 무력화된 함만) | 격침률 | 평균 발사 |', '|---|---|---|---|---|---|']
for tag in ('base', 'after'):
    rs = bb[tag]; dis = [float(r['firstDisabled']) for r in rs if r['firstDisabled']]
    lines.append(f"| {'기준' if tag == 'base' else '변경'} | {len(rs)} | {sum(int(r['rails']) == 0 for r in rs)} | {mean(dis):.0f} ({len(dis)}척) | "
                 f"{mean([int(r['destroyed']) for r in rs]):.2f} | {mean([int(r['rails']) for r in rs]):.0f} |")
for hull in sorted({r['hullId'] for r in bb['base']}):
    for tag in ('base', 'after'):
        rs = [r for r in bb[tag] if r['hullId'] == hull]; dis = [float(r['firstDisabled']) for r in rs if r['firstDisabled']]
        lines.append(f"| {hull} {'기준' if tag == 'base' else '변경'} | {len(rs)} | {sum(int(r['rails']) == 0 for r in rs)} | {mean(dis):.0f} ({len(dis)}척) | "
                     f"{mean([int(r['destroyed']) for r in rs]):.2f} | {mean([int(r['rails']) for r in rs]):.0f} |")

# Paired per battle (same anchor/seed/mirror on both sides): ships of changed vs unchanged hulls as a control.
def by_battle(d):
    out = defaultdict(list)
    for anchor in anchors:
        for r in ships(d, anchor): out[(anchor, r['seed'], r['mirror'])].append(r)
    return out


B, A = by_battle(base), by_battle(after)
metrics = {'받은 피해': lambda r: float(r['shieldDamageReceived']) + float(r['moduleDamageReceived']),
           '격침 수': lambda r: int(r['destroyed']), '생존 합(초)': lambda r: float(r['survivalSeconds'])}
lines += ['', '## 판 단위 짝 비교 (95% 구간)', '', f'{len(B)}판. 같은 판에서 바뀐 함급과 안 바뀐 함급(대조군)의 합계가 얼마나 변했는지 본다.', '',
          '| 묶음 | 지표 | 판당 기준 | 판당 변화 | 비율 |', '|---|---|---|---|---|']
groups = [('바뀐 함급', lambda r: r['hullId'] in changed), ('안 바뀐 함급', lambda r: r['hullId'] not in changed),
          ('바뀐 지구 함급', lambda r: r['hullId'] in changed and r['design'] == 'Earth'),
          ('바뀐 화성 함급', lambda r: r['hullId'] in changed and r['design'] == 'Mars')]
for name, pred in groups:
    for metric, f in metrics.items():
        total = lambda rs: sum(f(r) for r in rs if pred(r))
        diff = [total(A[k]) - total(B[k]) for k in B]; ref = mean([total(B[k]) for k in B])
        half = 1.96 * statistics.stdev(diff) / len(diff) ** .5
        lines.append(f'| {name} | {metric} | {ref:.1f} | {mean(diff):+.1f} ± {half:.1f} | {mean(diff) / ref * 100:+.1f}% |')
out.write_text('\n'.join(lines) + '\n', encoding='utf-8')
print('\n'.join(lines))
