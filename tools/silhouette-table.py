"""Compact per-station reference vs model envelope from silhouette-match span tables.
python tools/silhouette-table.py RUN_DIR ID [ID ...]
top: half-width (symmetrised); side: [bottom, top]. Every other station of the 40-row tables."""
import re, sys
from pathlib import Path

D = Path(sys.argv[1])
num = r'(-?\d+\.\d)'


def spans(text):
    return [(float(a), float(b)) for a, b in re.findall(r'\[' + num + ',' + num + r'\]', text)]


for key in sys.argv[2:]:
    views = {v: (D / f'{key}-{v}.txt').read_text(encoding='utf-8').splitlines() for v in ('top', 'side') if (D / f'{key}-{v}.txt').exists()}
    print(f'== {key}   z | top ref/model half-width | side ref/model [bottom,top]')
    rows = zip(*views.values())
    for i, row in enumerate(rows):
        if i % 2: continue
        out = []
        for v, line in zip(views, row):
            z, ref, mod = line.split('|'); ref = spans(ref); mod = spans(mod.replace('model', ''))
            ext = lambda s: (min(a for a, _ in s), max(b for _, b in s)) if s else (0, 0)
            r, m = ext(ref), ext(mod)
            out.append(f'{(r[1]-r[0])/2:6.1f}/{(m[1]-m[0])/2:<6.1f}' if v == 'top' else f'[{r[0]:6.1f},{r[1]:6.1f}]/[{m[0]:6.1f},{m[1]:6.1f}]')
        print(f'{float(z[1:]):8.1f} | ' + ' | '.join(out))
