import csv
import hashlib
import json
import re
import shutil
from pathlib import Path

root = Path(__file__).resolve().parents[2]
source = root / 'shots/perf-stage2-20261010'
dest = root / 'docs/balance/perf_stage2_20261010'
baseline_dir = root / 'docs/balance/perf_stage1_20261010'
dest.mkdir(parents=True, exist_ok=True)

def read_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def copy(path, name=None):
    shutil.copy2(path, dest / (name or path.name))

baseline = {(s['Seed'], s['Mirror']): s['Summary'] for s in read_json(baseline_dir/'baseline.summaries.json')}

def game(label):
    path = source / f'game-{label}.log'
    log = path.read_text(encoding='utf-8-sig')
    assert 'Exception' not in log
    stages = {m[0]: dict(zip(('sim_seconds', 'min_fps', 'mean_fps', 'engine_fps'), map(float, m[1:])))
              for m in re.findall(r'autoplay (start|10min|outcome): sim=([\d.]+)s FPS min=([\d.]+) mean=([\d.]+) engine=([\d.]+)', log)}
    assert set(stages) == {'start', '10min', 'outcome'}
    frames = [(float(t), float(fps), dict((k, float(v)) for k,v in re.findall(r'([\w+]+)=(\d+(?:\.\d+)?)/', values)))
              for t,fps,values in re.findall(r'perf t=([\d.]+)s fps=([\d.]+) frame=\d+ms \| ([^|]+)', log)]
    samples = []
    for t, ticks, values in re.findall(r'sim-profile t=([\d.]+)s ticks=(\d+) us/tick \| (.*)', log):
        end, ticks = float(t), int(ticks)
        data = {key: float(value) for key,value in re.findall(r'(\w+)=([\d.]+)', values)}
        if end <= stages['outcome']['sim_seconds']:
            samples.append((end-ticks/60, end, ticks, data))
    def average(start, end):
        selected = [s for s in samples if s[0] >= start and s[1] <= end]
        ticks = sum(s[2] for s in selected)
        assert ticks > 0
        return {'ticks': ticks, 'windows': len(selected), 'from': selected[0][0], 'to': selected[-1][1],
                'us_per_tick': {key: round(sum(s[3][key]*s[2] for s in selected)/ticks, 4) for key in selected[0][3]}}
    bands = {'All': average(0, float('inf')), 'time:0-300': average(0, 300),
             'time:300-540': average(300, 540), 'time:540+': average(540, float('inf'))}
    nearest = {}
    for stage in ('10min', 'outcome'):
        frame = min(frames, key=lambda x: abs(x[0]-stages[stage]['sim_seconds']))
        nearest[stage] = {'sim_seconds': frame[0], 'fps': frame[1], 'ms_per_frame': frame[2]}
    summary = re.search(r'^autoplay summary: (.*)$', log, re.M).group(1)
    copy(path)
    return {'stages': stages, 'engine_errors': re.findall(r'^ERROR: .*', log, re.M),
            'summary': summary, 'matches_headless_seed1': summary == baseline[(1, False)],
            'nearest_frame_report': nearest, 'section_windows': bands}

games = {name: game(name) for name in ('debug', 'release', 'debug-headless')}
assert games['debug']['summary'] == games['debug-headless']['summary']
(dest / 'game-analysis.json').write_text(json.dumps(games, indent=2)+'\n', encoding='utf-8')
comparisons = {}
for label in ('pd', 'pd-projectiles', 'ship-state', 'collision', 'profile-release', 'final-release', 'profile-debug'):
    current = {(s['Seed'], s['Mirror']): s['Summary'] for s in read_json(source/f'{label}.summaries.json')}
    assert current == ({(1, False): baseline[(1, False)]} if label == 'profile-debug' else baseline)
    hashes = {}
    for suffix in ('ships.csv', 'am.csv'):
        actual = (source/f'{label}.{suffix}').read_bytes()
        if label != 'profile-debug':
            assert actual == (baseline_dir/f'baseline.{suffix}').read_bytes()
        hashes[suffix] = hashlib.sha256(actual).hexdigest()
    comparisons[label] = {'summaries': f'PASS {len(current)}/{len(current)} ordinal strings and seed/mirror coverage',
                          'sha256': hashes, 'final_state_csvs_equal_baseline': label != 'profile-debug'}
    for suffix in ('csv', 'log', 'summaries.json', 'sections.csv', 'analysis.json', 'ticks.csv'):
        path = source/f'{label}.{suffix}'
        if path.exists(): copy(path)
    if label in ('final-release', 'profile-debug'):
        for suffix in ('ships.csv', 'am.csv'): copy(source/f'{label}.{suffix}')

for label in ('collision-probe','pd-projectile-probe'):
    for suffix in ('csv','log','sections.csv','summaries.json'): copy(source/f'{label}.{suffix}')

api_summary = read_json(source/'debug-api.summaries.json')[0]['Summary']
assert api_summary == baseline[(1, False)]
for suffix in ('csv','log','sections.csv','summaries.json','ships.csv','am.csv','ticks.csv','analysis.json'):
    copy(source/f'debug-api.{suffix}')
copy(source/'loaded-godotsharp.json')
math_paths = {
    'game_loaded': Path(read_json(source/'loaded-godotsharp.json')['module']),
    'original_debug_batch': root/'tools/BattleBatch/bin/Debug/net8.0/GodotSharp.dll',
    'debug_api_probe': source/'debug-api-probe/GodotSharp.dll',
}
math_hashes = {k: hashlib.sha256(p.read_bytes()).hexdigest() for k,p in math_paths.items()}
assert math_hashes['game_loaded'] == math_hashes['debug_api_probe'] != math_hashes['original_debug_batch']
assert (root/'tools/BattleBatch/bin/Debug/net8.0/BattleBatch.dll').read_bytes() == (source/'debug-api-probe/BattleBatch.dll').read_bytes()

export_name = 'SpaceFleet-20261010-optimized-Windows-x64'
export_logs = root / 'shots' / export_name
passes = {}
for test in ('flow-test', 'model-test', 'combat-visual-test', 'audio-test', 'fleet-audio-test'):
    log = (export_logs/f'{test}.log').read_text(encoding='utf-8-sig')
    assert 'ERROR:' not in log and 'Exception' not in log and 'PASS:' in log
    passes[test] = re.findall(r'PASS: .*', log)
    copy(export_logs/f'{test}.log', f'export-{test}.log')
checks = (source/'sim-checks.log').read_text(encoding='utf-8-sig')
sim_count = sum(map(int, re.findall(r'^PASS: (\d+)', checks, re.M)))
assert sim_count == 7023
for name in ('sim-checks.log','game-build.log','run-game-perf.ps1','package-evidence.py'): copy(source/name)
copy(root/'builds'/export_name/'build-info.json', 'export-build-info.json')
copy(baseline_dir/'environment.json', 'machine-environment.json')
manifest = {
    'base_commit': 'd5824a2760a51b6e8528906e0d1dc22c82526fa3',
    'stage': '2 - conservative candidate filtering and repeated read reduction; no balance or physics rate changes',
    'baseline_artifacts': '../perf_stage1_20261010',
    'batches': {'pd/pd-projectiles/ship-state/collision': {'configuration': 'Release', 'seeds': '1-10 + mirror', 'jobs': 6, 'profiling': False},
                'profile-release/final-release': {'configuration': 'Release', 'seeds': '1-10 + mirror', 'jobs': 1, 'profiling': 'sections + outer tick'},
                'profile-debug': {'configuration': 'Debug', 'seeds': '1', 'jobs': 1, 'profiling': 'sections + outer tick'}},
    'game_arguments': '--autoplay --seed=1 --perf-trace --perf-seconds=600 --autoplay-quit --post-seconds=0',
    'game_export': f'builds/{export_name}/SpaceFleet.exe',
    'game_window_aggregation': 'tick-weighted; only complete windows within each time band; first frame and last partial window excluded',
    'comparisons': comparisons, 'sim_checks': sim_count, 'export_checks': passes,
    'diagnostics': {'godotsharp_sha256': math_hashes,
                    'debug_api_probe': 'same Debug BattleBatch executable and seed; only GodotSharp.dll swapped; exact baseline summary retained',
                    'debug_headless_game': '--headless --fixed-fps 60 added; same summary as rendered Debug game; throughput is not display FPS',
                    'game_snapshot_summary': 'recorded on a render frame, after the outcome step; may include extra combat events and is not used for exact batch acceptance'},
    'limitations': ['one machine; CPU clocks, OS load and JIT not controlled',
                    'first final-code run per-battle brawl p99 maximum was 0.646 ms; retained separately',
                    'Debug game vs canonical batch battle-state divergence remains unresolved; GPU removal and math-library swap did not explain it',
                    'exact profiler overhead and human-play stutter have not been evaluated'],
}
source_files = list((root/'src/Sim').glob('*.cs')) + list((root/'data/ships').glob('*.json'))
source_files += [root/p for p in ('src/Game/BattlePerformance.cs','src/Game/PerfTrace.cs','src/Game/ScaleTest.cs',
                                'src/Game/ShipModelChecks.cs','tools/BattleBatch/Program.cs','tools/BattleBatch/SectionProfile.cs')]
manifest['source_sha256'] = {p.relative_to(root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(source_files)}
manifest['file_sha256'] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(dest.iterdir()) if p.is_file() and p.name != 'manifest.json'}
(dest/'manifest.json').write_text(json.dumps(manifest, indent=2)+'\n', encoding='utf-8')
print(json.dumps(games, indent=2))
print('PASS: preserved evidence, comparison hashes and required game milestones')
