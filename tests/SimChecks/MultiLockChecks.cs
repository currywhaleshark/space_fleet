using Godot;
using SpaceFleet.Sim;

/// <summary>Modular weapons phase 6: simultaneous lock channels split multi-cell salvos across several locked targets.</summary>
static class MultiLockChecks
{
    private static int checks;
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
    private static ShipDefinition Fit(string id,ShipComponentSelection? parts)=>ShipLoadouts.Resolve(ShipVariants.Loadout(id) with{Components=parts}).Definition;

    public static void Run(){Channels();Salvos();Console.WriteLine($"PASS: {checks} lock channel/multi-target salvo checks");}

    private static void Channels()
    {
        foreach(var (id,parts,expected) in new (string,ShipComponentSelection?,int)[]{("interceptor",null,1),("escort",null,2),("battleship",null,3),
            ("mars_missile_cruiser_t4",null,3),("earth_gun_cruiser",new(Sensor:"sensor-longrange"),3),("mars_battleship_t5",null,3)})
            Check(SensorLocks.Channels(Fit(id,parts))==expected,$"{id}: {expected} lock channels");
    }

    /// <summary>
    /// One AI missile cruiser against three locked enemy escorts. A single tube sends every missile at its target;
    /// an 8-cell launcher splits the salvo across its three channels; a twin launcher on a 2-channel escort uses two.
    /// </summary>
    private static (int Launched,int Targets,int MaxOnOne) Salvo(string hull,ShipComponentSelection? parts,int enemies,double seconds=40)
    {
        var world=new SimWorld();
        var shooter=world.Add(new ShipBody("SHOOTER",Fit(hull,parts),Faction.Red));
        var targets=Enumerable.Range(0,enemies).Select(i=>
        {
            var t=world.Add(new ShipBody($"T{i}",ShipDefinitions.ById("escort"),Faction.Blue));
            t.Place(new Vec3d(-4000+i*4000,0,-24000),Quaternion.Identity);t.Control=new ShipControl{FlightAssist=false};return t;
        }).ToArray();
        world.AttachBrain(shooter,ShipOrder.AttackOn(targets[0]));
        var seen=new Dictionary<ShipBody,int>();int launched=0;
        while(world.Time<seconds)
        {
            int before=world.Missiles.Count;world.Step();
            foreach(var m in world.Missiles.Skip(before).Where(m=>m.Shooter==shooter&&m.Target is ShipBody))
            {launched++;var t=(ShipBody)m.Target!;seen[t]=seen.GetValueOrDefault(t)+1;}
            foreach(var t in targets)t.Damage.Reset(); // keep every target alive and locked
        }
        return (launched,seen.Count,seen.Count==0?0:seen.Values.Max());
    }

    private static void Salvos()
    {
        var stock=Salvo("mars_missile_cruiser_t4",null,3);
        Check(stock.Launched>=2&&stock.Targets==1,$"Single tube: every missile at the ordered target ({stock})");
        var vls=Salvo("mars_missile_cruiser_t4",new(){Launcher="launcher-vls8"},3);
        Check(vls.Launched>=6&&vls.Targets==3,$"8 cells over 3 channels split across 3 locked targets ({vls})");
        Check(vls.MaxOnOne<=vls.Launched-2,"No single target takes the whole salvo");
        var two=Salvo("mars_missile_cruiser_t4",new(){Launcher="launcher-vls8"},2);
        Check(two.Targets==2,$"Fewer locked targets than channels: split over those present ({two})");
        var escort=Salvo("escort",new(){Launcher="launcher-twin"},3);
        Console.WriteLine($"  salvos (launched, targets, most on one): tube {stock}, vls8 x3 {vls}, vls8 x2 {two}, twin escort {escort}");
        Check(escort.Launched>=2&&escort.Targets==2,$"Twin cells on a 2-channel escort use two targets ({escort})");
    }
}
