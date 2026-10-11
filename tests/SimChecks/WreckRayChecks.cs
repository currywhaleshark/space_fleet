using Godot;
using SpaceFleet.Sim;

static class WreckRayChecks
{
    // Frozen pre-optimization first-hit formula, independent of group rejection.
    private static bool Original(ShipBody ship,Vec3d origin,Vector3 direction,float range,Vec3d position,Quaternion orientation,out float distance)
    {
        Vector3 o=orientation.Inverse()*(origin-position).ToVector3(),d=orientation.Inverse()*direction;
        distance=float.PositiveInfinity;
        foreach(var box in ship.Hull.Boxes)
        {
            float near=float.NegativeInfinity,far=float.PositiveInfinity;var relative=o-box.Center;bool miss=false;
            for(int axis=0;axis<3;axis++)
            {
                float p=relative[axis],v=d[axis],h=box.HalfSize[axis];
                if(Mathf.Abs(v)<1e-7f){if(Mathf.Abs(p)>h){miss=true;break;}continue;}
                float a=(-h-p)/v,b=(h-p)/v;if(a>b)(a,b)=(b,a);
                if(a>near)near=a;if(b<far)far=b;if(near>far){miss=true;break;}
            }
            if(!miss && far>=0 && near<=range)distance=Mathf.Min(distance,Mathf.Max(0,near));
        }
        return float.IsFinite(distance);
    }
    public static void Run()
    {
        int checks=0;var random=new Random(33492);
        Vec3d Noise(double scale)=>new((random.NextDouble()*2-1)*scale,(random.NextDouble()*2-1)*scale,(random.NextDouble()*2-1)*scale);
        foreach(var def in ShipDefinitions.All)
        foreach(double offset in new[]{0.0,1e12,-1e12})
        foreach(double age in new[]{0.0,30,900,10000})
        {
            var ship=new ShipBody("WRECK-RAY",def,Faction.Blue);ship.Damage.Catastrophe(0,"test");ship.Step(age);
            Vec3d position=new(offset,-offset,offset);var pose=Quaternion.FromEuler(Noise(2).ToVector3());ship.Place(position,pose);
            for(int n=0;n<200;n++)
            {
                var box=ship.Hull.Boxes[n%ship.Hull.Boxes.Count];
                Vec3d target=position+Vec3d.From(pose*box.Center),origin=target+Noise(2000);
                Vector3 direction=(n%2==0?target-origin:Noise(2000)).ToVector3().Normalized();
                if(n%11==0){origin=target;direction=Vector3.Up;}
                float range=n%7==0?1:400000;
                bool old=Original(ship,origin,direction,range,position,pose,out float expected);
                bool now=DamageRay.FirstHitAtPose(ship,origin,direction,range,position,pose,out float actual);
                if(old!=now || expected!=actual)throw new Exception("Wreck ray group bounds changed exact first hit");checks++;
            }
        }
        Console.WriteLine($"PASS: {checks} wreck ray group/reference checks");
    }
}
