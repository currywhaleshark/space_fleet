using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SpaceFleet.Sim;

public enum Battlefield { DeepSpace, AsteroidCluster }
public sealed record AsteroidRock(Obstacle Body,int Variant,double Radius);

/// <summary>Seeded static terrain. Its random stream is independent of combat and placement jitter.</summary>
public sealed class AsteroidField
{
    public static readonly Vec3d Axes=new(70000,25000,35000);
    public const int VariantCount=12;
    public const double MinimumGap=300;
    public Vec3d Center { get; }
    public IReadOnlyList<AsteroidRock> Rocks { get; }
    public int Seed { get; }
    public int Regenerations { get; }
    private AsteroidField(Vec3d center,int seed,int regeneration,List<AsteroidRock> rocks)
    { Center=center;Seed=seed;Regenerations=regeneration;Rocks=rocks.AsReadOnly(); }
    public static string Name(Battlefield field)=>field==Battlefield.AsteroidCluster?"소행성 군집":"심우주";

    // Six paired templates. The odd template reflects every part, not just its
    // orientation: a quaternion alone cannot represent central inversion.
    private static readonly IReadOnlyList<ObstaclePart>[] Templates=Enumerable.Range(0,VariantCount).Select(v=>(IReadOnlyList<ObstaclePart>)Array.AsReadOnly(BuildTemplate(v))).ToArray();
    public static IReadOnlyList<ObstaclePart> Template(int variant)=>Templates[variant];
    private static ObstaclePart[] BuildTemplate(int variant)
    {
        if((uint)variant>=VariantCount)throw new ArgumentOutOfRangeException(nameof(variant));
        var random=new FieldRandom((uint)(variant/2+7183));
        var parts=new List<ObstaclePart>{ObstaclePart.Sphere(Vec3d.Zero,.78)};
        for(int i=0;i<1+variant/2%3;i++)
        {
            Vec3d axis=Direction(ref random)*(.26+random.Next()*.18);
            parts.Add(ObstaclePart.Sphere(axis,.58+random.Next()*.11));
        }
        double extent=parts.Max(p=>p.A.Length()+p.Radius),sign=variant%2==0?1:-1;
        return parts.Select(p=>ObstaclePart.Sphere(p.A*(sign/extent),p.Radius/extent)).ToArray();
    }
    // Every constituent contains the origin, so the union is star-shaped.
    public static double SurfaceRadius(IReadOnlyList<ObstaclePart> parts,Vec3d direction)
    {
        double radius=0;
        foreach(var part in parts)
        {
            double dot=direction.Dot(part.A),q=part.Radius*part.Radius-part.A.LengthSquared()+dot*dot;
            if(q>=0)radius=Math.Max(radius,dot+Math.Sqrt(q));
        }
        return radius;
    }
    public static Vec3d SurfaceVertex(int variant,Vec3d direction)
    {
        Vec3d n=variant%2==0?direction:-direction;
        // Small inward facets retain the collision envelope; mirrored noise is exact.
        double noise=.006+.004*Math.Sin(n.X*31+n.Y*17+n.Z*23+variant/2);
        return direction*(SurfaceRadius(Template(variant),direction)-noise);
    }
    public static AsteroidField Generate(BattleConfig config,int regeneration=0)
    {
        Vec3d back=Vec3d.From(config.RedOffset)+new Vec3d(0,0,-config.StartDistance),center=back*.5;
        var random=new FieldRandom(unchecked((uint)config.FieldSeed+(uint)regeneration*0x9e3779b9u));
        var rocks=new List<AsteroidRock>();
        // Keep the same terrain across objectives; reserve the canonical capture
        // sites even when the selected mode does not instantiate the structures.
        var zones=BattleRules.DefaultZones(config.StartDistance,Vec3d.From(config.RedOffset));
        int[] counts={6+random.Int(5),30+random.Int(21),60+random.Int(41)};
        double[] lo={2000,600,150},hi={5000,2000,600};
        for(int size=0;size<3;size++)for(int pair=0;pair<counts[size];pair++)
        {
            bool placed=false;
            for(int attempt=0;attempt<20000;attempt++)
            {
                double radius=lo[size]+random.Next()*(hi[size]-lo[size]);
                Vec3d relative=new((random.Next()*2-1)*Axes.X,(random.Next()*2-1)*Axes.Y,(random.Next()*2-1)*Axes.Z);
                // Conservatively keep the entire bounding sphere inside the ellipsoid.
                double ax=Axes.X-radius,ay=Axes.Y-radius,az=Axes.Z-radius;
                if(relative.X*relative.X/(ax*ax)+relative.Y*relative.Y/(ay*ay)+relative.Z*relative.Z/(az*az)>1)continue;
                Vec3d a=center+relative,b=center-relative;
                bool Clear(Vec3d point)
                {
                    if(point.Length()<25000+radius || (point-back).Length()<25000+radius)return false;
                    if(zones is not null)foreach(var zone in zones)if((point-zone.Position).Length()<7000+radius)return false;
                    // Reserve the exact centre as a navigation destination.
                    if((point-center).Length()<2500+radius)return false;
                    foreach(var rock in rocks)if((point-rock.Body.Position).Length()<radius+rock.Radius+MinimumGap)return false;
                    return true;
                }
                if((a-b).Length()<2*radius+MinimumGap || !Clear(a) || !Clear(b))continue;
                int variant=random.Int(VariantCount/2)*2;
                var axis=Direction(ref random).ToVector3(); var pose=new Quaternion(axis,(float)(random.Next()*Math.Tau));
                void Add(Vec3d p,int v)
                {
                    var parts=Template(v).Select(s=>ObstaclePart.Sphere(s.A*radius,s.Radius*radius)).ToArray();
                    rocks.Add(new(new Obstacle($"Rock-{rocks.Count+1:000}",p,pose,parts),v,radius));
                }
                Add(a,variant);Add(b,variant+1);placed=true;break;
            }
            if(!placed)throw new InvalidOperationException($"Cannot place asteroid seed {config.FieldSeed}, size {size}, pair {pair}");
        }
        return new(center,config.FieldSeed,regeneration,rocks);
    }
    private static Vec3d Direction(ref FieldRandom random)
    {
        double z=random.Next()*2-1,a=random.Next()*Math.Tau,r=Math.Sqrt(1-z*z);
        return new(r*Math.Cos(a),z,r*Math.Sin(a));
    }
    private struct FieldRandom(uint seed)
    {
        private uint _state=seed;
        public double Next() { uint z=unchecked(_state+=0x9e3779b9u);z=unchecked((z^(z>>16))*0x21f0aaadu);z=unchecked((z^(z>>15))*0x735a2d97u);return (z^(z>>15))/4294967296.0; }
        public int Int(int count)=>(int)(Next()*count);
    }
}

public sealed partial class SimWorld
{
    public Battlefield Battlefield { get; internal set; }
    public int FieldSeed { get; internal set; }=1;
    public AsteroidField? Asteroids { get; internal set; }
    internal void PopulateBattlefield(BattleConfig config)
    {
        Battlefield=config.Battlefield;FieldSeed=config.FieldSeed;
        ZoneStructureShapes.Populate(this);
        if(Battlefield!=Battlefield.AsteroidCluster)return;
        Asteroids=AsteroidField.Generate(config);
        foreach(var rock in Asteroids.Rocks)Obstacles.Add(rock.Body);
        Obstacles.BuildIndex();
    }
}
