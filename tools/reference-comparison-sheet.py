"""Reference board beside the rebuilt hero renders, one sheet per reference board.
python tools/reference-comparison-sheet.py OUT_DIR
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / sys.argv[1]
BOARDS = {
    't1': ('art/reference/20261017_t1_starters/t1-starters.png', ['earth_starter_escort', 'mars_starter_escort']),
    't3-cruisers': (['art/reference/20261017_t3_cruisers/earth_gun_cruiser.png', 'art/reference/20261017_t3_cruisers/earth_air_defense_cruiser.png',
                     'art/reference/20261017_t3_cruisers/mars_gun_cruiser.png', 'art/reference/20261017_t3_cruisers/mars_missile_cruiser.png'],
                    ['earth_gun_cruiser', 'earth_air_defense_cruiser', 'mars_gun_cruiser', 'mars_missile_cruiser']),
    't3-small': (['art/reference/20261017_t3_interceptors/falke-herschel.png', 'art/reference/20261015_small_branches/earth_recon_t3_v2.png',
                  'art/reference/20261015_small_branches/mars_assault_t3_v2.png'],
                 ['interceptor_t3', 'mars_interceptor_t3', 'earth_recon_t3', 'mars_assault_t3']),
    't4-cruisers': ('art/reference/20261017_t4_cruisers/t4-cruisers.png', ['earth_gun_cruiser_t4', 'earth_air_defense_cruiser_t4', 'mars_gun_cruiser_t4', 'mars_missile_cruiser_t4']),
    't4-small': ('art/reference/20261017_t4_small/t4-small.png', ['earth_recon_t4', 'interceptor_t4', 'mars_assault_t4', 'mars_interceptor_t4']),
    't5-battleships': ('art/reference/20261017_t5_fleet/battleships.png', ['battleship_t5', 'mars_battleship_t5']),
    't5-cruisers': ('art/reference/20261017_t5_fleet/cruisers.png', ['earth_gun_cruiser_t5', 'earth_air_defense_cruiser_t5', 'mars_gun_cruiser_t5', 'mars_missile_cruiser_t5']),
    't5-small': ('art/reference/20261017_t5_fleet/small.png', ['interceptor_t5', 'earth_recon_t5', 'mars_interceptor_t5', 'mars_assault_t5']),
}
font = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 22)
for name, (refs, keys) in BOARDS.items():
    refs = [refs] if isinstance(refs, str) else refs
    W = 1600; refh = 520
    ref_images = [Image.open(ROOT / r).convert('RGB') for r in refs]
    cell = W // len(ref_images)
    tile_w = W // len(keys); tile_h = int(tile_w * .7)
    sheet = Image.new('RGB', (W, refh + tile_h + 70), (22, 26, 32)); d = ImageDraw.Draw(sheet)
    d.text((12, 8), 'Reference', fill=(225, 230, 235), font=font)
    for i, im in enumerate(ref_images):
        im.thumbnail((cell, refh - 40)); sheet.paste(im, (i * cell + (cell - im.width) // 2, 40))
    d.text((12, refh + 8), 'Rebuilt blockout', fill=(225, 230, 235), font=font)
    for i, key in enumerate(keys):
        candidates = [OUT / f'{key}_hero.png', OUT / f'{key}_hero.jpg', ROOT / 'docs/validation/reference_pilot_20261017' / f'{key}_hero.png']
        path = next(c for c in candidates if c.exists())
        im = Image.open(path).convert('RGB').crop((140, 120, 1140, 820)); im.thumbnail((tile_w, tile_h))
        sheet.paste(im, (i * tile_w, refh + 40)); d.text((i * tile_w + 10, refh + 44), key, fill=(200, 205, 210), font=font)
    sheet.save(OUT / f'compare-{name}.png'); print('wrote', name)
