"""Paired gunnery candidates against preserved escort-trial baseline evidence."""
import argparse
from collections import Counter
import gzip
import json
from pathlib import Path
from statistics import mean
import sys

sys.stdout.reconfigure(encoding='utf-8')


def read(path):
    data=path.read_bytes()
    return json.loads((gzip.decompress(data) if path.suffix=='.gz' else data).decode('utf-8'))


def summarize(data, baseline):
    trials=data['Trials']
    assert len({r['Case']['Id'] for r in trials})==len(trials)
    groups=[]
    for distance in data['Distances']:
        for e,m in data.get('SupportPairs',[[0,0],[2,2],[2,0],[0,2]]):
            rows=[r for r in trials if (r['Case']['Distance'],r['Case']['EarthEscorts'],r['Case']['MarsEscorts'])==(distance,e,m)]
            old=[baseline[r['Case']['Id']] for r in rows]
            def ships(side):
                s=[s for r in rows for s in r['Ships'] if s['Design']==side and s['Kind']=='Battleship']
                fired=[x for x in s if x['FirstFire'] is not None]
                rounds=sum(x['Rails'] for x in s); hits=sum(x['RailHits'] for x in s)
                return dict(fired=len(fired),rounds=rounds,hits=hits,hit_fraction=hits/rounds if rounds else None,
                    blind_linked_rounds=sum(x['BlindLinkedRounds'] for x in s),
                    first_fire_mean=mean(x['FirstFire'] for x in fired) if fired else None,
                    module_damage=sum(x['ModuleDamageInflicted'] for x in s))
            groups.append(dict(distance=distance,earth_escorts=e,mars_escorts=m,n=len(rows),
                baseline_wins=dict(Counter(r['Winner'] for r in old)),wins=dict(Counter(r['Winner'] for r in rows)),
                transitions=dict(Counter(a['Winner']+'->'+b['Winner'] for a,b in zip(old,rows))),
                earth=ships('Earth'),mars=ships('Mars'),
                mean_duration=mean(r['Time'] for r in rows)))
    return dict(tuning=data.get('Tuning',{}),seeds=data['Seeds'],games=len(trials),groups=groups)


if __name__=='__main__':
    ap=argparse.ArgumentParser()
    ap.add_argument('--baseline',type=Path,required=True)
    ap.add_argument('--inputs',type=Path,nargs='+',required=True)
    ap.add_argument('--output',type=Path,required=True)
    args=ap.parse_args()
    baseline={r['Case']['Id']:r for r in read(args.baseline)['Trials']}
    result={p.name:summarize(read(p),baseline) for p in args.inputs}
    args.output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    for name,summary in result.items():
        print(name,'games',summary['games'])
        for g in summary['groups']:
            if g['earth_escorts']!=g['mars_escorts']: continue
            print(f"  {g['distance']/1000:3.0f}km +{g['earth_escorts']}: {g['wins']}, "
                  f"BB fired E/M {g['earth']['fired']}/{g['mars']['fired']}, "
                  f"hit E/M {g['earth']['hit_fraction']:.1%}/{g['mars']['hit_fraction']:.1%}")
