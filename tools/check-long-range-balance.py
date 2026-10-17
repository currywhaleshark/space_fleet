"""Paired long-range balance matrix; run each revision's frozen BattleBatch DLL.

No source edits or binary rebuilds occur here. Existing completed cases are reused.
"""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--dll', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--jobs', type=int, default=2)
    args = parser.parse_args()
    dll, folder = (ROOT / args.dll).resolve(), (ROOT / args.output).resolve()
    folder.mkdir(parents=True, exist_ok=True)
    common = ['--mirror', '--telemetry', '--jobs', str(args.jobs)]
    cases = []
    for name, blue, red, seeds in [('mixed','Earth','Mars','1,2,3,4'),
                                  ('swapped','Mars','Earth','1,2,3,4'),
                                  ('earth','Earth','Earth','1,2'), ('mars','Mars','Mars','1,2')]:
        wing = {'Earth':'earth_recon_t3', 'Mars':'mars_assault_t3'}
        cases.append((name, common + ['--seeds', seeds, '--blue-design', blue, '--red-design', red,
                                     '--blue-wing', wing[blue], '--red-wing', wing[red]]))
    for distance in [20000, 70000, 130000]:
        for blue, red in [('Earth','Mars'), ('Mars','Earth')]:
            cases.append((f'duel-{distance}-{blue}', common + ['--seeds','1,2','--duel','Battleship',
                          '--distance',str(distance),'--blue-design',blue,'--red-design',red]))
    for name, flags in cases:
        output = folder / f'{name}.csv'
        if output.exists() and output.with_suffix('.ships.csv').exists():
            print(f'reuse {name}', flush=True)
            continue
        command = ['dotnet', str(dll), *flags, '--output', str(output)]
        spec = {'command': command, 'binarySha256': hashlib.sha256(dll.read_bytes()).hexdigest()}
        output.with_suffix('.run.json').write_text(json.dumps(spec, indent=2), encoding='utf-8')
        print(f'run {name}', flush=True)
        with output.with_suffix('.log').open('w', encoding='utf-8') as log:
            subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
    print('complete: 24 fleet battles and 24 battleship duels', flush=True)

if __name__ == '__main__':
    main()
