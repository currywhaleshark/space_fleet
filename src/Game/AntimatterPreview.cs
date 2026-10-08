using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class ScaleTest
{
    private string? _amPreview;
    private bool _amPreviewAction;
    internal void SetupAntimatterPractice(string? preview=null)
    {
        if(Controlled?.Body is not { } ic || ic.Definition.Antimatter is null) return;
        var target=Views.First(v=>v.Body.Faction!=ic.Faction && v.Body.Class.Kind==HullKind.Battleship);
        SuspendBrain(target.Body);
        target.Body.Place(new Vec3d(0,10_000_000,0),Quaternion.Identity);
        var engine=target.Body.Damage.Modules.First(m=>m.Definition.Kind==ModuleKind.Thruster);
        ic.Place(target.Body.Position+Vec3d.From(engine.Definition.Center)+new Vec3d(0,0,4000),Quaternion.Identity);
        ic.Control=target.Body.Control=new ShipControl { FlightAssist=false };
        Throttle=0; InspectTarget=target; AimPart=AimSubsystem.Engines;
        World.Sensors.Update(World.Ships,World.Time,force:true);
        Camera.ResetAim(ic.Orientation); _amPreview=preview;
        if(preview is not null) SelectWeapon(PlayerWeapon.Antimatter);
    }

    private void StepAntimatterPreview()
    {
        if(_amPreview is null || _amPreviewAction || Controlled?.Body is not { } ship) return;
        var am=ship.Ordnance.Antimatter;
        if(_amPreview=="arming") return;
        if(_amPreview=="risk" && World.Time>=1)
        { ship.Damage.Hurt(ship.Damage.Module(am.Definition!.ModuleId),8,World.Time,10,1); _amPreviewAction=true; }
        if(!am.Ready) return;
        if(_amPreview=="launch") FireSelectedWeapon();
        else if(_amPreview=="jettison") am.Jettison();
        else if(_amPreview=="failed") ship.Damage.Hurt(ship.Damage.Module(am.Definition!.ModuleId),16,World.Time,20,1);
        _amPreviewAction=true;
    }
}
