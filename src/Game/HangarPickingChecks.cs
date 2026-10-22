using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class Main
{
    private int _pickingFrame;
    private Vector2 _pickingPoint;
    private string _pickingSave="";
    private readonly List<string> _pickingFailures=new();
    private void CheckHangarPicking()
    {
        int frame=++_pickingFrame;var hulls=FleetLoadouts.Hulls.ToArray();
        void Verify(bool ok,string message){_hangarChecks++;if(!ok){_pickingFailures.Add(message);GD.Print("PICK FAIL: "+message);}}
        if(frame<=hulls.Length)
        {
            var def=FleetLoadouts.ResolveFit(new(hulls[frame-1].Id,Array.Empty<EquipmentMount>())).Definition;
            var preview=new HangarPreview{Definition=def,Size=new(816,352),ShowBayMarkers=false};AddChild(preview);
            var parts=preview.Fittings!.Parts;
            Verify(parts.Count(p=>p.Category==HangarView.FittingCategory.MainGun)==def.PrimaryMounts.Length,def.Id+": every primary socket has a target");
            Verify(parts.All(p=>p.Samples.Length>0),def.Id+": authored exterior regions contain rendered triangles: "+string.Join(",",parts.Where(p=>p.Samples.Length==0).Select(p=>p.Id)));
            var seen=new HashSet<string>();bool occluded=false;
            foreach(var (yaw,pitch,zoom) in new[]{(-.9f,.42f,1.35f),(.9f,.6f,.9f),(2.4f,.4f,1.1f),(-2.4f,-.7f,1.1f),(0f,1.2f,1.1f),(0f,-1.2f,1.1f),
                (Mathf.Pi,0f,.9f),(Mathf.Pi,-.3f,.9f),(-Mathf.Pi/2,0f,1.1f),(Mathf.Pi/2,0f,1.1f)})
            {
                preview.AimView(yaw,pitch,zoom);
                occluded|=parts.Any(p=>p.Screen is null);
                foreach(var part in parts)
                {
                    if(part.Screen is not { } point)continue;
                    Verify(preview.PickPart(point)==part,def.Id+": visible marker selects exact surface "+part.Id);
                    seen.Add(part.Id);
                }
            }
            Verify(occluded,def.Id+": far-side markers are hidden by actual hull geometry");
            Verify(parts.All(p=>seen.Contains(p.Id)),def.Id+": all exterior parts reachable by orbit: "+string.Join(",",parts.Where(p=>!seen.Contains(p.Id)).Select(p=>p.Id)));
            Verify(preview.PickPart(new(-1,-1)) is null,def.Id+": outside preview cannot select a fitting");
            RemoveChild(preview);preview.QueueFree();
            GD.Print("picking model checked: "+def.Id);
            return;
        }
        int step=frame-hulls.Length;
        if(step==1)
        {
            HangarVerify(_pickingFailures.Count==0,"Exterior picking failed: "+string.Join("; ",_pickingFailures));
            OpenHangar();ChooseHull("battleship");var view=_overlay.HangarView!;
            _pickingSave=HangarStorage.Serialize(Hangar);
            var preview=view.Preview;var part=preview.Fittings!.Parts.Single(p=>p.Id=="main:1");
            _pickingPoint=part.Screen!.Value;
            preview._GuiInput(new InputEventMouseMotion{Position=_pickingPoint});
            HangarVerify(preview.HoveredPart==part&&part.Highlight!.Visible&&part.Name.Length>0,"Hover highlights the actual part and gives its installed name");
            ClickPreview(preview,_pickingPoint);
            HangarVerify(view.SelectedCategory==HangarView.FittingCategory.MainGun&&view.SelectedSocket==1,"Click opens the second primary socket, not the first");
            HangarVerify(HangarStorage.Serialize(Hangar)==_pickingSave,"Selecting a part does not equip, research or spend RP");
            // Real viewport input while a picker is already open: the backdrop must pass the ship through.
            preview=view.Preview;preview.AimView(-.9f,.42f,1.35f);
            var sensor=preview.Fittings!.Parts.Single(p=>p.Id=="sensor");
            _pickingPoint=preview.GetGlobalTransformWithCanvas()*sensor.Screen!.Value;
            Input.ParseInputEvent(new InputEventMouseMotion{Position=_pickingPoint,GlobalPosition=_pickingPoint});
            Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=_pickingPoint,GlobalPosition=_pickingPoint});
            Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=_pickingPoint,GlobalPosition=_pickingPoint});
        }
        if(step==3)
        {
            var view=_overlay.HangarView!;
            HangarVerify(view.SelectedCategory==HangarView.FittingCategory.Sensor,"Real click through picker backdrop switches directly to the mast");
            HangarVerify(HangarStorage.Serialize(Hangar)==_pickingSave,"Switching fitting menus preserves all saved fits");
            view.CloseOverlay();var preview=view.Preview;var at=preview.Fittings!.Parts.First(p=>p.Screen is not null).Screen!.Value;
            float yaw=preview.Yaw;
            preview._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=at});
            preview._GuiInput(new InputEventMouseMotion{Position=at+new Vector2(60,0),Relative=new(60,0)});
            preview._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=at+new Vector2(60,0)});
            HangarVerify(preview.Yaw!=yaw&&preview.HoveredPart is null&&!view.CloseOverlay(),"Orbit drag neither opens nor purchases a part and clears hover");
            Hangar.Research!.Points[DesignFamily.Earth]=100000;
            Hangar.Research.Upgrades["battleship"]=new[]{"fire-control"};view.SelectSlot(0);
            preview=view.Preview;preview.AimView(-.9f,.42f,1.1f);
            ClickPreview(preview,preview.Fittings!.Parts.Single(p=>p.Id=="main:1").Screen!.Value);
            string before=HangarStorage.Serialize(Hangar);int firstBarrels=view.CurrentFit.Definition.Battery!.WeaponAt(0).BarrelCount;
            view.InstallPart(view.SelectedSocket,"earth-rail-l3",true);
            var modal=MenuConfirmation.Pending(view)!;
            HangarVerify(modal is not null&&modal.Body.Contains("소켓 2")&&HangarStorage.Serialize(Hangar)==before,"Surface-selected socket previews the correct replacement before any edit");
            modal!.CancelButton.EmitSignal(BaseButton.SignalName.Pressed);
            HangarVerify(HangarStorage.Serialize(Hangar)==before,"Cancel preserves research balance and all socket parts");
            view.InstallPart(view.SelectedSocket,"earth-rail-l3",true);
            MenuConfirmation.Pending(view)!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
            HangarVerify(view.SelectedParts!.MainGuns is [null,"earth-rail-l3",null]&&view.CurrentFit.Definition.Battery!.WeaponAt(1).BarrelCount==3
                &&view.CurrentFit.Definition.Battery.WeaponAt(0).BarrelCount==firstBarrels,
                "Confirmed surface edit replaces only the clicked socket for the same weapon family");
            preview=view.Preview;preview.AimView(-.9f,.42f,1.1f);
            var replacement=preview.Fittings!.Parts.Single(p=>p.Id=="main:1");
            HangarVerify(replacement.Name==MainGunParts.ById("earth-rail-l3").Name&&preview.PickPart(replacement.Screen!.Value)==replacement,
                "Replacement geometry remains pickable and hover describes the actual new part");
            ClickPreview(preview,new(20,300));
            HangarVerify(!view.CloseOverlay(),"Empty preview click dismisses the part picker");
            view.SelectBay("utility-port");
            HangarVerify(view.Preview.Fittings is null&&view.Preview.PickPart(at) is null,"Cutaway exposes bays without hidden exterior picking");
            view.CloseOverlay();preview=view.Preview;preview.GrabFocus();
            foreach(var part in preview.Fittings!.Parts)
            {
                preview._GuiInput(new InputEventKey{Keycode=Key.Right,Pressed=true});
                HangarVerify(preview.HoveredPart==part&&part.Screen is not null,"Keyboard selection reaches exterior part without an icon: "+part.Id);
            }
            preview._GuiInput(new InputEventKey{Keycode=Key.Enter,Pressed=true});
            HangarVerify(view.SelectedCategory==HangarView.FittingCategory.Engine&&GetViewport().GuiGetFocusOwner() is Button,"Enter opens the chosen exterior category and focuses its options");
            view.CloseOverlay();
            HangarVerify(GetViewport().GuiGetFocusOwner()==view.Preview,"Closing exterior options restores focus to the ship preview");
            GD.Print($"PASS: {_hangarChecks} hangar surface picking checks, {hulls.Length} hulls");GetTree().Quit();
        }
    }
    private static void ClickPreview(HangarPreview preview,Vector2 point)
    {
        preview._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=point});
        preview._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=point});
    }
}
