"""Read-only comparison of timing trials; tuned controls intentionally differ."""
import argparse
import collections
import json
import runpy
import statistics
from pathlib import Path

helpers = runpy.run_path(str(Path(__file__).with_name('analyze-wing-trials.py')))


def summarize(path):
    data = helpers['read'](path)
    result = helpers['analyze'](data)
    rows = []
    for row in result['Rows']:
        trials = [t for t in data['Trials'] if t['Case']['Distance'] == row['Distance']
                  and helpers['pair'](t) == (row['EarthInterceptors'], row['MarsInterceptors'])]
        sides = {}
        for design, stats in row['Sides'].items():
            bbs = [s for t in trials for s in t['Ships'] if s['Kind'] == 'Battleship' and s['Design'] == design]
            first_am = []
            for t in trials:
                calls = {s['Callsign'] for s in t['Ships'] if s['Design'] == design}
                launches = [f['LaunchTime'] for f in t['AntimatterFlights'] if f['Shooter'] in calls]
                if launches: first_am.append(min(launches))
            ic = stats['Roles'].get('Interceptor', {})
            sides[design] = dict(BattleshipDestructionCauses=dict(collections.Counter(s['Destruction'] for s in bbs)),
                BattleshipMedianDestruction=helpers['median'](s['DestroyedAt'] for s in bbs),
                BattleshipSurvivalCappedMean=statistics.mean(s['DestroyedAt'] if s['DestroyedAt'] is not None else t['Time']
                    for t in trials for s in t['Ships'] if s['Kind'] == 'Battleship' and s['Design'] == design),
                BattleshipActive=sum(not s['Destroyed'] and not s['Disabled'] for s in bbs),
                BattleshipNeverFired=sum(s['Rails'] == 0 for s in bbs),
                BattleshipRails=sum(s['Rails'] for s in bbs),
                MeanBattleshipActiveSeconds=statistics.mean(s['ActiveSeconds'] for s in bbs),
                MedianBattleshipFightingSeconds=helpers['median'](s.get('FightingSeconds') for s in bbs),
                MedianFirstRailFire=helpers['median'](s.get('FirstRailFire') for s in bbs),
                JointTorpedoes=sum(window['Count'] for t in trials for window in t.get('LaunchWindows', [])
                    if window['Design'] == design and window['OwnBattleshipFighting'] and window['EnemyBattleshipFighting']),
                OwnSupportedTorpedoes=sum(window['Count'] for t in trials for window in t.get('LaunchWindows', [])
                    if window['Design'] == design and window['OwnBattleshipFighting']),
                Torpedoes=ic.get('Torpedoes', 0), TorpedoHits=ic.get('TorpedoHits', 0),
                FirstAssault=ic.get('MedianFirstAssault'), FirstTorpedo=helpers['median'](first_am),
                EarlyTorpedoes=stats['CompletedTorpedoesLaunchedBeforeFirstBattleshipDestroyed'],
                LateOwnTorpedoes=stats['CompletedTorpedoesLaunchedAfterOwnBattleshipDestroyed'],
                InterceptorDestroyed=ic.get('DestroyedAtEnd', 0), Intercepts=ic.get('MissilesIntercepted', 0),
                TorpedoOutcomes=stats['TorpedoOutcomes'])
        rows.append(dict(Distance=row['Distance'], WingPair=[row['EarthInterceptors'], row['MarsInterceptors']],
            Winners=row['Winners'], MedianTime=row['MedianBattleSeconds'], Sides=sides))
    return dict(File=str(path), Tuning=data.get('Tuning'), Seeds=data['Seeds'], Battles=result['Battles'], Rows=rows)


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('inputs', type=Path, nargs='+')
    p.add_argument('--output', type=Path, required=True)
    args = p.parse_args()
    results = [summarize(path) for path in args.inputs]
    args.output.write_text(json.dumps(results, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    for r in results:
        print(Path(r['File']).name)
        for row in r['Rows']:
            if row['WingPair'] != [5, 5]: continue
            print(row['Distance'], row['Winners'], 'BB death median',
                  [s['BattleshipMedianDestruction'] for s in row['Sides'].values()],
                  'early AM', [s['EarlyTorpedoes'] for s in row['Sides'].values()])
