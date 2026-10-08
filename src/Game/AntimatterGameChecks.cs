using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

/// <summary>Actual scene/input/model/audio integration, separate from standalone physics tests.</summary>
public partial class AntimatterGameChecks : Node
{
    private int _checks;
    private void Check(bool condition,string why) { if(!condition) throw new InvalidOperationException(why); _checks++; }
    public override void _Ready()
    {
        try { Run(); GD.Print($"PASS: {_checks} AM game/input/model checks"); GetTree().Quit(); }
        catch(Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
    private void Run()
    {
        var game=new ScaleTest { LaunchControl="IC-21" }; AddChild(game);
        game.SetupAntimatterPractice();
        var ship=game.Controlled!.Body; var am=ship.Ordnance.Antimatter;
        var target=game.InspectTarget!.Body;
        var audio=game.GetNode<CombatAudio>("CombatAudio");
        Check(am.Rounds==2 && ship.Ordnance.Missiles==4,"Separate scene payload inventories");
        var select=new InputEventKey { PhysicalKeycode=Key.Key3,Pressed=true };
        var missile=new InputEventKey { PhysicalKeycode=Key.Key2,Pressed=true };
        var launch=new InputEventMouseButton { ButtonIndex=MouseButton.Left,Pressed=true };
        Input.MouseMode=Input.MouseModeEnum.Captured;
        game._UnhandledInput(select);
        Check(game.AntimatterSelected && am.Mode==AntimatterMode.Arming,"Key 3 selects and prepares AM");
        Check(game.AntimatterSolution is { Valid:true, FlightTime: > 2 },"Selected AM displays its slower accelerating lead solution");
        game._UnhandledInput(launch);
        Check(am.Rounds==2 && game.World.Missiles.Count==0,"Left click during preparation cannot fire early");
        for(int i=0;i<155;i++) game.World.Step();
        Check(am.Ready,"Scene payload reaches armed state");
        audio.Observe(game.World,ship);
        Check(audio.PlayCount(CombatSound.HitConfirm)==1,"Armed cue uses existing sound bus");
        game.Controlled.Sync(ship.Position,1,.016f);
        var glow=(MeshInstance3D)game.Controlled.FindChild("AntimatterContainmentGlow",true,false);
        Check(glow.Visible,"Armed model containment glows");
        game._UnhandledInput(launch);
        Check(am.Rounds==1 && ship.Ordnance.Missiles==4 && game.World.Missiles.Any(m=>m.Weapon==BattleWeapon.Antimatter),
            $"Left button launches selected AM only: {game.LastFireMessage}, mode={am.Mode}, selected={game.AntimatterSelected}, cursor={Input.MouseMode}");
        audio.Observe(game.World,ship);
        Check(audio.PlayCount(CombatSound.RailFire)==1,"AM launch triggers structural launch sound once");
        Check(PlayerRecord.Sample(ship,game.World.Log!).Torpedoes==1,"Player result counts AM separately from missiles");
        game._UnhandledInput(missile);
        Check(!game.AntimatterSelected && am.Mode==AntimatterMode.Safe,"Key 2 returns to normal missile / SAFE");
        game._UnhandledInput(launch);
        Check(ship.Ordnance.Missiles==3 && am.Rounds==1,"Normal missile input still works");
        game._UnhandledInput(select); game._UnhandledInput(missile);
        Check(am.Mode==AntimatterMode.Safe && am.Rounds==1,"Prepare/cancel keeps remaining AM");
        game._UnhandledInput(new InputEventKey { PhysicalKeycode=Key.M,Pressed=true });
        game._UnhandledInput(select); game._UnhandledInput(launch);
        Check(game.WorldMapOpen && !game.AntimatterSelected && am.Rounds==1,"Map blocks AM selection and launch");
        game._UnhandledInput(new InputEventKey { PhysicalKeycode=Key.M,Pressed=true });
        // Guarded continuous hold is tested via the same combat update used by the live scene.
        game._Process(.016); // release map input guard, just as the next rendered frame does
        game.SetProcess(false); game.SetPhysicsProcess(false);
        Input.ActionPress(InputSetup.JettisonAntimatter);
        for(int i=0;i<30;i++) game.StepJettison();
        Check(am.Rounds==1,"Half-second key press must not discard payload");
        game.Paused=true;
        for(int i=0;i<62;i++) game.StepJettison();
        Check(game.JettisonProgress==0 && am.Rounds==1,"Pause cancels the hold and blocks disposal");
        game.Paused=false;
        for(int i=0;i<30;i++) game.StepJettison();
        Input.ActionRelease(InputSetup.JettisonAntimatter); game.StepJettison();
        Check(game.JettisonProgress==0,"Releasing jettison resets progress");
        Input.ActionPress(InputSetup.JettisonAntimatter);
        for(int i=0;i<62;i++) game.StepJettison();
        Input.ActionRelease(InputSetup.JettisonAntimatter);
        Check(am.Rounds==0 && am.Jettisoned,"Continuous one-second hold discards all remaining AM");
        audio.Observe(game.World,ship);
        Check(audio.PlayCount(CombatSound.ArmorImpact)==1,"Jettison has a local release cue");
        game.Controlled.Sync(ship.Position,1,.016f);
        Check(!glow.Visible,"Empty containment glow stops");
        ship.Damage.Reset(); game.World.ResetWeapons();
        Check(am.Rounds==0,"Scene repair cannot replenish AM");
        game.Free();
    }
}
