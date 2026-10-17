"""Build the native game's small equipment schematics (no external artwork)."""
from pathlib import Path

root = Path(__file__).resolve().parents[1] / 'assets/ui/equipment'
root.mkdir(parents=True, exist_ok=True)
parts = {
    'point-defense': '<path d="M24 56h48l-7-22H31zM30 58v8h36v-8M38 34V10h8v24m8 0V10h8v24"/><path class="a" d="M12 32a37 37 0 0 1 10-16m52 0a37 37 0 0 1 10 16"/>',
    'forward-gun': '<path d="M15 45h66v14H15zM19 38h55v7M33 30h42v8M11 45v14m70-19v24M30 45v14m16-14v14m16-14v14"/><path class="a" d="M75 30h12M5 50h9"/>',
    'engine': '<path d="M32 12h32l-3 19 13 25H22l13-25zM28 21h40M27 46h42"/><ellipse cx="48" cy="56" rx="26" ry="8"/><path class="a" d="M36 65v10m12-10v15m12-15v10"/>',
    'sensor': '<path d="M23 21l48 28C50 68 23 51 23 21zM47 48l8 18H34l9-17M48 35l16-15"/><circle class="a" cx="67" cy="17" r="3"/><path class="a" d="M67 5a13 13 0 0 1 12 12M67 0a21 21 0 0 1 20 21"/>',
    'generator': '<path d="M32 9h32l10 15v36L64 74H32L22 60V24zM32 9v65m32-65v65M22 26h10m-10 14h10m-10 14h10m32-28h10M64 40h10M64 54h10"/><path class="a" d="M49 23L39 43h11l-4 19 13-27H48z"/>',
    'shield': '<path d="M48 8L76 19v25c0 18-28 31-28 31S20 62 20 44V19z"/><path class="a" d="M48 19l18 8v17c0 10-18 21-18 21S30 54 30 44V27zM48 20v44"/>',
    'ecm': '<path d="M35 30h26v29H35zM41 36h14v8H41zM42 59v12m12-12v12M35 70h26"/><path class="a" d="M28 21c-14 8-14 27 0 35m40-35c14 8 14 27 0 35M20 11C0 26 0 51 20 66m56-55c20 15 20 40 0 55M48 17v10"/>',
    'drone': '<path d="M48 10l10 25 21 13v12l-21-8-5 20H43l-5-20-21 8V48l21-13zM30 40V24h9v12m18 0V24h9v16"/><path class="a" d="M45 34h6v16h-6zM21 63v8m54-8v8"/>',
    'empty-bay': '<path d="M20 16h56v48H20zM20 16l12 12h32l12-12M32 28v24h32V28M20 64l12-12m32 0l12 12"/><path class="a" d="M40 40h16m-8-8v16"/>',
    'hull': '<path d="M48 8l24 20 7 33-16 10H33L17 61l7-33zM24 28h48M33 71l4-43 11-20 11 20 4 43M20 48h15m26 0h15"/><path class="a" d="M42 39h12v17H42z"/>',
}
for name, count in [('main-single', 1), ('main-twin', 2), ('main-triple', 3)]:
    barrels = ''.join(f'<path d="M{x-3} 34V7h6v27"/>' for x in [48 + (i-(count-1)/2)*12 for i in range(count)])
    parts[name] = '<path d="M26 37l10-10h24l10 10 5 22H21zM29 60v10h38V60M22 48h52"/>' + barrels + '<path class="a" d="M39 54h18"/>'
for name, body in parts.items():
    (root / f'{name}.svg').write_text(
        '<svg xmlns="http://www.w3.org/2000/svg" width="96" height="80" viewBox="0 0 96 80">'
        '<g fill="none" stroke="#b8cfde" stroke-width="2.4" stroke-linejoin="round" stroke-linecap="round">'
        + body.replace('class="a"', 'stroke="#5dc6ef"') + '</g></svg>', encoding='utf-8')
print(f'Built {len(parts)} equipment icons in {root}')
