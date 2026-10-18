"""Projected hit areas (front / side / top) of the union of hull section boxes, before (git REV) and now.
python tools/section-areas.py REV OUT.json ID [ID ...]"""
import json, subprocess, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
from branch_model_layout import merge, resolve as current

ROOT = Path(__file__).resolve().parents[1]


def show(rev, path):
    return json.loads(subprocess.run(['git', 'show', f'{rev}:{path}'], cwd=ROOT, capture_output=True, check=True).stdout.decode('utf-8-sig'))


def at(rev, key):
    try: return show(rev, f'data/ships/{key}.json')
    except subprocess.CalledProcessError: pass
    v = show(rev, f'data/variants/{key}.json'); return merge(at(rev, v['baseHullId']), v['patch'])


def areas(d, cells=400):
    boxes = [(np.array(s['center']), np.array(s['halfSize'])) for s in d['hullSections']]
    lo = np.min([c - h for c, h in boxes], axis=0); hi = np.max([c + h for c, h in boxes], axis=0)
    out = {}
    for name, (a, b) in {'front': (0, 1), 'side': (2, 1), 'top': (0, 2)}.items():
        step = max(hi[a] - lo[a], hi[b] - lo[b]) / cells
        ga = np.arange(lo[a], hi[a], step) + step / 2; gb = np.arange(lo[b], hi[b], step) + step / 2
        A, B = np.meshgrid(ga, gb); mask = np.zeros(A.shape, bool)
        for c, h in boxes: mask |= (abs(A - c[a]) <= h[a]) & (abs(B - c[b]) <= h[b])
        out[name] = float(mask.sum() * step * step)
    return out


rev, target = sys.argv[1], sys.argv[2]; rows = []
for key in sys.argv[3:]:
    a, b = areas(at(rev, key)), areas(current(ROOT, key))
    rows.append(dict(id=key, before=a, after=b))
    print(f'{key:28s} ' + '  '.join(f'{v} {a[v]:9.0f}->{b[v]:9.0f} ({b[v] / a[v] * 100 - 100:+4.0f}%)' for v in ('front', 'side', 'top')))
Path(target).write_text(json.dumps(rows, indent=1) + '\n', encoding='utf-8')
