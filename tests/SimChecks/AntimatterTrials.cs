using System.Globalization;
using Godot;
using SpaceFleet.Sim;

static class AntimatterTrials
{
    public static void Moving()
    {
        foreach(float lateral in new[]{0,100,200,300})
        foreach(float targetSpeed in new[]{0,100})
        {
            var(w,ic,bb)=AntimatterChecks.Fixture(4500);
            ic.Ordnance.Antimatter.BeginArming(); AntimatterChecks.Step(w,2.5);
            ic.Velocity=Vector3.Right*lateral; bb.Velocity=Vector3.Right*targetSpeed;
            var attempt=w.LaunchAntimatter(ic,bb);
            if(!attempt.Fired) { Console.WriteLine($"moving IC={lateral} BB={targetSpeed}: rejected {attempt.Reason}"); continue; }
            var flight=AntimatterChecks.Fly(w,bb); var log=w.Log!.AntimatterFlights.Single();
            Console.WriteLine($"moving IC={lateral} BB={targetSpeed}: hit={flight.Hit} closest={log.ClosestHull:F1} lock={log.SeekerSeconds:F2} outcome={log.Outcome}");
        }
    }
    public static void Run()
    {
        var rows=new List<string> { "rangeMeters,approach,defenses,trials,launches,hits,intercepted,expired,carrierLosses,engineKills,meanFlightSeconds" };
        foreach(bool defenses in new[]{false,true})
        foreach(string approach in new[]{"dorsal","stern_below"})
        foreach(float range in new[]{2000,3500,5000,10000})
        {
            int launched=0,hits=0,intercepted=0,expired=0,losses=0,kills=0; double flightTime=0;
            for(int seed=0;seed<8;seed++)
            {
                var(w,ic,bb)=AntimatterChecks.Fixture(range,defenses);
                var engine=bb.Damage.Modules.First(m=>m.Definition.Kind==ModuleKind.Thruster);
                Vector3 from=approach=="dorsal" ? Vector3.Up : new Vector3(0,-.5f,1).Normalized();
                ic.Place(bb.Position+Vec3d.From(engine.Definition.Center+from*range),Basis.LookingAt(-from,Vector3.Forward).GetRotationQuaternion());
                w.Sensors.Update(w.Ships,0,force:true);
                for(int i=0;i<seed;i++) w.FireTestShot(ic,ic.Position+new Vec3d(1e6,0,0),Vector3.Right,new DamagePacket(1,1,1,1));
                ic.Ordnance.Antimatter.BeginArming(); AntimatterChecks.Step(w,2.5);
                bb.Damage.AbsorbShield(bb.Damage.Shield,w.Time);
                if(w.LaunchAntimatter(ic,bb,engine.Definition.Center).Fired)
                {
                    launched++; var result=AntimatterChecks.Fly(w,bb); flightTime+=result.Time;
                    if(result.Hit) hits++; if(result.Intercepted) intercepted++; if(result.Expired) expired++;
                }
                if(ic.Damage.Destroyed) losses++; if(engine.Destroyed) kills++;
            }
            string row=string.Join(',',new[]{range.ToString(CultureInfo.InvariantCulture),approach,defenses?"PD+DRONES":"NONE","8",
                launched.ToString(),hits.ToString(),intercepted.ToString(),expired.ToString(),losses.ToString(),kills.ToString(),
                (launched==0 ? 0 : flightTime/launched).ToString("0.000",CultureInfo.InvariantCulture)});
            rows.Add(row); Console.WriteLine(row);
        }
        Directory.CreateDirectory("shots"); File.WriteAllLines("shots/am_trials.csv",rows);
    }
}
