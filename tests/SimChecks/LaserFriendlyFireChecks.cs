using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

static class LaserFriendlyFireChecks
{
    static int checks;
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
    static ShipDefinition Edit(ShipDefinition d,Action<JsonObject> edit)
    {var j=JsonNode.Parse(d.ToJson())!.AsObject();edit(j);return ShipDefinition.Parse(j.ToJsonString(),true);}
    static (SimWorld W,ShipBody Gun,ShipBody Enemy,ShipBody Ally) Fixture(double offset=0)
    {
        var w=new SimWorld();
        var laser=FleetLoadouts.ResolveFit(FleetLoadouts.Empty("interceptor") with{Components=new(MainGun:"main-laser")}).Definition;
        laser=Edit(laser,j=>{j["PointDefense"]=null;j["DefenseDrones"]=null;});
        var passive=Edit(ShipDefinitions.ById("escort"),j=>{
            j["PointDefense"]=null;j["DefenseDrones"]=null;
            j["HullSections"]=JsonNode.Parse(ShipDefinition.SerializeData(new[]{new HullSection("fixture","plate",Vector3.Zero,new(80,80,160),new ArmorPlate(100))}));
        });
        var origin=new Vec3d(offset,-offset,offset);
        var gun=w.Add(new ShipBody("LASER",laser,Faction.Blue));gun.Place(origin,Quaternion.Identity);
        var enemy=w.Add(new ShipBody("ENEMY",passive,Faction.Red));enemy.Place(origin+new Vec3d(0,0,-4000),Quaternion.Identity);
        var ally=w.Add(new ShipBody("ALLY",passive,Faction.Blue));ally.Place(origin+new Vec3d(1000,0,-1500),Quaternion.Identity);
        foreach(var ship in w.Ships){ship.Control=new(){FlightAssist=false};ship.DamageControl.Priority=RepairPriority.Hold;}
        w.Sensors.Update(w.Ships,0,true);w.Log=new(w);
        return(w,gun,enemy,ally);
    }
    static Vec3d ShellOnly(ShipBody gun,ShipBody ally)=>gun.Railgun!.MuzzlePosition+new Vec3d((80+ally.Definition.ShieldEnvelope.Radii.X)*.5,0,-1500);
    static void Burn(SimWorld w,ShipBody gun,int ticks,bool automatic=true)
    {for(int i=0;i<ticks;i++){w.FireRailguns(gun,Vector3.Forward,automatic:automatic);w.Step();}}

    static void GeometryAndOrders()
    {
        foreach(double offset in new[]{0d,1e12})
        {
            var(w,g,e,a)=Fixture(offset);a.Place(ShellOnly(g,a),Quaternion.Identity);
            var muzzle=g.Railgun!.MuzzlePosition;
            Check(!DamageRay.FirstHit(a,muzzle,Vector3.Forward,4000,out _),"Fixture misses allied armor");
            Check(DamageRay.FirstDefenseHitAtPose(a,muzzle,Vector3.Forward,4000,a.Position,a.Orientation,out _),"Fixture intersects allied shield only");
            Check(!w.FriendlyInLine(g,muzzle,Vector3.Forward,4000),"Non-laser legacy line behavior is preserved");
            Check(w.FriendlyInLine(g,muzzle,Vector3.Forward,4000,includeShields:true),"Laser preflight includes shield envelope at double coordinates");
            Check(!w.TryAutoFire(g,e,null,10,out var status)&&status==GunneryStatus.FriendlyLine,"Automatic ship fire reports allied shield obstruction");
            float before=a.Damage.Shield,enemyBefore=e.Damage.Shield;
            g.Gunnery=new(){Doctrine=FireDoctrine.Focus,Target=e};
            for(int i=0;i<20;i++)w.Step();
            Check(a.Damage.Shield==before&&e.Damage.Shield==enemyBefore&&g.Railgun.Laser!.Pulses==0,"Player automatic order cannot damage or shoot through allied shield");
            g.Gunnery=null;
            a.Damage.AbsorbShield(a.Damage.Shield,w.Time);
            Check(!w.FriendlyInLine(g,muzzle,Vector3.Forward,4000,includeShields:true),"Collapsed shield no longer obstructs an armor-clear line");
            Burn(w,g,8);
            Check(e.Damage.Shield<enemyBefore&&g.Railgun.Laser!.Pulses>0,"Fire resumes through the cleared shield-only gap");
            a.Damage.Reset();ulong pulses=g.Railgun.Laser!.Pulses;before=a.Damage.Shield;
            Burn(w,g,8);
            Check(a.Damage.Shield==before&&g.Railgun.Laser.Pulses==pulses&&!g.Railgun.Laser.Active,"Restored shield safely stops emission at impact time");
            a.Place(g.Position+new Vec3d(1000,0,-1500),Quaternion.Identity);Burn(w,g,8);
            Check(g.Railgun.Laser.Pulses>pulses,"Channel resumes after allied ship leaves the ray");
            g.Railgun.Reset();Check(!g.Railgun.Laser.AvoidFriendlyFire,"Reset clears automatic request provenance");
        }
    }

    static void EmissionTime()
    {
        var(w,g,e,a)=Fixture();Burn(w,g,3); // One more tick completes the first exposure unit.
        var beam=g.Railgun!.Laser!;
        Check(beam.Pulses==0,"Movement fixture is before the first damage pulse");
        var muzzle=g.Railgun.MuzzlePosition;
        a.Place(muzzle+new Vec3d(a.Definition.ShieldEnvelope.Radii.X+10,0,-1500),Quaternion.Identity);
        Check(!w.FriendlyInLine(g,muzzle,Vector3.Forward,4000,includeShields:true),"Ally is clear when the request is accepted");
        float before=a.Damage.Shield;w.FireRailgun(g,Vector3.Forward,automatic:true);
        a.Velocity=new(-2400,0,0);w.Step();
        Check(w.FriendlyInLine(g,muzzle,Vector3.Forward,4000,includeShields:true),"Ally physically moves into the shield-only ray during the tick");
        Check(beam.Pulses==0&&!beam.Active&&a.Damage.Shield==before&&w.Log!.Ship(g).LaserPulses==0,
            "Post-movement safety aborts before damage, telemetry and beam publication");
        a.Velocity=Vector3.Zero;a.Place(muzzle+new Vec3d(0,0,-1500),Quaternion.Identity);
        Burn(w,g,8);
        Check(beam.Pulses==0&&a.Damage.Shield==before,"Automatic direct request also blocks allied hulls");
        Burn(w,g,4,automatic:false);
        Check(a.Damage.Shield<before&&!beam.AvoidFriendlyFire,"Manual request replaces stale automatic mode and retains physical friendly-fire rules");
    }

    static void FirstPhysicalHit()
    {
        foreach(bool hitMissile in new[]{false,true})
        {
            var(w,g,e,a)=Fixture();a.Place(g.Railgun!.MuzzlePosition+new Vec3d(0,0,-1500),Quaternion.Identity);
            var position=g.Railgun.MuzzlePosition+new Vec3d(hitMissile?0:10,0,-500);
            var m=new Missile{Id=100,Shooter=e,Target=a,Definition=e.Definition.Missiles! with{AccelG=0},Health=.001f,
                Position=position,PrevPosition=position,Velocity=Vector3.Zero,Age=61,NextSeekerCheck=100,AimPoint=a.Position};
            ((List<Missile>)w.Missiles).Add(m);
            float before=a.Damage.Shield;Burn(w,g,8);var beam=g.Railgun.Laser!;
            Check(a.Damage.Shield==before,"An ally behind a small target cannot receive an automatic overshoot");
            Check(hitMissile?m.Health<=0&&beam.Pulses==1:m.Health>0&&beam.Pulses==0,
                "First physical hit allows a real missile hit, but blocks the miss and the pulse after removal");
        }
        var(ew,eg,enemy,ally)=Fixture();
        enemy.Place(eg.Railgun!.MuzzlePosition+new Vec3d(0,0,-800),Quaternion.Identity);
        ally.Place(eg.Railgun.MuzzlePosition+new Vec3d(0,0,-1500),Quaternion.Identity);
        float enemyBefore=enemy.Damage.Shield,allyBefore=ally.Damage.Shield;Burn(ew,eg,8);
        Check(enemy.Damage.Shield<enemyBefore&&ally.Damage.Shield==allyBefore,"Enemy before ally absorbs the beam without an overly broad friendly veto");
        var(ow,og,_,oa)=Fixture();oa.Place(og.Railgun!.MuzzlePosition+new Vec3d(0,0,-1500),Quaternion.Identity);
        ow.Obstacles.Add(new("ROCK",og.Railgun.MuzzlePosition+new Vec3d(0,0,-700),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,100)));
        float shield=oa.Damage.Shield;Burn(ow,og,8);
        Check(og.Railgun.Laser!.Pulses>0&&og.Railgun.Laser.HitShip is null&&oa.Damage.Shield==shield,"Terrain still terminates the ray before an ally behind it");
    }

    static void Waves(string output)
    {
        var rows=new List<object>();
        foreach(int speed in new[]{2000,6000})
        foreach(var support in new[]{InterceptionStressTrials.Support.Absent,InterceptionStressTrials.Support.Ecm})
        foreach(int seed in Enumerable.Range(1,8))
        {
            var trace=new List<object>();
            var row=InterceptionStressTrials.Wave(32,4,speed,support,seed,diagnostics:trace);
            int friendly=JsonSerializer.SerializeToElement(trace).EnumerateArray().Count(x=>x.GetProperty("Friendly").GetBoolean());
            Check(friendly==0,$"No friendly laser hit: {speed}m/s, {support}, seed {seed}");
            rows.Add(new{Trial=row,FriendlyLaserHitPulses=friendly});
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}}));
    }
    public static void Run(string? output=null)
    {
        GeometryAndOrders();EmissionTime();FirstPhysicalHit();Waves(output??"shots/laser-friendly-fire/waves-after.json");
        Console.WriteLine($"PASS: {checks} laser allied-shield/preflight/emission/32-wave checks");
    }
}
