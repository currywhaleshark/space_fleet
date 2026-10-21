"""Aggregate controlled AA waves; seed/wave is the experimental unit, not each missile."""
import csv
import hashlib
import json
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "docs/validation/interception_20261021"


def read(name):
    return json.loads((OUT / name).read_text(encoding="utf-8-sig"))


rows = read("stress.json") + read("flight-profiles.json")
groups = defaultdict(list)
for row in rows:
    row.setdefault("MotorBurning", False)  # The initial wave file predates the powered-flight column.
    assert row["Incoming"] == 48
    assert sum(row[k] for k in ("Intercepted", "GuardHits", "AnchorHits", "OtherHits", "ExpiredOrMissed", "Remaining")) == 48
    assert sum(row["KillsByWeapon"].values()) == row["Intercepted"]
    assert row["KillsByShip"] == {"GUARD": row["Intercepted"]}
    assert sum(s["Intercepted"] for s in row["Sectors"]) == row["Intercepted"]
    if row["Support"] == "Ecm":
        assert row["EcmSeconds"] > 0 and row["MinimumEcmEffect"] > 0
    groups[tuple(row[k] for k in ("FrontRangeKm", "Directions", "SpeedMps", "MotorBurning", "Support"))].append(row)

summary = []
for key, samples in groups.items():
    assert sorted(r["Seed"] for r in samples) == list(range(1, 9))
    n = sum(r["Incoming"] for r in samples)
    kills = sum(r["Intercepted"] for r in samples)
    hits = sum(r["GuardHits"] + r["AnchorHits"] for r in samples)
    weapons = Counter()
    for r in samples:
        weapons.update(r["KillsByWeapon"])
    summary.append(dict(FrontRangeKm=key[0], Directions=key[1], SpeedMps=key[2], MotorBurning=key[3], Support=key[4],
        Waves=len(samples), Incoming=n, Intercepted=kills, InterceptPercent=100 * kills / n,
        HitPercent=100 * hits / n, GuardHits=sum(r["GuardHits"] for r in samples), AnchorHits=sum(r["AnchorHits"] for r in samples),
        Remaining=sum(r["Remaining"] for r in samples), OtherHits=sum(r["OtherHits"] for r in samples),
        ExpiredOrMissed=sum(r["ExpiredOrMissed"] for r in samples),
        WaveMinPercent=min(r["Intercepted"] / 48 * 100 for r in samples),
        WaveMaxPercent=max(r["Intercepted"] / 48 * 100 for r in samples),
        LaserKills=weapons["Laser"], PdKills=weapons["PointDefense"], DroneKills=weapons["DefenseDrone"], CounterKills=weapons["Missile"],
        GuardLosses=sum(r["GuardDestroyed"] for r in samples), SupportLosses=sum(r["SupportDestroyed"] for r in samples),
        LaserShipHitPulses=sum(r["LaserShipHitPulses"] for r in samples),
        CoolingMountSeconds=sum(r["CoolingMountSeconds"] for r in samples)))

with (OUT / "stress-summary.csv").open("w", encoding="utf-8-sig", newline="") as f:
    writer = csv.DictWriter(f, fieldnames=list(summary[0]))
    writer.writeheader()
    writer.writerows(summary)

# Earlier four-seed results must remain identical; no simulation balance is changed by this experiment.
old = {(r["RangeKm"], r["Seed"]): r for r in read("after.json")
       if r["Hull"] == "earth_air_defense_cruiser_t5" and r["Layered"] and r["Count"] == 48}
baseline_pairs = 0
for r in rows:
    if r["Support"] != "Absent" or r["Directions"] != 1 or r["SpeedMps"] != 2000 or r["MotorBurning"] or r["Seed"] > 4:
        continue
    reference = old[r["FrontRangeKm"], r["Seed"]]
    assert (reference["Intercepted"], reference["Hits"], reference["InnerScreen"]) == (r["Intercepted"], r["GuardHits"] + r["AnchorHits"], r["InnerScreen"])
    baseline_pairs += 1
silent_pairs = 0
for key, samples in groups.items():
    if key[-1] != "Silent" or key[3]:
        continue
    for a, b in zip(samples, groups[key[:-1] + ("Absent",)]):
        for field in ("Intercepted", "GuardHits", "AnchorHits", "OtherHits", "Remaining"):
            assert a[field] == b[field]
        silent_pairs += 1

with (OUT / "stress-tables.md").open("w", encoding="utf-8") as f:
    f.write("# 자동 집계: 다방향·고속·ECM 시험\n\n각 행 8회 × 48발 = 384발. 범위는 8회 시험의 최소~최대이며 신뢰구간이 아니다.\n\n")
    f.write("| 기준 거리 | 방향 수 | 초기 속도 | 연소 중 | 지원함 | 요격률 | 명중 돌파율 | 요격률 최소~최대 | 방공함 격침 | 40초 잔존 |\n|---:|---:|---:|---|---|---:|---:|---:|---:|---:|\n")
    for r in summary:
        f.write(f"| {r['FrontRangeKm']}km | {r['Directions']} | {r['SpeedMps']}m/s | {'예' if r['MotorBurning'] else '아니오'} | {r['Support']} | {r['InterceptPercent']:.1f}% | {r['HitPercent']:.1f}% | {r['WaveMinPercent']:.1f}~{r['WaveMaxPercent']:.1f}% | {r['GuardLosses']}/8 | {r['Remaining']} |\n")

hash_input = hashlib.sha256()
files = sorted(set((ROOT / "src/Sim").glob("*.cs")) | set((ROOT / "src/Shared").glob("*.cs")) | set((ROOT / "data").rglob("*.json")))
for file in files:
    hash_input.update(file.relative_to(ROOT).as_posix().encode())
    hash_input.update(b"\0")
    hash_input.update(file.read_bytes())
checks = dict(Waves=len(rows), Missiles=sum(r["Incoming"] for r in rows), Groups=len(groups), Seeds=list(range(1, 9)),
              OutcomeAccounting="PASS", AllInterceptionsByGuard="PASS", EcmActuallyPowered="PASS",
              EarlierFourSeedMatches=baseline_pairs, SilentVsAbsentCoastingMatches=silent_pairs,
              SimulationAndDataFiles=len(files), SimulationAndDataSha256=hash_input.hexdigest(),
              HullOrWeaponBalanceChanged=False, Files={})
for name in ("stress.json", "flight-profiles.json", "stress-diagnostics.json", "stress-summary.csv"):
    checks["Files"][name] = hashlib.sha256((OUT / name).read_bytes()).hexdigest()
(OUT / "stress-checks.json").write_text(json.dumps(checks, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(checks, ensure_ascii=False, indent=2))
print("32 km, one direction, no support, coasting weapon contribution:")
for r in summary:
    if r["FrontRangeKm"] == 32 and r["Directions"] == 1 and r["Support"] == "Absent" and not r["MotorBurning"]:
        print({k:r[k] for k in ("SpeedMps", "LaserKills", "PdKills", "DroneKills", "CounterKills")})
