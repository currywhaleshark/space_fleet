import csv, glob, statistics as st, os, json
def load(tag):
    probes=[];ident=[];contact=[];matches=set()
    for f in sorted(glob.glob(f'cmp_{tag}_*.csv')):
        lines=open(f,encoding='cp949',errors='replace').read().splitlines()
        rows=list(csv.DictReader([l for l in lines if not l.startswith('#')]))
        cfg=os.path.basename(f)[len(f'cmp_{tag}_'):-4]
        for r in rows: r['cfg']=cfg
        probes+=rows
        for l in lines:
            if l.startswith('#ident,'): p=l.split(',');ident.append(p+[cfg]);matches.add((cfg,p[1],p[2]))
            if l.startswith('#contact,') and not l.startswith('#contact,hull'): contact.append(l.split(',')+[cfg])
    return probes,ident,contact,matches
out={}
for tag in ['before','after']:
    probes,ident,contact,matches=load(tag)
    useful=[r for r in probes if int(r['probeContacts'])+int(r['probeIdents'])>0]
    def med(k,rows=probes):
        v=[float(r[k]) for r in rows if r[k] not in ('','NaN')]
        return round(st.median(v),1) if v else None
    d=dict(matches=len(matches),probes=len(probes),perMatch=round(len(probes)/max(1,len(matches)),1),
        launchedFirst30s=sum(float(r['launchT'])<30 for r in probes),launchMedian=med('launchT'),
        throwKmMedian=med('throwKm'),arrivalCapKmMedian=med('arrivalCapKm'),
        usefulShare=round(len(useful)/max(1,len(probes)),3),
        newContacts=sum(int(r['probeContacts']) for r in probes),newIdents=sum(int(r['probeIdents']) for r in probes),
        launchersEarth=sum(not r['ownerHull'].startswith('mars') for r in probes),launchersMars=sum(r['ownerHull'].startswith('mars') for r in probes))
    for kind in ['Escort','Interceptor']:
        avgs=[float(p[8]) for p in ident if p[4]==kind and p[8]!='NaN'];got=sum(int(p[6]) for p in ident if p[4]==kind);n=sum(int(p[5]) for p in ident if p[4]==kind)
        d[f'ident{kind}']=f'{got}/{n}';d[f'ident{kind}AvgSecMedian']=round(st.median(avgs),1) if avgs else None
        cav=[float(p[8]) for p in contact if p[4]==kind and p[8]!='NaN']
        d[f'contact{kind}AvgSecMedian']=round(st.median(cav),1) if cav else None
    out[tag]=d
print(json.dumps(out,ensure_ascii=False,indent=1))
