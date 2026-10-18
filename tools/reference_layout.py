"""Reference-matched layout pieces -> concrete mirrored pieces and hit sections (no Blender dependency).

A layout's blockout.pieces describe the visible primary forms. A piece carrying "section" also becomes a hull
damage section equal to its bounding box, so silhouette and hit volume stay aligned by construction.
Piece kinds (game axes: +X right, +Y up, -Z forward, metres):
  loft  rings [[z, halfWidth, halfHeight, yCentre], ...] with optional x offset  (chamfered octagon)
  box   center, size
  prism points [[x, z], ...] at y with thickness (flat plate: wings, decks)
  fin   root [[x, y, z] x2] / tip [[x, y, z] x2] quad with thickness
  truss center, size  (open rod frame; its section is the frame's box)
  plate points [[x, y, z], ...] planar polygon with thickness (sloped wings, anhedral panels)
"mirror": true creates a port (-X) and starboard (+X) copy; ids/sections get "-port" / "-starboard" (or the given
"suffix" pair) appended and the authored coordinates are the starboard (+X) side.
"""
import copy


def mirrored(piece):
    if not piece.get('mirror'):
        return [piece]
    out = []
    names = piece.get('suffix', ['port', 'starboard'])
    for sign, suffix in ((-1, names[0]), (1, names[1])):
        p = copy.deepcopy(piece); p.pop('mirror', None); p.pop('suffix', None); p['side'] = sign
        for key in ('name', 'section'):
            if key in p: p[key] = f"{p[key]}-{suffix}" if key == 'section' else f"{p[key]} {suffix}"
        k = p['kind']
        if k == 'loft': p['x'] = sign * abs(p.get('x', 0))
        elif k in ('box', 'truss'): p['center'][0] = sign * abs(p['center'][0])
        elif k == 'prism': p['points'] = [[sign * abs(x), z] for x, z in p['points']]
        elif k == 'fin':
            for key in ('root', 'tip'): p[key] = [[sign * abs(v[0]), v[1], v[2]] for v in p[key]]
        elif k == 'plate': p['points'] = [[sign * abs(v[0]), v[1], v[2]] for v in p['points']]
        out.append(p)
    return out


def pieces(layout):
    return [q for p in layout.get('blockout', {}).get('pieces', []) for q in mirrored(p)]


def bounds(p):
    k = p['kind']
    if k == 'loft':
        x = p.get('x', 0); r = p['rings']
        lo = [x - max(v[1] for v in r), min(v[3] - v[2] for v in r), min(v[0] for v in r)]
        hi = [x + max(v[1] for v in r), max(v[3] + v[2] for v in r), max(v[0] for v in r)]
    elif k in ('box', 'truss'):
        lo = [c - s / 2 for c, s in zip(p['center'], p['size'])]; hi = [c + s / 2 for c, s in zip(p['center'], p['size'])]
    elif k == 'prism':
        t = p.get('thickness', .5) / 2; y = p.get('y', 0)
        lo = [min(x for x, _ in p['points']), y - t, min(z for _, z in p['points'])]
        hi = [max(x for x, _ in p['points']), y + t, max(z for _, z in p['points'])]
    elif k == 'fin':
        vs = p['root'] + p['tip']; t = p.get('thickness', .24) / 2
        lo = [min(v[0] for v in vs) - t, min(v[1] for v in vs), min(v[2] for v in vs)]
        hi = [max(v[0] for v in vs) + t, max(v[1] for v in vs), max(v[2] for v in vs)]
    elif k == 'plate':
        vs = p['points']; t = p.get('thickness', .3) / 2
        lo = [min(v[a] for v in vs) - t for a in range(3)]; hi = [max(v[a] for v in vs) + t for a in range(3)]
    else:
        raise ValueError(k)
    return lo, hi


def sections(layout):
    """blockout.fixedSections plus one AABB section per piece that names one."""
    out = copy.deepcopy(layout.get('blockout', {}).get('fixedSections', []))
    for p in pieces(layout):
        if 'section' not in p: continue
        lo, hi = bounds(p)
        out.append(dict(id=p['section'], name=p.get('name', p['section']),
                        center=[round((a + b) / 2, 4) for a, b in zip(lo, hi)],
                        halfSize=[round((b - a) / 2, 4) for a, b in zip(lo, hi)],
                        armorFrom=p.get('armorFrom', 'hull')))
    return out


def contains(section, center, half, eps=.001):
    return all(abs(center[a] - section['center'][a]) + half[a] <= section['halfSize'][a] + eps for a in range(3))
