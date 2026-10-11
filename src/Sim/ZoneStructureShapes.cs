using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Sim;

/// <summary>Neutral fixed geometry shared by simulation and debug rendering. Solar/radiator sheets are excluded.</summary>
public static class ZoneStructureShapes
{
    public static Obstacle For(string name, Vec3d position)
    {
        var parts = new List<ObstaclePart>(); Quaternion pose = Quaternion.Identity;
        void Sphere(double x, double y, double z, double r) => parts.Add(ObstaclePart.Sphere(new(x,y,z),r));
        void Capsule(Vec3d a, Vec3d b, double r) => parts.Add(ObstaclePart.Capsule(a,b,r));
        if (name == "A")
        {
            var rockPose = new DoubleRotation(Quaternion.FromEuler(new Vector3(8,25,-6) * (Mathf.Pi/180)));
            foreach (double x in new[] { -380.0,380 }) foreach (double z in new[] { -230.0,230 })
                parts.Add(ObstaclePart.Sphere(rockPose.Apply(new(x,0,z)),650));
            Sphere(0,0,0,690); Sphere(-1050,-380,420,285); Sphere(880,-520,-640,205);
            Capsule(new(-270,530,0),new(110,530,0),215);
            Capsule(new(270,500,120),new(370,500,120),160);
            for (int i=0;i<3;i++) Capsule(new(-300+i*170,570,-260),new(-300+i*170,850,-260),72);
            Capsule(new(260,430,-180),new(260,1120,-180),65);
            Capsule(new(560,510,120),new(1700,650,360),52);
            Capsule(new(1770,650,360),new(1850,650,360),135);
            Capsule(new(1260,570,360),new(1620,570,360),65);
        }
        else if (name == "B")
        {
            pose = Quaternion.FromEuler(new Vector3(106,0,12) * (Mathf.Pi/180));
            Capsule(new(0,-550,0),new(0,550,0),240);
            // 32 chords, sagitta 7.18 m; radius 118 encloses the 110 m round tube.
            const int count=32;
            for(int i=0;i<count;i++) Capsule(Ring(i*Math.Tau/count),Ring((i+1)*Math.Tau/count),118);
            for(int i=0;i<4;i++)
            {
                Vec3d direction=new(Math.Cos(i*Math.Tau/4),0,Math.Sin(i*Math.Tau/4));
                Capsule(direction*230,direction*1400,43);
                Capsule(direction*1440,direction*1540,190);
            }
        }
        else if (name == "C")
        {
            pose=Quaternion.FromEuler(new Vector3(0,70,10)*(Mathf.Pi/180));
            Capsule(new(0,0,-1500),new(0,0,1500),110);
            for(int row=0;row<5;row++) for(int k=0;k<4;k++)
            {
                double angle=k*Math.Tau/4+Math.PI/4;
                Sphere(Math.Cos(angle)*250,Math.Sin(angle)*250,-1100+row*520,150);
            }
            Capsule(new(0,0,1600),new(0,0,1720),150);
            Capsule(new(0,120,1700),new(0,620,1760),12);
            Capsule(new(-110,640,1760),new(110,640,1760),55);
        }
        else throw new ArgumentException("Unknown capture structure: "+name);
        return new Obstacle(name,position,pose,parts.ToArray());
    }
    private static Vec3d Ring(double angle) => new(Math.Cos(angle)*1490,0,Math.Sin(angle)*1490);

    internal static void Populate(SimWorld world)
    {
        if (world.Rules?.Objective != BattleObjective.Domination) return;
        foreach(var zone in world.Rules.CaptureZones) world.Obstacles.Add(For(zone.Name,zone.Position));
    }
}
