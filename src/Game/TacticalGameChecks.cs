using System;
using System.IO;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class TacticalGameChecks : Node
{
    private int _checks;
    private void Check(bool valid,string reason){if(!valid)throw new InvalidOperationException(reason);_checks++;}
    public override void _Ready()
    {
        try{Bindings();InputGates();GD.Print($"PASS: {_checks} key settings / tactical input checks");GetTree().Quit();}
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
    private void Bindings()
    {
        InputSetup.Register();InputBindings.Load(true);
        Check(InputBindings.Text(InputSetup.DroneMenu)=="K"&&InputBindings.Text(InputSetup.DamageControlMenu)=="L","New radial defaults");
        var legacy=InputBindings.Current().Where(k=>k.Key!=InputSetup.HudDetails).ToDictionary(k=>k.Key,k=>(long)k.Value);
        legacy[InputSetup.DroneMenu]=(long)Key.I;
        var migrated=InputBindings.Parse(System.Text.Json.JsonSerializer.Serialize(legacy));
        Check(migrated[InputSetup.DroneMenu]==Key.I&&migrated[InputSetup.HudDetails]!=Key.I,"Adding details key preserves existing custom I binding");
        var draft=InputBindings.Current();draft[InputSetup.DroneMenu]=Key.P;
        Check(InputBindings.Validate(draft) is null,"Free key can bind");draft[InputSetup.DamageControlMenu]=Key.P;
        Check(InputBindings.Validate(draft)?.Contains("중복")==true,"Conflicts explained");draft[InputSetup.DamageControlMenu]=Key.L;
        var file=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"fleet-keys-"+Guid.NewGuid()+".json");
        InputBindings.Save(draft,file);Check(InputMap.ActionHasEvent(InputSetup.DroneMenu,new InputEventKey{PhysicalKeycode=Key.P}),"Saved key affects input");
        Check(InputBindings.Parse(File.ReadAllText(file))[InputSetup.DroneMenu]==Key.P,"Settings disk round trip");
        InputSetup.Register();Check(InputBindings.Text(InputSetup.DroneMenu)=="P","Scene registration preserves remap");
        Check(ShipHelp.Entries(new ShipBody("test",ShipDefinitions.ById("battleship"),Faction.Blue),ControlScheme.Helm,1).Any(e=>e.Keys=="P"&&e.Action.Contains("드론")),"Help reflects remapping");
        var view=new KeyBindingsView{Closed=()=>{}};AddChild(view);
        view.Listen(InputSetup.PowerMenu);view._Input(new InputEventKey{PhysicalKeycode=Key.U,Pressed=true,Echo=true});
        Check(view.Draft[InputSetup.PowerMenu]==Key.F,"Repeating key does not remap");view.Assign(Key.U);
        Check(InputBindings.Text(InputSetup.PowerMenu)=="F","Draft does not change live input");view.Reset();Check(view.Draft[InputSetup.DroneMenu]==Key.K,"Reset defaults available");
        view.Listen(InputSetup.PowerMenu);view.Assign(Key.Escape);Check(view.Draft[InputSetup.PowerMenu]==Key.F,"Escape cancels capture");
        RemoveChild(view);view.QueueFree();InputBindings.Save(InputSetup.Defaults,file);
    }
    private void InputGates()
    {
        var baseFit=FleetLoadouts.Empty("battleship");var hull=FleetLoadouts.ResolveFit(baseFit).Definition;var bay=TacticalModulesCatalog.Bays(hull)[0];
        var fit=baseFit with{Components=new(){TacticalModules=new[]{new TacticalMount(bay.Id,"focus-search")}}};
        var squad=FleetLoadouts.Default("battleship").Members.ToArray();squad[0]=fit;
        var escort=FleetLoadouts.ResolveFit(squad[1]).Definition;
        squad[1]=squad[1] with{Components=new(){TacticalModules=new[]{new TacticalMount(TacticalModulesCatalog.Bays(escort)[0].Id,"focus-search")}}};
        var match=Matchmaking.Create(42,new SquadronLoadout(squad));
        var game=new ScaleTest{BattleMode=true,LaunchConfig=new BattleConfig{Seed=42,Match=match},LaunchControl=match.Player.Callsign(0)};AddChild(game);
        game.SetProcess(false);game.SetPhysicsProcess(false);
        foreach(var ship in game.World.Ships)game.World.DetachBrain(ship);
        var controlled=game.Controlled!.Body;var skill=controlled.Tactical.ActiveSlots[0];
        void Key(Key key,bool pressed=true,bool echo=false)=>game._UnhandledInput(new InputEventKey{PhysicalKeycode=key,Pressed=pressed,Echo=echo});
        void Blocked(string message){Key(Godot.Key.Key4);Check(skill.Uses==0,message);}
        game.Paused=true;Blocked("Pause blocks skill");game.Paused=false;
        Key(Godot.Key.M);Blocked("Map blocks skill");Key(Godot.Key.M);game._Process(.016);
        Key(Godot.Key.F1);Blocked("Help blocks skill");Key(Godot.Key.F1);
        Key(Godot.Key.F);Blocked("Radial blocks skill");Key(Godot.Key.Escape);
        Key(Godot.Key.Key4,echo:true);Check(skill.Uses==0,"Echo does not activate");
        int rounds=controlled.Railgun!.Rounds;var orientation=controlled.Orientation;var weapon=game.SelectedWeapon;
        Key(Godot.Key.Key4);Check(skill.Uses==1,"Skill activates once");Key(Godot.Key.Key4);
        Check(skill.Uses==1&&controlled.Railgun.Rounds==rounds&&controlled.Orientation==orientation&&game.SelectedWeapon==weapon,"Skill neither repeats nor fires/steers/selects weapon");
        Key(Godot.Key.K);Check(game.MenuOpen,"K opens drone radial");Key(Godot.Key.K,false);
        Key(Godot.Key.L);Check(game.MenuOpen,"L opens repair radial");Key(Godot.Key.L,false);
        double originalCooldown=skill.CooldownUntil;Key(Godot.Key.F9);
        Check(game.Controlled!.Body!=controlled&&game.Controlled.Body.Tactical.ActiveSlots[0].Uses==0,"Handoff selects the new ship's modules");
        Key(Godot.Key.Key4);Check(game.Controlled.Body.Tactical.ActiveSlots[0].Uses==1&&skill.CooldownUntil==originalCooldown,"Handoff neither copies nor resets cooldowns");
        game.BeginSpectating();double until=skill.CooldownUntil;Key(Godot.Key.Key4);Check(skill.CooldownUntil==until,"Spectating preserves ship cooldown");
        RemoveChild(game);game.QueueFree();
    }
}
