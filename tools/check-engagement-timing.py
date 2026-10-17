"""Audit the paired timing experiment, applied data, and preserved geometry."""
import collections
import copy
import csv
import hashlib
import json
import runpy
import statistics
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FOLDER = ROOT / 'docs/validation/engagement_timing_20261016'
helpers = runpy.run_path(str(ROOT / 'tools/analyze-wing-trials.py'))
read, combine, analyze = (helpers[k] for k in ('read', 'combine', 'analyze'))


def f32(value):
    return struct.unpack('f', struct.pack('f', value))[0]


def rows(data):
    analyze(data)  # Complete case matrix, no dropped events, every launch counted.
    result = []
    for distance in data['Distances']:
        for wings in data['WingPairs']:
            trials = [t for t in data['Trials'] if t['Case']['Distance'] == distance
                      and list(helpers['pair'](t)) == wings]
            sides = {}
            for design in ('Earth', 'Mars'):
                ships = [s for t in trials for s in t['Ships'] if s['Design'] == design]
                bbs = [s for s in ships if s['Kind'] == 'Battleship']
                ic = [s for s in ships if s['Kind'] == 'Interceptor']
                windows = [w for t in trials for w in t['LaunchWindows'] if w['Design'] == design]
                sides[design] = dict(
                    MedianFightingSeconds=statistics.median(s['FightingSeconds'] for s in bbs),
                    NeverFired=sum(s['Rails'] == 0 for s in bbs),
                    JointTorpedoes=sum(w['Count'] for w in windows
                                       if w['OwnBattleshipFighting'] and w['EnemyBattleshipFighting']),
                    JointBattles=sum(any(w['Design'] == design and w['OwnBattleshipFighting']
                        and w['EnemyBattleshipFighting'] for w in t['LaunchWindows']) for t in trials),
                    Torpedoes=sum(s['Torpedoes'] for s in ic), TorpedoHits=sum(s['TorpedoHits'] for s in ic),
                    Intercepts=sum(s['MissilesIntercepted'] for s in ic),
                    Destroyed=sum(s['Destroyed'] for s in bbs), Disabled=sum(s['Disabled'] for s in bbs))
            result.append(dict(Distance=distance, Wings=wings, Games=len(trials),
                Winners=dict(collections.Counter(t['Winner'] for t in trials)), Sides=sides))
    return result


def main():
    baseline = combine([FOLDER / 'window-baseline.json.gz', FOLDER / 'holdout-baseline.json.gz'])
    selected = combine([FOLDER / 'window-all3-structure3.json.gz', FOLDER / 'holdout-selected.json.gz'])
    final = read(FOLDER / 'final.json.gz')
    expected = {t['Case']['Id']: t for t in selected['Trials']}
    for trial in final['Trials']:
        a, b = dict(trial), dict(expected[trial['Case']['Id']])
        a.pop('WallSeconds'); b.pop('WallSeconds')
        assert a == b, trial['Case']['Id']
    replay = read(FOLDER / 'replay-baseline.json.gz')
    controls = {t['Case']['Id']: t for t in baseline['Trials']}
    for trial in replay['Trials']:
        a, b = dict(trial), dict(controls[trial['Case']['Id']])
        a.pop('WallSeconds'); b.pop('WallSeconds')
        assert a == b, ('Baseline replay', trial['Case']['Id'])
    (FOLDER / 'comparison.json').write_text(json.dumps(dict(Before=rows(baseline), After=rows(final),
        HoldoutBefore=rows(read(FOLDER / 'holdout-baseline.json.gz')),
        HoldoutAfter=rows(read(FOLDER / 'holdout-selected.json.gz'))), indent=2) + '\n', encoding='utf-8')

    originals = read(FOLDER / 'before-ships.json')
    for hull, before in originals.items():
        current = read(ROOT / f'data/ships/{hull}.json')
        unchanged = copy.deepcopy(current)
        if before['kind'] == 'Battleship':
            assert f32(current['shield']['capacity']) == f32(f32(before['shield']['capacity']) * 4)
            assert f32(current['structureHitPoints']) == f32(f32(before['structureHitPoints']) * 3)
            unchanged['shield']['capacity'] = before['shield']['capacity']
            unchanged['structureHitPoints'] = before['structureHitPoints']
            for old, new, remaining in zip(before['modules'], current['modules'], unchanged['modules']):
                assert f32(new['hitPoints']) == f32(f32(old['hitPoints']) * 3)
                remaining['hitPoints'] = old['hitPoints']
                if old['kind'] in ('Magazine', 'Reactor', 'Generator', 'PowerBus'):
                    assert f32(new['criticalEnergy']) == f32(f32(old.get('criticalEnergy', 300)) * 2)
                    remaining.pop('criticalEnergy')
                    if 'criticalEnergy' in old: remaining['criticalEnergy'] = old['criticalEnergy']
        elif before['kind'] == 'Interceptor':
            assert current['ai']['assaultReachMeters'] == (160000 if hull.startswith('mars') else 136000)
            unchanged['ai'].pop('assaultReachMeters')
        assert unchanged == before, ('Unexpected data/geometry/weapon change', hull)

    hashes = read(FOLDER / 'before-hashes.json')
    changed = [p for p, h in hashes.items() if hashlib.sha256((ROOT / p).read_bytes()).hexdigest() != h]
    models = [p for p in hashes if p.startswith(('art/', 'assets/', 'data/model_layouts/'))]
    assert not set(models).intersection(changed), 'Model/layout changed'
    runs = {p.name: len(read(p)['Trials']) for p in FOLDER.glob('*.json.gz')}
    csv_runs = {}
    for folder in ('fleet', 'specialist-pilot'):
        for path in (FOLDER / folder).glob('*.csv'):
            if len(path.name.split('.')) != 2: continue
            with path.open(encoding='utf-8-sig', newline='') as stream:
                csv_runs[str(path.relative_to(FOLDER))] = len(list(csv.DictReader(stream)))
    audit = dict(ExactAppliedReplay=len(final['Trials']), ExactBaselineReplay=len(replay['Trials']),
                 InputHashes=len(hashes), PreservedModels=len(models),
                 ChangedProductionFiles=changed, CompletedTrials=runs, CompletedCsvTrials=csv_runs,
                 TotalMeasuredBattles=sum(runs.values()) + sum(csv_runs.values()))
    (FOLDER / 'audit.json').write_text(json.dumps(audit, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(audit, indent=2))


if __name__ == '__main__':
    main()
