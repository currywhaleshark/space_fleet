"""Summarize paired interceptor-wing additions, with a legacy no-wing control."""
import argparse
import collections
import gzip
import json
import statistics
from pathlib import Path


def read(path):
    with (gzip.open(path, 'rt', encoding='utf-8') if str(path).endswith('.gz') else open(path, encoding='utf-8-sig')) as f:
        return json.load(f)


def median(values):
    values = [v for v in values if v is not None]
    return statistics.median(values) if values else None


def ratio(a, b):
    return a / b if b else None


def key(t):
    c = t['Case']
    return c['Seed'], c['Distance'], c['EarthBlue'], c['Mirror']


def pair(t):
    c = t['Case']
    return c['EarthInterceptors'], c['MarsInterceptors']


def combine(paths):
    documents = [read(p) for p in paths]
    first = documents[0]
    for data in documents[1:]:
        for field in ('Schema', 'Minutes', 'Distances', 'BaseComposition', 'WingHulls', 'WingPairs', 'Tuning', 'DefinitionBaseline'):
            assert data.get(field) == first.get(field), ('Different protocols', field)
    seeds = [seed for data in documents for seed in data['Seeds']]
    assert len(seeds) == len(set(seeds)), 'Overlapping seed chunks'
    return dict(first, Seeds=seeds, Trials=[t for data in documents for t in data['Trials']])


def analyze(data, baseline=None):
    trials = data['Trials']
    expected = {(s, d, b, m, tuple(p)) for s in data['Seeds'] for d in data['Distances']
                for b in (False, True) for m in (False, True) for p in data['WingPairs']}
    actual = {(*key(t), pair(t)) for t in trials}
    assert len(trials) == len(actual) and actual == expected, 'Incomplete/duplicate matrix'
    assert all(t['DroppedEvents'] == 0 for t in trials), 'Truncated events'
    for t in trials:
        assert len(t['Ships']) == 6 + sum(pair(t))
        if 'LaunchWindows' in t:
            for design in ('Earth', 'Mars'):
                assert sum(w['Count'] for w in t['LaunchWindows'] if w['Design'] == design) == sum(
                    s['Torpedoes'] for s in t['Ships'] if s['Design'] == design), 'Missing launch window'
        for ship in t['Ships']:
            assert ship['TorpedoHits'] <= ship['Torpedoes']
            assert ship['ActiveSeconds'] <= t['Time'] + .001

    # The added optional wing fixture and read-only counters must reproduce the
    # preceding production-tuning run when both wing counts are zero.
    old = {} if baseline is None else {key(t): t for t in baseline['Trials'] if t['Case']['EarthEscorts'] == t['Case']['MarsEscorts'] == 2}
    common = ('Hull', 'Design', 'Kind', 'Team', 'ActiveSeconds', 'Rails', 'RailHits', 'Missiles', 'MissileHits',
              'MissilesIntercepted', 'TorpedoesIntercepted', 'DronesDestroyed', 'ShieldDamageInflicted',
              'ModuleDamageInflicted', 'ShieldDamage', 'ModuleDamage', 'DisabledAt', 'DestroyedAt',
              'Destroyed', 'Disabled', 'StructureFraction', 'MeanFireRange')
    controls = [t for t in trials if pair(t) == (0, 0)]
    for t in controls if baseline is not None else []:
        previous = old[key(t)]
        for f in ('Winner', 'WinnerTeam', 'Reason', 'Time', 'FriendlyCollisions'):
            assert t[f] == previous[f], (t['Case']['Id'], f)
        by_call = {s['Callsign']: s for s in previous['Ships']}
        for ship in t['Ships']:
            for f in common:
                assert ship[f] == by_call[ship['Callsign']][f], (t['Case']['Id'], ship['Callsign'], f)
        legacy_events = []
        for event in previous['Events']:
            event = dict(event)
            for f in ('Victim', 'Attacker'):
                if isinstance(event[f], int): event[f] = ('Blue', 'Red')[event[f]]
            if isinstance(event['Kind'], int):
                event['Kind'] = ('ModuleDestroyed', 'Disabled', 'Destroyed', 'Collision')[event['Kind']]
            legacy_events.append(event)
        assert t['Events'] == legacy_events, (t['Case']['Id'], 'events')

    groups = collections.defaultdict(list)
    for t in trials:
        groups[(t['Case']['Distance'], *pair(t))].append(t)
    rows = []
    control_map = {key(t): t for t in controls}
    for (distance, earth, mars), battles in sorted(groups.items()):
        sides = {}
        for design in ('Earth', 'Mars'):
            ships = [s for t in battles for s in t['Ships'] if s['Design'] == design]
            total_damage = sum(s['ModuleDamageInflicted'] for s in ships)
            roles = {}
            for kind in ('Battleship', 'Escort', 'Interceptor'):
                units = [s for s in ships if s['Kind'] == kind]
                if not units: continue
                totals = {f: sum(s[f] for s in units) for f in ('Rails', 'RailHits', 'Missiles', 'MissileHits',
                    'Torpedoes', 'TorpedoHits', 'MissilesIntercepted', 'TorpedoesIntercepted', 'CounterMissileKills',
                    'DronesDestroyed', 'ShieldDamageInflicted', 'ModuleDamageInflicted', 'ProbesLaunched',
                    'ProbeContacts', 'ProbeIdentifications', 'ProbeKills', 'AmFailures', 'AmJettisons')}
                roles[kind] = dict(Units=len(units), **totals,
                    RailHitRate=ratio(totals['RailHits'], totals['Rails']),
                    TorpedoHitRate=ratio(totals['TorpedoHits'], totals['Torpedoes']),
                    ModuleDamageShare=ratio(totals['ModuleDamageInflicted'], total_damage),
                    ActiveAtEnd=sum(not s['Disabled'] and not s['Destroyed'] for s in units),
                    DestroyedAtEnd=sum(s['Destroyed'] for s in units),
                    EverDisabled=sum(s['DisabledAt'] is not None for s in units),
                    NeverFired=sum(s['Rails'] == 0 for s in units),
                    FirstActionUnits=sum(s['FirstAction'] is not None for s in units),
                    FirstAssaultUnits=sum(s['FirstAssault'] is not None for s in units),
                    MedianFirstAction=median(s['FirstAction'] for s in units),
                    MedianFirstAssault=median(s['FirstAssault'] for s in units),
                    MedianFirstDisable=median(s['DisabledAt'] for s in units),
                    MedianDestroyedAt=median(s['DestroyedAt'] for s in units),
                    MedianClosestCapitalMeters=median(s['ClosestCapitalMeters'] for s in units),
                    MeanActiveSeconds=statistics.mean(s['ActiveSeconds'] for s in units))
            mission_seconds = collections.Counter()
            flights = []
            probe_flights = []
            am_battles = probe_battles = 0
            early_am = late_own_am = 0
            for t in battles:
                units = [s for s in t['Ships'] if s['Design'] == design]
                names = {s['Callsign'] for s in units}
                own_flights = [f for f in t['AntimatterFlights'] if f['Shooter'] in names]
                flights.extend(own_flights)
                first_bb_loss = min((s['DestroyedAt'] for s in t['Ships'] if s['Kind'] == 'Battleship'
                                     and s['DestroyedAt'] is not None), default=float('inf'))
                own_bb_loss = min((s['DestroyedAt'] for s in units if s['Kind'] == 'Battleship'
                                  and s['DestroyedAt'] is not None), default=float('inf'))
                early_am += sum(f['LaunchTime'] < first_bb_loss for f in own_flights)
                late_own_am += sum(f['LaunchTime'] > own_bb_loss for f in own_flights)
                probe_flights.extend(f for f in t['ProbeFlights'] if f['Owner'] in names)
                am_battles += any(s['Torpedoes'] for s in units)
                probe_battles += any(s['ProbesLaunched'] for s in units)
                for wing in t['Wings']:
                    if wing['Design'] == design: mission_seconds.update(wing['MissionSeconds'])
            enemy_missiles = sum(s['Missiles'] for t in battles for s in t['Ships'] if s['Design'] != design)
            sides[design] = dict(Roles=roles, MissionSeconds=dict(mission_seconds),
                TorpedoBattles=am_battles, ProbeBattles=probe_battles,
                TorpedoOutcomes=dict(collections.Counter(f['Outcome'] for f in flights)),
                TorpedoTargets=dict(collections.Counter(f['TargetKind'] for f in flights)),
                CompletedTorpedoesLaunchedBeforeFirstBattleshipDestroyed=early_am,
                CompletedTorpedoesLaunchedAfterOwnBattleshipDestroyed=late_own_am,
                MedianTorpedoRange=median(f['Range'] for f in flights),
                CompletedProbeFlights=len(probe_flights), ProbesShotDown=sum(f['Destroyed'] for f in probe_flights),
                MedianCompletedProbeLifetime=median(f['Lifetime'] for f in probe_flights),
                EnemyMissilesLaunched=enemy_missiles,
                FleetMissileInterceptionRate=ratio(sum(s['MissilesIntercepted'] for s in ships), enemy_missiles))
        rows.append(dict(Distance=distance, EarthInterceptors=earth, MarsInterceptors=mars, Battles=len(battles),
            Winners=dict(collections.Counter(t['Winner'] for t in battles)),
            TeamWins={str(earth_blue): dict(collections.Counter(t['Winner'] for t in battles if t['Case']['EarthBlue'] == earth_blue))
                      for earth_blue in (True, False)},
            Reasons=dict(collections.Counter(t['Reason'] for t in battles)),
            MedianBattleSeconds=median(t['Time'] for t in battles),
            FirstBattleshipDestroyed=dict(collections.Counter(
                min((s for s in t['Ships'] if s['Kind'] == 'Battleship' and s['DestroyedAt'] is not None),
                    key=lambda s: s['DestroyedAt'], default={'Design': 'Neither'})['Design'] for t in battles)),
            PairedOutcomeChanges=dict(collections.Counter(control_map[key(t)]['Winner'] + '->' + t['Winner'] for t in battles)),
            Sides=sides))
    return dict(Battles=len(trials), BaselineControlsMatched=len(controls) if baseline is not None else 0, Rows=rows,
        Notes=['FirstAction includes attaining a mission station; it is not a damage event.',
               'ProbeContacts/ProbeIdentifications count new probe contributions in SensorNet, not necessarily previously unknown fleet contacts.',
               'ActiveSeconds and survival are censored at battle end; mission time is per living wing, not per ship.',
               'Torpedo outcomes cover completed flights; rounds still flying at verdict remain in launch denominators.',
               'Adding ships changes initial strength and annihilation thresholds; wins alone do not measure damage synergy.',
               'Team and spatial mirrors are paired variants of each seed, not independent random seeds.'])


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('input', type=Path, nargs='+')
    parser.add_argument('--baseline', type=Path, default=Path('docs/validation/gunnery_tuning_20261016/final.json.gz'))
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = analyze(combine(args.input), read(args.baseline))
    result['Inputs'] = [str(p) for p in args.input]
    args.output.write_text(json.dumps(result, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    print(f"PASS: {result['Battles']} battles, {result['BaselineControlsMatched']} baseline controls identical")
    for row in result['Rows']:
        print(row['Distance'], (row['EarthInterceptors'], row['MarsInterceptors']), row['Winners'],
              'median seconds', round(row['MedianBattleSeconds'], 1))
