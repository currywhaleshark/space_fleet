"""Compare preserved pre-refactor outputs with the completed hardpoint checks."""
from pathlib import Path
import hashlib
import json
import re

root = Path(__file__).resolve().parent
before = json.loads((root / "before-definitions.json").read_text(encoding="utf-8-sig"))
after = json.loads((root / "after-definitions.json").read_text(encoding="utf-8-sig"))
assert len(before) == len(after) == 34 and before == after, "Stock definitions/loadout IDs changed"

old_lines = (root / "before-sim-checks.log").read_text(encoding="utf-8-sig").splitlines()
new_lines = (root / "after-sim-checks.log").read_text(encoding="utf-8-sig").splitlines()
new_group = [line for line in new_lines if "primary hardpoint/weapon/legacy compatibility checks" in line]
assert len(new_group) == 1 and new_group[0].startswith("PASS: ")
assert [line for line in new_lines if line not in new_group] == old_lines, "Existing regression output changed"
model = (root / "model-checks.log").read_text(encoding="utf-8-sig")
assert "PASS: 2484 ship model checks" in model and "ERROR:" not in model
build = (root / "game-build.log").read_text(encoding="utf-8-sig")
assert "경고 0개" in build and "오류 0개" in build, "Game build was not clean"

def totals(lines):
    counts = [int(m[1]) for line in lines if (m := re.match(r"PASS: (\d+) ", line))]
    return {"groups": len(counts), "checks": sum(counts)}

report = {
    "stock_hulls": len(before),
    "stock_definitions_and_loadout_ids_identical": before == after,
    "existing_regression_output_identical": True,
    "before": totals(old_lines), "after": totals(new_lines),
    "model_checks": 2484,
    "game_build_warnings": 0, "game_build_errors": 0,
    "source_sha256": {name: hashlib.sha256((root.parents[2] / name).read_bytes()).hexdigest()
                      for name in ("src/Sim/MainBattery.cs", "src/Sim/ShipDefinition.cs", "src/Sim/ShipBody.cs",
                                   "src/Sim/Railgun.cs", "src/Sim/Turret.cs", "src/Sim/SimBallistics.cs",
                                   "src/Sim/ShipLoadouts.cs", "src/Sim/ShipVariants.cs",
                                   "tests/SimChecks/MainBatteryChecks.cs", "tests/SimChecks/Program.cs")},
    "artifacts_sha256": {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                         for p in sorted(root.iterdir())
                         if p.is_file() and p.name not in ("verification.json", "README.md")},
}
(root / "verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps({k: v for k, v in report.items() if not k.endswith("sha256")}, indent=2))
