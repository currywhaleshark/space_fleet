"""Compare fixed-seed AI trials against the preserved small-hull combat baseline."""
import csv
import gzip
import json
import statistics
import sys
from collections import Counter
from pathlib import Path

root = Path(__file__).resolve().parents[1]
folder = Path(sys.argv[1]).resolve()


def rows(path):
    source = (path.open(encoding="utf-8-sig", newline="") if path.exists()
              else gzip.open(str(path) + ".gz", "rt", encoding="utf-8-sig", newline=""))
    with source:
        return list(csv.DictReader(source))


def mean(values):
    values = [float(v) for v in values if v not in (None, "")]
    return round(statistics.mean(values), 3) if values else None


def summarize(path):
    games, ships, samples = (rows(path.with_suffix(ext)) for ext in (".csv", ".ships.csv", ".telemetry.csv"))
    result = {"path": str(path.relative_to(root)), "wins": dict(Counter(g["winner"] for g in games)),
              "seconds": mean(g["outcomeTime"] for g in games),
              "collisions": sum(int(g["friendlyCollisions"]) for g in games), "ships": []}
    for side in ("Blue", "Red"):
        for kind in ("Battleship", "Escort", "Interceptor"):
            group = [s for s in ships if s["faction"] == side and s["kind"] == kind]
            active = [s for s in samples if s["faction"] == side and s["kind"] == kind and s["active"] == "1"]
            result["ships"].append({"side": side, "kind": kind, "count": len(group),
                **{k: sum(float(s[k] or 0) for s in group) for k in
                   ("rails", "railHits", "torpedoes", "torpedoHits", "destroyed", "disabled", "amFailures",
                    "probesLaunched", "probeContacts", "missilesIntercepted", "moduleDamageInflicted", "shieldDamageInflicted")},
                "survivalSeconds": mean(s["survivalSeconds"] for s in group),
                "firstAssault": mean(s["firstAssault"] for s in group),
                "trackSamples": dict(Counter(s["track"] for s in active)),
                "assaultActivities": dict(Counter(s["activity"] for s in active if s["wingMission"] == "Assault")),
                "amModes": dict(Counter(s["amMode"] for s in active)),
                "firingWindowSamples": sum(int(s["flightEligibleGuns"]) > 0 and s["track"] == "Locked" for s in active),
                "railRange": sum(float(s["railRangeSum"]) for s in group) / max(1, sum(int(s["railRangeSamples"]) for s in group)),
            })
    return result


files = [root / "docs/validation/small_hull_combat_20261015/after" / f"{s}-current.csv"
         for s in ("mixed", "earth", "mars")]
files += sorted(p for p in folder.rglob("*.csv") if p.with_suffix(".ships.csv").exists())
summary = [summarize(p) for p in files]
(folder / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
sys.stdout.reconfigure(encoding="utf-8")
for item in summary:
    print(item["path"], item["wins"], item["seconds"])
    for s in item["ships"]:
        print(s["side"], s["kind"], "rail", s["railHits"], "/", s["rails"], "AM", s["torpedoHits"], "/", s["torpedoes"],
              "dead", s["destroyed"], "assault", s["firstAssault"])
