using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

static class EquipmentChecks
{
    private static int _checks;
    private static void Check(bool ok, string why) { if (!ok) throw new Exception(why); _checks++; }
    private static bool Near(float a, float b) => Math.Abs(a-b) <= Math.Max(.0001f, Math.Abs(b)*.00001f);
    private static ResolvedLoadout Fit(string hull, params EquipmentMount[] mounts)
        => ShipLoadouts.Resolve(new(hull, Equipment: mounts));
    private static ShipBody Ship(ShipDefinition def, string name="EQUIPPED", Faction side=Faction.Blue) => new(name,def,side);
    private static ModuleState Part(ShipBody s, string bay) => s.Damage.Module("fit-"+bay);
    private static void Hit(ShipBody s, ModuleState m, float fraction=1, float energy=0)
        => s.Damage.Hurt(m,m.Definition.HitPoints*fraction,s.SimTime,energy,17);
    private static void Step(ShipBody s, double seconds)
    { for(int i=0;i<(int)Math.Ceiling(seconds/SimWorld.TickDelta);i++) s.Step(SimWorld.TickDelta); }
    private static void Reject(Action action, string why)
    {
        bool rejected=false;
        try { action(); } catch(Exception e) when(e is InvalidDataException or JsonException or ArgumentException) { rejected=true; }
        Check(rejected,why);
    }
    private static JsonObject Data(ShipDefinition def) => JsonNode.Parse(def.ToJson())!.AsObject();
    public static void Run()
    {
        Composition(); Validation(); Effects(); DamageRays(); Recovery(); Drones();
        Console.WriteLine($"PASS: {_checks} physical equipment/bay/damage/recovery checks");
    }
    private static void Composition()
    {
        Check(ShipEquipment.All.Count()==4,"Four validated equipment resources embedded");
        foreach(var original in ShipDefinitions.All)
        {
            string before=original.ToJson();
            var mounts=original.Bays.Select(b=>new EquipmentMount(b.Id,b.Accepts.Contains(EquipmentCategory.Drone)?"drone-hangar"
                :b.Accepts.Contains(EquipmentCategory.Shield)?"shield-capacity":"sensor-array")).ToArray();
            var fit=Fit(original.Id,mounts); var def=fit.Definition;
            Check(def.Modules.Length==original.Modules.Length+mounts.Length,"Every installed device adds one damage volume");
            Check(ShipDefinition.SerializeData(def.Modules.Take(original.Modules.Length).ToArray())==ShipDefinition.SerializeData(original.Modules),
                "Core modules preserved in original order");
            Check(def.ModelId==original.ModelId && def.Hull.Boxes.SequenceEqual(original.Hull.Boxes),"Existing model and hull collision unchanged");
            Check(fit.ContentHash==Fit(original.Id,mounts.Reverse().ToArray()).ContentHash,"Mount order does not change identity or drone ownership");
            Check(JsonNode.DeepEquals(Data(ShipDefinition.Parse(fit.DefinitionJson)),Data(def)),"Installed snapshots restore without reapplying costs");
            float mass=original.Flight.MassKg+mounts.Sum(m=>ShipEquipment.ById(m.EquipmentId).MassKg);
            Check(Near(def.Flight.MassKg,mass) && Near(def.Flight.ForwardAccel,original.Flight.ForwardAccel*original.Flight.MassKg/mass)
                && Near(def.Flight.StrafeAccel,original.Flight.StrafeAccel*original.Flight.MassKg/mass)
                && Near(def.Flight.BrakeAccel,original.Flight.BrakeAccel*original.Flight.MassKg/mass),"Mass reduces all translational thrust accelerations");
            foreach(var item in def.InstalledEquipment)
            {
                var bay=def.Bays.Single(b=>b.Id==item.BayId);var module=def.Modules.Single(m=>m.Id==item.ModuleId);
                Check(module.Center==bay.Center && module.HalfSize==bay.HalfSize && module.Grid==bay.Grid,"Damage module exactly occupies its bay and grid");
            }
            Check(original.ToJson()==before && Fit(original.Id).Definition.ToJson()==before,"Catalog and future empty loadouts remain unchanged");
        }
        var baseline=Ship(ShipDefinitions.ById("interceptor"));
        var loaded=Ship(Fit("interceptor",new EquipmentMount("utility-port","ecm-amplifier")).Definition);
        baseline.Control=loaded.Control=new ShipControl { Thrust=new(0,0,.5f),FlightAssist=false };
        Step(baseline,1);Step(loaded,1);
        Check(loaded.Velocity.Length()<baseline.Velocity.Length(),"Added mass changes actual flight, not only the displayed data");
    }
    private static void Validation()
    {
        Reject(()=>Fit("escort",new EquipmentMount("missing","sensor-array")),"Unknown bay rejected");
        Reject(()=>Fit("escort",new EquipmentMount("utility-port","missing")),"Unknown device rejected");
        Reject(()=>Fit("escort",new EquipmentMount("utility-starboard","shield-capacity")),"Oversized device rejected");
        Reject(()=>Fit("battleship",new EquipmentMount("hangar","sensor-array")),"Wrong category rejected");
        Reject(()=>Fit("escort",new EquipmentMount("utility-port","sensor-array"),new EquipmentMount("utility-port","ecm-amplifier")),"Duplicate bay rejected");
        Reject(()=>ShipLoadouts.Resolve(new("escort","""{"power":{"outputMw":400}}""",Equipment:new[]{new EquipmentMount("utility-port","sensor-array")})),"Power overload rejected");
        Reject(()=>ShipLoadouts.Resolve(new("escort","""{"power":{"coolingMw":1}}""",Equipment:new[]{new EquipmentMount("utility-port","sensor-array")})),"Heat overload rejected");
        foreach(var mutate in new Action<JsonObject>[] {
            d=>d["Bays"]![0]!["Center"]=JsonNode.Parse("[99999,0,0]"),
            d=>d["Bays"]![0]!["Center"]=d["Modules"]![0]!["Center"]!.DeepClone(),
            d=>d["Bays"]![0]!["HalfSize"]=JsonNode.Parse("[0,1,1]"),
            d=>d["Bays"]![1]=d["Bays"]![0]!.DeepClone(),
            d=>d["Bays"]![0]!["Accepts"]=JsonNode.Parse("[]"),
            d=>d["Bays"]![0]!["Grid"]="Unknown",
            d=>d["Bays"]=null,
            d=>d["InstalledEquipment"]=null,
        }) { var data=Data(ShipDefinitions.ById("escort"));mutate(data);Reject(()=>ShipDefinition.Parse(data.ToJsonString()),"Invalid bay definition rejected"); }
        var fitted=Fit("battleship",new EquipmentMount("sensor","sensor-array"));
        foreach(var mutate in new Action<JsonObject>[] {
            d=>d["InstalledEquipment"]![0]!["Equipment"]!["Designs"]=JsonNode.Parse("[\"Mars\"]"),
            d=>d["InstalledEquipment"]![0]!["Equipment"]!["HeatMw"]=-1,
            d=>d["InstalledEquipment"]![0]!["Equipment"]!["Effects"]!["Jammer"]=1,
            d=>d["InstalledEquipment"]![0]!["Equipment"]!["CriticalChance"]=1,
            d=>d["Modules"]!.AsArray().Last()!["Center"]=JsonNode.Parse("[0,0,0]"),
            d=>d["InstalledEquipment"]=new JsonArray(),
        }) { var data=Data(fitted.Definition);mutate(data);Reject(()=>ShipDefinition.Parse(data.ToJsonString()),"Invalid device/geometry/design snapshot rejected"); }
        Reject(()=>ShipLoadouts.Resolve(new("battleship",fitted.DefinitionJson)),"Patches cannot smuggle installed effects around bay composition");
        var hangar=Data(Fit("battleship",new EquipmentMount("hangar","drone-hangar")).Definition);
        hangar["InstalledEquipment"]![0]!["DroneStartIndex"]=0;
        Reject(()=>ShipDefinition.Parse(hangar.ToJsonString()),"Invalid drone ownership interval rejected");
        var noDrones=Data(ShipDefinitions.ById("battleship"));noDrones["DefenseDrones"]=null;
        Reject(()=>ShipLoadouts.Resolve(new("battleship",noDrones.ToJsonString(),Equipment:new[]{new EquipmentMount("hangar","drone-hangar")})),"Expansion without base drone system rejected");
    }
    private static void Effects()
    {
        var baseDef=ShipDefinitions.ById("battleship");var baseline=Ship(baseDef);
        var shield=Ship(Fit(baseDef.Id,new EquipmentMount("utility-port","shield-capacity")).Definition);
        Check(Near(shield.Damage.ShieldCapacity,baseline.Damage.ShieldCapacity*1.4f),"Shield capacity benefit applied");
        Check(Near(shield.Damage.ShieldRechargeDelay,baseDef.Shield.RechargeDelay+2),"Capacity pays a recharge delay cost");
        baseline.Damage.AbsorbShield(100,0);shield.Damage.AbsorbShield(100,0);
        float before=shield.Damage.Shield;baseline.Damage.Step(baseDef.Shield.RechargeDelay+1);shield.Damage.Step(baseDef.Shield.RechargeDelay+1);
        Check(shield.Damage.Shield==before && baseline.Damage.Shield>baseDef.Shield.Capacity-100,"Extra delay affects actual recharge");
        var sensor=Ship(Fit(baseDef.Id,new EquipmentMount("sensor","sensor-array")).Definition);
        Check(Near(SensorNet.Strength(sensor),SensorNet.Strength(baseline)*1.35f)
            && Near(SensorNet.Signature(sensor),SensorNet.Signature(baseline)*1.2f),"Sensor power and exposure both increase");
        var ecm=Ship(Fit(baseDef.Id,new EquipmentMount("sensor","ecm-amplifier")).Definition,"JAMMER",Faction.Red);
        baseline.Power.SetPips(2,1,1,2,2);ecm.Power.SetPips(2,1,1,2,2);
        Check(Near(SensorNet.Jammer(ecm),SensorNet.Jammer(baseline)+1.5f),"ECM jammer increase is available to seekers and sensors");
        var enemy=Ship(baseDef,"OBSERVER");enemy.Place(new(0,0,30000),Quaternion.Identity);
        float jam=SensorNet.Measure(enemy,ecm).Jam;Hit(ecm,Part(ecm,"sensor"));
        Check(SensorNet.Measure(enemy,ecm).Jam<jam,"ECM damage changes actual sensor interference");
        // Additional costs follow pips, activity and supply, with no persistent charge after failure.
        var plain=Ship(baseDef);var costly=Ship(Fit(baseDef.Id,new EquipmentMount("sensor","sensor-array")).Definition);
        plain.Power.Step(.1);costly.Power.Step(.1);
        Check(Near(costly.Power.DemandMw-plain.Power.DemandMw,5) && Near(costly.Power.HeatInMw-plain.Power.HeatInMw,7),"Runtime power and extra heat costs apply once");
        Hit(costly,Part(costly,"sensor"));costly.Power.Step(.1);
        Check(costly.Power.DemandMw==plain.Power.DemandMw && costly.Power.HeatInMw==plain.Power.HeatInMw,"Destroyed device loses both benefits and operating costs");
        var own=Part(sensor,"sensor");Hit(sensor,own,.5f);
        Check(Near(sensor.Damage.SensorStrengthBonus,.175f),"Partial damage scales benefit");
        foreach(var bus in sensor.Damage.Modules.Where(m=>m.Definition.Kind==ModuleKind.PowerBus && m.Definition.Grid==own.Definition.Grid))Hit(sensor,bus);
        Check(sensor.Damage.SensorStrengthBonus==0 && sensor.Damage.SignatureBonus==0 && sensor.Damage.EquipmentPowerMw(PowerChannel.Sensors)==0,"Grid loss disables the still-intact device");
        Hit(shield,Part(shield,"utility-port"));
        Check(shield.Damage.ShieldCapacity==baseDef.Shield.Capacity && shield.Damage.Shield<=shield.Damage.ShieldCapacity,"Lost booster clamps stored shield to surviving capacity");
    }
    private static void DamageRays()
    {
        foreach(var hull in ShipDefinitions.All)
        foreach(var bay in hull.Bays)
        {
            string equipment=bay.Accepts.Contains(EquipmentCategory.Drone)?"drone-hangar":bay.Accepts.Contains(EquipmentCategory.Shield)?"shield-capacity":"sensor-array";
            var s=Ship(Fit(hull.Id,new EquipmentMount(bay.Id,equipment)).Definition);
            s.DamageControl.Priority=RepairPriority.Hold;
            // End immediately after the bay: do not shoot through unrelated core compartments.
            Vector3 direction=bay.Center.X<0?Vector3.Right:Vector3.Left;
            Vector3 local=bay.Center-direction*hull.Flight.Length;
            float range=hull.Flight.Length+bay.HalfSize.X+.01f;
            var origin=new Vec3d(1e9,-2e9,3e9);var rotation=new Quaternion(Vector3.Up,.7f);
            s.Place(origin,rotation);
            var start=origin+Vec3d.From(rotation*local);var aim=rotation*direction;
            var blocked=DamageRay.Apply(s,start,aim,new(1,100000,400,range),0,1);
            Check(blocked.ShieldStopped && Part(s,bay.Id).HealthFraction==1,"Shield boundary blocks a bay hit before the hull");
            s.Damage.AbsorbShield(float.MaxValue,0);
            var hit=DamageRay.Apply(s,start,aim,new(100,100000,400,range),1,2);
            Check(hit.Modules.Any(m=>m.Id=="fit-"+bay.Id && m.Destroyed),$"{hull.Id}/{bay.Id}: actual transformed penetration reaches and destroys equipment");
            Check(hit.StructuralDamage>0 && !s.Damage.Destroyed,"Bay penetration retains existing structure damage without an arbitrary instant kill");
            Check(s.Damage.EquipmentPowerMw(ShipEquipment.ById(equipment).Channel)==0,"Ray-destroyed equipment stops consuming power");
        }
    }
    private static void Recovery()
    {
        var s=Ship(Fit("escort",new EquipmentMount("utility-port","shield-capacity")).Definition);
        var module=Part(s,"utility-port");float mass=s.Class.MassKg;Hit(s,module);
        s.DamageControl.Priority=RepairPriority.Defense;
        int kits=s.DamageControl.Kits;Step(s,60);
        Check(Near(module.HealthFraction,.35f) && s.DamageControl.Kits==kits-1,"Emergency defense repair restores an installed module using one kit");
        Check(Near(s.Damage.ShieldCapacity,s.Definition.Shield.Capacity*1.14f) && s.Class.MassKg==mass,"Repair grants partial capacity and destruction never discards installed mass");
        s.Damage.Reset();Check(module.HealthFraction==1 && Near(s.Damage.ShieldCapacity,s.Definition.Shield.Capacity*1.4f),"Explicit reset recomputes full equipment benefits");
        var core=Data(Fit("escort",new EquipmentMount("utility-starboard","sensor-array")).Definition);
        foreach(var m in core["Modules"]!.AsArray())m!["CriticalChance"]=0;
        var ship=Ship(ShipDefinition.Parse(core.ToJsonString()));
        foreach(var m in ship.Damage.Modules.Where(m=>!m.Definition.Id.StartsWith("fit-")))Hit(ship,m);
        Check(ship.Damage.Destroyed && ship.Damage.SensorStrengthBonus==0,"Surviving accessory cannot keep a destroyed core alive");
    }
    private static void Drones()
    {
        var def=Fit("battleship",new EquipmentMount("hangar","drone-hangar")).Definition;
        var world=new SimWorld();var carrier=world.Add(Ship(def));var drones=carrier.Ordnance.Drones;
        var attacker=world.Add(Ship(ShipDefinitions.ById("interceptor"),"ATTACKER",Faction.Red));
        attacker.Place(new(0,0,4000),Quaternion.Identity);attacker.Ordnance.Antimatter.Jettison();
        carrier.Control=attacker.Control=new ShipControl { FlightAssist=false };
        carrier.DamageControl.Priority=RepairPriority.Hold;
        var hangar=Part(carrier,"hangar");int first=def.InstalledEquipment[0].DroneStartIndex;
        Check(first==8 && drones.Rounds.Length==10 && drones.ArmedCount==10,"Expansion adds two distinct armed drones");
        for(int i=0;i<first;i++)drones.Rounds[i]=0;
        drones.Assign(DroneSector.Fore);AntimatterChecks.Step(world,20);
        world.Sensors.Update(world.Ships,world.Time,force:true);
        Check(world.LaunchMissile(attacker,carrier).Fired,"Threat uses actual missile launch");
        var missile=world.Missiles.Last();missile.Position=missile.PrevPosition=carrier.Position+new Vec3d(0,0,-1800);
        missile.Velocity=Vector3.Zero;missile.Age=missile.Definition.BurnSeconds+1;missile.Health=100000;
        int extraRounds=drones.Rounds.Skip(first).Sum();AntimatterChecks.Step(world,2);
        Check(drones.Rounds.Skip(first).Sum()<extraRounds,"New drones fire through the existing defense pipeline");
        Hit(carrier,hangar);int[] remaining=(int[])drones.Rounds.Clone();AntimatterChecks.Step(world,1);
        Check(!drones.Operational(first) && drones.Alive(first) && drones.Rounds.SequenceEqual(remaining),"Destroyed hangar disables its drones without deleting or reloading them");
        Check(Enumerable.Range(0,first).All(drones.Operational),"Base drones retain their own command support");
        world.DamageDrone(attacker,carrier,first,100,world.Time);
        Check(carrier.Damage.EmergencyRestore(hangar,.35f,world.Time),"Hangar can receive an emergency bypass");
        Check(!drones.Alive(first) && drones.Operational(first+1) && drones.Rounds.SequenceEqual(remaining),"Repair restores surviving extra drone only, preserving ammunition");
        foreach(var bus in carrier.Damage.Modules.Where(m=>m.Definition.Kind==ModuleKind.PowerBus && m.Definition.Grid==hangar.Definition.Grid))Hit(carrier,bus);
        Check(!drones.Operational(first+1),"Hangar grid outage cuts command support");
        carrier.Damage.Reset();Check(!drones.Alive(first) && drones.Rounds.SequenceEqual(remaining),"Damage reset alone never resurrects spent drone assets");
        world.ResetWeapons();Check(drones.Alive(first) && drones.Rounds.Sum()==800,"Explicit new-battle ordnance reset restores full loadout");
        var critical=Data(def);
        critical["InstalledEquipment"]![0]!["Equipment"]!["CriticalChance"]=1;
        critical["Modules"]!.AsArray().Last()!["CriticalChance"]=1;
        var doomed=Ship(ShipDefinition.Parse(critical.ToJsonString()));Hit(doomed,Part(doomed,"hangar"),energy:1000);
        Check(doomed.Damage.Destroyed && doomed.Damage.Destruction==DestructionKind.Magazine && !doomed.Ordnance.Drones.Active,
            "Configured local hangar catastrophe uses magazine destruction and disables the swarm");
        Check(!doomed.Damage.EmergencyRestore(Part(doomed,"hangar"),.35f,1),"Catastrophic destruction cannot be repaired");
    }
}
