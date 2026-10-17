import hashlib
import json
import re
import statistics
from pathlib import Path

root = Path(__file__).resolve().parent
comparison = {}
for field in ('DeepSpace', 'AsteroidCluster'):
    versions = {}
    for version in ('before', 'after'):
        rows = []
        for repeat in range(1, 4):
            data = json.loads((root / f'{field}-{version}-{repeat}.json').read_text(encoding='utf-8-sig'))
            assert len(data) == 1
            row = data[0]
            assert (row['ships'], row['warmupTicks'], row['measuredTicks'], row['battlefield']) == (240, 900, 1800, field)
            assert ('pdSolutions' in row) == (version == 'after'), 'Wrong assembly loaded'
            rows.append(row)
        assert len({r['summarySha256'] for r in rows}) == 1, f'{field}/{version}: nondeterministic outcome'
        versions[version] = {
            **{key: statistics.median(r[key] for r in rows) for key in
               ('meanMs', 'p95Ms', 'p99Ms', 'maxMs', 'allocatedBytesPerTick', 'pdShots', 'peakRails', 'peakMissiles', 'activeAtEnd')},
            'pdMeanMs': statistics.median(r['sectionsUs']['PointDefense'] / 1000 for r in rows),
            'summarySha256': rows[0]['summarySha256'],
        }
        if version == 'after':
            versions[version]['pdSolutions'] = statistics.median(r['pdSolutions'] for r in rows)
    versions['changePercent'] = {
        key: (versions['after'][key] / versions['before'][key] - 1) * 100
        for key in ('meanMs', 'p95Ms', 'p99Ms', 'pdMeanMs', 'allocatedBytesPerTick')
    }
    comparison[field] = versions
log = (root / 'sim-checks.log').read_text(encoding='utf-8-sig')
assert 'Unhandled exception' not in log and 'Exception:' not in log
checks = re.findall(r'^PASS: ([\d,]+) (.+)$', log, re.M)
assert len(checks) >= 59
comparison['checks'] = {'groups': len(checks), 'assertions': sum(int(n.replace(',', '')) for n, _ in checks)}
(root / 'comparison.json').write_text(json.dumps(comparison, indent=2), encoding='utf-8')
manifest = {p.relative_to(root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in root.rglob('*')
            if p.is_file() and p.name not in ('sha256.json',)}
(root / 'sha256.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print(json.dumps(comparison, indent=2))
