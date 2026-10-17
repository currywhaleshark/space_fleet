"""Check the exported package, test evidence and assembly without altering the build."""
import hashlib
import json
from pathlib import Path
import re
import zipfile

here = Path(__file__).resolve().parent
repo = here.parents[2]
name = "SpaceFleet-20261017-confirmations-Windows-x64"
release = repo / "builds" / name
archive = release.with_suffix(".zip")
info = json.loads((release / "build-info.json").read_text(encoding="utf-8-sig"))
digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
sha = digest(archive)
assert sha == Path(str(archive) + ".sha256").read_text().split()[0]
with zipfile.ZipFile(archive) as bundle:
    assert bundle.testzip() is None
    entries = len(bundle.infolist())
assembly = release / "data_SpaceFleet_windows_x86_64" / "SpaceFleet.dll"
latest = repo / ".godot/mono/temp/bin/ExportRelease/win-x64/SpaceFleet.dll"
assert digest(assembly) == digest(latest)
assert (assembly.parent / "coreclr.dll").is_file()
assert (release / "SpaceFleet.exe").is_file()
assert len(info["checks"]) == 15
counts = [int(re.search(r"PASS: (\d+)", check)[1]) for check in info["checks"]]
source_counts = []
for test in ["hangar", "progression", "flow", "match-flow"]:
    log = (here / f"{test}-test.log").read_text(encoding="utf-8-sig")
    assert "ERROR:" not in log and "Exception" not in log
    source_counts.append(int(re.search(r"PASS: (\d+)", log)[1]))
report = {
    "release": name, "exe": str(release / "SpaceFleet.exe"), "zip": str(archive),
    "zipBytes": archive.stat().st_size, "sha256": sha, "zipEntries": entries, "zipCrc": "pass",
    "releaseCheckGroups": len(counts), "releaseChecks": sum(counts), "sourceChecks": sum(source_counts),
    "checks": info["checks"], "selfContained": True,
    "assemblySha256": digest(assembly), "matchesLatestExportAssembly": True,
    "sourceCommit": info["sourceCommit"], "localChanges": info["localChanges"],
}
(here / "release-verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps({key: report[key] for key in ["release", "releaseChecks", "sourceChecks", "zipCrc", "matchesLatestExportAssembly"]}))
