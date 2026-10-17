using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class Main
{
    private int _researchFrame,_researchChecks;
    private long _pointsBeforeBattle;
    private void ResearchVerify(bool ok,string why)
    {if(!ok){GetTree().Quit(1);throw new InvalidOperationException(why);}_researchChecks++;}
    private void CheckProgressionFlow()
    {
        int frame=++_researchFrame;var tree=ResearchTree.Catalog;
        if(frame==5)
        {
            ResearchVerify(Hangar.Owned.Count==2&&PlayerHullId=="earth_starter_escort","New campaign starts with the two free T1 hulls");
            SelectRole();ChooseHull("battleship");
            ResearchVerify(Screen==BattleScreen.Research&&ResearchNodeId==tree.ByHull("battleship").Id,"Locked hull opens its research node");
            ResearchVerify(Input.MouseMode==Input.MouseModeEnum.Visible,"Research releases flight input");
            Button ResearchAction()=>_overlay.ResearchView!.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.GetMeta("focus_key","").AsString()=="research-action");
            ResearchVerify(ResearchAction().Disabled&&_overlay.ResearchView!.GetChildren().OfType<Label>().Any(l=>l.Text.Contains("먼저 연구")),
                "Locked hull states its prerequisite next to the unavailable action");
            var research=Hangar.Research!;
            research.Reward("flow-fixture",DesignFamily.Earth,300,true,10000,0);
            _overlay.ResearchView!.SelectNode(tree.ByHull("escort").Id);
            ResearchAction().GrabFocus();ResearchAction().EmitSignal(BaseButton.SignalName.Pressed);
            string unchanged=HangarStorage.Serialize(Hangar);
            var confirmation=MenuConfirmation.Pending(_overlay.ResearchView!)!;
            ResearchVerify(confirmation is not null&&!Hangar.Owned.Contains("escort")&&research.Points[DesignFamily.Earth]==260,
                "Hull research waits for confirmation before unlocking or deducting RP");
            _UnhandledInput(new InputEventKey{PhysicalKeycode=Key.Escape,Pressed=true});
            ResearchVerify(Screen==BattleScreen.Research&&MenuConfirmation.Pending(_overlay.ResearchView!) is null
                &&HangarStorage.Serialize(Hangar)==unchanged,"Escape cancels research and keeps the ledger unchanged");
            ResearchAction().EmitSignal(BaseButton.SignalName.Pressed);
            MenuConfirmation.Pending(_overlay.ResearchView!)!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
            ResearchVerify(Hangar.Owned.Contains("escort")&&research.Points[DesignFamily.Earth]==60,"Research button unlocks the hull and deducts its cost");
            ResearchVerify(ResearchAction().Text=="격납고에서 편성"&&GetViewport().GuiGetFocusOwner()==ResearchAction(),
                "Purchasing via the button restores focus to the now-available fitting action");
            research.Reward("flow-fixture-2",DesignFamily.Earth,300,false,0,0);
            _overlay.ResearchView!.Purchase("drive");
            unchanged=HangarStorage.Serialize(Hangar);confirmation=MenuConfirmation.Pending(_overlay.ResearchView!)!;
            ResearchVerify(confirmation.Body.Contains("프리셋 1")&&confirmation.Body.Contains("프리셋 3")&&confirmation.Body.Contains("→"),
                "Permanent research previews each resolved preset");
            confirmation.CancelButton.EmitSignal(BaseButton.SignalName.Pressed);
            ResearchVerify(HangarStorage.Serialize(Hangar)==unchanged,"Cancelling permanent research keeps all upgrades and RP");
            _overlay.ResearchView!.Purchase("drive");confirmation=MenuConfirmation.Pending(_overlay.ResearchView!)!;
            confirmation.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
            confirmation.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
            ResearchVerify(research.ForHull("escort").Contains("drive")&&research.Points[DesignFamily.Earth]==40,"Part button purchases a permanent upgrade");
            ChooseHull("escort");OpenHangar();
            ResearchVerify(_overlay.HangarView!.CurrentFit.Definition.Flight.ForwardAccel>ShipDefinitions.ById("escort").Flight.ForwardAccel,"Hangar preview includes researched acceleration");
            Hangar.Hulls["escort"].Presets[1]=Array.Empty<EquipmentMount>();
            ResearchVerify(Hangar.Fit(new("escort",1)).Upgrades!.Contains("drive"),"Research applies across equipment presets");
            _pointsBeforeBattle=research.Points[DesignFamily.Earth];BeginMatch(true);
            ResearchVerify(PendingMatch is {LowTier:1,HighTier:3}&&PendingMatch.Player.Loadout!.Members.All(m=>m.Definition.Flight.ForwardAccel>ShipDefinitions.ById("escort").Flight.ForwardAccel),"Queue freezes researched squad in its tier bracket");
        }
        if(frame==120)
        {
            ResearchVerify(Screen==BattleScreen.Battle&&Battle is not null,"Researched squad enters battle");
            var game=Battle!;game.AdvanceBattle(35);
            foreach(var ship in game.World.Ships.Where(s=>s.Faction==Faction.Red))ship.Damage.Breakup(game.World.Time,"research check");
            MatchTicks(game,5);
        }
        if(frame==280)
        {
            ResearchVerify(Screen==BattleScreen.Result&&Outcome?.Winner==Faction.Blue,"Completed battle reaches results");
            ResearchVerify(LastResearchReward is >=160 and <=300&&Hangar.Research!.Points[DesignFamily.Earth]==_pointsBeforeBattle+LastResearchReward,"Results award earned RP to the deployed design");
            long paid=Hangar.Research!.Points[DesignFamily.Earth];int receipts=Hangar.Research.Receipts.Length;
            ClaimResearchReward();ContinueSpectating();ClaimResearchReward();
            ResearchVerify(Hangar.Research.Points[DesignFamily.Earth]==paid&&Hangar.Research.Receipts.Length==receipts,"Reopening results cannot pay a battle twice");
            ReturnToPort();SelectRole();BeginMatch(true);SelectRole();
            ResearchVerify(Hangar.Research.Points[DesignFamily.Earth]==paid&&PendingMatch is null,"Cancelling matchmaking gives no reward");
            BeginMatch(true);
        }
        if(frame==400)
        {
            ResearchVerify(Battle is not null&&Screen==BattleScreen.Battle,"A second deployment uses persistent research");
            long paid=Hangar.Research!.Points[DesignFamily.Earth];ReturnToPort();
            ResearchVerify(Hangar.Research.Points[DesignFamily.Earth]==paid&&LastResearchReward is null,"Leaving an unfinished battle gives no reward");
            OpenResearch(tree.ByHull("escort").Id);
            _UnhandledInput(new InputEventKey{PhysicalKeycode=Key.Escape,Pressed=true});
            ResearchVerify(Screen==BattleScreen.Hangar,"Escape safely returns from research to hangar");
            ResearchBuyPath("earth_recon_t5");ChooseHull("earth_recon_t5");OpenHangar();
            ResearchVerify(_overlay.HangarView!.CurrentFit.Definition.ReconProbes!.Count==6,
                "T5 reconnaissance purchases and previews its actual probe equipment");
        }
        if(frame==440)BeginMatch(true);
        if(frame==560)
        {
            ResearchVerify(Screen==BattleScreen.Battle&&Battle!.Controlled!.Body.Definition.Id=="earth_recon_t5",
                "Reconnaissance hull deploys through the normal player flow");
            var ship=Battle!.Controlled!.Body;
            ResearchVerify(ship.Ordnance.Antimatter.Rounds==1&&Battle.World.LaunchProbe(ship,ship.Forward).Fired,
                "Player reconnaissance stores support actual probe launch and retain one AM round");
            ReturnToPort();ResearchBuyPath("mars_assault_t5");ChooseHull("mars_assault_t5");OpenHangar();
            ResearchVerify(_overlay.HangarView!.CurrentFit.Definition.Antimatter!.Rounds==6,
                "T5 assault preview has its six-round AM magazine");
        }
        if(frame==600)BeginMatch(true);
        if(frame==720)
        {
            ResearchVerify(Screen==BattleScreen.Battle&&Battle!.Controlled!.Body.Definition.Id=="mars_assault_t5",
                "Assault hull deploys through the normal player flow");
            var ship=Battle!.Controlled!.Body;
            ResearchVerify(ship.Ordnance.Interceptor.Probes==0&&!Battle.World.LaunchProbe(ship,ship.Forward).Fired
                &&ship.Ordnance.Antimatter.Rounds==6&&ship.Ordnance.Antimatter.BeginArming(),
                "Player assault has no probes and uses the existing AM arming controls");
            ReturnToPort();
            GD.Print($"PASS: {_researchChecks} research/purchase/deploy/result/requeue flow checks");GetTree().Quit();
        }
    }
    private void ResearchBuyPath(string hull)
    {
        var tree=ResearchTree.Catalog;var path=new System.Collections.Generic.List<ResearchNode>();var node=tree.ByHull(hull);
        while(true){path.Add(node);if(node.ParentId is null)break;node=tree.ById(node.ParentId);}
        Hangar.Research!.Points[tree.Design(node)]=100000; // Isolated flow-test profile, never persisted.
        foreach(var n in path.AsEnumerable().Reverse().Where(n=>!Hangar.Owned.Contains(n.HullId!)))
        {OpenResearch(n.Id);_overlay.ResearchView!.Purchase();MenuConfirmation.Pending(_overlay.ResearchView!)!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
            ResearchVerify(Hangar.Owned.Contains(n.HullId!),"Branch purchase respects its prerequisite chain");}
    }
}
