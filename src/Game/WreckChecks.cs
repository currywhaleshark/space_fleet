using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

internal static class WreckChecks
{
    public static int Run(Node parent,Camera3D camera)
    {
        int count=0;
        void Check(bool ok,string message) { if(!ok) throw new InvalidOperationException(message); count++; }
        foreach(var definition in ShipDefinitions.All)
        {
            var world=new SimWorld();
            var body=world.Add(new ShipBody("WRECK-"+definition.Id,definition,Faction.Blue));
            var peerBody=world.Add(new ShipBody("POWERED-PEER",definition,Faction.Blue));
            var view=ShipView.Create(body,4); parent.AddChild(view);
            var peer=ShipView.Create(peerBody,5); parent.AddChild(peer);
            var model=view.GetNode<Node3D>("Model");
            ShaderMaterial[] Panels(Node node) => ImportedShipModels.Meshes(node)
                .SelectMany(m=>Enumerable.Range(0,m.Mesh.GetSurfaceCount()).Select(m.GetActiveMaterial))
                .OfType<ShaderMaterial>().Where(WreckView.IsHullPanel).ToArray();
            void Sync() { view.Sync(Vec3d.Zero,1,.016f); view.SyncCombat(world,camera); view.Wreck.CompleteConstructionForChecks(); view.SyncCombat(world,camera); peer.Sync(Vec3d.Zero,1,.016f); }
            Sync(); var healthy=Panels(model); var peers=Panels(peer.GetNode<Node3D>("Model"));
            Check(healthy.Length>0 && peers.All(m=>m.GetShaderParameter("power").AsSingle()==1),"All six new hull skins start powered");
            foreach(var module in body.Damage.Modules.Where(m=>m.Definition.Kind is ModuleKind.Reactor or ModuleKind.Generator))
                body.Damage.Hurt(module,module.Health,body.SimTime,0,0);
            Sync();
            Check(body.Damage.Disabled && view.Wreck.PieceCount==0 && healthy.All(m=>m.GetShaderParameter("power").AsSingle()==0),"Power loss extinguishes panels without breaking the hull");
            Check(peers.All(m=>m.GetShaderParameter("power").AsSingle()==1),"A different live ship retains its lights");
            body.Damage.Reset(); Sync();
            Check(healthy.All(m=>m.GetShaderParameter("power").AsSingle()==1),"Repair restores the same authored skin");
            foreach(bool shatter in new[]{false,true})
            {
                if(shatter) body.Damage.Catastrophe(body.SimTime,"wreck fixture");
                else body.Damage.Breakup(body.SimTime,"wreck fixture");
                Sync(); var wreck=view.Wreck;
                Check(wreck.PieceCount==body.Wreck!.Pieces.Count && wreck.PieceCount>=2 && wreck.PieceCount<=(shatter?8:2),"Six hulls retain their simulation breakup layout, excluding empty cells");
                Check(wreck.ConstructionSteps>20 && !wreck.ConstructionPending,"Wreck construction is divided into bounded work units before replacing the intact hull");
                int limit=definition.Flight.Length>600?24:definition.Flight.Length>100?16:8;
                Check(wreck.DetachedPartCount>0 && wreck.DetachedPartCount<=limit,"Actual external parts detach within a per-hull cap");
                Check(wreck.DetachedTriangleCount>0 && wreck.RetainedTriangleCount>0
                    && wreck.DetachedTriangleCount+wreck.RetainedTriangleCount==wreck.SourceTriangleCount,"Detached geometry is removed from the retained triangle set");
                Check(wreck.InteriorSectionCount>0,"Actual cut hull surfaces expose structural detail");
                var burnt=Panels(wreck);
                Check(burnt.Length>0 && burnt.All(m=>m.GetShaderParameter("power").AsSingle()==0 && m.GetShaderParameter("wreck_burn").AsSingle()==1),"Wreck shader materials are burnt and unpowered");
                Check(healthy.All(m=>m.GetShaderParameter("wreck_burn").AsSingle()==0) && peers.All(m=>m.GetShaderParameter("power").AsSingle()==1),"Wreck conversion never contaminates live resources");
                var loose=wreck.GetChildren().OfType<Node3D>().First(n=>n.Name.ToString().StartsWith("DetachedPlate",StringComparison.Ordinal));
                var start=loose.Transform; body.Step(4); Sync();
                Check(loose.Transform!=start && loose.Visible,"Detached plates independently drift and tumble");
                int nodes=wreck.GetChildCount(); Sync(); Sync();
                Check(wreck.GetChildCount()==nodes,"Repeated draws do not rebuild or duplicate wreck pieces");
                var at=loose.GlobalTransform;
                var origin=new Vec3d(1e12,-2e12,3e12); body.Teleport(origin); view.Sync(origin,1,.016f); view.SyncCombat(world,camera);
                Check(loose.GlobalTransform.Origin.DistanceTo(at.Origin)<.1f,"Origin rebasing keeps visual debris attached to its world location");
                GD.Print($"wreck {definition.Id} {wreck.PieceCount}: {wreck.DetachedPartCount} detached / {wreck.InteriorSectionCount} sections / {wreck.BuildMilliseconds:0.0} ms total / {wreck.LongestConstructionStepMs:0.0} ms longest step");
                body.Step(30); Sync();
                Check(wreck.VisibleDetachedParts==0,"Skipped render frames still expire bounded decorative debris");
                body.Damage.Reset(); body.Teleport(Vec3d.Zero); Sync();
                Check(wreck.PieceCount==0 && wreck.DetachedPartCount==0 && wreck.InteriorSectionCount==0 && model.Visible,"Reset clears every extra part and restores the intact model");
            }
            body.Damage.Breakup(body.SimTime,"cancel in progress");
            view.Sync(Vec3d.Zero,1,.016f); view.SyncCombat(world,camera);
            Check(view.Wreck.ConstructionPending && model.Visible,"Intact silhouette remains during pending construction");
            body.Damage.Reset(); Sync();
            Check(!view.Wreck.ConstructionPending && view.Wreck.DetachedPartCount==0 && model.Visible,"Repair cancels an unfinished wreck build safely");
            view.Free(); peer.Free();
        }
        return count;
    }
}
