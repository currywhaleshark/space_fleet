"""Re-apply edited geometry contracts to existing authored variants (stats, armor and research untouched).
python tools/apply-model-layouts.py earth_gun_cruiser_t5 interceptor_t5
Only hull sections, module/bay centers, sockets and model ID change; section armor keeps its authored value.
"""
import copy, json, sys
from pathlib import Path
from branch_model_layout import resolve, apply_layout
import reference_layout

ROOT = Path(__file__).resolve().parents[1]


def diff(a, b):
    r = {k: None for k in a if k not in b}
    for k, v in b.items():
        if k not in a: r[k] = v
        elif isinstance(v, dict) and isinstance(a[k], dict):
            part = diff(a[k], v)
            if part: r[k] = part
        elif v != a[k]: r[k] = v
    return r


def expand(key):
    """Regenerate hull sections from blockout.pieces and check every module/bay fits inside one section."""
    path = ROOT / f'data/model_layouts/{key}.json'
    layout = json.loads(path.read_text(encoding='utf-8'))
    if 'pieces' not in layout.get('blockout', {}): return
    layout['sections'] = reference_layout.sections(layout)
    ids = [s['id'] for s in layout['sections']]
    assert len(ids) == len(set(ids)), (key, 'duplicate section ids')
    path.write_text(json.dumps(layout, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def check(key, d):
    bad = [m['id'] for m in d['modules'] + d['bays'] if not any(reference_layout.contains(s, m['center'], m['halfSize']) for s in d['hullSections'])]
    assert not bad, (key, 'outside every section', bad)


def apply(key):
    expand(key)
    path = ROOT / f'data/variants/{key}.json'
    variant = json.loads(path.read_text(encoding='utf-8'))
    base = resolve(ROOT, variant['baseHullId'])
    d = resolve(ROOT, key)
    previous = {s['id']: s for s in d['hullSections']}
    # Sections removed by an earlier layout are still valid armor sources.
    for s in base['hullSections']:
        if s['id'] not in previous: d['hullSections'].append(copy.deepcopy(s))
    result = apply_layout(ROOT, d)
    for s in result['hullSections']:
        for field in ('armor', 'faces'):
            if field in previous.get(s['id'], {}): s[field] = copy.deepcopy(previous[s['id']][field])
    check(key, result)
    variant['patch'] = diff(base, result)
    path.write_text(json.dumps(variant, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('Applied', key, len(result['hullSections']), 'sections')


if __name__ == '__main__':
    for key in sys.argv[1:]: apply(key)
