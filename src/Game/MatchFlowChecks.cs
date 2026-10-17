using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class Main
{
    private int _matchCheckFrame, _matchChecks;
    private int _previousMatchSeed;
    private void MatchVerify(bool ok, string message)
    {
        if (!ok) { GetTree().Quit(1); throw new InvalidOperationException(message); }
        _matchChecks++;
    }
    private static void MatchTicks(ScaleTest game, int ticks)
    { for (int i=0;i<ticks;i++) game._PhysicsProcess(SimWorld.TickDelta); }
    private void CheckMatchFlow()
    {
        int frame=++_matchCheckFrame;
        if(frame==5)
        {
            _overlay.ActivateButton(0);
            MatchVerify(Screen==BattleScreen.Hangar,"Port opens fitting first");
            ChooseHull("mars_battleship");BeginMatch();
            MatchVerify(Screen==BattleScreen.Matching && PendingMatch?.Player.HullId=="mars_battleship" && Battle is null,
                "Selected hull queues without starting simulation early");
            _overlay.ActivateButton(0);
            MatchVerify(Screen==BattleScreen.Hangar && PendingMatch is null && Battle is null,"Queue can be cancelled back to fitting");
            ChooseHull("mars_interceptor");BeginMatch();
            MatchVerify(PendingMatch?.Participants.Count==20 && PendingMatch.Player.HullId=="mars_interceptor","Twenty participants include chosen Phobos");
        }
        if(frame==110)
        {
            MatchVerify(Screen==BattleScreen.Battle && Battle is not null,"Matching enters battle");
            var game=Battle!;var world=game.World;var owned=game.OwnedSquadron!;
            MatchVerify(world.Squadrons.Count==20 && world.Ships.Count==game.LaunchConfig!.Match!.Participants.Sum(p=>p.ShipCount),"10 vs 10 existing squad templates within tier bracket");
            MatchVerify(owned.Members.Count==5 && owned.Members.Contains(game.Controlled!.Body) && owned.PlayerLed,"Player owns exactly their wing");
            MatchVerify(world.Brains.Count==world.Ships.Count-1 && world.BrainOf(game.Controlled!.Body) is null,"Other ships retain AI");
            MatchVerify(InputMap.ActionHasEvent(InputSetup.TeamRoster,new InputEventKey{PhysicalKeycode=Key.Tab}) &&
                !InputMap.ActionHasEvent(InputSetup.SwitchShip,new InputEventKey{PhysicalKeycode=Key.Tab}),"Tab is roster, not ship switching");
            var controlled=game.Controlled!.Body;
            Input.MouseMode=Input.MouseModeEnum.Captured;
            game._Input(new InputEventKey{PhysicalKeycode=Key.Tab,Pressed=true});
            MatchVerify(game.RosterOpen && Input.MouseMode==Input.MouseModeEnum.Visible,"Tab opens roster before GUI focus traversal");
            MatchVerify(TeamRoster.Rows(game.LaunchConfig!.Match,game,Faction.Blue).Length==10 &&
                TeamRoster.Rows(game.LaunchConfig.Match,game,Faction.Red).Length==10,"Roster has both ten-participant teams");
            int ammo=controlled.Railgun!.Rounds;float throttle=game.Throttle;long tick=world.Tick;
            Input.ActionPress(InputSetup.Fire);Input.ActionPress(InputSetup.ThrottleUp);Input.ActionPress(InputSetup.RollRight);
            MatchTicks(game,5);
            MatchVerify(world.Tick>tick && controlled.Railgun.Rounds==ammo && game.Throttle==throttle && controlled.Control.Roll==0,
                "Roster permits simulation but blocks held fire and flight controls");
            game._Input(new InputEventKey{PhysicalKeycode=Key.Tab,Pressed=false});MatchTicks(game,1);
            MatchVerify(!game.RosterOpen && Input.MouseMode==Input.MouseModeEnum.Captured && controlled.Railgun.Rounds==ammo,
                "Roster restores capture without firing a held click");
            Input.ActionRelease(InputSetup.Fire);Input.ActionRelease(InputSetup.ThrottleUp);Input.ActionRelease(InputSetup.RollRight);
            game._UnhandledInput(new InputEventKey{PhysicalKeycode=Key.M,Pressed=true});
            game._UnhandledInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=new(400,350)});
            float mapYaw=game.WorldMap.Yaw;
            game._Input(new InputEventKey{PhysicalKeycode=Key.Tab,Pressed=true});
            game._UnhandledInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=new(400,350)});
            game._Input(new InputEventKey{PhysicalKeycode=Key.Tab,Pressed=false});
            game._UnhandledInput(new InputEventMouseMotion{Position=new(460,350),Relative=new(60,0)});
            MatchVerify(game.WorldMap.Yaw==mapYaw,"Roster cancels map drag so releasing behind it cannot leave stuck rotation");
            MatchVerify(game.WorldMapOpen && Input.MouseMode==Input.MouseModeEnum.Visible,"Roster preserves map state and cursor");
            game._UnhandledInput(new InputEventKey{PhysicalKeycode=Key.M,Pressed=true});
            controlled.Damage.Breakup(world.Time,"match check");MatchTicks(game,190);
            MatchVerify(game.Controlled.Body!=controlled && owned.Members.Contains(game.Controlled.Body) && !game.Spectating,
                "Loss hands control to surviving member of own squad");
            // Recoverable incapacitation must not transfer ownership to another participant.
            foreach(var ship in owned.Members.Where(s=>!s.Damage.Destroyed))
            {
                ship.DamageControl.Priority=RepairPriority.Hold;
                foreach(var module in ship.Damage.Modules.Where(m=>m.Definition.Kind is ModuleKind.Reactor or ModuleKind.Generator))
                    ship.Damage.Hurt(module,module.Health,world.Time,0,1);
            }
            MatchTicks(game,200);
            MatchVerify(Screen==BattleScreen.Eliminated && game.Spectating && !game.SquadronEliminated,"Disabled wing can wait for recovery");
            var repaired=owned.Members.First(s=>!s.Damage.Destroyed);repaired.Damage.Reset();MatchTicks(game,2);
            MatchVerify(Screen==BattleScreen.Battle && !game.Spectating && game.Controlled.Body==repaired && owned.PlayerLed,
                "Recovered squad regains control and player orders");
            foreach(var ship in owned.Members)ship.Damage.Breakup(world.Time,"match check");MatchTicks(game,200);
            MatchVerify(Screen==BattleScreen.Eliminated && game.SquadronEliminated && game.Spectating && !game.Paused,"Own wing eliminated offers ongoing spectator battle");
            MatchVerify(world.Ships.Any(s=>s.Faction==Faction.Blue && Squadron.Active(s)) && world.Rules!.Outcome is null,
                "Own squad loss does not end team battle");
            MatchVerify(TeamRoster.Rows(game.LaunchConfig.Match,game,Faction.Blue).Single(r=>r.Mine).Surviving==0,"Roster reports eliminated player squad");
            _overlay.ActivateButton(0);var watched=game.SpectatedView!.Body;
            game._UnhandledInput(new InputEventKey{PhysicalKeycode=Key.Right,Pressed=true});
            MatchVerify(Screen==BattleScreen.Battle && game.SpectatedView!.Body!=watched && game.SpectatedView.Body.Faction==Faction.Blue,
                "Spectator can cycle allies");
            CheckSpectatorAudio(game);
            tick=world.Tick;MatchTicks(game,4);
            MatchVerify(world.Tick>tick && owned.Members.Contains(game.Controlled.Body),"Spectating does not take over another participant");
            game._UnhandledInput(new InputEventAction{Action=InputSetup.ReleaseMouse,Pressed=true});
            MatchVerify(Screen==BattleScreen.Pause && game.Paused,"Spectator can pause");_overlay.ActivateButton(0);
            game._Input(new InputEventKey{PhysicalKeycode=Key.Tab,Pressed=true});MatchVerify(game.RosterOpen,"Spectator has team roster");
            game._Input(new InputEventKey{PhysicalKeycode=Key.Tab,Pressed=false});
            _previousMatchSeed=Seed;_overlay.ActivateButton(2);
            MatchVerify(Screen==BattleScreen.Title && Battle is null && PlayerHullId=="mars_interceptor","Leave returns to port retaining selected hull");
            _overlay.ActivateButton(0);BeginMatch();
        }
        if(frame==220)
        {
            MatchVerify(Screen==BattleScreen.Battle && Battle is {Spectating:false} && Seed!=_previousMatchSeed,"Requeue creates a fresh random battle");
            MatchVerify(Battle!.OwnedSquadron!.Members.All(s=>!s.Damage.Destroyed),"Squad losses do not leak into the next battle");
            foreach(var ship in Battle.World.Ships.Where(s=>s.Faction==Faction.Red))ship.Damage.Breakup(Battle.World.Time,"match check");
        }
        if(frame==370)
        {
            MatchVerify(Screen==BattleScreen.Result && Outcome?.Winner==Faction.Blue,"Match completes and opens result");
            _overlay.ActivateButton(2);MatchVerify(Screen==BattleScreen.Title && Battle is null,"Result returns to port");
            GD.Print($"PASS: {_matchChecks} matchmaking/ownership/roster/spectator flow checks");GetTree().Quit();
        }
    }
    private void CheckSpectatorAudio(ScaleTest game)
    {
        var audio=game.Audio;var world=game.World;var watched=game.SpectatedView!.Body;
        var owner=game.Controlled!.Body;int records=game.ControlSegments.Count;
        MatchTicks(game,2);audio.AdvanceLoops(.5f);
        MatchVerify(audio.FollowedShip==watched&&audio.EnginePlaying,"Spectator hears the followed ship's live engine");
        int rails=audio.PlayCount(CombatSound.RailFire);
        world.Log!.Ship(watched).Rails++;audio.Observe(world,game.AudioShip);
        MatchVerify(audio.PlayCount(CombatSound.RailFire)==rails+1,"Followed ship firing feeds observer sound");
        int hits=audio.PlayCount(CombatSound.Penetration);
        var module=watched.Damage.Modules.First(m=>m.Health>10);
        watched.Damage.Hurt(module,1,world.Time,0,0);audio.Observe(world,game.AudioShip);
        MatchVerify(audio.PlayCount(CombatSound.Penetration)==hits+1,"Followed ship damage feeds observer impact sound");
        var neighbor=world.Ships.First(s=>s!=watched&&s.Faction==Faction.Blue&&Squadron.Active(s));
        neighbor.Place(watched.Position+new Vec3d(100,0,0),Quaternion.Identity);
        audio.SyncListener(watched.Position,Basis.Identity);audio.AdvanceLoops(.5f);
        // A busy fleet has four occupied voices; their release fades must finish
        // before the newly nearby engine can take a slot. Keep simulation frozen.
        bool immediate=audio.Fleet.HasEngine(neighbor);int fadeSteps=0;
        while(!audio.Fleet.HasEngine(neighbor)&&fadeSteps<20){audio.AdvanceLoops(.1f);fadeSteps++;}
        if(!immediate)GD.Print($"Spectator audio voice handoff: {fadeSteps} fade steps, nearby={audio.Fleet.HasEngine(neighbor)}, "
            +$"watchedDuplicate={audio.Fleet.HasEngine(watched)}, generation={neighbor.Damage.GenerationFraction}, propulsion={neighbor.Damage.PropulsionFraction}");
        MatchVerify(audio.Fleet.HasEngine(neighbor)&&!audio.Fleet.HasEngine(watched),"Nearby engines remain spatial after voice handoff without doubling the watched engine");
        game.CycleSpectator(1);var next=game.SpectatedView!.Body;
        MatchVerify(audio.FollowedShip==next&&!audio.EnginePlaying,"Switching observers clears the old loop immediately");
        audio.Observe(world,game.AudioShip);audio.AdvanceLoops(.5f);
        MatchVerify(audio.EnginePlaying&&audio.PlayCount(CombatSound.RailFire)==rails+1,"Observer handoff starts new propulsion without replaying earlier shots");
        game.Paused=true;int pausedRails=audio.PlayCount(CombatSound.RailFire);
        world.Log.Ship(next).Rails++;audio.Observe(world,game.AudioShip);
        MatchVerify(audio.GetNode<AudioStreamPlayer>("engine_drive_loop").StreamPaused,"Pause also pauses spectator engine sound");
        game.Paused=false;audio.Observe(world,game.AudioShip);
        MatchVerify(audio.PlayCount(CombatSound.RailFire)==pausedRails,"Resume does not replay paused observer events");
        next.Damage.Breakup(world.Time,"spectator audio check");MatchTicks(game,2);audio.AdvanceLoops(.5f);
        MatchVerify(audio.FollowedShip==game.SpectatedView!.Body&&audio.FollowedShip!=next&&audio.EnginePlaying,"Destroyed observer target automatically hands sound to the next surviving ship");
        MatchVerify(game.Controlled!.Body==owner&&game.ControlSegments.Count==records&&world.BrainOf(audio.FollowedShip!) is not null,
            "Audio perspective never transfers ownership, control records or AI");
    }
}
