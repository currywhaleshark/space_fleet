import csv, sys, statistics as st
lines=open(sys.argv[1],encoding='cp949',errors='replace').read().splitlines()
head=[l for l in lines if not l.startswith('#')]
ident=[l.split(',') for l in lines if l.startswith('#ident,')]
rows=list(csv.DictReader(head))
contact=[l.split(',') for l in lines if l.startswith('#contact,') and not l.startswith('#contact,hull')]
def f(x):
    try: return float(x)
    except: return None
print('probes',len(rows),'matches',len({(r['hull'],r['seed']) for r in rows}))
print('launched <10s:',sum(float(r['launchT'])<10 for r in rows),' <30s:',sum(float(r['launchT'])<30 for r in rows))
for k in ['launchT','nearestCapKm','nearestAnyKm','throwKm','arrivalCapKm']:
    v=[f(r[k]) for r in rows if f(r[k]) is not None]
    print(k,'median',round(st.median(v),1),'p25',round(sorted(v)[len(v)//4],1),'min',min(v),'max',max(v))
print('knownCaps at launch: zero',sum(r['knownCaps']=='0' for r in rows),'of',len(rows))
print('probes with 0 contacts and 0 idents:',sum(r['probeContacts']=='0' and r['probeIdents']=='0' for r in rows))
print('total probe contacts',sum(int(r['probeContacts']) for r in rows),'idents',sum(int(r['probeIdents']) for r in rows))
for a,b in [(0,40),(40,60),(60,80),(80,100),(100,200)]:
    sel=[r for r in rows if f(r['arrivalCapKm']) is not None and a<=f(r['arrivalCapKm'])<b]
    if sel: print(f'arrival nearest enemy non-IC {a}-{b}km: n={len(sel)} contacts={sum(int(r["probeContacts"]) for r in sel)} idents={sum(int(r["probeIdents"]) for r in sel)} useful={sum(int(r["probeContacts"])+int(r["probeIdents"])>0 for r in sel)}')
sel=[r for r in rows if f(r['arrivalCapKm']) is None]
print('never on station:',len(sel),'contacts',sum(int(r['probeContacts']) for r in sel),'idents',sum(int(r['probeIdents']) for r in sel))
for a,b in [(0,10),(10,30),(30,60),(60,300)]:
    sel=[r for r in rows if a<=float(r['launchT'])<b]
    if sel: print(f'launch {a}-{b}s: n={len(sel)} capKm median={st.median(f(r["nearestCapKm"]) for r in sel):.0f} useful={sum(int(r["probeContacts"])+int(r["probeIdents"])>0 for r in sel)} contacts={sum(int(r["probeContacts"]) for r in sel)} idents={sum(int(r["probeIdents"]) for r in sel)}')
print('owner hulls',{h:sum(r['ownerHull']==h for r in rows) for h in sorted({r['ownerHull'] for r in rows})})
print('--- first launches')
for r in sorted(rows,key=lambda r:float(r['launchT']))[:10]: print(r['hull'],r['seed'],r['ownerHull'],'t',r['launchT'],'capKm',r['nearestCapKm'],'knownCaps',r['knownCaps'],'contacts',r['knownContacts'],'throw',r['throwKm'],'arrive',r['arrivalCapKm'],'found',r['probeContacts'],r['probeIdents'])
print('--- fleet own contact times (kind, contacted/ships, firstT, avgT)')
agg={}
for c in contact:
    _,hull,seed,fac,kind,ships,got,first,avg=c
    a=agg.setdefault(kind,[0,0,[],[]]);a[0]+=int(ships);a[1]+=int(got)
    if first!='NaN':a[2].append(float(first));a[3].append(float(avg))
for k,a in agg.items(): print(k,f'{a[1]}/{a[0]} contacted, first median {st.median(a[2]) if a[2] else None}, avg median {st.median(a[3]) if a[3] else None}')

print('--- fleet identification (kind, identified/ships, avgT median)')
agg={}
for c in ident:
    _,hull,seed,fac,kind,ships,got,first,avg=c
    a=agg.setdefault(kind,[0,0,[]]);a[0]+=int(ships);a[1]+=int(got)
    if avg!='NaN':a[2].append(float(avg))
for k,a in agg.items(): print(k,f'{a[1]}/{a[0]} identified, avg time median {st.median(a[2]) if a[2] else None}')
