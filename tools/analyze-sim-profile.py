"""Validate section accounting and summarize a BattleBatch --profile CSV.

python tools/analyze-sim-profile.py <batch.csv> --output <analysis.json>
Times are summed per outer 60 Hz tick. Inclusive parents must not be added
to children; percentile summaries below are per-battle, never pooled p99.
"""
import argparse
import csv
import json
import math
import statistics
from collections import defaultdict
from pathlib import Path


def read_csv(path):
    with path.open(encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def analyze(path):
    battles = read_csv(path)
    sections = read_csv(path.with_suffix(".sections.csv"))
    by_battle = defaultdict(dict)
    aggregate = defaultdict(lambda: [0.0, 0])
    substeps = {"ShipStep", "Collision", "Projectiles", "Missiles", "PointDefense", "Drones"}
    for row in sections:
        key = (int(row["seed"]), int(row["mirror"]))
        identity = (row["scope"], row["section"])
        assert identity not in by_battle[key], f"Duplicate section: {key}, {identity}"
        ticks, calls, ms = int(row["ticks"]), int(row["calls"]), float(row["totalMs"])
        assert ticks > 0 and calls >= 0 and math.isfinite(ms) and ms >= 0
        assert abs(ms * 1000 / ticks - float(row["usPerTick"])) < 0.00051
        if row["section"] == "Tick":
            assert ticks == calls, f"Mixed/missing tick counts: {key}, {identity}"
        if row["section"] in substeps:
            assert calls == ticks * 4, f"Lost substeps: {key}, {identity}"
        by_battle[key][identity] = (ms, ticks)
        aggregate[identity][0] += ms
        aggregate[identity][1] += ticks
    assert set(by_battle) == {(int(r["seed"]), int(r["mirror"])) for r in battles}
    for key, rows in by_battle.items():
        for (scope, section), (ms, ticks) in rows.items():
            if scope != "All":
                continue
            for prefix in ("phase:", "time:"):
                partition = [value for (group, kind), value in rows.items() if group.startswith(prefix) and kind == section]
                assert sum(v[1] for v in partition) == ticks, f"Partition lost ticks: {key}, {section}, {prefix}"
                assert abs(sum(v[0] for v in partition) - ms) < 0.0001, f"Partition lost timing: {key}, {section}, {prefix}"
        exclusive = sum(rows[("All", name)][0] for name in (
            "AI", "Gunnery", "ShipStep", "Collision", "Projectiles", "Missiles", "PointDefense", "Drones", "Sensors", "Post"))
        assert exclusive <= rows[("All", "Tick")][0], f"Double-counted sections: {key}"
    weighted = defaultdict(dict)
    for (scope, section), (ms, ticks) in aggregate.items():
        weighted[scope][section] = round(ms * 1000 / ticks, 4)
    ticks = sum(rows[("All", "Tick")][1] for rows in by_battle.values())
    total_mean_ms = sum(float(r["stepMeanMs"]) * by_battle[(int(r["seed"]), int(r["mirror"]))][("All", "Tick")][1]
                        for r in battles) / ticks
    def spread(column):
        samples = [float(r[column]) for r in battles if r[column]]
        return {"battles": len(samples), "min": min(samples), "median": statistics.median(samples), "max": max(samples)} if samples else None
    result = {
        "battles": len(battles), "ticks": ticks, "accounting": "PASS",
        "tick_weighted_mean_ms": total_mean_ms,
        "per_battle_mean_ms": spread("stepMeanMs"),
        "per_battle_p99_ms": spread("stepP99Ms"),
        "per_battle_brawl_p99_ms": spread("Brawl_stepP99Ms"),
        "section_us_per_tick": dict(weighted),
    }
    pooled_path = path.with_suffix(".ticks.csv")
    if pooled_path.exists():
        pooled = {}
        for row in read_csv(pooled_path):
            count = int(row["ticks"])
            if count == 0:
                continue
            scope = "All" if row["scope"] == "All" else "phase:" + row["scope"]
            assert count == aggregate[(scope, "Tick")][1], "Pooled percentile sample count differs from section ticks"
            pooled[row["scope"]] = {key: float(value) for key,value in row.items() if key not in ("scope", "ticks")}
            pooled[row["scope"]]["ticks"] = count
        result["pooled_tick_ms"] = pooled
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("csv", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    result = analyze(args.csv)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"PASS: {result['battles']} battle profiles, {result['ticks']} ticks; phase/time partitions and substeps reconcile")
    print(f"Weighted step {result['tick_weighted_mean_ms']:.4f} ms; per-battle brawl p99 {result['per_battle_brawl_p99_ms']}")
