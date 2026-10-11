using Godot;
using SpaceFleet.Sim;

static class ObstacleChecks
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool yes, string why) { if (!yes) throw new Exception(why); checks++; }
        var empty = new ObstacleSet();
        Check(!empty.SegmentBlocked(Vec3d.Zero, Vec3d.Zero, out _), "Empty segment query");
        Check(!empty.ClosestApproach(Vec3d.Zero, 1e20, out _), "Empty proximity query");
        foreach (double offset in new[] { 0.0, 1e12, -1e12 })
        foreach (var rotation in new[] { Quaternion.Identity, new Quaternion(Vector3.Up, .73f) })
        foreach (bool capsule in new[] { false, true })
        {
            Vec3d origin = new(offset, -offset, offset);
            var shape = capsule ? ObstaclePart.Capsule(new(0,-10,0), new(0,10,0), 5) : ObstaclePart.Sphere(Vec3d.Zero, 5);
            var obstacle = new Obstacle("test", origin, rotation, shape);
            var set = new ObstacleSet(); set.Add(obstacle);
            Vec3d W(double x, double y, double z) => obstacle.ToWorld(new(x,y,z));
            Check(set.SegmentBlocked(W(-20,0,0), W(20,0,0), out var hit) && Math.Abs(hit.Fraction - .375) < 1e-5, "First surface hit at large origin and rotated pose");
            Check((hit.Position - W(-5,0,0)).Length() < .001, "Hit world position");
            Check(set.SegmentBlocked(W(-20,0,4.999),W(20,0,4.999),out _), "Grazing segment");
            // Exact tangent is tested in axis-aligned poses; transformed 1e12 points have finite input quantization.
            if (rotation == Quaternion.Identity) Check(set.SegmentBlocked(W(-20,0,5),W(20,0,5),out _), "Tangent segment");
            Check(!set.SegmentBlocked(W(-20,0,5.01),W(20,0,5.01),out _), "Near tangent outside misses");
            Check(set.SegmentBlocked(W(0,0,0),W(20,0,0),out hit) && hit.Fraction == 0, "Inside start returns immediately");
            Check(set.SegmentBlocked(W(0,0,0),W(0,0,0),out hit) && hit.Fraction == 0, "Inside zero-length segment");
            Check(!set.SegmentBlocked(W(20,0,0),W(20,0,0),out _), "Outside zero-length segment");
            Check(!set.SegmentBlocked(W(20,0,0),W(30,0,0),out _), "Moving away misses");
            Check(set.ClosestApproach(W(8,0,0),4,out var proximity) && Math.Abs(proximity.Clearance+1)<.001, "Proximity clearance");
            Check(!set.ClosestApproach(W(8,0,0),2,out _), "Outside proximity radius");
            Check(ObstacleSet.BoxContact(obstacle,0,W(5.5,0,0),rotation,new(Vector3.Zero,Vector3.One),out var contact)
                && Math.Abs(contact.Penetration-.5)<.001, "Box overlaps curved surface");
            Check(!ObstacleSet.BoxContact(obstacle,0,W(7,0,0),rotation,new(Vector3.Zero,Vector3.One),out _), "Separated box");
            Check(ObstacleSet.BoxContact(obstacle,0,W(0,0,0),rotation,new(Vector3.Zero,Vector3.One),out contact)
                && contact.Normal.Length()>.99 && contact.Penetration>0, "Inside box has escape normal");
            if (capsule)
            {
                Check(set.SegmentBlocked(W(0,-30,0),W(0,30,0),out hit) && Math.Abs(hit.Fraction-.25)<1e-6, "Capsule parallel axial endcap");
                Check(!set.SegmentBlocked(W(-20,16,0),W(20,16,0),out _), "Infinite cylinder beyond endcap does not count");
            }
        }
        var ordered = new ObstacleSet();
        ordered.Add(new("far",new(20,0,0),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,2)));
        ordered.Add(new("near",new(10,0,0),Quaternion.Identity,ObstaclePart.Sphere(Vec3d.Zero,2)));
        Check(ordered.SegmentBlocked(Vec3d.Zero,new(40,0,0),out var first) && first.Obstacle.Name=="near", "Nearest hit wins despite insertion order");
        var cap = new ObstacleSet(); cap.Add(new("degenerate",Vec3d.Zero,Quaternion.Identity,ObstaclePart.Capsule(Vec3d.Zero,Vec3d.Zero,2)));
        Check(cap.SegmentBlocked(new(-5,0,0),new(5,0,0),out first) && Math.Abs(first.Fraction-.3)<1e-12, "Degenerate capsule behaves as sphere");
        var ring = new ObstacleSet(); var parts = Enumerable.Range(0,16).Select(i => ObstaclePart.Capsule(
            new(Math.Cos(i*Math.Tau/16)*100, Math.Sin(i*Math.Tau/16)*100,0),
            new(Math.Cos((i+1)*Math.Tau/16)*100,Math.Sin((i+1)*Math.Tau/16)*100,0),5)).ToArray();
        ring.Add(new("ring",Vec3d.Zero,Quaternion.Identity,parts));
        Check(!ring.SegmentBlocked(new(0,0,-200),new(0,0,200),out _), "Compound ring hole remains open");
        Check(ring.SegmentBlocked(new(100,0,-200),new(100,0,200),out _), "Ring rim blocks segment");
        foreach(var mode in Enum.GetValues<BattleObjective>())
        {
            var world=new SimWorld(); BattleSetup.Spawn(world,new BattleConfig { Objective=mode, Seed=1 });
            Check(world.Obstacles.Count==(mode==BattleObjective.Domination?3:0), "Only domination populates fixed structures");
            for(int i=0;i<world.Obstacles.Count;i++)
            {
                var obstacle=world.Obstacles[i]; var zone=world.Rules!.CaptureZones[i];
                Check(obstacle.Name==zone.Name && obstacle.Position==zone.Position,"Structure shares zone position and identity");
                Check(obstacle.BoundingRadius>1000 && obstacle.BoundingRadius<2200,"Structure conservative radius matches kilometre-scale visual");
            }
        }
        foreach(double offset in new[]{0.0,1e12})
        {
            var station=ZoneStructureShapes.For("B",new(offset,offset,offset)); var set=new ObstacleSet(); set.Add(station);
            Check(!set.SegmentBlocked(station.ToWorld(new(700,-3000,700)),station.ToWorld(new(700,3000,700)),out _),"Station opening between hub/spokes/rim is traversable");
            Check(set.SegmentBlocked(station.ToWorld(new(0,-3000,0)),station.ToWorld(new(0,3000,0)),out _),"Station central hub blocks passage");
            Check(set.SegmentBlocked(station.ToWorld(new(1490,-3000,0)),station.ToWorld(new(1490,3000,0)),out _),"Station habitat blocks passage");
            var mine=ZoneStructureShapes.For("A",new(offset,offset,offset)); set.Clear(); set.Add(mine);
            Check(set.ClosestApproach(mine.ToWorld(new(1810,650,360)),0,out _),"Loading dock at far end is solid");
            var depot=ZoneStructureShapes.For("C",new(offset,offset,offset)); set.Clear(); set.Add(depot);
            Check(set.ClosestApproach(depot.ToWorld(new(Math.Sqrt(.5)*250,Math.Sqrt(.5)*250,-1100)),0,out _),"Rotated fuel tank has a solid centre");
        }
        Console.WriteLine($"PASS: {checks} obstacle geometry checks");
    }
}
