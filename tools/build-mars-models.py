"""Author only Mars sources/GLBs. Earth .blend, GLB and JSON files are never opened for writing.
blender --background --python-exit-code 1 --python tools/build-mars-models.py -- --root <repo> --render
Shared hard-surface primitives, independently authored silhouettes and data-driven fittings.
"""
import importlib.util
import json
import math
from pathlib import Path
import sys
import bpy
from mathutils import Vector

root = Path(sys.argv[sys.argv.index('--root') + 1])
sys.path.insert(0, str(root/'tools'))
spec = importlib.util.spec_from_file_location('earth_primitives', root/'tools/build-ship-models.py')
base = importlib.util.module_from_spec(spec); spec.loader.exec_module(base)
from fleet_model_export import rig_turrets, rig_point_defense, export_collection, g


class MarsShip(base.Ship):
    def __init__(self, key):
        super().__init__(key)
        colors = {'armor': (.19,.205,.22), 'plate': (.32,.092,.045), 'edge': (.09,.1,.105),
                  'copper': (.38,.19,.065), 'engine': (1,.25,.025), 'shield': (.72,.24,.055),
                  'glass': (.065,.13,.17), 'radiator': (.026,.034,.042)}
        for key,color in colors.items():
            mat = self.m[key]; mat.diffuse_color=(*color,1)
            bsdf=mat.node_tree.nodes.get('Principled BSDF'); bsdf.inputs['Base Color'].default_value=(*color,1)
            if key in ('engine','shield'): bsdf.inputs['Emission Color'].default_value=(*color,1)

    def section(self, name): return next(s for s in self.data['hullSections'] if s['id']==name)

    def fixtures(self):
        """Guide boxes, markers and physical external equipment all come from the hull JSON."""
        L=self.data['flight']['length']; small=L<100
        for m in self.data['modules']:
            c=m['center']; h=m['halfSize']; kind=m['kind']
            self.socket('module_'+m['id'].replace('-','_'),c,module_id=m['id'],module_kind=kind,half_size=h)
            guide=self.box('HIT VOLUME | '+m['id'],c,[a*2 for a in h],'iff',0)
            self.coll.objects.unlink(guide); self.guides.objects.link(guide); self.meshes.remove(guide)
            guide.display_type='WIRE'; guide.hide_render=True; guide.hide_set(True)
            if kind=='Thruster':
                # Nozzle lives on the aft face of the engine module, or on the hull skin when the module is buried.
                aft=c[2]+h[2]
                for sec in self.data['hullSections']:
                    sc,sh=sec['center'],sec['halfSize']
                    if sec['id'].startswith('radiator'): continue
                    if abs(c[0]-sc[0])<=sh[0] and abs(c[1]-sc[1])<=sh[1]: aft=max(aft,sc[2]+sh[2])
                r=min(h[0],h[1])*.92; p=[c[0],c[1],max(c[2]+h[2],aft+r*.55)]
                self.box('Engine load cradle '+m['id'],(c[0],c[1],c[2]-h[2]*.4),
                         (r*1.7,r*1.7,h[2]*2.5),'edge',r*.08)
                self.engine(p,r,L*.10,m['visualEngineIndex'])
            if m['id'].startswith('radiator-'):
                self.vertical_radiator(m)
            if kind=='Sensor':
                if not small:
                    bridge=self.section('bridge'); top=bridge['center'][1]+bridge['halfSize'][1]
                    self.box('Sensor head pedestal',(c[0],(top+c[1]-h[1])/2,c[2]),(h[0]*.9,c[1]-h[1]-top+2,h[2]*.9),'edge',.5)
                if small: self.box('Sensor housing '+m['id'],c,[a*1.98 for a in h],'edge')
                else:
                    self.box('Sensor housing '+m['id'],(c[0],c[1]-h[1]*.35,c[2]),(h[0]*1.6,h[1]*1.3,h[2]*1.7),'edge',L*.002)
                    self.box('Sensor crown '+m['id'],(c[0],c[1]+h[1]*.5,c[2]),(h[0]*.9,h[1]*.7,h[2]*1.0),'armor',L*.001)
                for side in (-1,1):
                    self.box('External phased array', (c[0]+side*h[0],c[1],c[2]),(h[0]*.12,h[1]*1.85,h[2]*1.85),'dark',0)
                    for j in range(5):
                        self.box('Array emitter grid',(c[0]+side*h[0]*1.065,c[1]+(j-2)*h[1]*.32,c[2]),
                                 (h[0]*.015,h[1]*.035,h[2]*1.65),'copper',0)
            if kind=='AntimatterContainment':
                # The storage module is inside the hull; expose its access hatch on the starboard skin.
                x=self.section('hull')['halfSize'][0]
                self.box('AM containment armored access',(x,c[1],c[2]),(.08,h[1]*1.6,h[2]*1.75),'plate',.025)
                for z in (-.6,.6): self.box('AM magnetic loop indicator',(x+.045,c[1],c[2]+z*h[2]),(.035,h[1]*1.3,.06),'shield',0)
                self.socket('fx_am_containment',(x+.05,c[1],c[2]))
        self.socket('socket_railgun_muzzle',self.data['railgun']['muzzle'])
        self.socket('socket_missile_launch',self.data['missiles']['launchPoint'])
        if self.data.get('antimatter'): self.socket('socket_antimatter_launch',self.data['antimatter']['flight']['launchPoint'])
        size={'Battleship':4,'Escort':1.5,'Interceptor':.55}[self.data['kind']]
        for i,p in enumerate(self.data['pointDefense']['mounts']):
            self.socket(f'socket_point_defense_{i}',p)
            self.support(p,size*1.1,'PD '+str(i)+' pedestal')
            self.point_defense(p,size,i)
        for mount in self.data['railgun'].get('mounts',[]): self.precision_turret(mount)

    def support(self,p,r,name):
        sign=1 if p[1]>=0 else -1
        surfaces=[]
        for sec in self.data['hullSections']:
            c,h=sec['center'],sec['halfSize']
            if abs(p[0]-c[0])<=h[0]+r and abs(p[2]-c[2])<=h[2]+r:
                y=c[1]+sign*h[1]
                if sign*(p[1]-y)>=-r: surfaces.append(y)
        y=(max(surfaces) if sign>0 else min(surfaces)) if surfaces else 0
        self.box(name,(p[0],(p[1]+y)/2,p[2]),(r*2,abs(p[1]-y)+r*.35,r*2),'edge',r*.12)

    def precision_turret(self,m):
        p=Vector(m['pivot']); t=Vector(m['trunnion']); sign=-1 if m['ventral'] else 1
        def world(v): return p+Vector((v[0]*sign,v[1]*sign,v[2]))
        name='Rail battery '+m['moduleId']
        h=m['housingHalfSize']; c=m['housingCenter']; s=h[0]
        self.support(p,s*.88,name+' foundation')
        # Wide barbette ring and a low, long base slab: the gun house reads planted, not perched.
        self.cylinder(name+' bearing',p,s*1.22,s*.22,'edge',axis=(0,1,0),sides=28)
        base_top=max(.2*s,t[1]-s*.42)
        self.box(name+' gun house base',world((0,base_top/2,-h[2]*.15)),(s*2.25,base_top,h[2]*2.7),'armor',s*.08)
        self.box(name+' base glacis',world((0,base_top*.55,-h[2]*1.55)),(s*1.9,base_top*.8,h[2]*.5),'plate',s*.06)
        # Two stationary cheeks leave the entire elevation sweep open between them.
        for side in (-1,1):
            self.box(name+' armored cheek',world((side*s*.86,c[1]+h[1]*.1,c[2]-h[2]*.25)),(s*.48,h[1]*2.3,h[2]*2.5),'armor',s*.09)
            self.box(name+' cheek insert',world((side*s*.87,c[1]+h[1]*1.18,c[2]-h[2]*.25)),(s*.36,s*.04,h[2]*2.05),'plate',s*.012)
            self.box(name+' cheek sponson',world((side*s*1.12,base_top*.5+h[1]*.4,c[2]+h[2]*.2)),(s*.22,h[1]*1.5,h[2]*1.6),'plate',s*.05)
            self.cylinder(name+' armored hinge',world((side*s*.93,t[1],0)),s*.3,s*.3,'plate',axis=(1,0,0))
        self.box(name+' rear counterweight',world((0,c[1],h[2]*1.05)),(s*1.95,h[1]*1.7,h[2]*.95),'armor',s*.08)
        self.box(name+' counterweight cap',world((0,c[1]+h[1]*.9,h[2]*1.05)),(s*1.5,s*.06,h[2]*.75),'plate',s*.02)
        self.cylinder(name+' conductor trunnion',world(t),s*.26,s*1.95,'edge',axis=(1,0,0))
        far=min(m['muzzles'][0][2],-s)
        # Elevating mantlet and armoured cradle: the barrels grow out of a heavy sleeve, then step down.
        self.box(name+' rail shroud mantlet',world(t+Vector((0,0,-s*.25))),(s*1.12,s*.62,s*.85),'armor',s*.06)
        self.box(name+' rail shroud cradle',world(t+Vector((0,0,far*.18))),(s*.98,s*.5,abs(far)*.3),'armor',s*.05)
        self.box(name+' rail shroud cradle step',world(t+Vector((0,0,far*.42))),(s*.82,s*.38,abs(far)*.2),'plate',s*.04)
        for muzzle in m['muzzles']:
            start=t+Vector((muzzle[0],0,s*.15)); end=t+Vector(muzzle)
            mid=(start+end)/2; length=abs(end.z-start.z)
            self.box(name+' rail shroud',world(mid),(s*.26,s*.26,length),'dark',s*.03)
            jacket=start.lerp(end,.62)
            self.box(name+' rail shroud jacket',world((start+jacket)/2),(s*.34,s*.34,(jacket-start).length),'edge',s*.03)
            for side in (-1,1):
                self.box(name+' conductor',world(mid+Vector((side*s*.16,0,0))),(s*.08,s*.17,length*.985),'edge',s*.013)
            for j in range(6):
                at=start.lerp(end,(j+.5)/6); grow=1.25-j*.08
                self.box(name+' accelerator clamp',world(at),(s*.42*grow,s*.36*grow,s*.22),'plate' if j%3==0 else 'armor',s*.025)
            self.tube(name+' open muzzle',world(end+Vector((0,0,s*.11))),s*.14,s*.24,'edge')

    def vertical_radiator(self,m):
        c=Vector(m['center']); w,h,d=m['halfSize']; sign=1 if c.x>0 else -1
        hull=self.section('hull'); edge=hull['halfSize'][0]*sign
        self.box('Radiator perimeter',c,(2*w,2*h,2*d),'edge',w*.25)
        for side in (-1,1):
            x=c.x+side*w
            self.box('Graphite radiator face',(x,c.y,c.z),(.2*w,1.86*h,1.92*d),'radiator',0)
            for j in range(21):
                z=c.z-d*.90+j*d*1.8/20
                self.box('Thermal fin',(x+side*w*.12,c.y,z),(.12*w,1.78*h,d*.018),'copper',0)
        for y in (-h*.95,h*.95):
            self.box('Radiator edge rail',(c.x,c.y+y,c.z),(w*2.2,h*.07,d*2.05),'plate',w*.2)
        for z in (-d*.80,d*.80):
            for y in (-h*.65,h*.65):
                self.rod('Radiator structural link',(edge,y,c.z+z),(c.x,y,c.z+z),max(w*.8,.12),'edge')

    def capital(self):
        bb=self.data['kind']=='Battleship'; L=self.data['flight']['length']; k=L/1080
        hull=self.section('hull'); c=Vector(hull['center']); w,h,d=hull['halfSize']
        # Collision sections are also the base shell: module compartments sit inside actual visible solids.
        bays=12; span=2*d/bays
        truss0, truss1 = c.z-d+3*span, c.z-d+9*span   # open-frame middle (bays 3..8)
        for sec in self.data['hullSections']:
            if sec['id'].startswith(('radiator','bow','sensor-head')) or sec['id'] in ('bridge','engine-block'): continue
            p=sec['center']; half=sec['halfSize']
            if sec['id']=='hull':
                # Solid armoured fore/aft blocks; the middle is a narrow core inside an exposed space frame.
                z0,z1=p[2]-half[2],p[2]+half[2]
                self.loft('Pressure hull fore',[(z0,half[0]*.97,half[1],p[1]),(truss0,half[0]*.97,half[1],p[1])],'dark')
                self.loft('Pressure hull core',[(truss0,half[0]*.48,half[1]*.94,p[1]),(truss1,half[0]*.48,half[1]*.94,p[1])],'dark')
                self.loft('Pressure hull aft',[(truss1,half[0]*.97,half[1],p[1]),(z1,half[0]*.97,half[1],p[1])],'dark')
                continue
            self.loft('Pressure hull '+sec['id'],[(p[2]-half[2],half[0]*.97,half[1],p[1]),
                      (p[2]+half[2],half[0]*.97,half[1],p[1])],'dark')
        bow=min(s['center'][2]-s['halfSize'][2] for s in self.data['hullSections'])
        aft=max(s['center'][2]+s['halfSize'][2] for s in self.data['hullSections'])
        self.box('Continuous axial spine',(0,-h*.2,(bow+aft-25*k)/2),(w*.35,h*.32,aft-bow-25*k),'edge',k*2)
        for sec in self.data['hullSections']:
            if not sec['id'].startswith('bow'): continue
            p=sec['center']; a,b,f=sec['halfSize']
            self.loft('Faceted prow shell',[(p[2]-f,a*.70,b*.85,p[1]),(p[2]+f,a,b,p[1])],'armor')
            for side in (-1,1):
                self.box('Prow armored splint',(side*a*.30,p[1]+b*.87,p[2]),(a*.35,k*3,f*1.80),'plate',k*.65)
                self.rod('Prow seam',(side*a*.55,p[1]+b*.66,p[2]-f*.8),(side*a*.78,p[1]+b*.66,p[2]+f*.85),k*.9)
                for j in range(5): self.box('Prow plate fastener',(side*a*.57,p[1]+b*.90,p[2]-f*.7+j*f*.33),(k*2,k*.6,k*2),'copper',0)
        for j in range(bays):
            z=c.z-d+(j+.5)*span
            open_bay=3<=j<=8
            for side in (-1,1):
                x=side*w*1.01
                tile=w*(.46 if open_bay else .94); tx=side*(w*.24 if open_bay else w*.52)
                self.box('Deck armor tile',(tx,h*.98,z),(tile,h*.12,span*.91),'armor',k*1.5)
                self.box('Ventral armor tile',(tx,-h*.98,z),(tile,h*.10,span*.91),'armor',k*1.1)
                if not open_bay:
                    self.box('Armored end cassette',(x,0,z),(w*.08,h*1.30,span*.87),'plate' if j%3==0 else 'armor',k)
                    self.box('Inset cassette',(x+side*k*2,0,z),(k*.7,h*.80,span*.61),'dark',k*.35)
                    if j<3:   # fore armour sponsons widen the solid prow block
                        self.box('Fore armour sponson',(side*(w+k*7),-h*.05,z),(k*12,h*1.25,span*.95),'armor' if j%2 else 'plate',k*1.2)
                        self.box('Sponson seam',(side*(w+k*13.2),-h*.05,z),(k*.8,h*.9,span*.7),'dark',0)
                else:
                    # Open space frame: heavy chords, posts and X bracing on the side and top/bottom faces.
                    for y in (-h*.9,h*.9):
                        self.box('Truss chord',(x,y,z),(k*6,k*6,span*1.01),'edge',k*.6)
                        self.box('Truss chord inner',(side*w*.55,y,z),(k*4,k*4,span*1.01),'edge',k*.4)
                    self.rod('Cross truss',(x,-h*.88,z-span/2),(x,h*.88,z+span/2),k*2.6)
                    self.rod('Cross truss',(x,h*.88,z-span/2),(x,-h*.88,z+span/2),k*2.6)
                    for y in (-h*.9,h*.9):
                        self.rod('Lateral truss',(side*w*.5,y,z-span/2),(x,y,z+span/2),k*1.8)
                        self.rod('Lateral truss',(side*w*.5,y,z+span/2),(x,y,z-span/2),k*1.8)
                        self.rod('Frame cross beam',(side*w*.5,y,z-span/2),(x,y,z-span/2),k*2.2)
                    if j%2==0:
                        self.rod('Core coolant loop',(side*w*.5,-h*.4,z-span*.4),(side*w*.5,-h*.4,z+span*.4),k*3,'copper')
                self.box('Frame bulkhead',(x,0,z-span*.5),(k*4.5,h*1.9,k*4.5),'edge',k*.3)
                for y in (-h*.7,h*.7): self.box('Frame bolt',(x+side*k*1.5,y,z-span*.5),(k*2,k*3,k*3),'copper',k*.25)
                self.box('Service cable',(x+side*k,0,z),(k*1.3,k*2,span*.96),'copper',k*.15)
                for t in (-.25,.25): self.box('Service indicator',(x+side*k*1.8,k*2.5,z+t*span),(k*.6,k*1.3,k*2),'amber',0)
                self.box('Deck maintenance tray',(side*w*.60,h*1.055,z),(w*.36,k*1.8,span*.53),'edge',k*.35)
                for q in range(4): self.box('Deck vent slat',(side*w*.6,h*1.055+k*1.10,z+(q-1.5)*span*.09),(w*.30,k*.5,span*.018),'dark',0)
                # Break the long deck into machinery, longitudinal conduits and diagonal shoulder plates.
                self.box('Deck equipment cassette',(side*w*.68,h*1.04,z-span*.31),(w*.29,k*3,span*.17),'plate' if j%4==1 else 'armor',k*.6)
                self.rod('Longitudinal power feed',(side*w*.91,h*.79,z-span*.48),(side*w*.91,h*.79,z+span*.48),k*1.1,'copper')
                for q in (-1,1): self.box('Shoulder lug',(side*w*.9,h*.80,z+q*span*.31),(k*5,k*5,k*3),'edge',k*.5)
        if bb:
            deck=self.section('deck'); dc=deck['center']; dh=deck['halfSize']
            for j in range(14):
                z=dc[2]-dh[2]+(j+.5)*(dh[2]*2/14)
                for side in (-1,1):
                    self.box('Spinal deck panel',(side*dh[0]*.5,dc[1]+dh[1],z),(dh[0]*.94,k*1.8,dh[2]*2/14*.91),'armor',k*.4)
                    self.box('Spinal conduit',(side*dh[0]*.88,dc[1]+dh[1]+k,z),(k*2.4,k*2,dh[2]*2/14*.9),'edge',k*.3)
                    for q in (-1,1): self.box('Spinal captive bolt',(side*dh[0]*.71,dc[1]+dh[1]+k,z+q*dh[2]/14*.65),(k*2,k,k*2),'copper',0)
        # Exposed machinery inside the open frame (the module boxes themselves, armoured as equipment).
        for mod in self.data['modules']:
            mc,mh=mod['center'],mod['halfSize']
            if not (truss0<mc[2]<truss1) or mod['kind'] in ('Cooling','Sensor','Gun','Thruster') or abs(mc[0])+mh[0]<w*.45: continue
            self.box('Exposed machinery '+mod['id'],mc,(mh[0]*1.9,mh[1]*1.9,mh[2]*1.9),'edge',k)
            for q in (-1,1): self.box('Machinery band',(mc[0],mc[1],mc[2]+q*mh[2]*.6),(mh[0]*2.02,mh[1]*2.02,k*2.5),'copper',0)
        # Aft drive nacelles bulk out the stern silhouette.
        for side in (-1,1):
            rad_end=max((r['center'][2]+r['halfSize'][2] for r in self.data['hullSections'] if r['id'].startswith('radiator')),default=truss1)
            nz0,nz1=max(truss1+span*.2,rad_end+span*.1),aft-span*.15
            self.cylinder('Drive nacelle',(side*(w+h*.28),-h*.15,(nz0+nz1)/2),h*.46,nz1-nz0,'armor',sides=10)
            for q in range(4): self.cylinder('Nacelle frame ring',(side*(w+h*.28),-h*.15,nz0+(q+.5)*(nz1-nz0)/4),h*.5,k*4,'edge',sides=10)
            self.tube('Nacelle exhaust bell',(side*(w+h*.28),-h*.15,nz1+k*3),h*.36,k*8,'edge')
            self.cylinder('Nacelle exhaust glow',(side*(w+h*.28),-h*.15,nz1+k*1),h*.3,k*.8,'engine',sides=16)
        bridge=self.section('bridge'); bc=Vector(bridge['center']); bw,bh,bd=bridge['halfSize']
        self.box('Command tower foot',(0,(h+bc.y-bh)/2,bc.z),(bw*1.85,abs(bc.y-bh-h)+k*5,bd*1.95),'edge',k)
        # Compact two-tier command block; height comes from a slim spire cluster, not stacked decks.
        for j in range(2):
            y=bc.y-bh+(j+.5)*bh*.36; factor=1-j*.22
            self.loft('Command tower tier',[(bc.z-bd*factor,bw*factor,bh*.18,y),
                      (bc.z+bd*factor,bw*factor,bh*.18,y)],'armor' if j%2==0 else 'plate')
            for side in (-1,1):
                for q in range(4): self.box('Command viewport',(side*bw*factor,y,bc.z+(q-1.5)*bd*.32*factor),(k*.7,k*2.2,bd*.19),'glass',0)
                self.box('Command cooling fascia',(side*bw*factor,y-bh*.17,bc.z),(k,k*3,bd*1.3*factor),'edge',k*.25)
                for q in (-1,1): self.rod('Command tier brace',(side*bw*factor*.72,y-bh*.24,bc.z+q*bd*factor),
                    (side*bw*factor*.72,y+bh*.24,bc.z+q*bd*factor),k*1.2)
        tier_top=bc.y-bh+bh*.72
        sensor=next(m for m in self.data['modules'] if m['kind']=='Sensor')
        sy=sensor['center'][1]-sensor['halfSize'][1]
        # Slim column up to the sensor head, ringed by short spires (lower than the old telemetry masts).
        self.box('Sensor column',(0,(tier_top+sy)/2,bc.z),(bw*.42,sy-tier_top,bd*.5),'armor',k)
        for q in range(3): self.box('Sensor column band',(0,tier_top+(q+1)*(sy-tier_top)/4,bc.z),(bw*.5,k*2.2,bd*.58),'plate',k*.3)
        spires=[(-.62,-.45,.95),(.62,-.4,1.0),(-.55,.55,.8),(.55,.6,.85),(0,-.8,.7)]
        for dx,dz,kk in spires:
            px,pz=dx*bw,bc.z+dz*bd; high=tier_top+(sy-tier_top)*1.15*kk+bh*.25
            self.box('Spire foot',(px,tier_top+k*4,pz),(k*5,k*8,k*5),'edge',k*.4)
            self.rod('Sensor spire',(px,tier_top,pz),(px,high,pz),k*2.4)
            self.rod('Sensor spire tip',(px,high,pz),(px,high+bh*.2,pz),k*1.1)
            self.rod('Spire collar',(px-k*3,tier_top+(high-tier_top)*.6,pz),(px+k*3,tier_top+(high-tier_top)*.6,pz),k*.9)
            self.box('Spire beacon',(px,high+bh*.2,pz),(k*2.6,k*2.6,k*2.6),'amber',k*.3)
        stop=sensor['center'][1]+sensor['halfSize'][1]
        self.rod('Sensor mast',(0,stop,bc.z),(0,stop+28*k,bc.z),k*1.1)
        self.box('Sensor mast beacon',(0,stop+28*k,bc.z),(k*2.2,k*2.2,k*2.2),'amber',k*.3)
        launch=self.data['missiles']['launchPoint']; self.support(launch,10*k,'Missile bay riser')
        self.box('Missile bay',(launch[0],launch[1]-k,launch[2]),(24*k,4*k,30*k),'edge',k)
        for x in (-7*k,0,7*k):
            for z in (-9*k,0,9*k): self.box('Missile cell hatch',(launch[0]+x,launch[1]+k,launch[2]+z),(5*k,k,7*k),'plate',k*.3)
        for side in (-1,1):
            self.box('Hull identification plaque',(side*w*1.02,0,c.z-d*.53),(k*2,h*.8,span*.84),'plate',k*.5)
            self.label('A-01' if bb else 'D-01',(side*(w*1.02+k*1.05),-h*.13,c.z-d*.53),11*k,'starboard' if side>0 else 'port')
            self.box('Team stripe',(side*w*.7,h*1.06,c.z-d*.65),(k*5,k*.45,span*.66),'iff',0)
        self.rcs_clusters(c.z-d*.70,c.z+d*.72,(w,h),(w,h),3*k,L*.024)
        self.fixtures()

    def interceptor(self):
        hull=self.section('hull'); w,h,d=hull['halfSize']; nose=self.section('nose')
        n=nose['center']; nh=nose['halfSize']
        self.loft('Slender pressure fuselage',[(n[2]-nh[2],.5,nh[1]*.6,-.1),(n[2]+nh[2],w*.88,h*.80,0),
                   (2,w,h,0),(7.3,w*.92,h*.92,0)])
        for j in range(5):
            z=-1.2+j*1.85
            self.box('Spinal armor cassette',(0,h+.055,z),(w*1.24,.12,1.65),'plate' if j in(1,4) else 'armor',.03)
            for side in (-1,1):
                self.box('Spinal louver well',(side*w*.35,h+.126,z),(w*.48,.04,1.03),'dark',.008)
                for q in range(5): self.box('Spinal louver',(side*w*.35,h+.152,z+(q-2)*.17),(w*.40,.028,.055),'edge',.006)
        # A continuous rear bulkhead joins both engine pods and carries the dorsal auxiliary.
        self.box('Aft equipment bridge',(0,.4,8.0),(w*1.9,.55,2.3),'edge',.08)
        self.box('Dorsal auxiliary bed',(0,.94,8.5),(1.4,.85,1.8),'armor',.08)
        for side in (-1,1):
            self.box('Engine load spar',(side*.77,0,8.1),(.52,.64,2.0),'edge',.05)
            self.box('Aft RCS shoulder',(side*1.52,.2,7.45),(.35,1.7,.6),'armor',.04)
            self.box('Aft RCS vertical web',(side*.978,0,7.5),(.35,2.24,.5),'edge',.025)
        self.loft('Armored cockpit',[(-8.5,.45,.18,1),(-5,1.04,.60,1),(-2.9,1.12,.35,1)])
        self.loft('Canopy glazing',[(-8.2,.38,.14,1.15),(-5, .88,.49,1.17),(-3.2,.96,.24,1.15)],'glass')
        for side in (-1,1):
            self.rod('Canopy structural rail',(side*.4,1.28,-8.2),(side*.9,1.66,-5),.055)
            self.rod('Canopy structural rail',(side*.9,1.66,-5),(side*1,1.40,-3.1),.055)
            for j in range(5):
                z=-.8+j*1.7
                self.box('Flank propulsion housing',(side*w*.96,0,z),(.20,h*1.6,1.48),'armor' if j%2 else 'plate',.055)
                self.box('Flank coolant line',(side*w*1.04,-.50,z),(.05,.08,1.48),'copper',.01)
            # Swept, cropped wings; each slab has a solid root and mirrored vertices.
            top=[(side*1.3,-.20,-3.3),(side*3.4,-.20,-2.2),(side*7.0,-.20,6.8),(side*4.7,-.20,7.2),(side*1.3,-.20,3.8)]
            verts=top+[(x,y-.26,z) for x,y,z in top]
            faces=[tuple(range(5)),tuple(reversed(range(5,10)))]+[(i,(i+1)%5,(i+1)%5+5,i+5) for i in range(5)]
            self.mesh('Swept wing '+str(side),verts,faces,'armor',.035)
            self.rod('Wing leading spar',top[1],top[2],.12,'edge')
            for j in range(6):
                z=.5+j*.92; x=side*(2.2+j*.36)
                self.box('Wing layered armor',(x,-.16,z),(.67,.08,.7),'plate' if j==2 else 'armor',.018)
                self.box('Wing engraved channel',(x,-.117,z),(.55,.015,.045),'edge',0)
            self.box('Wing identification tile',(side*4.4,-.16,4.8),(.9,.08,1.1),'plate',.02)
            self.label('P-01',(side*4.4,-.118,4.8),.31)
            self.box('Wing team stripe',(side*3.2,-.16,3.7),(.25,.08,.7),'iff',.01)
            # The two fins share one construction; tip geometry is reflected from the same coordinates.
            poly=[(side*1.03,1,3.3),(side*1.24,3.6,6.6),(side*1.24,3.6,8.2),(side*1.03,1,7.8)]
            vs=poly+[(x+side*.15,y,z) for x,y,z in poly]
            fs=[(0,1,2,3),(7,6,5,4)]+[(i,(i+1)%4,(i+1)%4+4,i+4) for i in range(4)]
            self.mesh('Vertical fin '+str(side),vs,fs,'plate',.025)
            self.rod('Fin tip cap',(side*1.315,3.6,6.55),(side*1.315,3.6,8.22),.12,'edge')
            for j in range(3):
                self.rod('Fin bracing',(side*1.13,1.45,4.1+j*.8),(side*1.32,3.12,6.62+j*.4),.035,'edge')
            self.box('Wing radiator recess',(side*2,-.30,4.4),(.6,.24,3.6),'dark',.015)
            for j in range(10): self.box('Wing cooling channels',(side*2,-.44,3+j*.3),(.5,.04,.08),'copper',0)
        muzzle=self.data['railgun']['muzzle']
        for side in (-1,1):
            x=side*.25; end=muzzle[2]; start=-10.4
            self.box('Fixed forward rail shroud',(x,muzzle[1],(start+end)/2),(.25,.28,start-end),'dark',.025)
            for j in range(4): self.box('Fixed rail clamp',(x,muzzle[1],start+(end-start)*(j+.5)/4),(.36,.36,.3),'armor',.04)
            self.tube('Forward gun muzzle',(x,muzzle[1],end+.09),.10,.18,'edge')
        launch=self.data['missiles']['launchPoint']
        self.box('Missile rail mount',(launch[0],launch[1]+.15,launch[2]),(1.4,.4,3),'edge',.07)
        for side in (-1,1):
            self.cylinder('Light missile',(side*.45,launch[1]-.06,launch[2]),.17,2.3,'armor',r2=.04)
        self.rcs_clusters(-9,7.5,(1.08,.66),(1.63,1.12),.23,1.6)
        self.fixtures()

    def export(self):
        bpy.context.window.scene=self.scene
        rig_turrets(self.coll,self.data); rig_point_defense(self.coll,self.data)
        stats=export_collection(self.coll,base.OUT/f'{self.key}.glb')
        self.lighting(); self.scene['design']='Mars'; self.scene['module_source']=f'data/ships/{self.key}.json'
        self.scene['game_axes']='Godot meters, forward -Z, up +Y'
        for obj in self.sockets: obj.hide_set(True)
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(base.ART/f'{self.key}.blend'),compress=True)
        if base.opt.render:
            self.scene.cycles.samples=16
            self.scene.render.filepath=str(base.SHOTS/f'{self.key}_hero.png'); bpy.ops.render.render(write_still=True)
        return dict(id=self.key,**stats,modules=len(self.data['modules']),source=f'art/blender/{self.key}.blend',model=f'assets/ships/{self.key}.glb',**self.fx)


manifest_path=base.OUT/'manifest.json'; manifest=json.loads(manifest_path.read_text(encoding='utf-8'))
for key in ('mars_battleship','mars_escort','mars_interceptor'):
    bpy.ops.wm.read_factory_settings(use_empty=True); initial=bpy.context.scene
    ship=MarsShip(key); bpy.data.scenes.remove(initial)
    ship.interceptor() if key.endswith('interceptor') else ship.capital()
    entry=ship.export(); manifest['ships']=[e for e in manifest['ships'] if e['id']!=key]+[entry]
    manifest_path.write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    print('BUILT MARS',key,entry['triangles'],flush=True)
