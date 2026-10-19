using Godot;
using SpaceFleet.Game;
using SpaceFleet.Sim;

static class TierBranchChecks
{
    private static int checks;
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
    public static void Run()
    {
        Catalog();Storage();Missions();Probe();Containment();AuthoredPorts();InterceptorGeometry();T5Geometry();
        Console.WriteLine($"PASS: {checks} tier branch/catalog/save/recon/rally checks");
    }
    private static void Catalog()
    {
        var tree=ResearchTree.Catalog;
        var added=ShipVariants.All.Where(v=>v.Node.Id.Contains("_t")).ToArray();
        Check(added.Length==22&&tree.Nodes.All(n=>n.HullId is not null),"All 34 planned hulls are playable");
        Check(FleetLoadouts.Hulls.Select(d=>d.ModelId).Distinct().Count()==34,"Every planned hull has an independent model ID");
        foreach(var v in added)
        {
            var d=v.Definition;var original=ShipDefinitions.ById(v.Node.BaseHullId);
            bool authoredSmall=d.Id is "earth_recon_t3" or "mars_assault_t3" or "interceptor_t3" or "mars_interceptor_t3"
                or "earth_recon_t4" or "mars_assault_t4" or "interceptor_t4" or "mars_interceptor_t4"
                or "earth_recon_t5" or "mars_assault_t5" or "interceptor_t5" or "mars_interceptor_t5";
            bool authoredCruiser=d.Id is "earth_gun_cruiser_t4" or "earth_air_defense_cruiser_t4" or "mars_gun_cruiser_t4" or "mars_missile_cruiser_t4";
            bool authored=authoredSmall||authoredCruiser||v.Node.Tier==5;
            Check(d.ModelId==(authored?d.Id:original.ModelId)&&d.Flight.Length==original.Flight.Length,"Authored branch gets its own model; all tiers preserve nominal scale");
            if(!authored)Check(d.Modules.All(m=>original.Modules.Any(b=>b.Id==m.Id&&b.Center==m.Center&&b.HalfSize==m.HalfSize)),"Core module volumes stay aligned with reused model");
            else if(authoredSmall)
            {
                var am=d.Modules.Single(m=>m.Kind==ModuleKind.AntimatterContainment);var reactor=d.Modules.Single(m=>m.Kind==ModuleKind.Reactor);
                Check(Math.Abs(am.Center.Z-reactor.Center.Z)>am.HalfSize.Z+reactor.HalfSize.Z,"Authored AM bay is separated from the reactor");
                // Engines may hug the fuselage or sit far outboard (reference silhouettes); either way the
                // damage volume must lie inside the authored engine nacelle section, off the centreline.
                Check(d.Modules.Where(m=>m.VisualEngineIndex>=0).All(m=>Math.Abs(m.Center.X)>1&&d.HullSections.Any(s=>s.Id.StartsWith("engine")
                    &&Math.Abs(m.Center.X-s.Center.X)+m.HalfSize.X<=s.HalfSize.X+.001f&&Math.Abs(m.Center.Y-s.Center.Y)+m.HalfSize.Y<=s.HalfSize.Y+.001f
                    &&Math.Abs(m.Center.Z-s.Center.Z)+m.HalfSize.Z<=s.HalfSize.Z+.001f)),"Engine damage lies inside its authored nacelle");
            }
            Check((d.Railgun!.Mounts??Array.Empty<TurretDefinition>()).Select(m=>m.Pivot).SequenceEqual((original.Railgun!.Mounts??Array.Empty<TurretDefinition>()).Select(m=>m.Pivot)),"Tier growth preserves gun pivots");
            Check(tree.ByHull(d.Id).Tier==v.Node.Tier,"Variant and progression tiers agree");
            var campaign=HangarProfile.Campaign(d.Design);campaign.Research!.GrantPath(d.Id);campaign.SelectedHullId=d.Id;
            var match=Matchmaking.CreateTiered(13,campaign.Squadron(),campaign.Owned,campaign.Research.UpgradeSets());
            Check(match.Player.Loadout!.Leader.Id==d.Id&&match.Participants.Count==20,"Every newly unlocked hull can deploy with its owned default squad");
            var blue=match.Participants.Where(p=>p.Team==Faction.Blue).ToArray();var red=match.Participants.Where(p=>p.Team==Faction.Red).ToArray();
            IEnumerable<(MatchWeightClass,int)> Tiers(MatchParticipant[] team)=>team.SelectMany(p=>p.Loadout!.Members.Select((m,i)=>(m.Definition.MatchClass,p.MemberTiers![i]))).Order();
            Check(Tiers(blue).SequenceEqual(Tiers(red))&&Math.Abs(blue.Sum(p=>p.MatchPoints)-red.Sum(p=>p.MatchPoints))<.00001,"High-tier opponents mirror member classes, tiers and research score");
            Check(match.Participants.SelectMany(p=>p.MemberTiers!).All(t=>t>=match.LowTier&&t<=match.HighTier),"New branch ships obey the full squad tier bracket");
            if(d.Branch==ShipBranch.Recon)
            {
                var ic=ShipDefinitions.ById("interceptor_t"+v.Node.Tier);
                Check(d.ReconProbes!.Count>ic.ReconProbes!.Count&&d.ReconProbes.LifetimeSeconds>ic.ReconProbes.LifetimeSeconds&&d.Sensors.Strength>ic.Sensors.Strength,"Recon gains probes and sensing over same-tier interceptor");
                Check(d.Antimatter!.Rounds==1&&d.Railgun.Energy<ic.Railgun!.Energy&&d.StructureHitPoints<ic.StructureHitPoints,"Recon pays for sensing with assault power and durability");
            }
            if(d.Branch==ShipBranch.Assault)
            {
                var ic=ShipDefinitions.ById("mars_interceptor_t"+v.Node.Tier);
                Check(d.ReconProbes is null&&d.Antimatter!.Rounds>ic.Antimatter!.Rounds&&d.StructureHitPoints>ic.StructureHitPoints,"Assault trades probes for AM and survival");
                Check(d.Flight.BoostMultiplier>ic.Flight.BoostMultiplier&&d.Flight.ForwardAccel<ic.Flight.ForwardAccel&&d.PointDefense!.DamagePerHit<ic.PointDefense!.DamagePerHit,"Assault is heavier with stronger boost but weaker PD");
                Check(d.Antimatter!.ArmedFailureHealth==ic.Antimatter!.ArmedFailureHealth,"Extra AM does not remove containment vulnerability");
            }
        }
    }
    private static void InterceptorGeometry()
    {
        foreach(string id in new[]{"interceptor_t3","mars_interceptor_t3","earth_recon_t4","interceptor_t4","mars_assault_t4","mars_interceptor_t4","earth_recon_t5","interceptor_t5","mars_assault_t5","mars_interceptor_t5"})
        {
            var def=ShipDefinitions.ById(id);
            foreach(var module in def.Modules.Where(m=>m.Kind is ModuleKind.AntimatterContainment or ModuleKind.Thruster or ModuleKind.Sensor or ModuleKind.Gun or ModuleKind.Cooling or ModuleKind.ShieldEmitter))
            {
                var ship=new ShipBody("RAY",def,Faction.Blue);ship.Place(Vec3d.Zero,Quaternion.Identity);
                var outward=module.Kind==ModuleKind.Thruster?Vector3.Back:Vector3.Up*(module.Center.Y<0?-1:1);
                var hit=DamageRay.ApplyAtPose(ship,Vec3d.From(module.Center+outward*1000),-outward,
                    new DamagePacket(1000000,1000000,1000),0,1,ship.Position,ship.Orientation,shieldApplies:false);
                Check(hit.HullHit&&hit.Modules.Any(m=>m.Id==module.Id),$"{id}: hull entry reaches relocated {module.Id}");
            }
        }
    }
    private static void Storage()
    {
        var old=HangarProfile.Create("interceptor");
        foreach(string id in old.Hulls.Keys.Where(id=>id.Contains("_t")).ToArray())old.Hulls.Remove(id);
        old.Research=ResearchProgress.New();foreach(string id in old.Hulls.Keys)old.Research.GrantPath(id);
        old.Research.Points[DesignFamily.Earth]=1234;old.Research.BuyUpgrade("interceptor","drive");
        string exact=HangarStorage.Serialize(old);string path=Path.Combine(AppContext.BaseDirectory,"fleet-stage6-"+Guid.NewGuid()+".json");
        try
        {
            File.WriteAllText(path,exact);var loaded=HangarStorage.Load(path,null);
            Check(loaded.CanSave&&loaded.Profile.Hulls.Count==34&&loaded.Profile.Owned.SetEquals(old.Owned),"Stage-five v2 save receives 22 empty hull entries without free unlocks");
            Check(loaded.Profile.Research!.Points[DesignFamily.Earth]==old.Research.Points[DesignFamily.Earth]&&loaded.Profile.Research.ForHull("interceptor").Contains("drive"),"Catalog expansion preserves RP and purchased research");
            loaded.Profile.Research.BuyHull("earth_recon_t3");loaded.Profile.SelectedHullId="earth_recon_t3";
            Matchmaking.CreateTiered(1,loaded.Profile.Squadron(),loaded.Profile.Owned,loaded.Profile.Research.UpgradeSets());
            HangarStorage.Save(path,loaded.Profile);
            Check(File.ReadAllText(path+".bak")==exact&&HangarStorage.Load(path,null).Profile.SelectedHullId=="earth_recon_t3","New branch purchase deploys and reloads, preserving the old save backup");
        }
        finally{foreach(string p in new[]{path,path+".bak",path+".tmp"})if(File.Exists(p))File.Delete(p);}
    }
    private static void T5Geometry()
    {
        var pose=new Quaternion(new Vector3(1,2,3).Normalized(),.63f);
        foreach(var variant in ShipVariants.All.Where(v=>v.Node.Tier==5))
        foreach(var module in variant.Definition.Modules)
        {
            var ship=new ShipBody("T5-RAY",variant.Definition,Faction.Blue);
            ship.Place(new(1e12,-1e12,1e12),pose);
            var local=module.Kind==ModuleKind.Thruster?Vector3.Back:module.Id.StartsWith("radiator-")
                ?Vector3.Right*Mathf.Sign(module.Center.X):Vector3.Up*(module.Center.Y<0?-1:1);
            var direction=pose*local;
            var origin=ship.Position+Vec3d.From(pose*module.Center+direction*3000);
            var hit=DamageRay.ApplyAtPose(ship,origin,-direction,new DamagePacket(1000000,1000000,1000),
                0,1,ship.Position,ship.Orientation,shieldApplies:false);
            Check(hit.HullHit&&hit.Modules.Any(m=>m.Id==module.Id),
                $"{variant.Node.Id}: rotated large-coordinate hull ray reaches {module.Id}");
        }
    }
    private static void Missions()
    {
        var(w,wing,anchor,target)=BranchTrials.Fixture("mars_assault_t3");
        w.OrderWing(wing,null);
        Check(wing.AssaultAvailable&&wing.Rallying&&wing.Members.All(s=>w.BrainOf(s)!.Target is null),"Autonomous assault holds for rendezvous despite an exposed target");
        Check(wing.Members.All(s=>s.Ordnance.Interceptor.FirstAssault is null),"Rallying is not recorded as an attack run");
        var points=wing.Members.Select(s=>w.BrainOf(s)!.Order.Point).ToArray();
        target.Place(new(500000,0,500000),Quaternion.Identity); // Deliberately do not update sensors.
        w.OrderWing(wing,null);
        Check(wing.Rallying&&points.SequenceEqual(wing.Members.Select(s=>w.BrainOf(s)!.Order.Point)),"Rally ignores unobserved changes to enemy position");
        target.Place(new(0,0,-42000),Quaternion.Identity);
        for(int i=0;i<2;i++)wing.Members[i].Place(points[i],Quaternion.Identity);
        w.OrderWing(wing,null);Check(wing.Rallying,"A scattered minority cannot trigger the full assault");
        wing.Members[2].Place(points[2],Quaternion.Identity);w.OrderWing(wing,null);
        Check(!wing.Rallying&&wing.Members.All(s=>w.BrainOf(s)!.Target==target),"Ready majority releases the wing together");
        Check(wing.Members.Select(s=>s.Ordnance.Interceptor.FirstAssault).Distinct().Count()==1&&wing.Members[0].Ordnance.Interceptor.FirstAssault.HasValue,"Coordinated launch has a common first assault time");
        w.OrderWing(wing,InterceptorMission.Return);
        Check(!wing.Rallying&&wing.Members.All(s=>w.BrainOf(s)!.Order.Kind==OrderKind.Escort),"Explicit return cancels the assault immediately");
        foreach(var s in wing.Members)s.Place(new(0,0,20000),Quaternion.Identity);
        w.OrderWing(wing,InterceptorMission.Assault,target);
        Check(!wing.Rallying&&wing.Members.All(s=>w.BrainOf(s)!.Target==target),"Player assault order can override automatic rally");
        foreach(var s in wing.Members.Take(3))s.Ordnance.Antimatter.Jettison();
        w.OrderWing(wing,null);
        foreach(var s in wing.Members.Skip(3))s.Place(w.BrainOf(s)!.Order.Point,Quaternion.Identity);
        w.OrderWing(wing,null);
        Check(!wing.Rallying&&wing.Members.Skip(3).All(s=>w.BrainOf(s)!.Target==target),"Spent wingmates cannot deadlock the remaining armed attackers at rendezvous");
        Check(wing.Members.Take(3).All(s=>w.BrainOf(s)!.Target is null),"Ships without AM return to air defense instead of following the assault");
        // 80 km: inside assault reach, but beyond where the anchor alone already covers what a probe would add.
        var(r,scouts,home,enemy)=BranchTrials.Fixture("earth_recon_t3",80_000);r.OrderWing(scouts,null);
        Check(scouts.Mission==InterceptorMission.Recon&&scouts.AssaultAvailable&&scouts.Target is null,"Recon branch stays on observation even with a tempting assault opportunity");
        Check(r.Probes.Count>0&&scouts.Members.All(s=>r.BrainOf(s)!.Target is null),"Recon begins actual probe work without an attack run");
        enemy.Place(scouts.Members[0].Position+new Vec3d(0,0,-5000),Quaternion.Identity);
        r.Sensors.Update(r.Ships,r.Time,true,r.Probes,r.Missiles);r.OrderWing(scouts,null);
        Check(r.BrainOf(scouts.Members[0])!.Order.Point.Z>home.Position.Z,"Close identified threat sends the scout behind the friendly anchor");
        r.OrderWing(scouts,InterceptorMission.Assault,enemy);
        Check(scouts.Mission==InterceptorMission.Assault&&scouts.Members.All(s=>r.BrainOf(s)!.Target==enemy),"Recon still honors an explicit assault order and its single AM round");
        var scoutTrial=BranchTrials.Run("earth_recon_t3",60,80_000);
        Check(scoutTrial.FirstAssault is null&&scoutTrial.Probes>0&&scoutTrial.MinimumRange>20000,"Sixty-second reconnaissance patrol stays clear of the capital's defense bubble");
        var assaultTrial=BranchTrials.Run("mars_assault_t3",120);
        Check(assaultTrial.RallySeconds>1&&assaultTrial.FirstAssault is >1&&assaultTrial.FirstAssault<90&&assaultTrial.AssaultSpread<1,"Dispersed assault wing actually navigates to rendezvous and begins a synchronized run");
        Check(assaultTrial.Torpedoes>0,"Rally doctrine retains real AM delivery");
    }
    private static void Probe()
    {
        var w=new SimWorld();var s=w.Add(new ShipBody("SCOUT",ShipDefinitions.ById("earth_recon_t5"),Faction.Blue));
        var enemy=w.Add(new ShipBody("HIDDEN",ShipDefinitions.ById("interceptor"),Faction.Red));enemy.Place(new(0,0,-41000),Quaternion.Identity);
        foreach(var module in s.Damage.Modules.Where(m=>m.Definition.Kind==ModuleKind.Sensor))s.Damage.Hurt(module,module.Health,0,0,1);
        s.DamageControl.Priority=RepairPriority.Hold;
        Check(w.LaunchProbeAt(s,new Vec3d(0,0,-36000)).Fired,"T5 reconnaissance probe launches"); // Station 5 km short of the target, inside the 80 km reach.
        CruiserTrials.Step(w,.5);Check(w.Sensors.Track(Faction.Blue,enemy).Level==TrackLevel.None,"High-tier probe does not reveal its destination instantly");
        CruiserTrials.Step(w,30);var track=w.Sensors.Track(Faction.Blue,enemy);
        Check(track.Level==TrackLevel.Identified&&track.Observation is null&&s.Ordnance.Interceptor.NewIdentifications>0,"Improved probe shares identification, never a free high-quality firing lock");
        int probes=s.Ordnance.Interceptor.Probes;w.ResetWeapons();
        Check(s.Ordnance.Interceptor.Probes==probes,"Combat reset does not refill improved probes");
    }
    private static void Containment()
    {
        foreach(var def in FleetLoadouts.Hulls.Where(d=>d.Branch is ShipBranch.Recon or ShipBranch.Assault))
        foreach(bool armed in new[]{false,true})
        {
            var w=new SimWorld();var s=w.Add(new ShipBody("PAYLOAD",def,Faction.Blue));var am=s.Ordnance.Antimatter;
            if(armed)Check(am.BeginArming(),"New branch uses existing arming state");
            var module=s.Damage.Module(def.Antimatter!.ModuleId);
            float fraction=(def.Antimatter.SafeFailureHealth+def.Antimatter.ArmedFailureHealth)*.5f;
            s.Damage.Hurt(module,module.Health*(1-fraction),0,20,1);
            Check(armed?s.Damage.Destroyed&&am.Mode==AntimatterMode.Failed:!s.Damage.Destroyed&&am.Warning,
                "Same relative containment hit remains more dangerous when arming, at every branch tier");
            if(!armed)
            {
                Check(am.Jettison()&&am.Rounds==0,"Emergency jettison discards the entire new magazine");
                s.Damage.Reset();w.ResetWeapons();Check(am.Rounds==0&&am.Jettisoned,"Repair/reset cannot refill the branch AM magazine");
            }
        }
    }

    private static void AuthoredPorts()
    {
        foreach(string reconId in new[]{"earth_recon_t3","earth_recon_t4","earth_recon_t5"})
        {
        var def=ShipDefinitions.ById(reconId);
        var w=new SimWorld();var ship=w.Add(new ShipBody("PORTS",def,Faction.Blue));
        ship.Place(new(1e12,-1e12,1e12),new Quaternion(Vector3.Up,.7f));
        ship.Control=new ShipControl {FlightAssist=false};
        var ports=def.ReconProbes!.LaunchPoints!;
        Check(ports.Length==def.ReconProbes.Count,"Recon has one authored exit per cartridge");
        for(int i=0;i<ports.Length;i++)
        {
            var expected=ship.Position+Vec3d.From(ship.Orientation*ports[i]);
            var request=ship.Position+new Vec3d(0,0,-100000);
            Check(SimWorld.TryProbeDestination(ship,request,out var preview),"Authored rack has a map preview");
            Check(w.LaunchProbeAt(ship,request).Fired,"Every authored rack launches once");
            var probe=w.Probes.Last();
            Check((probe.Position-expected).Length()<.001,"Launch uses rotated rack position at double-coordinate range");
            Check((preview-probe.Destination).Length()<.001 && Math.Abs((probe.Destination-probe.Position).Length()-def.ReconProbes.DeploymentMeters)<.01,
                "Map preview and actual range agree for offset launch racks");
            CruiserTrials.Step(w,def.ReconProbes.CooldownSeconds+.1);
        }
        Check(ship.Ordnance.Interceptor.Probes==0 && ship.Ordnance.Missiles==4 && ship.Ordnance.Antimatter.Rounds==1,
            "Physical rack depletion preserves independent missile/AM stores");
        Check(!w.LaunchProbe(ship,Vector3.Forward).Fired,"Empty authored rack cannot relaunch");
        }
        foreach(string id in new[]{"earth_recon_t3","mars_assault_t3","interceptor_t3","mars_interceptor_t3",
            "earth_recon_t4","interceptor_t4","mars_assault_t4","mars_interceptor_t4","earth_recon_t5","interceptor_t5","mars_assault_t5","mars_interceptor_t5"})
        {
            var world=new SimWorld();var shooter=world.Add(new ShipBody("FIRE",ShipDefinitions.ById(id),Faction.Blue));
            var target=world.Add(new ShipBody("TARGET",CruiserTrials.PassiveTarget(),Faction.Red));
            target.Place(new(0,0,-3000),Quaternion.Identity);
            shooter.Control=target.Control=new ShipControl {FlightAssist=false};
            world.Sensors.Update(world.Ships,0,true);
            Check(world.LaunchMissile(shooter,target).Fired,"Blockout retains missile launch");
            Check((world.Missiles.Last().Position-shooter.Position).ToVector3().DistanceTo(shooter.Definition.Missiles!.LaunchPoint)<.001,
                "Missile starts at authored rack mouth");
            Check(shooter.Ordnance.Antimatter.BeginArming(),"Blockout AM bay arms");
            CruiserTrials.Step(world,2.6);
            Check(world.LaunchAntimatter(shooter,target).Fired,"Blockout AM bay launches");
            Check((world.Missiles.Last().Position-shooter.Position).ToVector3().DistanceTo(shooter.Definition.Antimatter!.Flight.LaunchPoint)<.001,
                "AM starts at separate forward launch port");
            if(shooter.Definition.ReconProbes?.LaunchPoints is { } probePorts)
            {
                foreach(var port in probePorts)
                {
                    var expected=shooter.Position+Vec3d.From(shooter.Orientation*port);
                    Check(world.LaunchProbe(shooter,Vector3.Forward).Fired,"Small hull retains every physical probe rack");
                    Check((world.Probes.Last().Position-expected).Length()<.001,"Probe starts at the new rack mouth");
                    CruiserTrials.Step(world,shooter.Definition.ReconProbes.CooldownSeconds+.1);
                }
                Check(shooter.Ordnance.Interceptor.Probes==0&&shooter.Ordnance.Missiles==shooter.Definition.Missiles!.Rounds-1
                    &&shooter.Ordnance.Antimatter.Rounds==shooter.Definition.Antimatter!.Rounds-1,
                    "Relocated stores still consume probe, missile and AM magazines independently");
            }
        }
    }
}
