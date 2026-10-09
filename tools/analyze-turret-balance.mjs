import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(process.argv[2] ?? 'shots/turret-balance-20261008');
const names = ['baseline', 'guns_only', 'pd_only', 'current'];
function csv(file) {
  const lines = fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, '').trim().split(/\r?\n/);
  const split = line => (line.match(/("(?:[^"]|"")*"|[^,]*)(,|$)/g) ?? []).filter((v, i, a) => v !== '' || i !== a.length - 1)
    .map(v => v.replace(/,$/, '').replace(/^"|"$/g, '').replace(/""/g, '"'));
  const header = split(lines.shift());
  return lines.filter(Boolean).map(line => Object.fromEntries(split(line).map((v, i) => [header[i], v])));
}
const sum = (rows, field) => rows.reduce((total, row) => total + Number(row[field] || 0), 0);
const mean = values => values.reduce((a, b) => a + b, 0) / values.length;
function quantile(values, p) {
  if (!values.length) return null;
  const sorted = [...values].sort((a, b) => a - b), i = (sorted.length - 1) * p;
  return sorted[Math.floor(i)] + (sorted[Math.ceil(i)] - sorted[Math.floor(i)]) * (i % 1);
}
const pct = (num, den) => den ? 100 * num / den : null;
const counts = (rows, field) => Object.fromEntries([...new Set(rows.map(r => r[field]))].sort().map(v => [v, rows.filter(r => r[field] === v).length]));
function weapon(rows, fired, hits, bothSides = false) {
  const n = bothSides ? sum(rows, `Blue_${fired}`) + sum(rows, `Red_${fired}`) : sum(rows, fired);
  const h = bothSides ? sum(rows, `Blue_${hits}`) + sum(rows, `Red_${hits}`) : sum(rows, hits);
  return { fired: n, hit: h, hitPercent: pct(h, n) };
}
const all = Object.fromEntries(names.map(name => [name, {
  battles: csv(path.join(root, `${name}.csv`)), ships: csv(path.join(root, `${name}.ships.csv`)), am: csv(path.join(root, `${name}.am.csv`)),
}]));
const summaries = {};
for (const name of names) {
  const { battles: b, ships: s, am } = all[name];
  if (b.length !== 40 || s.length !== 960 || new Set(b.map(r => `${r.seed}/${r.mirror}`)).size !== 40)
    throw new Error(`${name}: incomplete sample or duplicate battle keys`);
  for (const side of ['Blue', 'Red']) for (const [field, shipField] of [['rails', 'rails'], ['railHits', 'railHits'], ['missiles', 'missiles'], ['missileHits', 'missileHits'], ['torpedoes', 'torpedoes'], ['torpedoHits', 'torpedoHits']])
    if (sum(b, `${side}_${field}`) !== sum(s.filter(r => r.faction === side), shipField)) throw new Error(`${name}: ${side}/${field} log mismatch`);
  const times = b.map(r => +r.outcomeTime);
  const byClass = {};
  for (const kind of ['Battleship', 'Escort', 'Interceptor']) {
    const rows = s.filter(r => r.kind === kind);
    byClass[kind] = { count: rows.length, operationalPercent: pct(sum(rows, 'operational'), rows.length), destroyedPercent: pct(sum(rows, 'destroyed'), rows.length),
      disabledPercent: pct(sum(rows, 'disabled'), rows.length), meanModuleHealthPercent: 100 * sum(rows, 'moduleHealthFraction') / rows.length,
      rails: weapon(rows, 'rails', 'railHits'), missiles: weapon(rows, 'missiles', 'missileHits'), am: weapon(rows, 'torpedoes', 'torpedoHits'),
      meanRailAmmoLeft: sum(rows, 'railRoundsRemaining') / rows.length, emptyGunsPercent: pct(rows.filter(r => +r.railRoundsRemaining === 0).length, rows.length),
      amLaunchingShips: rows.filter(r => +r.torpedoes > 0).length, amReadyButUnused: rows.filter(r => +r.torpedoes === 0 && +r.torpedoesRemaining > 0 && r.operational === '1').length,
      meanShieldDamageReceived: sum(rows, 'shieldDamageReceived') / rows.length, meanModuleDamageReceived: sum(rows, 'moduleDamageReceived') / rows.length };
  }
  summaries[name] = { battles: b.length, wins: counts(b, 'winner'), positions: { normal: counts(b.filter(r => r.mirror === '0'), 'winner'), mirror: counts(b.filter(r => r.mirror === '1'), 'winner') },
    outcomeSeconds: { median: quantile(times, .5), mean: mean(times), min: Math.min(...times), max: Math.max(...times), q25: quantile(times, .25), q75: quantile(times, .75) },
    in15to20Minutes: times.filter(t => t >= 900 && t <= 1200).length,
    rails: weapon(b, 'rails', 'railHits', true), missiles: weapon(b, 'missiles', 'missileHits', true), am: weapon(b, 'torpedoes', 'torpedoHits', true),
    amOutcomes: counts(am, 'outcome'), amFlightsPending: sum(b, 'Blue_torpedoes') + sum(b, 'Red_torpedoes') - am.length,
    battlesWithAmLaunch: b.filter(r => +r.Blue_torpedoes + +r.Red_torpedoes > 0).length,
    amFailures: sum(b, 'Blue_amFailures') + sum(b, 'Red_amFailures'), amJettisons: sum(b, 'Blue_amJettisons') + sum(b, 'Red_amJettisons'),
    meanFriendlyCollisions: sum(b, 'friendlyCollisions') / b.length,
    byClass, phases: Object.fromEntries(['Approach', 'Missile', 'Gunnery', 'Sniping', 'Brawl'].map(phase => [phase, {
      medianSeconds: quantile(b.map(r => +r[`${phase}_seconds`]), .5), over60Seconds: b.filter(r => +r[`${phase}_seconds`] >= 60).length,
    }])) };
}

// Paired comparisons retain the same seed AND mirrored placement. Bootstrap clusters are seed pairs.
let randomState = 0x739712;
function random() { randomState = (Math.imul(randomState, 1664525) + 1013904223) >>> 0; return randomState / 2 ** 32; }
function comparison(before, after) {
  const a = all[before].battles, b = new Map(all[after].battles.map(r => [`${r.seed}/${r.mirror}`, r]));
  const deltas = a.map(r => ({ seed: +r.seed, delta: +b.get(`${r.seed}/${r.mirror}`).outcomeTime - +r.outcomeTime }));
  const clusters = [...new Set(deltas.map(d => d.seed))].map(seed => mean(deltas.filter(d => d.seed === seed).map(d => d.delta)));
  const boot = Array.from({ length: 10000 }, () => mean(Array.from({ length: clusters.length }, () => clusters[Math.floor(random() * clusters.length)])));
  return { meanOutcomeSecondsDelta: mean(deltas.map(d => d.delta)), medianOutcomeSecondsDelta: quantile(deltas.map(d => d.delta), .5),
    seedClusterBootstrap95: [quantile(boot, .025), quantile(boot, .975)], fasterBattles: deltas.filter(d => d.delta < 0).length,
    winnerChanged: a.filter(r => r.winner !== b.get(`${r.seed}/${r.mirror}`).winner).length };
}
const paired = Object.fromEntries([['baseline', 'guns_only'], ['baseline', 'pd_only'], ['baseline', 'current'], ['guns_only', 'current'], ['pd_only', 'current']]
  .map(([a, b]) => [`${a}->${b}`, comparison(a, b)]));
for (const name of names) {
  const battles = all[name].battles;
  const score = r => r.winner === 'Blue' ? 1 : r.winner === 'Red' ? 0 : .5;
  const seeds = [...new Set(battles.map(r => r.seed))];
  const clusters = seeds.map(seed => mean(battles.filter(r => r.seed === seed).map(score)));
  const boot = Array.from({ length: 10000 }, () => mean(Array.from({ length: clusters.length }, () => clusters[Math.floor(random() * clusters.length)])));
  summaries[name].blueScore = { fraction: mean(battles.map(score)), seedClusterBootstrap95: [quantile(boot, .025), quantile(boot, .975)] };
}
const result = { summaries, paired };
fs.writeFileSync(path.join(root, 'summary.json'), JSON.stringify(result, null, 2) + '\n');
console.log(JSON.stringify(result, null, 2));
