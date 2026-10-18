"""Summarise BattleBatch runs per opening (Deployment): pace, losses and battleship function.
python tools/compare-deployments.py OUT.md LABEL=DIR/PREFIX [LABEL=DIR/PREFIX ...]
Each PREFIX matches <PREFIX>-<anchor>.ships.csv and .losses.json written by --tier-hull runs."""
import csv, json, statistics, sys
from pathlib import Path


def mean(xs): return statistics.fmean(xs) if xs else float('nan')


lines = ['# 시작 진형별 전투 요약 (자동 생성)', '',
         '| 진형 | 판 | 평균 종료(초) | 판당 격침 | 전함 주포 0발 | 전함 평균 발사 | 전함 격침률 | 전함 최초 무력화(초) | 요격함 첫 행동(초) | 첫 격침(초) |',
         '|---|---|---|---|---|---|---|---|---|---|']
for arg in sys.argv[2:]:
    label, prefix = arg.split('=', 1); prefix = Path(prefix)
    ships, battles = [], []
    for p in sorted(prefix.parent.glob(prefix.name + '-*.ships.csv')):
        ships += [dict(r, file=p.name) for r in csv.DictReader(open(p, encoding='utf-8'))]
        battles += json.loads(Path(str(p).replace('.ships.csv', '.losses.json')).read_text(encoding='utf-8'))
    bb = [r for r in ships if r['kind'] == 'Battleship']
    wings = [float(r['firstAction']) for r in ships if r['kind'] == 'Interceptor' and r['firstAction']]
    first_kill = {}
    for r in ships:
        if r['firstDestroyed']:
            k = (r['file'], r['seed'], r['mirror']); first_kill[k] = min(first_kill.get(k, 1e9), float(r['firstDestroyed']))
    disabled = [float(r['firstDisabled']) for r in bb if r['firstDisabled']]
    lines.append(f"| {label} | {len(battles)} | {mean([b['Outcome']['Time'] for b in battles if b['Outcome']]):.0f} | "
                 f"{mean([b['Destroyed'] for b in battles]):.1f} | {sum(int(r['rails']) == 0 for r in bb)}/{len(bb)} | "
                 f"{mean([int(r['rails']) for r in bb]):.0f} | {mean([int(r['destroyed']) for r in bb]):.2f} | "
                 f"{mean(disabled):.0f} ({len(disabled)}척) | {mean(wings):.0f} | {mean(list(first_kill.values())):.0f} |")
Path(sys.argv[1]).write_text('\n'.join(lines) + '\n', encoding='utf-8')
print('\n'.join(lines))
