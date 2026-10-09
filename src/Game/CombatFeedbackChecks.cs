using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>피해 판정부터 연출까지 검증한다. --feedback-test, --feedback-preview=Critical.</summary>
public partial class CombatFeedbackChecks : Node
{
    private int _checks;
    private void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); _checks++; }

    internal static void Strike(SimWorld world, ShipBody shooter, ShipBody target, HitKind kind, uint id)
    {
        if (kind != HitKind.Shield) target.Damage.AbsorbShield(1e9f, world.Time);
        float shieldBefore = target.Damage.Shield;
        ModuleState module = target.Damage.Modules.First(m => m.Definition.Kind == ModuleKind.Gun);
        Vector3 direction = (target.Orientation * new Vector3(-1, -.35f, -.2f)).Normalized();
        Vec3d origin = target.Position + Vec3d.From(target.Orientation * module.Definition.Center - direction * target.Class.Length);
        var packet = new DamagePacket(80, kind is HitKind.Shield or HitKind.Armor ? 1 : 10000,
            kind == HitKind.Critical ? 1e6f : 2, target.Class.Length * 3);
        ShotResult hit = DamageRay.Apply(target, origin, direction, packet, world.Time, id);
        world.RecordImpact(id, shooter, hit, world.Time, direction, packet.Energy, shieldBefore, BattleWeapon.Railgun);
    }

    public override void _Ready()
    {
        try { Run(); GD.Print($"PASS: {_checks} combat feedback checks"); GetTree().Quit(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Run()
    {
        foreach (HitKind kind in Enum.GetValues<HitKind>())
        {
            var world = new SimWorld();
            var me = world.Add(new ShipBody("ME", ShipClass.Interceptor, Faction.Blue));
            var enemy = world.Add(new ShipBody("ENEMY", ShipClass.Battleship, Faction.Red));
            enemy.Place(new Vec3d(0, 0, -10000), Quaternion.Identity);
            var own = new CombatFeedback(); var outgoing = new CombatFeedback();
            own.Prime(world, enemy); outgoing.Prime(world, me);
            Strike(world, me, enemy, kind, 80);
            own.Observe(world, enemy); outgoing.Observe(world, me);
            Check(own.Incoming.Count == 1 && own.Incoming[0].Kind == kind, $"Actual ray classifies {kind}");
            Check(outgoing.Outgoing?.Kind == kind, $"Outgoing and incoming agree for {kind}");
            Check(own.Incoming[0].SourceDirection.X > 0 && own.Incoming[0].SourceDirection.Y > 0, "Incoming bearing is toward source");
            own.Observe(world, enemy); outgoing.Observe(world, me);
            Check(own.Incoming.Count == 1, "Retained event never duplicates");
            own.Advance(0); Check(own.Incoming[0].Age == 0, "Pause freezes feedback time");
            own.Advance(2); outgoing.Advance(2);
            Check(own.Incoming.Count == 0 && outgoing.Outgoing is null, "Feedback expires after display time");
        }

        var events = new SimWorld();
        var blue = events.Add(new ShipBody("BLUE", ShipClass.Interceptor, Faction.Blue));
        var red = events.Add(new ShipBody("RED", ShipClass.Battleship, Faction.Red));
        var observer = new CombatFeedback(); observer.Prime(events, blue);
        int confirmations = 0; observer.Confirmed += _ => confirmations++;
        Strike(events, blue, red, HitKind.Shield, 100); observer.Observe(events, blue);
        Strike(events, blue, red, HitKind.Shield, 3); observer.Observe(events, blue);
        Check(confirmations == 2, "Older projectile arriving after newer projectile is confirmed");
        Strike(events, blue, red, HitKind.Shield, 101); observer.Observe(events, blue, paused:true);
        observer.Observe(events, blue); Check(confirmations == 2, "Paused observation does not replay on resume");
        var ally = events.Add(new ShipBody("ALLY", ShipClass.Battleship, Faction.Blue));
        Strike(events, blue, ally, HitKind.Shield, 102); observer.Observe(events, blue);
        Check(confirmations == 2, "Friendly fire is not enemy success");
        observer.Prime(events, red); observer.Observe(events, red);
        Check(observer.Incoming.Count == 0 && observer.Outgoing is null, "Control handoff clears old feedback");
        red.Damage.Breakup(events.Time, "fixture"); observer.Observe(events, red);
        Check(observer.Incoming.Single().Kind == HitKind.Critical, "Collision breakup is critical");
        observer.Observe(events, red); Check(observer.Incoming.Count == 1, "Breakup does not repeat");

        // Snapshot remains the original shot's result even if the target dies later.
        Check(!CombatFeedback.Describe(events.Impacts[0]).Label.Contains("격침"), "Later destruction cannot relabel an earlier shield hit");
        var shieldBreak = events.Impacts[0] with { ShieldBroken = true };
        Check(CombatFeedback.Describe(shieldBreak).Label == "실드 붕괴", "Shield collapse has a separate result label");

        var decay = new CombatFeedback(); decay.Prime(events, ally);
        Strike(events, red, ally, HitKind.Critical, 200); decay.Observe(events, ally);
        for (uint id = 201; id < 221; id++)
        {
            decay.Advance(.1f); Strike(events, red, ally, HitKind.Armor, id); decay.Observe(events, ally);
        }
        Check(decay.Incoming.All(h => h.Kind != HitKind.Critical), "Small follow-up hits cannot keep an old critical alert alive");

        var camera = new ChaseCamera(); AddChild(camera); camera.MakeCurrent();
        camera.ResetAim(Quaternion.Identity);
        camera.Follow(ShipDefinitions.For(HullKind.Interceptor), Vector3.Zero, Quaternion.Identity, .016f);
        Vector3 position = camera.Position, aim = camera.AimForward;
        camera.AddImpact(Vector3.Right, .8f, HullKind.Interceptor);
        camera.AdvanceImpacts(.04f, 1);
        camera.Follow(ShipDefinitions.For(HullKind.Interceptor), Vector3.Zero, Quaternion.Identity, .016f);
        Check(camera.VisualRotation.Length() > .001f, "Impact changes visual rotation");
        Check(camera.Position.IsEqualApprox(position) && camera.AimForward.IsEqualApprox(aim), "Visual kick preserves firing origin and aim");
        Check(camera.ProjectRayNormal(camera.AimScreenPosition()).AngleTo(aim) < .001f, "Displayed reticle projects onto unchanged aim ray");
        Vector3 rotation = camera.VisualRotation;
        camera.AdvanceImpacts(0, 1); Check(camera.VisualRotation.IsEqualApprox(rotation), "Paused camera freezes kick");
        camera.AdvanceImpacts(0, 0); Check(camera.VisualRotation == Vector3.Zero, "Zero percent immediately disables shake");
        for (int i = 0; i < 100; i++) camera.AddImpact(Vector3.Right, 1, HullKind.Interceptor);
        camera.AdvanceImpacts(.04f, 1); Check(camera.VisualRotation.Length() <= Mathf.DegToRad(1.801f), "Sustained fire has a bounded camera amplitude");
        camera.AdvanceImpacts(1, 1); Check(camera.VisualRotation == Vector3.Zero, "Shake settles exactly without drift");
        camera.AddImpact(Vector3.Right, 1, HullKind.Battleship); camera.AdvanceImpacts(.4f, 1);
        Check(camera.VisualRotation.Length() > 0, "Battleship has a slower lingering response");
        camera.ResetAim(Quaternion.Identity); Check(camera.VisualRotation == Vector3.Zero, "Changing control clears camera kick");
        camera.QueueFree();
    }
}

public partial class ScaleTest
{
    private void PreviewFeedback()
    {
        if (_shot is null || _frame != _shot.Frames - 8 || Controlled is null) return;
        var args = BattleArgs.Parse(OS.GetCmdlineUserArgs());
        if (!Enum.TryParse(args.GetValueOrDefault("feedback-preview"), true, out HitKind kind)) return;
        ShipBody me = Controlled.Body;
        ShipBody enemy = World.Ships.First(s => s.Faction != me.Faction);
        CombatFeedbackChecks.Strike(World, enemy, me, kind, uint.MaxValue - 1);
        CombatFeedbackChecks.Strike(World, me, enemy, kind, uint.MaxValue);
        _audio.Observe(World, me);
    }
}
