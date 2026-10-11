using Godot;
using SpaceFleet.Sim;

static class AsteroidChecks
{
    public static void Run()
    {
        int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
        byte[] Bytes(AsteroidField field)
        {
            using var memory=new MemoryStream();using var w=new BinaryWriter(memory);
            foreach(var rock in field.Rocks)
            {
                w.Write(rock.Body.Name);w.Write(rock.Variant);w.Write(rock.Radius);
                void V(Vec3d p){w.Write(p.X);w.Write(p.Y);w.Write(p.Z);}
                V(rock.Body.Position);var q=rock.Body.Orientation;w.Write(q.X);w.Write(q.Y);w.Write(q.Z);w.Write(q.W);
                foreach(var part in rock.Body.Parts){V(part.A);w.Write(part.Radius);}
            }
            return memory.ToArray();
        }
        byte[]? previous=null;
        foreach(int seed in new[]{1,2,3,17,-1})
        {
            var config=new BattleConfig{FieldSeed=seed,Battlefield=Battlefield.AsteroidCluster};
            var field=AsteroidField.Generate(config);byte[] bytes=Bytes(field);
            Check(bytes.SequenceEqual(Bytes(AsteroidField.Generate(config))),"Byte deterministic field");
            Check(bytes.SequenceEqual(Bytes(AsteroidField.Generate(config with{Seed=183,Mirror=true,Objective=BattleObjective.Domination}))),"Terrain independent of combat seed, side swap and objective");
            if(previous is not null)Check(!bytes.SequenceEqual(previous),"Field seed changes layout");previous=bytes;
            Check(field.Rocks.Count is >=192 and <=320,"Population bounds");
            Check(field.Rocks.Count(r=>r.Radius>=2000) is >=12 and <=20,"Large rock count");
            Check(field.Rocks.Count(r=>r.Radius>=600 && r.Radius<2000) is >=60 and <=100,"Medium rock count");
            Check(field.Rocks.Count(r=>r.Radius<600) is >=120 and <=200,"Small rock count");
            for(int i=0;i<field.Rocks.Count;i++)
            {
                var rock=field.Rocks[i];var mirror=field.Rocks[i^1];Vec3d p=rock.Body.Position-field.Center;
                Check((rock.Body.Position+mirror.Body.Position-field.Center*2).Length()<1e-7,"Central position inversion");
                for(int part=0;part<rock.Body.Parts.Count;part++)
                    Check((rock.Body.ToWorld(rock.Body.Parts[part].A)+mirror.Body.ToWorld(mirror.Body.Parts[part].A)-field.Center*2).Length()<1e-6,"Every compound lobe reflects");
                Check(p.X*p.X/Math.Pow(AsteroidField.Axes.X-rock.Radius,2)+p.Y*p.Y/Math.Pow(AsteroidField.Axes.Y-rock.Radius,2)+p.Z*p.Z/Math.Pow(AsteroidField.Axes.Z-rock.Radius,2)<=1,"Full bounds inside ellipsoid");
                foreach(var start in new[]{Vec3d.Zero,field.Center*2})Check((rock.Body.Position-start).Length()>=25000+rock.Radius,"Spawn safety volume");
                foreach(var zone in BattleRules.DefaultZones(config.StartDistance,Vec3d.Zero))Check((rock.Body.Position-zone.Position).Length()>=7000+rock.Radius,"Capture safety volume");
                for(int j=0;j<i;j++)Check((rock.Body.Position-field.Rocks[j].Body.Position).Length()>=rock.Radius+field.Rocks[j].Radius+300-1e-7,"Rock separation");
            }
        }
        double maxOutside=0,maxInside=0;
        for(int variant=0;variant<AsteroidField.VariantCount;variant++)
        {
            var parts=AsteroidField.Template(variant);
            Vec3d Vertex(int y,int x){double a=x*Math.Tau/64,b=y*Math.PI/32;return AsteroidField.SurfaceVertex(variant,new(Math.Sin(b)*Math.Cos(a),Math.Cos(b),Math.Sin(b)*Math.Sin(a)));}
            void Sample(Vec3d point)
            {
                double outside=parts.Min(p=>(point-p.A).Length()-p.Radius);maxOutside=Math.Max(maxOutside,outside);
                double length=point.Length();double inside=AsteroidField.SurfaceRadius(parts,point*(1/length))-length;maxInside=Math.Max(maxInside,inside);
                Check(outside<=.03,"Rendered triangle remains within 3% collision envelope");
                Check(inside<.03,"Collision does not extend over 3% beyond triangle surface");
            }
            for(int y=0;y<32;y++)for(int x=0;x<64;x++)
            {
                Vec3d a=Vertex(y,x),b=Vertex(y,x+1),c=Vertex(y+1,x),d=Vertex(y+1,x+1);
                Sample(a);Sample((a+b+c)*(1.0/3));Sample((b+c+d)*(1.0/3));Sample((b+c)*.5);
                Check((AsteroidField.SurfaceVertex(variant^1,-a*(1/a.Length()))+a).Length()<1e-10,"Mesh reflection matches compound reflection");
            }
        }
        var deep=new SimWorld();BattleSetup.Spawn(deep,new());Check(deep.Obstacles.Count==0 && deep.Asteroids is null,"Deep-space empty fast path");
        foreach(var mode in Enum.GetValues<BattleObjective>())
        {var world=new SimWorld();BattleSetup.Spawn(world,new(){Battlefield=Battlefield.AsteroidCluster,Objective=mode});Check(world.Asteroids is not null && world.Obstacles.Count==world.Asteroids.Rocks.Count+(mode==BattleObjective.Domination?3:0),"Every objective supports field");foreach(var ship in world.Ships)Check(!world.Obstacles.ClosestApproach(ship.Position,ship.Hull.BoundingRadius,out _),"Entire initial formation is clear");}
        Console.WriteLine($"Rock visual surface error (fraction of bounding radius): outward {maxOutside:F6}, inward {maxInside:F6}");
        Console.WriteLine($"PASS: {checks} asteroid layout/shape checks");
    }
}
