"""Summarize paired role-expansion trials without treating a small seed set as a win-rate estimate."""
import argparse
import csv
import json
import re
import statistics
from collections import Counter
from pathlib import Path


def rows(path):
    with path.open(encoding="utf-8-sig", newline="") as source:
        return list(csv.DictReader(source))


def median(items):
    values = list(items)
    return round(statistics.median(values), 3) if values else None


def summarize(directory):
    battles = {p.stem: rows(p) for p in directory.glob("*.csv") if p.name.count(".") == 1}
    ships = [r for p in directory.glob("*.ships.csv") for r in rows(p)]
    probes = [r for p in directory.glob("*.probes.csv") for r in rows(p)]
    flights = [r for p in directory.glob("*.am.csv") for r in rows(p)]
    telemetry = [r for p in directory.glob("*.telemetry.csv") for r in rows(p) if r["kind"] == "Interceptor"]
    result = {"battles": {name: dict(Counter(r["winner"] for r in data)) for name, data in battles.items()},
              "sample_count": sum(map(len, battles.values())), "designs": {}, "probe_flights": {
                  "completed": len(probes), "destroyed": sum(int(r["destroyed"]) for r in probes),
                  "median_lifetime_seconds": median(float(r["lifetime"]) for r in probes)},
              "am_outcomes": dict(Counter(r["outcome"] for r in flights)),
              "mission_samples": dict(Counter(r.get("wingMission", "legacy") for r in telemetry))}
    for design in ("Earth", "Mars"):
        result["designs"][design] = {}
        for kind in ("Interceptor", "Escort", "Battleship"):
            selected = [r for r in ships if r["design"] == design and r["kind"] == kind]
            def total(key):
                return sum(float(r.get(key) or 0) for r in selected)
            result["designs"][design][kind] = {
                "count": len(selected), "survival_median_seconds": median(float(r["survivalSeconds"]) for r in selected),
                "destroyed": total("destroyed"), "disabled": total("disabled"), "operational": total("operational"),
                "first_action_median_seconds": median(float(r["firstAction"]) for r in selected if r.get("firstAction")),
                "first_action_observed": sum(bool(r.get("firstAction")) for r in selected),
                "first_assault_median_seconds": median(float(r["firstAssault"]) for r in selected if r.get("firstAssault")),
                "rail_hit_fraction": round(total("railHits") / total("rails"), 4) if total("rails") else None,
                "rail_mean_range_meters": round(total("railRangeSum") / total("railRangeSamples"), 1) if total("railRangeSamples") else None,
                "am_hit_fraction": round(total("torpedoHits") / total("torpedoes"), 4) if total("torpedoes") else None,
                **{key: total(key) for key in ("missilesIntercepted", "torpedoesIntercepted", "torpedoes", "torpedoHits",
                    "rails", "railHits", "probesLaunched", "probeContacts", "probeIdentifications", "probeKills",
                    "shieldDamageInflicted", "moduleDamageInflicted")}}
    result["wing_defense"] = [{"pair": p.stem.removesuffix(".ships"), "seed": seed, "mirror": mirror, "faction": side,
        "missile_kills": sum(int(r["missilesIntercepted"]) for r in data if r["seed"] == seed and r["mirror"] == mirror and r["faction"] == side),
        "torpedo_kills": sum(int(r["torpedoesIntercepted"]) for r in data if r["seed"] == seed and r["mirror"] == mirror and r["faction"] == side)}
        for p in directory.glob("*.ships.csv")
        for data in [[r for r in rows(p) if r["kind"] == "Interceptor"]]
        for seed, mirror, side in sorted({(r["seed"], r["mirror"], r["faction"]) for r in data})]
    result["fleet_missile_defense"] = []
    for path in directory.glob("*.ships.csv"):
        data = rows(path)
        for seed, mirror, side in sorted({(r["seed"], r["mirror"], r["faction"]) for r in data}):
            allies = [r for r in data if r["seed"] == seed and r["mirror"] == mirror and r["faction"] == side]
            enemies = [r for r in data if r["seed"] == seed and r["mirror"] == mirror and r["faction"] != side]
            incoming = sum(int(r["missiles"]) for r in enemies)
            killed = sum(int(r["missilesIntercepted"]) for r in allies)
            result["fleet_missile_defense"].append({"pair": path.stem.removesuffix(".ships"), "seed": seed,
                "mirror": mirror, "faction": side, "enemy_missiles_launched": incoming,
                "intercepted": killed, "fraction_of_launched": round(killed / incoming, 4) if incoming else None})
    return result


def performance(path):
    if not path.exists():
        return None
    text = path.read_text(encoding="utf-8-sig")
    fps = [float(f) for t, f in re.findall(r"perf t=([\d.]+)s fps=([\d.]+)", text) if float(t) >= 4]
    sections = [dict((key, float(value)) for key, value in re.findall(r"(\w+)=([\d.]+)", line.split("|", 1)[1]))
        for line in text.splitlines() if line.startswith("sim-profile") and float(re.search(r"t=([\d.]+)", line)[1]) >= 4]
    return {"fps_median": median(fps), "windows": len(fps), "median_us_per_tick": {
        key: median(s[key] for s in sections) for key in ("Tick", "Sensors", "Probes", "PointDefense")}}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--after", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--perf-prefix", type=Path)
    args = parser.parse_args()
    report = {"baseline": summarize(args.baseline), "after": summarize(args.after),
        "notes": ["Same seeds 1,2, normal/mirrored positions; each design pairing is four 24-ship battles, 25-minute limit.",
                  "First action includes probe deployment, establishing a recon/defense/superiority station (within 1.5 km), and firing. First assault is the attack order, not the first AM launch.",
                  "Unique additional probe contacts/identifications are counted per team across the battle; ordinary ships alone could not provide that level.",
                  "Battle lifetime is censored at the verdict. Completed probe lifetime excludes equipment still active at verdict.",
                  "FPS comparison uses two 12-second runs per condition in reversed order, excluding the first three seconds; 24 ships, AI disabled, no combat projectiles. This is equipment overhead, not a full-battle FPS benchmark.",
                  "This is regression and first-pass balance evidence, not a statistically stable faction win-rate estimate."]}
    if args.perf_prefix:
        report["performance"] = {str(count): performance(Path(str(args.perf_prefix) + f"-{count}.log")) for count in (0, 20)}
        report["performance_reversed_order"] = {str(count): performance(Path(str(args.perf_prefix) + f"-{count}-repeat.log")) for count in (0, 20)}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(args.output)
