"""Compare concept-board silhouettes with the model's projected silhouettes.
python tools/silhouette-match.py SIL_DIR OUT_DIR [ID ...]
Both silhouettes are scaled to the same length (bow at the left), aligned on the best vertical offset, and
compared by IoU. Per-station width/height (in % of length) shows where the hull is too wide or too narrow.
"""
import json, sys
from pathlib import Path
import numpy as np
from scipy import ndimage
from PIL import Image, ImageDraw, ImageFont
sys.path.insert(0, str(Path(__file__).resolve().parent))
from reference_views import silhouette

ROOT = Path(__file__).resolve().parents[1]
SIL = Path(sys.argv[1]); OUT = Path(sys.argv[2]); OUT.mkdir(parents=True, exist_ok=True)
VIEWS = {k: v for k, v in json.loads((ROOT / 'tools/reference_views.json').read_text(encoding='utf-8')).items() if not k.startswith('_')}
KEYS = sys.argv[3:] or list(VIEWS)
LENGTH = 600; STATIONS = 20
font = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 18)


def crop(mask):
    ys, xs = np.nonzero(mask); return mask[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


def scaled(mask):
    h, w = mask.shape; height = max(1, round(h * LENGTH / w))
    return np.asarray(Image.fromarray(mask.astype(np.uint8) * 255).resize((LENGTH, height), Image.BILINEAR)) > 127


def reference(view):
    rgb = np.asarray(Image.open(ROOT / view['board']).convert('RGB'))
    x0, y0, x1, y1 = view['box']; mask = silhouette(rgb[y0:y1, x0:x1])
    turn = {'left': 0, 'up': 1, 'down': -1, 'right': 2}[view['nose']]
    return scaled(crop(np.rot90(mask, turn)))


def model(tris):
    lo = tris.reshape(-1, 2).min(axis=0); hi = tris.reshape(-1, 2).max(axis=0); scale = LENGTH / (hi[0] - lo[0])
    size = (LENGTH + 1, int((hi[1] - lo[1]) * scale) + 1)
    image = Image.new('L', size, 0); d = ImageDraw.Draw(image)
    for t in tris: d.polygon([tuple(((p - lo) * scale)) for p in t], fill=255)
    return crop(ndimage.binary_fill_holes(np.asarray(image) > 127))  # outlines, as for the reference mask


def best_overlap(ref, mod):
    """Place both on one canvas and slide the model vertically for maximum IoU."""
    H = max(ref.shape[0], mod.shape[0]) * 2; W = max(ref.shape[1], mod.shape[1])
    canvas_r = np.zeros((H, W), bool); r0 = (H - ref.shape[0]) // 2; canvas_r[r0:r0 + ref.shape[0], :ref.shape[1]] = ref
    best = (-1, None)
    for top in range(0, H - mod.shape[0] + 1, 2):
        canvas_m = np.zeros((H, W), bool); canvas_m[top:top + mod.shape[0], :mod.shape[1]] = mod
        iou = (canvas_r & canvas_m).sum() / max(1, (canvas_r | canvas_m).sum())
        if iou > best[0]: best = (iou, canvas_m, top)
    return best[0], canvas_r, best[1], best[2]


def runs(column):
    """Contiguous filled pixel runs of one column: [(first_row, last_row), ...]."""
    idx = np.nonzero(column)[0]
    if not len(idx): return []
    cuts = np.nonzero(np.diff(idx) > 1)[0]
    starts = np.r_[idx[0], idx[cuts + 1]]; ends = np.r_[idx[cuts], idx[-1]]
    return list(zip(starts, ends))


def profile(mask):
    """Silhouette thickness at each station (% of the length)."""
    cols = np.array_split(np.arange(mask.shape[1]), STATIONS)
    return [round(float(mask[:, c].any(axis=1).sum()) / LENGTH * 100, 1) for c in cols]


report = {}; rows = []
for key in KEYS:
    data = np.load(SIL / f'{key}.npz'); report[key] = {}
    for view in VIEWS[key]:
        ref = reference(view); mod = model(data[view['view']])
        iou, r, m, top = best_overlap(ref, mod)
        # Metre table: per station, the reference and model filled spans (top: x, side: y; game axes).
        tris = data[view['view']]; lo = tris.reshape(-1, 2).min(axis=0); hi = tris.reshape(-1, 2).max(axis=0); s = (hi[0] - lo[0]) / LENGTH
        def metres(span): a, b = (lo[1] + (span[0] - top) * s, lo[1] + (span[1] - top) * s); return (a, b) if view['view'] == 'top' else (-b, -a)
        lines = []
        for i in range(40):
            col = min(LENGTH - 1, int((i + .5) * LENGTH / 40)); z = lo[0] + col * s
            fmt = lambda sp: ' '.join(f'[{a:.1f},{b:.1f}]' for a, b in (metres(x) for x in runs(sp)))
            lines.append(f'z {z:8.1f} | ref {fmt(r[:, col]):40s} | model {fmt(m[:, col])}')
        (OUT / f'{key}-{view["view"]}.txt').write_text(chr(10).join(lines) + chr(10), encoding='utf-8')
        ys = np.nonzero(r | m)[0]; r = r[ys.min():ys.max() + 1]; m = m[ys.min():ys.max() + 1]
        overlay = np.zeros(r.shape + (3,), np.uint8); overlay[r] = (200, 60, 60); overlay[m] = (60, 170, 220); overlay[r & m] = (225, 225, 225)
        report[key][view['view']] = dict(iou=round(float(iou), 3), reference=profile(r), model=profile(m))
        rows.append((f'{key} {view["view"]}  IoU {iou:.2f}', overlay))
        # Per-view sheet with 10 % stations and the model's metre scale, for reading corrections.
        tris = data[view['view']]; lo = tris.reshape(-1, 2).min(axis=0); hi = tris.reshape(-1, 2).max(axis=0)
        metres = hi[0] - lo[0]; big = Image.fromarray(overlay).resize((overlay.shape[1] * 2, overlay.shape[0] * 2), Image.NEAREST)
        canvas = Image.new('RGB', (big.width + 20, big.height + 70), (22, 26, 32)); canvas.paste(big, (10, 50)); dd = ImageDraw.Draw(canvas)
        for i in range(11):
            x = 10 + i * big.width / 10; dd.line((x, 46, x, 50 + big.height), fill=(90, 90, 40)); dd.text((x + 2, 50 + big.height + 4), f'{lo[0] + metres * i / 10:.0f}', fill=(200, 200, 120), font=font)
        dd.text((10, 8), f'{key} {view["view"]} IoU {iou:.3f}  red=reference blue=model white=both  1 px = {metres / LENGTH / 2:.3f} m', fill=(230, 230, 230), font=font)
        canvas.save(OUT / f'{key}-{view["view"]}.png')
        print(f'{key:30s} {view["view"]:5s} IoU {iou:.3f}', flush=True)
(OUT / 'silhouette-report.json').write_text(json.dumps(report, indent=1) + '\n', encoding='utf-8')
height = sum(o.shape[0] + 34 for _, o in rows)
sheet = Image.new('RGB', (LENGTH + 20, height), (22, 26, 32)); d = ImageDraw.Draw(sheet); y = 0
for title, overlay in rows:
    d.text((10, y + 6), title, fill=(230, 230, 230), font=font); sheet.paste(Image.fromarray(overlay), (10, y + 30)); y += overlay.shape[0] + 34
sheet.save(OUT / 'silhouette-overlay.png')
print('Mean IoU', round(float(np.mean([v['iou'] for k in report.values() for v in k.values()])), 3))
