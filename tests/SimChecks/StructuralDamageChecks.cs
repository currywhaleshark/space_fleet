using System.Text.Json.Nodes;
using Godot;
using SpaceFleet.Sim;

static class StructuralDamageChecks
{
    private static int _checks;
    private static void Check(bool ok,string why) { if(!ok)throw new InvalidOperationException(why);_checks++; }
    private static bool Near(float a,float b) => Math.Abs(a-b)<.001f;
    private static JsonObject Data(string id="interceptor")
    {
        using var stream=typeof(ShipDefinitions).Assembly.GetManifestResourceStream($"SpaceFleet.data.ships.{id}.json")!;
        using var reader=new StreamReader(stream);
        return JsonNode.Parse(reader.ReadToEnd())!.AsObject();
    }
    private static JsonObject Fixture(float armor=0,float shield=0)
    {
        var data=Data();data.Remove("railgun");data.Remove("antimatter");
        data["structureHitPoints"]=400;
        data["shield"]!["capacity"]=shield;
        data["hullSections"]=JsonNode.Parse("""
            [{"id":"hull","name":"시험 선체","center":[0,0,0],"halfSize":[10,10,10],"armor":{"thicknessMm":0}}]
            """);
        data["hullSections"]![0]!["armor"]!["thicknessMm"]=armor;
        data["modules"]=JsonNode.Parse("""
            [{"id":"reactor","name":"시험 반응로","kind":"Reactor","center":[-3,0,0],"halfSize":[1,1,1],"hitPoints":10,"resistanceMm":0},
             {"id":"sensor","name":"보존 모듈","kind":"Sensor","center":[0,5,0],"halfSize":[1,1,1],"hitPoints":1000,"resistanceMm":0}]
            """);
        return data;
    }
    private static ShipBody Ship(JsonObject data) => new("STRUCTURE",ShipDefinition.Parse(data.ToJsonString()),Faction.Blue);
    private static ShotResult Fire(ShipBody s,uint seq=1,Vec3d? origin=null,Vector3? direction=null,DamagePacket? packet=null)
        => DamageRay.Apply(s,origin??new Vec3d(-30,0,0),direction??Vector3.Right,packet??new(100,100,100,100),seq,seq);
    public static void Run()
    {
        var disabled=Ship(Fixture());
        Fire(disabled);
        Check(disabled.Damage.Disabled&&!disabled.Damage.Destroyed,"Power failure leaves a disabled physical hull");
        Check(Near(disabled.Damage.StructureHealth,300),"One full penetrating ray counts once rather than per module");
        var repeated=Fire(disabled,2);
        Check(repeated.Modules.Count==0&&Near(repeated.StructuralDamage,100)&&Near(disabled.Damage.StructureHealth,200),
            "A destroyed compartment still transmits permanent structural damage");
        Check(disabled.Damage.Module("sensor").Health==1000,"Structural wear does not fabricate off-ray module hits");
        var reactor=disabled.Damage.Module("reactor");
        Check(disabled.Damage.EmergencyRestore(reactor,.35f,3)&&!disabled.Damage.Disabled,"Emergency restoration can recover mobility/power");
        Check(Near(disabled.Damage.StructureHealth,200),"Emergency module repair never heals structural wear");
        Fire(disabled,3);var killed=Fire(disabled,4);
        Check(disabled.Damage.Destroyed&&disabled.Damage.Destruction==DestructionKind.Structural&&killed.CausedDestruction&&killed.Summary=="치명 파괴",
            "Repeated penetration finishes a disabled ship without a critical roll");
        Check(disabled.Damage.StructureHealth==0&&disabled.Damage.Shield==0&&disabled.Damage.Modules.All(m=>m.Destroyed),"Collapse leaves an irrecoverable wreck");
        Check(!disabled.Damage.EmergencyRestore(reactor,.35f,5),"Structural collapse cannot be emergency repaired");
        Check(disabled.Damage.DestroyedAt==4&&disabled.Damage.DestructionPoint.IsEqualApprox(Vector3.Zero),"Breakup records the final penetration location/time");
        var debris=new WreckLayout(disabled.Definition,disabled.Damage.Destruction,disabled.Damage.DestructionPoint);
        Check(debris.Pieces.Count==2,"Cumulative structural collapse uses the existing split-hull wreck");
        var wreckHit=Fire(disabled,5);Check(!wreckHit.CausedDestruction&&disabled.Damage.DestroyedAt==4,"Hitting wreckage cannot cause repeated deaths");
        disabled.Damage.Reset();Check(!disabled.Damage.Destroyed&&Near(disabled.Damage.StructureFraction,1),"Full new-battle reset restores structure");

        var shield=Ship(Fixture(shield:200));var blocked=Fire(shield);
        Check(blocked.ShieldStopped&&blocked.StructuralDamage==0&&Near(shield.Damage.StructureFraction,1),"Shield-stopped shots cannot wear the hull");
        var armor=Ship(Fixture(armor:100));blocked=Fire(armor);
        Check(blocked.ArmorStopped&&blocked.StructuralDamage==0&&Near(armor.Damage.StructureFraction,1),"Unpenetrated entry armor cannot wear the hull");
        var halfShield=Ship(Fixture(shield:50));
        Check(Near(Fire(halfShield).StructuralDamage,50),"Partial shield absorption scales structural damage");
        var bonusPacket=new DamagePacket(100,100,100,100) { StructuralDamageMultiplier=2 };
        var normalBonusTarget=Ship(Fixture());var bonusTarget=Ship(Fixture());
        var normalHit=Fire(normalBonusTarget);var bonusHit=Fire(bonusTarget,packet:bonusPacket);
        Check(Near(bonusHit.StructuralDamage,normalHit.StructuralDamage*2)&&bonusHit.Modules.SequenceEqual(normalHit.Modules),
            "Structure multiplier doubles only frame damage, beyond the ordinary per-shot cap");
        Check(Fire(Ship(Fixture(shield:200)),packet:bonusPacket).StructuralDamage==0,
            "Structure bonus cannot bypass a stopping shield");
        Check(Fire(Ship(Fixture(armor:100)),packet:bonusPacket).StructuralDamage==0,
            "Structure bonus cannot bypass entry armor");
        Check(Near(Fire(Ship(Fixture(shield:50)),packet:bonusPacket).StructuralDamage,100),
            "Partially absorbed hits apply the bonus after shield attenuation");
        Check(Near(Fire(Ship(Fixture(armor:60)),packet:bonusPacket).StructuralDamage,80),
            "Exit-stopped penetrations still receive the structural bonus");
        Check(Fire(bonusTarget,2,packet:bonusPacket).CausedDestruction&&bonusTarget.Damage.Destruction==DestructionKind.Structural,
            "A boosted hit can finish a previously destroyed compartment through frame collapse");
        foreach(float invalid in new[]{0f,-1f,float.NaN,float.PositiveInfinity,float.MaxValue})
        {
            bool rejected=false;
            try{(bonusPacket with { StructuralDamageMultiplier=invalid }).Validate();}catch(ArgumentException){rejected=true;}
            Check(rejected,"Invalid or overflowing structure multipliers must be rejected");
        }
        var exitStop=Ship(Fixture(armor:60));var penetrated=Fire(exitStop);
        Check(penetrated.ArmorStopped&&Near(penetrated.StructuralDamage,40),"Exit armor stopping a penetrator preserves its internal structural damage");
        var gap=Ship(Fixture());var empty=Fire(gap,origin:new(-30,-5,0));
        Check(empty.Modules.Count==0&&Near(empty.StructuralDamage,100),"Penetrating hull frame without intersecting a module still damages structure");
        var miss=Ship(Fixture());var missed=Fire(miss,origin:new(-30,30,0));
        Check(missed.Target is null&&Near(miss.Damage.StructureFraction,1),"An actual miss cannot damage structure");
        var shortRay=Ship(Fixture());
        Check(Near(Fire(shortRay,packet:new(100,100,100,22)).StructuralDamage,10),"A short ray only counts the traversed hull depth");
        var tooShort=Ship(Fixture());
        Check(Fire(tooShort,packet:new(100,100,100,19)).Target is null&&tooShort.Damage.StructureFraction==1,"A ray ending before entry cannot cause damage");
        var graze=Ship(Fixture());var central=Ship(Fixture());var diagonal=new Vector3(1,1,0).Normalized();
        float edge=Fire(graze,origin:new(-30,-11,0),direction:diagonal).StructuralDamage;
        float center=Fire(central,origin:new(-30,-30,0),direction:diagonal).StructuralDamage;
        Check(edge>0&&edge<center,"Corner grazing is weaker than a central chord");

        var overlap=Fixture();overlap["hullSections"]!.AsArray().Add(overlap["hullSections"]![0]!.DeepClone());
        overlap["hullSections"]![1]!["id"]="overlap";
        Check(Near(Fire(Ship(overlap)).StructuralDamage,100),"Overlapping authored hull boxes cannot multiply structural damage");
        var separated=Fixture();separated["hullSections"]=JsonNode.Parse("""
            [{"id":"left","name":"Left","center":[-6,0,0],"halfSize":[2,10,10],"armor":{"thicknessMm":0}},
             {"id":"right","name":"Right","center":[6,0,0],"halfSize":[2,10,10],"armor":{"thicknessMm":0}}]
            """);
        separated["modules"]![0]!["center"]=JsonNode.Parse("[-6,0,0]");
        separated["modules"]![1]!["center"]=JsonNode.Parse("[6,5,0]");
        Check(Near(Fire(Ship(separated)).StructuralDamage,50),"Empty space between separate hull sections never adds structural damage");
        var resistance=Fixture();resistance["modules"]![0]!["resistanceMm"]=200;
        float stopped=Fire(Ship(resistance)).StructuralDamage;
        Check(stopped>0&&stopped<100,"An internal stop only damages the traversed part of the hull");
        var framed=Ship(Fixture());var moved=Ship(Fixture());
        var rotation=new Quaternion(Vector3.Up,.7f);var offset=new Vec3d(1e12,-2e12,3e12);moved.Place(offset,rotation);
        var transformed=DamageRay.Apply(moved,offset+Vec3d.From(rotation*new Vector3(-30,0,0)),rotation*Vector3.Right,new(100,100,100,100),1,1);
        Check(Near(Fire(framed).StructuralDamage,transformed.StructuralDamage),"Structural damage respects orientation and double world coordinates");
        var critical=Fixture();critical["modules"]![0]!["criticalChance"]=1;critical["modules"]![0]!["criticalEnergy"]=0;
        var explosive=Ship(critical);Fire(explosive);
        Check(explosive.Damage.Destruction==DestructionKind.Reactor,"A core detonation keeps its own cause rather than being overwritten by wear");
        foreach(float invalid in new[]{0f,-1f})
        {
            var data=Fixture();data["structureHitPoints"]=invalid;bool rejected=false;
            try{ShipDefinition.Parse(data.ToJsonString());}catch(InvalidDataException){rejected=true;}
            Check(rejected,"Invalid structure capacity must fail data loading");
        }
        foreach(var definition in ShipDefinitions.All)
        {
            // Preserve production geometry/armor/capacity while isolating frame failure from core/AM explosions.
            var data=Data(definition.Id);
            foreach(var module in data["modules"]!.AsArray())module!["criticalChance"]=0;
            var s=new ShipBody("PRODUCTION",ShipDefinition.Parse(data.ToJsonString()),Faction.Blue);
            s.Ordnance.Antimatter.Jettison();
            var d=s.Damage;d.AbsorbShield(999999,0);
            // Disable the ship without damaging its frame, then finish through the same wrecked compartment.
            foreach(var m in d.Modules.Where(m=>m.Definition.Kind is ModuleKind.Reactor or ModuleKind.Generator))d.Hurt(m,m.Health,0,0,1);
            Check(d.Disabled&&!d.Destroyed&&d.StructureFraction==1,$"{definition.Id}: independent frame survives power failure");
            var core=d.Modules.First(m=>m.Definition.Kind==ModuleKind.Reactor);
            Vec3d from=Vec3d.From(core.Definition.Center-Vector3.Right*definition.Flight.Length*2);
            var packet=new DamagePacket(1000,100000,100,definition.Flight.Length*4);
            uint hits=0;
            while(!d.Destroyed&&hits<1000)DamageRay.Apply(s,from,Vector3.Right,packet,++hits,hits);
            Check(d.Destroyed&&d.Destruction==DestructionKind.Structural&&hits>1&&hits<1000,$"{definition.Id}: repeat fire finishes disabled production hull ({d.Destruction}, {hits} rays)");
            Console.WriteLine($"structure finish {definition.Id}: {hits} penetrating 100-damage rays");
        }
        Console.WriteLine($"PASS: {_checks} cumulative hull structure checks");
    }
}
