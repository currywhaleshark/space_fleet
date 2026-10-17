using Godot;
using SpaceFleet.Sim;
using System.Text.Json.Nodes;

static class GunneryChecks
{
    private static int _checks;
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); _checks++; }
    private static ShipBody Add(SimWorld world, string id, ShipClass cls, Faction faction, Vec3d at)
    {
        var ship = world.Add(new ShipBody(id, cls, faction));
        ship.Place(at, Quaternion.Identity);
        ship.Control = new ShipControl { FlightAssist = false };
        return ship;
    }
    private static void Step(SimWorld world, double seconds)
    { for (int i = 0; i < seconds * SimWorld.TickRate; i++) world.Step(); }
    public static void Run()
    {
        CheckArmorDoesNotVetoFire();
        foreach (var doctrine in new[] { FireDoctrine.Focus, FireDoctrine.Disable })
        {
            var world = new SimWorld();
            var dd = Add(world, "DD", ShipClass.Escort, Faction.Blue, Vec3d.Zero);
            var enemy = Add(world, "UNLOCKED", ShipClass.Escort, Faction.Red, new Vec3d(0, 0, -500_000));
            dd.Gunnery = new GunneryOrder { Doctrine = doctrine, Target = enemy };
            world.Step();
            Require(dd.Gunnery.Engaged == enemy && dd.Gunnery.Status == GunneryStatus.WaitLock, $"{doctrine} must retain selected target and wait for lock");
        }
        {
            var world = new SimWorld();
            var dd = Add(world, "DD", ShipClass.Escort, Faction.Blue, Vec3d.Zero);
            var enemy = Add(world, "ENEMY", ShipClass.Escort, Faction.Red, new Vec3d(0, 0, -20_000));
            var module = enemy.Damage.Modules.First(m => m.Definition.Kind == ModuleKind.Thruster);
            dd.Gunnery = new GunneryOrder { Doctrine = FireDoctrine.Focus, Target = enemy, PriorityModuleId = module.Definition.Id, AimPart = AimSubsystem.Sensors };
            Step(world, 1);
            Require(dd.Gunnery.EngagedModule == module, "Clicked module must take precedence over Y subsystem");
            dd.Gunnery.PriorityModuleId = "missing";
            world.Step();
            Require(dd.Gunnery.EngagedModule?.Definition.Kind == ModuleKind.Sensor, "Missing priority must fall back to Y subsystem");
        }
        {
            var world = new SimWorld();
            var dd = Add(world, "DD", ShipClass.Escort, Faction.Blue, Vec3d.Zero);
            var enemy = Add(world, "ENEMY", ShipClass.Escort, Faction.Red, new Vec3d(0, 0, -20_000));
            world.AttachBrain(dd, ShipOrder.HoldAt(dd.Position));
            dd.Gunnery = new GunneryOrder { Doctrine = FireDoctrine.Focus, Target = enemy };
            int rounds = dd.Railgun!.Rounds;
            Step(world, 1);
            Require(dd.Gunnery.Engaged is null && dd.Railgun.Rounds == rounds, "Gunnery must not take over an enabled AI ship");
        }
        foreach (FireDoctrine doctrine in new[] { FireDoctrine.Focus, FireDoctrine.Hold, FireDoctrine.Manual })
        {
            var world = new SimWorld();
            var dd = Add(world, "DD", ShipClass.Escort, Faction.Blue, Vec3d.Zero);
            var enemy = Add(world, "ENEMY", ShipClass.Escort, Faction.Red, new Vec3d(0, 0, -20_000));
            dd.Gunnery = new GunneryOrder { Doctrine = doctrine, Target = enemy };
            int rounds = dd.Railgun!.Rounds;
            Step(world, 20);
            Require(doctrine == FireDoctrine.Focus ? dd.Railgun.Rounds < rounds : dd.Railgun.Rounds == rounds, $"{doctrine} firing rule");
            if (doctrine == FireDoctrine.Focus) Require(enemy.Damage.Shield < enemy.Definition.Shield.Capacity, "Focus must hit enemy");
        }
        {
            var world = new SimWorld();
            var dd = Add(world, "DD", ShipClass.Escort, Faction.Blue, Vec3d.Zero);
            var ic = Add(world, "IC", ShipClass.Interceptor, Faction.Red, new Vec3d(0, 0, -15_000));
            Add(world, "BB", ShipClass.Battleship, Faction.Red, new Vec3d(0, 0, -60_000));
            dd.Gunnery = new GunneryOrder();
            Step(world, .5); // Inspect selection before the direct hit can detonate the interceptor's containment.
            Require(dd.Gunnery.Engaged == ic, "Free must prefer nearby interceptor");
        }
        {
            var world = new SimWorld();
            var dd = Add(world, "DD", ShipClass.Escort, Faction.Blue, Vec3d.Zero);
            var enemy = Add(world, "ENEMY", ShipClass.Escort, Faction.Red, new Vec3d(0, 0, -10_000));
            dd.Gunnery = new GunneryOrder { Doctrine = FireDoctrine.Disable, Target = enemy, AimPart = AimSubsystem.Engines };
            Step(world, 1);
            var module = dd.Gunnery.EngagedModule;
            Require(module is not null && module.Definition.Kind == ModuleKind.Thruster, "Disable must aim at functional module");
            Step(world, 90);
            Require(module!.HealthFraction < 1, "Disable must damage selected module");
        }
        {
            // 무력화 + 중심 조준: 가까운 겉 부위를 고른다. 관통 예측으로 제외하지 않는다.
            var world = new SimWorld();
            var dd = Add(world, "DD", ShipClass.Escort, Faction.Blue, Vec3d.Zero);
            var enemy = Add(world, "ENEMY", ShipClass.Escort, Faction.Red, new Vec3d(0, 0, -10_000));
            dd.Gunnery = new GunneryOrder { Doctrine = FireDoctrine.Disable, Target = enemy, AimPart = AimSubsystem.Center };
            Step(world, 1);
            var module = dd.Gunnery.EngagedModule;
            Require(module is not null, "Disable with Center must pick a module");
            Require(module!.Definition.Kind is ModuleKind.Thruster or ModuleKind.Gun or ModuleKind.Sensor
                || Subsystems.Matches(module.Definition, AimSubsystem.Radiators), $"Disable picked internal module {module.Definition.Id}");
            Require(!module.Destroyed,"Disable auto pick remains a functional module");
        }
        {
            // 발사 실패는 문구가 아니라 코드로 돌려준다.
            var world = new SimWorld();
            var dd = Add(world, "DD", ShipClass.Escort, Faction.Blue, Vec3d.Zero);
            var attempt = world.FireRailgun(dd, Vector3.Back);
            Require(!attempt.Fired && attempt.Failure == FireFailure.Arc, $"Rear shot failure: {attempt.Failure}");
            var bb = Add(world, "BB", ShipClass.Battleship, Faction.Blue, new Vec3d(50_000, 0, 0));
            attempt = world.FireRailgun(bb, new Vector3(0, -1, -0.2f).Normalized());
            Require(!attempt.Fired && attempt.Failure == FireFailure.Arc, $"Dorsal gun below arc: {attempt.Failure}");
        }
        {
            var world = new SimWorld();
            var bb = Add(world, "BB", ShipClass.Battleship, Faction.Blue, Vec3d.Zero);
            var enemy = Add(world, "BELOW", ShipClass.Escort, Faction.Red, new Vec3d(0, -8000, -2000));
            bb.Gunnery = new GunneryOrder { Doctrine = FireDoctrine.Focus, Target = enemy };
            int rounds = bb.Railgun!.Rounds;
            // 하부 주포가 아래로 약 76° 숙이는 시간(전함 포탑 8°/s) + 잠금.
            Step(world, 14);
            Require(bb.Railgun.Rounds == rounds, "Dorsal gun cannot shoot through its deck");
            Require(bb.Railguns[2].Rounds < rounds, "Ventral gun must engage below independently");
            bb.Control = new ShipControl { Roll = 1 };
            for (int i = 0; bb.Up.Y > -0.99f && i < 1800; i++) world.Step();
            bb.Control = ShipControl.Idle;
            Step(world, 30);
            Require(bb.Railgun.Rounds < rounds, "Battleship must fire after rolling clear");
        }
        Console.WriteLine($"PASS: {_checks} gunnery checks");
    }

    private static void CheckArmorDoesNotVetoFire()
    {
        var world=new SimWorld();
        var shooter=Add(world,"GUN",ShipClass.Interceptor,Faction.Blue,Vec3d.Zero);
        var data=JsonNode.Parse(ShipDefinitions.For(HullKind.Escort).ToJson())!.AsObject();
        data["HullSections"]=JsonNode.Parse(ShipDefinition.SerializeData(new[]{
            new HullSection("plate","thick test plate",Vector3.Zero,new(80,80,160),new ArmorPlate(100_000))}));
        var target=world.Add(new ShipBody("PLATE",ShipDefinition.Parse(data.ToJsonString(),true),Faction.Red));
        target.Place(new(0,0,-3000),Quaternion.Identity);
        foreach(var m in target.Damage.Modules.Where(m=>m.Definition.Kind==ModuleKind.ShieldEmitter))
            target.Damage.Hurt(m,m.Health,0,0,1);
        var gun=shooter.Railgun!;
        world.Sensors.Update(world.Ships,world.Time,true);
        var solution=FireControl.Solve(shooter,target,world.Time,track:world.Sensors.Track(shooter.Faction,target));
        Require(solution.Valid&&target.Damage.Shield<=1&&!DamageRay.PreviewArmor(target,gun.MuzzlePosition,solution.Direction,gun.Definition.PenetrationMm,out _),
            "Fixture is locked, in range and physically cannot penetrate its unshielded armor");
        int rounds=gun.Rounds;
        Require(world.TryAutoFire(shooter,target,null,30,out var status)&&status==GunneryStatus.Firing&&gun.Rounds==rounds-1,
            $"Thick armor must not veto a valid shot: {status}");
        Step(world,1);
        Require(world.Impacts.Any(i=>i.Hit.Target==target&&i.Hit.ArmorStopped),"Real impact still obeys the target armor");
        gun.Reset();rounds=gun.Rounds;
        target.Place(new(0,0,3000),Quaternion.Identity);world.Sensors.Update(world.Ships,world.Time,true);
        Require(!world.TryAutoFire(shooter,target,null,30,out status)&&status==GunneryStatus.Arc&&gun.Rounds==rounds,"Armor change does not bypass firing arc");
        target.Place(new(0,0,-gun.Definition.MaxRange-5000),Quaternion.Identity);
        var observer=Add(world,"LINK",ShipClass.Battleship,Faction.Blue,target.Position+new Vec3d(5000,0,0));
        world.Sensors.Update(world.Ships,world.Time,true);
        Require(world.Sensors.Track(shooter.Faction,target).Level==TrackLevel.Locked,"Range fixture has an allied firing lock");
        Require(!world.TryAutoFire(shooter,target,null,30,out status)&&status==GunneryStatus.Range&&gun.Rounds==rounds,"Armor change does not bypass weapon range");
    }
}
