using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public partial class ScaleTest
{
    public string? SelectedExternalId { get; private set; }
    internal ExternalAim? SelectedExternalAim
    {
        get
        {
            if(Controlled?.Body is not {} me||FireTarget is not {} target)return null;
            string? id=Gunnery?.PriorityExternalId??SelectedExternalId;
            return id is not null&&ExternalTargets.TryAim(me,target.Body,TrackOf(target),World.Time,id,out var aim,obstacles:World.Obstacles)?aim:null;
        }
    }
    internal Vector3? AimLocalPoint=>SelectedExternalAim?.Point
        ??(Scheme==ControlScheme.Helm&&!Camera.TelescopeHeld?Gunnery?.EngagedLocalAim:AimModule?.Definition.Center);
    internal bool SelectExternalTarget(string? id)
    {
        if(Controlled?.Body is not {} me||InspectTarget is not {} target)return false;
        if(id is not null&&!ExternalTargets.TryAim(me,target.Body,TrackOf(target),World.Time,id,out _,obstacles:World.Obstacles))return false;
        GuardTargetChange();SelectedExternalId=id;AimPart=AimSubsystem.Center;
        if(Gunnery is {} order)
        {
            order.Target=target.Body;order.PriorityExternalId=id;order.PriorityModuleId=null;
            order.EngagedModule=null;order.EngagedLocalAim=null;order.Solution=null;
            if(order.Doctrine==FireDoctrine.Free)order.Doctrine=FireDoctrine.Focus;
        }
        return true;
    }
    private void CycleExternalTarget()
    {
        if(Controlled?.Body is not {} me||InspectTarget is not {} target)return;
        var model=ExternalTargets.For(target.Body.Definition);if(model is null)return;
        int current=Array.FindIndex(model.Parts,p=>p.Id==SelectedExternalId);
        for(int next=current+1;next<model.Parts.Length;next++)
            if(SelectExternalTarget(model.Parts[next].Id)){Notify("조준 · "+model.Parts[next].Label,false);return;}
        SelectExternalTarget(null);Notify("조준 · 선체 전체",false);
    }
}
