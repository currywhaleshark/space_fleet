using Godot;
using SpaceFleet.Sim;

static class ShieldEnvelopeChecks
{
    private static int _checks;
    private static void Check(bool ok,string why) { if(!ok) throw new InvalidOperationException(why); _checks++; }
    public static void Run()
    {
        foreach(var kind in Enum.GetValues<HullKind>())
        {
            var def=ShipDefinitions.For(kind); var shell=def.ShieldEnvelope;
            var ship=new ShipBody("FIELD",def.Flight,Faction.Red);
            var pose=new Vec3d(1e12,-1e12,1e12); var q=new Quaternion(Vector3.Up,.71f)*new Quaternion(Vector3.Right,.35f);
            ship.Place(pose,q);
            foreach(var section in def.HullSections)
            for(int corner=0;corner<8;corner++)
            {
                var p=section.Center+section.HalfSize*new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1);
                Check(((p-shell.Center)/shell.Radii).Length()<1,"All damage hull corners are inside the shared shield envelope");
            }
            Vector3 localStart=shell.Center+Vector3.Forward*shell.Radii.Z*2;
            Vec3d origin=pose+Vec3d.From(q*localStart); Vector3 direction=q*Vector3.Back;
            Check(DamageRay.FirstDefenseHitAtPose(ship,origin,direction,def.Flight.Length*4,pose,q,out float field),"Rotated far-world shield intersection");
            Check(DamageRay.FirstHit(ship,origin,direction,def.Flight.Length*4,out float hull) && field<hull,"Shield precedes physical hull contact");
            var result=DamageRay.Apply(ship,origin,direction,new DamagePacket(1,100,10,def.Flight.Length*4),0,1);
            Check(result.ShieldStopped && !result.HullHit && result.ShieldAbsorbed==1 && result.Modules.Count==0,"Boundary hit consumes energy without touching armor/modules");
            Check(result.ShieldLocalPoint is {} contact && Mathf.Abs(((contact-shell.Center)/shell.Radii).Length()-1)<.0001f,"Visual impact lies exactly on the interception skin");
            // A flank grazing the field still hits it even when it misses the actual hull.
            Vector3 graze=shell.Center+new Vector3(shell.Radii.X*.96f,0,-shell.Radii.Z*2);
            Check(shell.Entry(graze,Vector3.Back,def.Flight.Length*4,out _),"Grazing shell ray intersects");
            Check(!shell.Entry(shell.Center,Vector3.Forward,def.Flight.Length*4,out _),"Outgoing rays and attacks already inside the field do not collide with its inner face");
            Check(!shell.Entry(localStart,Vector3.Back,shell.Radii.Z*.1f,out _),"Shield respects finite weapon range");
            Vector3 oblique=new Vector3(1,.75f,-.7f).Normalized(), farStart=shell.Center+oblique*def.Flight.Length*70;
            Check(shell.Entry(farStart,-oblique,def.Flight.Length*80,out float farEntry)
                && Mathf.Abs(((farStart-oblique*farEntry-shell.Center)/shell.Radii).Length()-1)<.0002f,
                "Long oblique rays preserve shield-surface precision for every hull size");
            ship.Damage.AbsorbShield(1e9f,0);
            DamageRay.FirstDefenseHitAtPose(ship,origin,direction,def.Flight.Length*4,pose,q,out float bare);
            Check(Mathf.Abs(bare-hull)<.001f,"Depleted shield immediately exposes the original armor surface");
            ship.Damage.Reset();
            Check(ship.Damage.Shield>0 && ship.Wreck is null,"Repair restores field and clears breakup state");
            ship.Damage.Breakup(0,"test");
            Check(ship.Wreck!.Pieces.Count==2 && ship.Damage.Destruction==DestructionKind.Collision,"Collision catastrophe is a two-piece breakup");
            var first=ship.Wreck.Hull(0); var later=ship.Wreck.Hull(4);
            Check(later.Boxes.Where((b,i)=>b.Center.DistanceTo(first.Boxes[i].Center)>1).Count()==later.Boxes.Count,"Collision boxes move with every rendered fragment");
            ship.Damage.Reset(); ship.Damage.Catastrophe(0,"test");
            Check(ship.Wreck!.Pieces.Count is >=4 and <=8 && ship.Damage.Destruction==DestructionKind.Antimatter,
                "Containment catastrophe produces multiple occupied fragments, omitting empty cells");
            ship.Damage.Reset(); Check(ship.Wreck is null && ReferenceEquals(ship.Hull,def.Hull),"Repair removes the wreck collider as well as its visuals");
        }
        Console.WriteLine($"PASS: {_checks} shield boundary/breakup checks");
    }
}
