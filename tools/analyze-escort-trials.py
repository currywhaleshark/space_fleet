"""Summarize the opt-in BattleBatch --escort-trials experiment without tuning data."""
import argparse
from collections import Counter
import gzip
import hashlib
import json
from pathlib import Path
from statistics import mean
import sys
import zipfile

sys.stdout.reconfigure(encoding="utf-8")


def load_json(path):
    if path.suffix == ".gz":
        with gzip.open(path,"rt",encoding="utf-8") as f:
            return json.load(f)
    return json.loads(path.read_text(encoding="utf-8"))


def average(values):
    values = [v for v in values if v is not None]
    return mean(values) if values else None


def ship_stats(trials, design, kind):
    ships = [s for r in trials for s in r["Ships"] if s["Design"] == design and s["Kind"] == kind]
    def total(key):
        return sum(s[key] for s in ships)
    def avg(key):
        return average(s[key] for s in ships)
    damage = total("ModuleDamageInflicted")
    side_damage = sum(s["ModuleDamageInflicted"] for r in trials for s in r["Ships"] if s["Design"] == design)
    return dict(ships=len(ships), fired=sum(s["Rails"] > 0 for s in ships),
                first_fire_seconds=avg("FirstFire"), first_fire_range_km=average(s["FirstFireRange"]/1000 for s in ships if s["FirstFireRange"] is not None),
                rounds=total("Rails"), hits=total("RailHits"), linked_rounds=total("LinkedRounds"),
                blind_rounds=total("BlindRounds"), blind_linked_rounds=total("BlindLinkedRounds"),
                blind_link_firing_ships=sum(s["BlindLinkedRounds"] > 0 for s in ships),
                lost_sensors=sum(s["FirstBlind"] is not None for s in ships),
                active_seconds=total("ActiveSeconds"), lock_seconds=total("LockedSeconds"),
                linked_lock_seconds=total("LinkedLockSeconds"), blind_seconds=total("BlindSeconds"),
                blind_no_lock_seconds=total("BlindNoLockSeconds"),
                missile_launches=total("Missiles"), missile_hits=total("MissileHits"),
                missile_intercepts=total("MissilesIntercepted"), drone_kills=total("DronesDestroyed"),
                module_damage=damage, module_damage_share=damage/side_damage if side_damage else None,
                shield_damage=total("ShieldDamageInflicted"),
                destroyed=sum(s["Destroyed"] for s in ships), disabled_at_end=sum(s["Disabled"] for s in ships),
                first_disable_count=sum(s["DisabledAt"] is not None for s in ships),
                mean_first_disable_seconds=avg("DisabledAt"), mean_destroyed_seconds=avg("DestroyedAt"),
                mean_escort_distance_km=average(s["MeanEscortDistance"]/1000 for s in ships if s["MeanEscortDistance"] is not None))


def analyze(path):
    data = load_json(path)
    trials = data["Trials"]
    supports = data.get("SupportPairs", [[0,0],[2,2],[2,0],[0,2]])
    expected = len(data["Seeds"]) * len(data["Distances"]) * 4 * len(supports)
    assert len(trials) == expected
    assert len({r["Case"]["Id"] for r in trials}) == expected
    assert all(r["DroppedEvents"] == 0 for r in trials)
    for r in trials:
        c = r["Case"]
        assert len(r["Ships"]) == 2+c["EarthEscorts"]+c["MarsEscorts"]
        for s in r["Ships"]:
            assert 0 <= s["BlindLinkedRounds"] <= s["LinkedRounds"] <= s["Rails"]
            assert s["BlindLinkedRounds"] <= s["BlindRounds"] <= s["Rails"]
            assert s["LinkedLockSeconds"] <= s["LockedSeconds"] <= s["ActiveSeconds"] + 1e-6
            if not c[s["Design"]+"Escorts"]:
                assert s["LinkedRounds"] == 0

    groups = []
    pairs = []
    for distance in data["Distances"]:
        by_support = {}
        for earth, mars in supports:
            runs = [r for r in trials if (r["Case"]["Distance"], r["Case"]["EarthEscorts"], r["Case"]["MarsEscorts"]) == (distance, earth, mars)]
            assert len(runs) == len(data["Seeds"])*4
            by_support[earth, mars] = {(r["Case"]["Seed"], r["Case"]["EarthBlue"], r["Case"]["Mirror"]): r for r in runs}
            groups.append(dict(distance_km=distance/1000, earth_escorts=earth, mars_escorts=mars,
                games=len(runs), wins=dict(Counter(r["Winner"] for r in runs)),
                mean_duration_seconds=average(r["Time"] for r in runs),
                min_duration_seconds=min(r["Time"] for r in runs),
                reasons=dict(Counter(r["Reason"] for r in runs)),
                by_team={str(eb):dict(Counter(r["Winner"] for r in runs if r["Case"]["EarthBlue"]==eb)) for eb in [True,False]},
                by_seed={str(seed):dict(Counter(r["Winner"] for r in runs if r["Case"]["Seed"]==seed)) for seed in data["Seeds"]},
                ships={f"{design}_{kind}":ship_stats(runs,design,kind) for design in ["Earth","Mars"] for kind in ["Battleship","Escort"]}))
        for before, after in [((0,0),(2,2)),((0,0),(2,0)),((0,0),(0,2)),((0,2),(2,2)),((2,0),(2,2))]:
            if before not in by_support or after not in by_support: continue
            a,b = by_support[before],by_support[after]
            pairs.append(dict(distance_km=distance/1000, before=before, after=after,
                outcome_transitions=dict(Counter(a[k]["Winner"]+"->"+b[k]["Winner"] for k in a)),
                earth_score_change=sum((b[k]["Winner"]=="Earth")-(a[k]["Winner"]=="Earth") for k in a)/len(a)))
    result=dict(games=expected, independent_pose_seeds=len(data["Seeds"]),
                groups=groups, paired=pairs,
                warning="Team/mirror variants reuse the same seed; these are not fully independent trials. Ending at victory censors ship survival time.")
    base=path.with_suffix("") if path.suffix==".gz" else path
    out=base.with_name(base.stem+".summary.json")
    out.write_text(json.dumps(result,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print(f"Validated {expected} trials; saved {out}")
    for g in groups:
        e=g['ships']['Earth_Battleship']; m=g['ships']['Mars_Battleship']
        print(f"{g['distance_km']:3.0f} km E{g['earth_escorts']} M{g['mars_escorts']}: {g['wins']} "
              f"BB rounds E/M {e['rounds']}/{m['rounds']}, blind-linked E/M {e['blind_linked_rounds']}/{m['blind_linked_rounds']}")
    return result


def check_artifacts(path):
    root=Path(__file__).resolve().parents[1]
    before=json.loads((path.parent/"production-before.json").read_text(encoding="utf-8"))
    assert all(hashlib.sha256((root/p).read_bytes()).hexdigest()==h for p,h in before.items()), "Production changed during measurement"
    base=path.with_suffix("") if path.suffix==".gz" else path
    traces=base.with_suffix(".traces")
    raw=load_json(path)["Trials"]
    archive=traces.with_suffix(".traces.zip")
    if traces.is_dir():
        assert len(list(traces.glob("*.csv.gz")))==len(raw)
        blocks=[p.read_bytes() for p in sorted(traces.glob("*.csv.gz"))]
    else:
        with zipfile.ZipFile(archive) as z:
            assert len(z.namelist())==len(raw)
            blocks=[z.read(r['Case']['Id']+'.csv.gz') for r in raw]
    for block in blocks:
        lines=gzip.decompress(block).decode('utf-8').splitlines()  # Full read verifies CRC.
        assert len(lines)>1 and lines[0].startswith("seed,mirror,time,")
    # Pilot and full-run seed 1 should agree, including event/module outcomes.
    pilot=path.parent/"pilot.json"
    if not pilot.exists(): pilot=pilot.with_suffix(".json.gz")
    repeat=0
    if pilot.exists():
        by_id={r["Case"]["Id"]:r for r in raw}
        for r in load_json(pilot)["Trials"]:
            actual=by_id[r["Case"]["Id"]]
            assert {k:v for k,v in actual.items() if k!='WallSeconds'} == {k:v for k,v in r.items() if k!='WallSeconds'}
            repeat+=1
    checks=dict(production_hashes_unchanged=len(before), gzip_traces_verified=len(raw), pilot_repeated_identically=repeat)
    (path.parent/"artifact-checks.json").write_text(json.dumps(checks,indent=2)+"\n",encoding="utf-8")
    print(checks)


if __name__=="__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("results",type=Path)
    parser.add_argument("--check-artifacts",action="store_true")
    args=parser.parse_args()
    analyze(args.results)
    if args.check_artifacts:
        check_artifacts(args.results)
