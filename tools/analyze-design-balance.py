"""Summarize paired 24-ship and 1v1 experiments without treating draws as wins."""
import argparse
import collections
import csv
import json
from pathlib import Path

p=argparse.ArgumentParser();p.add_argument('folder',type=Path);a=p.parse_args()
results=[]
raw={}
for path in sorted(a.folder.glob('*.csv')):
    if len(path.suffixes)!=1: continue
    with path.open(encoding='utf-8-sig') as f: battles=list(csv.DictReader(f))
    ships_path=path.with_suffix('.ships.csv')
    if not ships_path.exists(): continue
    with ships_path.open(encoding='utf-8-sig') as f: ships=list(csv.DictReader(f))
    raw[path.stem]=(battles,ships)
    groups=collections.defaultdict(list)
    for s in ships: groups[(s['faction'],s['design'],s['kind'])].append(s)
    metrics=[]
    def num(row,key): return float(row[key] or 0)
    for (team,design,kind),rows in groups.items():
        def total(k): return sum(num(r,k) for r in rows)
        launches=total('rails'); samples=total('railRangeSamples')
        # Denominator is all opposing missiles launched, including shots headed to other friendly hulls.
        incoming=sum(num(r,'missiles') for r in ships if r['faction']!=team)
        metrics.append(dict(team=team,design=design,kind=kind,ships=len(rows),
            shieldDamageInflicted=round(total('shieldDamageInflicted'),2),moduleDamageInflicted=round(total('moduleDamageInflicted'),2),
            meanSurvivalSeconds=round(total('survivalSeconds')/len(rows),2),destroyed=int(total('destroyed')),disabled=int(total('disabled')),
            railShots=int(launches),railHits=int(total('railHits')),railHitRate=round(total('railHits')/launches,4) if launches else None,
            meanRailRangeKm=round(total('railRangeSum')/samples/1000,2) if samples else None,
            missiles=int(total('missiles')),missileHits=int(total('missileHits')),missilesIntercepted=int(total('missilesIntercepted')),
            shareOfEnemyLaunchesIntercepted=round(total('missilesIntercepted')/incoming,4) if incoming else None,
            torpedoes=int(total('torpedoes')),torpedoHits=int(total('torpedoHits'))))
    results.append(dict(case=path.stem,battles=len(battles),outcomes=dict(collections.Counter(r['winner'] for r in battles)),metrics=metrics))
(a.folder/'analysis.json').write_text(json.dumps(results,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
for r in results: print(r['case'],r['outcomes'])

lines=['# 설계 계통 전투 비교', '',
    '자동 생성 원자료 요약. 무승부를 승리로 계산하지 않는다. 동일 시드의 공간 반전은 독립 표본이 아니다.', '',
    '## 24척 함대전', '', '| Blue / Red | 판수 | Blue 승 | Red 승 | 무승부 |', '|---|---:|---:|---:|---:|']
for r in results:
    if r['case'].startswith('duel-'): continue
    o=r['outcomes']; lines.append(f"| {r['case']} | {r['battles']} | {o.get('Blue',0)} | {o.get('Red',0)} | {o.get('Draw',0)} |")

cross=[row for case,(_,ships) in raw.items() if case in ('Earth-Mars','Mars-Earth') for row in ships]
groups=collections.defaultdict(list)
for s in cross: groups[(s['design'],s['kind'])].append(s)
lines += ['', '## 비대칭 함대전의 함종별 기여', '',
    'Earth-Mars와 Mars-Earth를 합산한다. 피해는 함선 1척당 누적 실드/모듈 피해의 평균이다. 생존은 최초 무력화 또는 격침까지이며, 끝까지 전투 가능하면 판정 시각에서 관측을 종료한다. 격침과 무력화 수는 종료 시점이며 서로 겹칠 수 있다.', '',
    '| 설계 / 함종 | 표본 함선 | 실드 피해 | 모듈 피해 | 생존(s) | 격침 / 무력화 | 주포 명중률 | 평균 발사거리(km) |',
    '|---|---:|---:|---:|---:|---:|---:|---:|']
for (design,kind),rows in sorted(groups.items()):
    def total(k): return sum(float(r[k] or 0) for r in rows)
    n=len(rows); shots=total('rails'); samples=total('railRangeSamples')
    hit=f"{total('railHits')/shots:.1%}" if shots else '발사 없음'
    dist=f"{total('railRangeSum')/samples/1000:.1f}" if samples else '—'
    lines.append(f"| {design} / {kind} | {n} | {total('shieldDamageInflicted')/n:.1f} | {total('moduleDamageInflicted')/n:.1f} | {total('survivalSeconds')/n:.1f} | {int(total('destroyed'))} / {int(total('disabled'))} | {hit} | {dist} |")

lines += ['', '| 설계 | 일반 미사일 발사 / 명중 | 적 일반 미사일 물리 요격 / 상대 발사 | AM 발사 / 명중 | AM 격리 실패 / 투기 |', '|---|---:|---:|---:|---:|']
for design in ('Earth','Mars'):
    rows=[r for r in cross if r['design']==design]
    def total(k): return int(sum(float(r[k] or 0) for r in rows))
    incoming=sum(int(r['missiles']) for r in cross if r['design']!=design)
    ratio=f'{total("missilesIntercepted")/incoming:.1%}' if incoming else '—'
    lines.append(f'| {design} | {total("missiles")} / {total("missileHits")} | {total("missilesIntercepted")} / {incoming} ({ratio}) | {total("torpedoes")} / {total("torpedoHits")} | {total("amFailures")} / {total("amJettisons")} |')
lines += ['', '물리 요격은 PD와 방어 드론의 실제 제거만 포함한다. 빗나감·디코이·수명 소멸은 포함하지 않는다. 분모는 상대의 전체 발사량이다.', '',
    '## 거리별 1:1 비대칭 대전', '', '| 함종 | 시작 거리(km) | 판수 | Earth 승 | Mars 승 | 무승부 |', '|---|---:|---:|---:|---:|---:|']
duels=collections.defaultdict(collections.Counter)
for case,(battles,_) in raw.items():
    if not case.startswith('duel-'): continue
    _,kind,distance,blue,red=case.split('-')
    if blue==red: continue
    key=(kind,int(distance))
    for b in battles: duels[key][blue if b['winner']=='Blue' else red if b['winner']=='Red' else 'Draw']+=1
for (kind,distance),o in sorted(duels.items()):
    lines.append(f'| {kind} | {distance/1000:g} | {sum(o.values())} | {o["Earth"]} | {o["Mars"]} | {o["Draw"]} |')
lines += ['', '각 대칭 1:1 결과와 함종별 세부 합계는 `analysis.json`, 원전투·함선·어뢰 기록은 같은 폴더의 CSV에 보존한다.', '']
(a.folder/'report.md').write_text('\n'.join(lines),encoding='utf-8')
