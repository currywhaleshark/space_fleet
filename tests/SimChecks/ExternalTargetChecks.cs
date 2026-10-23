using Godot;
using SpaceFleet.Sim;

static class ExternalTargetChecks
{
    private static int checks;
    private static void Check(bool value,string reason){if(!value)throw new Exception(reason);checks++;}
    public static void Run()
    {
        var hulls=FleetLoadouts.Hulls.ToArray();
        Parallel.For(0,hulls.Length*4,i=>{if(ExternalTargets.For(hulls[i%hulls.Length]) is null)throw new Exception("Concurrent model cache load");});
        foreach(var def in hulls)
        {
            var model=ExternalTargets.For(def);Check(model is not null,def.Id+" external metadata");
            Check(model!.Parts.Length is >0 and <=64,def.Id+" bounded targets");
            Check(model.Parts.Select(p=>p.Id).Distinct().Count()==model.Parts.Length,def.Id+" stable IDs");
            foreach(var p in model.Parts)
            {
                Check(p.ModuleId is null||def.Modules.Any(m=>m.Id==p.ModuleId),def.Id+" module mapping "+p.Id);
                Check(!def.Modules.Any(m=>m.Id==p.ModuleId&&m.Kind is ModuleKind.Reactor or ModuleKind.Generator or ModuleKind.Magazine or ModuleKind.PowerBus or ModuleKind.TacticalModule),"No internals "+p.Id);
                Check(p.Samples.Length>0&&p.Samples.All(v=>v.IsFinite()),"Actual surface samples");
            }
        }
        var shooter=new ShipBody("S",ShipDefinitions.ById("battleship"),Faction.Blue);
        var target=new ShipBody("T",ShipDefinitions.ById("battleship"),Faction.Red);
        target.Place(Vec3d.Zero,Quaternion.Identity);shooter.Place(new(0,1200,1200),Quaternion.Identity);
        var sample=ExternalTargets.Observe(target,shooter.Position);
        var track=new SensorTrack(TrackLevel.Locked,1000000,1000000,0,1700,0,target.Position)
        {SampleTime=1,ExternalVisible=sample.Visible,ExternalDestroyed=sample.Destroyed,ObservedOrientation=target.Orientation,
            ObservationOrigin=shooter.Position,Observation=new(shooter,1,target.Position,Vector3.Zero,target.Orientation,1)};
        var modelBB=ExternalTargets.For(target.Definition)!;
        var choices=modelBB.Parts.Where(p=>ExternalTargets.TryAim(shooter,target,track,1,p.Id,out _)).ToArray();
        Check(choices.Length>0,"Close locked external targets");
        foreach(var p in choices)
        {
            Check(!ExternalTargets.TryAim(shooter,target,track with{Level=TrackLevel.Identified},1,p.Id,out _),"Identification is not precision lock");
            Check(!ExternalTargets.TryAim(shooter,target,track with{Level=TrackLevel.Contact},1,p.Id,out _),"Contact cannot target parts");
            Check(!ExternalTargets.TryAim(shooter,target,track,3,p.Id,out _),"Stale observation");
            Check(!ExternalTargets.TryAim(shooter,target,track with{ExternalVisible=0},1,p.Id,out _),"No shared observation of hidden surfaces");
        }
        var first=choices[0];
        Check(!ExternalTargets.TryAim(shooter,target,track with{JamRatio=1e9f},1,first.Id,out _),"Locked but imprecise cannot select a small part");
        var cover=new ObstacleSet();cover.Add(new("cover",new(0,600,600),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,450)));
        Check(!ExternalTargets.TryAim(shooter,target,track,1,first.Id,out _,obstacles:cover),"Datalink cannot see through terrain on firing line");
        Check(!ExternalTargets.TryAim(shooter,target,track with{ExternalDestroyed=ulong.MaxValue},1,first.Id,out _),"Observed destroyed part cannot be selected");
        var engine=modelBB.Parts.First(p=>p.Kind==AimSubsystem.Engines);
        Check(modelBB.Visible(engine,new(0,0,5000),out _,out _),"Aft nozzle exposed");
        Check(!modelBB.Visible(engine,new(0,0,-5000),out _,out _),"Bow cannot see aft nozzle");
        var lazy=track with{ExternalObservation=ExternalTargets.Capture(target,shooter.Position)};
        foreach(var part in modelBB.Parts)
            Check(ExternalTargets.TryAim(shooter,target,track,1,part.Id,out _)==ExternalTargets.TryAim(shooter,target,lazy,1,part.Id,out _),"Lazy observation equals eager geometry");
        var p0=choices[0];Check(ExternalTargets.TryAim(shooter,target,track,1,p0.Id,out var before),"Known part");
        target.Place(new(900000,800000,700000),new(Vector3.Up,1));
        Check(ExternalTargets.TryAim(shooter,target,lazy,1,p0.Id,out var after)&&before.Point==after.Point,"Live hidden pose cannot change observed aim");
        var internalPoint=target.Definition.Modules.First(m=>m.Kind==ModuleKind.Reactor).Center;
        Check(ExternalTargets.ResolvePoint(shooter,target,track,1,internalPoint) is null,"Internal module centre rejected");
        target.Place(Vec3d.Zero,new(Vector3.Up,.2f));
        var probeTrack=track with{Observation=null,ObservedOrientation=target.Orientation,ExternalObservation=ExternalTargets.Capture(target,shooter.Position)};
        var probePart=modelBB.Parts.First(p=>ExternalTargets.TryAim(shooter,target,probeTrack,1,p.Id,out _));
        ExternalTargets.TryAim(shooter,target,probeTrack,1,probePart.Id,out var probeAim);
        var probeSolution=FireControl.Solve(shooter,target,1,sensorError:false,track:probeTrack,localAim:probeAim.Point);
        Check((probeSolution.AimPoint-Vec3d.From(probeTrack.ObservedOrientation*probeAim.Point)).Length()<.01,"Probe provenance still applies the measured hull orientation to a surface aim");
        Console.WriteLine($"PASS: {checks} external targeting checks");
    }
}
