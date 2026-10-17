"""Summarize completed paired battle CSVs, including approximate first-shot times.

Telemetry is sampled every five seconds; firing ranges below use exact launch logs.
"""
import csv
import gzip
import json
import statistics
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FOLDER = ROOT / 'docs/validation/long_range_balance_20261016'

def rows(path):
    if not path.exists() and path.with_suffix(path.suffix + '.gz').exists():
        path = path.with_suffix(path.suffix + '.gz')
    opener = gzip.open if path.suffix == '.gz' else open
    with opener(path, 'rt', encoding='utf-8-sig', newline='') as stream:
        return list(csv.DictReader(stream))

def average(values):
    return round(statistics.mean(values), 3) if values else None

def summarize(path):
    games = rows(path)
    ships = rows(path.with_suffix('.ships.csv'))
    telemetry = rows(path.with_suffix('.telemetry.csv'))
    first = {}
    for row in telemetry:
        key = (row['seed'], row['mirror'], row['callsign'])
        if row['kind'] == 'Battleship' and int(row['rails']) > 0:
            first.setdefault(key, float(row['time']))
    by_type = defaultdict(list)
    for ship in ships:
        by_type[ship['design'] + '/' + ship['kind']].append(ship)
    stats = {}
    for key, group in by_type.items():
        shots = sum(int(s['rails']) for s in group)
        hits = sum(int(s['railHits']) for s in group)
        ranges = sum(int(s['railRangeSamples']) for s in group)
        stats[key] = dict(ships=len(group), fired=sum(int(s['rails']) > 0 for s in group),
            destroyed=sum(int(s['destroyed']) for s in group), disabled=sum(int(s['disabled']) for s in group),
            rails=shots, hits=hits, hitPercent=round(hits / shots * 100, 2) if shots else None,
            meanSurvival=average([float(s['survivalSeconds']) for s in group]),
            meanFiringRangeKm=round(sum(float(s['railRangeSum']) for s in group)/ranges/1000, 3) if ranges else None,
            moduleDamage=round(sum(float(s['moduleDamageInflicted']) for s in group), 3),
            firstShotSample=average([first[(s['seed'],s['mirror'],s['callsign'])] for s in group
                if (s['seed'],s['mirror'],s['callsign']) in first]))
    design = {(s['seed'],s['mirror'],s['faction']):s['design'] for s in ships}
    winning_design = Counter(design[(g['seed'],g['mirror'],g['winner'])]
                             for g in games if g['winner'] in ('Blue','Red'))
    return dict(games=len(games), wins=dict(Counter(g['winner'] for g in games)),
                designWins=dict(winning_design), meanEnd=average([float(g['outcomeTime']) for g in games if g['outcomeTime']]),
                ships=stats)

def main():
    report = {}
    for revision in ['before','after']:
        report[revision] = {}
        for path in sorted((FOLDER/revision).glob('*.csv')):
            if len(path.name.split('.')) != 2:
                continue
            report[revision][path.stem] = summarize(path)
    (FOLDER/'summary.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    for name in sorted(report['after']):
        for revision in ['before','after']:
            row = report[revision].get(name)
            if row is None: continue
            bb = row['ships'].get('Earth/Battleship', {})
            print(revision, name, row['wins'], 'Earth BB fired', str(bb.get('fired'))+'/'+str(bb.get('ships')),
                  'rounds', bb.get('rails'), 'hits', bb.get('hits'), 'survival', bb.get('meanSurvival'))

if __name__ == '__main__':
    main()
