using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

static class CriticalDamageChecks
{
    private static int _checks;
    private static void Require(bool ok,string message) { if(!ok)throw new Exception(message);_checks++; }
    public static void Run()
    {
        foreach(var definition in ShipDefinitions.All)
        {
            foreach(var part in definition.Modules.Where(m=>m.CriticalChance>0))
            {
                const int samples=2048;
                var deaths=new int[3];
                for(uint shot=1;shot<=samples;shot++)
                {
                    bool previous=false;
                    for(int repairs=0;repairs<3;repairs++)
                    {
                        var damage=new ShipDamage(definition,"critical-rate");var module=damage.Module(part.Id);
                        for(int i=0;i<repairs;i++)
                        {
                            damage.Hurt(module,module.Health,0,0,0);
                            Require(damage.EmergencyRestore(module,.35f,1),"Critical compartment can receive partial emergency repair");
                        }
                        damage.Hurt(module,module.Health,2,part.CriticalEnergy,shot);
                        if(damage.Destroyed)deaths[repairs]++;
                        Require(!previous||damage.Destroyed,"Same shot becomes no safer after emergency bypasses");
                        previous=damage.Destroyed;
                    }
                }
                for(int repairs=0;repairs<3;repairs++)
                    Require(Math.Abs(deaths[repairs]/(double)samples-part.CriticalChance*(1+.5*repairs))<.04,
                        $"{definition.Id}/{part.Id} measured destruction probability follows configured risk");
                Require(deaths[0]>0 && deaths[0]<deaths[1] && deaths[1]<deaths[2],"Repairs increase critical vulnerability without guaranteeing destruction");
                var safe=new ShipDamage(definition,"critical-guard");var m=safe.Module(part.Id);
                safe.Hurt(m,m.Health,0,part.CriticalEnergy-.01f,1);
                for(uint i=1;i<=128;i++)safe.Hurt(m,99999,1,99999,i);
                Require(!safe.Destroyed,"Destroyed compartment cannot farm repeated critical rolls");
                for(int i=0;i<5;i++)
                {safe.EmergencyRestore(m,.35f,i);safe.Hurt(m,m.Health,i,0,1);}
                Require(Math.Abs(m.CriticalChance-part.CriticalChance*2)<.0001,"Repeated repair vulnerability is capped at twice base risk");
                safe.Reset();
                Require(m.EmergencyRepairs==0 && m.CriticalChance==part.CriticalChance,"Full reset clears emergency repair history");
            }
            var ordinary=new ShipDamage(definition,"ordinary");var sensor=ordinary.Modules.First(m=>m.Definition.Kind==ModuleKind.Sensor);
            ordinary.Hurt(sensor,sensor.Health,0,99999,1);ordinary.EmergencyRestore(sensor,.35f,1);ordinary.Hurt(sensor,sensor.Health,2,99999,2);
            Require(!ordinary.Destroyed && sensor.CriticalChance==0,"Ordinary sensor damage and repair cannot randomly detonate a hull");
            if(definition.Kind==HullKind.Interceptor)
                Require(definition.Modules.Where(m=>m.CriticalChance>0).All(m=>m.CriticalEnergy<200),"Light railguns can reach interceptor critical energy thresholds");
            foreach(ModuleKind kind in new[]{ModuleKind.Magazine,ModuleKind.Reactor})CheckRay(definition,kind);
        }
        Console.WriteLine($"PASS: {_checks} critical damage / emergency repair risk checks");
    }
    private static void CheckRay(ShipDefinition definition,ModuleKind kind)
    {
        using var stream=typeof(ShipDefinitions).Assembly.GetManifestResourceStream($"SpaceFleet.data.ships.{definition.Id}.json")!;
        using var reader=new StreamReader(stream);
        var json=JsonNode.Parse(reader.ReadToEnd())!;
        string id=definition.Modules.First(m=>m.Kind==kind).Id;
        foreach(var module in json["modules"]!.AsArray())module!["criticalChance"]=module["id"]!.GetValue<string>()==id?1:0;
        var ship=new ShipBody("ray-critical",ShipDefinition.Parse(json.ToJsonString()),Faction.Blue);
        ship.Place(Vec3d.Zero,Quaternion.Identity);
        var target=ship.Damage.Module(id);
        Vec3d origin=Vec3d.From(target.Definition.Center-Vector3.Right*5000);
        var shield=DamageRay.Apply(ship,origin,Vector3.Right,new DamagePacket(1,100000,100000,10000),0,1);
        Require(shield.ShieldStopped&&!ship.Damage.Destroyed,"Shield absorption prevents a critical penetration");
        ship.Damage.AbsorbShield(99999,0);
        var armor=DamageRay.Apply(ship,origin,Vector3.Right,new DamagePacket(99999,1,100000,10000),0,2);
        Require(armor.ArmorStopped&&!ship.Damage.Destroyed,"Unpenetrated armor prevents a critical hit");
        ship.Damage.Hurt(target,target.Health*.1f,0,99999,3);
        Require(!ship.Damage.Destroyed,"Even guaranteed critical risk requires compartment destruction");
        var hit=DamageRay.Apply(ship,origin,Vector3.Right,new DamagePacket(99999,100000,100000,10000),1,4);
        Require(ship.Damage.Destroyed && hit.Modules.Any(m=>m.Id==id),"Actual armor penetration reaches critical compartment");
        Require(ship.Damage.Destruction==(kind==ModuleKind.Magazine?DestructionKind.Magazine:DestructionKind.Reactor),"Existing explosion/breakup type remains correct");
        Require(ship.Damage.DestructionPoint==target.Definition.Center,"Explosion begins at the damaged module");
    }
}
