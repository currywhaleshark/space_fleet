using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class ScaleTest
{
    private void PreviewAsteroids()
    {
        if(_shot is null || World.Asteroids is not { } field
            || !BattleArgs.Parse(OS.GetCmdlineUserArgs()).TryGetValue("asteroid-preview",out string? mode))return;
        if(_frame==70 && mode=="map")
        {WorldMap.Fit(new[]{field.Center-new Vec3d(75000,30000,40000),field.Center+new Vec3d(75000,30000,40000)});return;}
        if(_frame!=10 || Controlled is null)return;
        foreach(var ship in World.Ships)
        {
            World.DetachBrain(ship);ship.Place(new(1e8+World.Ships.ToList().IndexOf(ship)*10000,0,0),Quaternion.Identity);
            ship.Velocity=Vector3.Zero;ship.Control=new(){FlightAssist=false};
            if(ship.Gunnery is { } gun)gun.Doctrine=FireDoctrine.Hold;
        }
        var rock=field.Rocks.Where(r=>r.Radius>3000).OrderBy(r=>(r.Body.Position-field.Center).LengthSquared()).First();
        double radius=rock.Body.BoundingRadius;Vec3d center=rock.Body.Position;
        Controlled.Body.Place(center+new Vec3d(0,0,radius+(mode=="warning"?850:2200)),Quaternion.Identity);
        var target=Views.First(v=>v.Body.Callsign=="BB-X1");
        target.Body.Place(center-new Vec3d(0,0,radius+2200),Quaternion.Identity);
        var original=World.Obstacles.ToArray();World.Obstacles.Clear();
        World.Sensors.Update(World.Ships,World.Time,force:true);UpdateContacts();SelectEnemy(target);
        foreach(var obstacle in original)World.Obstacles.Add(obstacle);World.Obstacles.BuildIndex();
        if(mode is "fleet" or "map")
        {
            int i=0;foreach(var ship in World.Ships.Where(s=>s.Faction==Faction.Blue && s!=Controlled.Body).Take(4))
            {ship.Place(center+new Vec3d((++i%2==0?1:-1)*(1200+i*500),i*90,radius+1100+i*350),Quaternion.Identity);}
        }
        for(int i=0;i<720;i++)World.Step();
        UpdateContacts();Camera.ResetAim(Quaternion.Identity);Paused=true;
        if(mode=="warning")Controlled.Body.Velocity=new(0,0,-1200);
        if(mode=="map"){_worldMap.Open();_hud.Visible=false;}
        GD.Print($"Asteroid preview {mode}: rocks={field.Rocks.Count}, memory={ContactOf(target)?.Prediction is not null}, warning={Hud.PredictTerrainImpact(World.Obstacles,Controlled.Body,out _)}");
    }
}
