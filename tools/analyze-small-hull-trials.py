"""Summarize SmallHullTrials and canonical fleet CSVs (standard library only).

Usage: python tools/analyze-small-hull-trials.py docs/validation/<run>
Measurements are descriptive, not statistical balance certification.
"""
import csv
import gzip
import hashlib
import json
import re
import statistics
import sys
from collections import Counter, defaultdict
from pathlib import Path

root = Path(__file__).resolve().parents[1]
folder = Path(sys.argv[1]).resolve()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def csvrows(path):
    compressed = path.with_suffix(path.suffix + ".gz")
    source = path.open(encoding="utf-8-sig", newline="") if path.exists() else gzip.open(compressed, "rt", encoding="utf-8-sig", newline="")
    with source:
        return list(csv.DictReader(source))


def avg(values):
    values = list(values)
    return round(statistics.mean(values), 3) if values else None


def numeric(rows, field):
    return [float(r[field]) for r in rows if r[field] not in (None, "")]


summary = {"assault": [], "fleets": [], "geometry": read(folder / "after/projection.json"),
           "coverage": read(folder / "after/coverage.json")}
for phase in ("before", "after"):
    groups = defaultdict(list)
    for row in read(folder / phase / "assault.json"):
        groups[(row["Hull"], row["Legacy"], row["Defense"])].append(row)
    for (hull, legacy, defense), rows in groups.items():
        summary["assault"].append({
            "phase": phase, "hull": hull, "legacy": legacy, "defense": defense, "runs": len(rows),
            **{k: sum(r[k] for r in rows) for k in ("Launched", "Hits", "Intercepted", "Destroyed", "Disabled", "Escaped")},
            "firstLaunchMean": avg(r["FirstLaunch"] for r in rows if r["FirstLaunch"] is not None),
            "escapeSecondsMean": avg(r["EscapeSeconds"] for r in rows if r["EscapeSeconds"] is not None),
            "pdShotsMean": avg(r["PdShots"] for r in rows),
            "destructions": dict(Counter(r["Destruction"] for r in rows if r.get("Destruction", "None") != "None")),
        })

for shape in ("legacy", "current"):
    for scenario in ("mixed", "earth", "mars"):
        path = folder / "after" / f"{scenario}-{shape}.csv"
        rows = csvrows(path)
        ships = csvrows(path.with_suffix(".ships.csv"))
        telemetry = csvrows(path.with_suffix(".telemetry.csv"))
        record = {"scenario": scenario, "geometry": shape, "runs": len(rows),
                  "wins": dict(Counter(r["winner"] for r in rows)),
                  "reasons": dict(Counter(r["reason"] for r in rows)),
                  "meanSeconds": avg(numeric(rows, "outcomeTime")),
                  "friendlyCollisions": sum(numeric(rows, "friendlyCollisions")),
                  "battles": [{k: r[k] for k in ("seed", "mirror", "winner", "reason", "outcomeTime")} for r in rows],
                  "ships": []}
        for side in ("Blue", "Red"):
            for kind in ("Battleship", "Escort", "Interceptor"):
                members = [s for s in ships if s["faction"] == side and s["kind"] == kind]
                sampled = [s for s in telemetry if s["faction"] == side and s["kind"] == kind and s["active"] == "1"]
                record["ships"].append({"side": side, "kind": kind, "count": len(members),
                    "hulls": sorted({s["hullId"] for s in members}),
                    **{k: sum(numeric(members, k)) for k in ("destroyed", "disabled", "rails", "railHits", "missiles",
                        "missileHits", "torpedoes", "torpedoHits", "amFailures", "probesLaunched", "probeContacts",
                        "probeIdentifications", "missilesIntercepted", "shieldDamageInflicted", "moduleDamageInflicted")},
                    "firstInactiveOrCensoredSeconds": avg(numeric(members, "survivalSeconds")),
                    "firstAssaultMean": avg(numeric(members, "firstAssault")),
                    "meanRailRange": (sum(numeric(members, "railRangeSum")) / sum(numeric(members, "railRangeSamples")))
                        if sum(numeric(members, "railRangeSamples")) else None,
                    "activeSamples": len(sampled),
                    "lockedActiveSamples": sum(s["track"] == "Locked" for s in sampled),
                    "assaultActivities": dict(Counter(s["activity"] for s in sampled if s["wingMission"] == "Assault")),
                })
        summary["fleets"].append(record)

log = (folder / "after/simchecks.log").read_text(encoding="utf-8-sig")
summary["simChecks"] = sum(int(n) for n in re.findall(r"^PASS: (\d+)", log, re.M))
summary["simFailure"] = "Unhandled exception" in log
old = read(root / "docs/validation/blockout_small_branches_20261015/sha256.json")
summary["changedSinceBlockoutManifest"] = [name for name, digest in old.items()
    if hashlib.sha256((root / name).read_bytes()).hexdigest() != digest]
(folder / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"simChecks": summary["simChecks"], "simFailure": summary["simFailure"],
                  "preservationDifferences": summary["changedSinceBlockoutManifest"],
                  "fleets": [{k: v for k, v in f.items() if k != "ships"} for f in summary["fleets"]]}, ensure_ascii=False, indent=2))
