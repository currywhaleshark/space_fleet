using System;
using System.Linq;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

/// <summary>--contact-test: 소실/재탐지, 정보 비노출, 고정 위치, 실제 센서와 사격통제 분리.</summary>
public partial class ContactMemoryChecks : Node
{
    private int _checks;
    private void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); _checks++; }
    public override void _Ready()
    {
        try { Run(); GD.Print($"PASS: {_checks} contact memory checks"); GetTree().Quit(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Run()
    {
        var world = new SimWorld();
        var me = world.Add(new ShipBody("OBSERVER", ShipClass.Battleship, Faction.Blue));
        var enemy = world.Add(new ShipBody("CONTACT", ShipClass.Escort, Faction.Red));
        enemy.Place(new(0, 2500, -20000), Quaternion.Identity);
        var memory = new ContactMemory();
        memory.Observe(Faction.Blue, enemy, SensorTrack.Unknown, 0);
        Check(memory.Get(Faction.Blue, enemy) is null, "Never detected enemies have no ghost");
        var unknown = new SensorTrack(TrackLevel.Contact, 1, 1, 0, 20000, 300, enemy.Position + new Vector3(100, 50, 0), new(100, 50, 0));
        memory.Observe(Faction.Blue, enemy, unknown, 1);
        Check(memory.Get(Faction.Blue, enemy) is { Identified: false, State: null }, "Contact-only observations do not reveal name or damage");
        memory.Observe(Faction.Blue, enemy, SensorTrack.Unknown, 2);
        Check(memory.Get(Faction.Blue, enemy) is { SignalLost: true, Identified: false, Name: "미식별 접촉" }, "Unidentified ghosts remain anonymous");
        Check(memory.Get(Faction.Red, enemy) is null, "Contact records are isolated per observing faction");

        world.Sensors.Update(world.Ships, 3, force: true);
        SensorTrack track = world.Sensors.Track(Faction.Blue, enemy);
        Check(track.Level >= TrackLevel.Identified, "Actual nearby sensor track is identified");
        enemy.Damage.Modules[0].Health *= .8f;
        memory.Observe(Faction.Blue, enemy, track, 3);
        ContactSnapshot observed = memory.Get(Faction.Blue, enemy)!;
        Check(observed.Identified && !observed.SignalLost && observed.State!.Callsign == enemy.Callsign, "Identification replaces anonymous ghost with current identity");
        Check(observed.Position == enemy.Position + track.Offset, "Last position includes sensor estimation error");
        Check(observed.State!.Status == "손상" && observed.State.LostModules == 0, "Nonfatal damage is preserved even before a module is destroyed");
        enemy.Teleport(new(1e9, 1e9, 1e9));
        world.Sensors.Update(world.Ships, 4, force: true);
        Check(world.Sensors.Track(Faction.Blue, enemy).Level == TrackLevel.None, "Moving out of sensor coverage loses live contact");
        memory.Observe(Faction.Blue, enemy, world.Sensors.Track(Faction.Blue, enemy), 4);
        ContactSnapshot lost = memory.Get(Faction.Blue, enemy)!;
        Check(lost.SignalLost && lost.Position == observed.Position && lost.LastSeenAt == 3, "Loss freezes last observed position and time");
        enemy.Damage.Breakup(5, "hidden damage"); enemy.Teleport(new(10000, -20000, 30000));
        memory.Observe(Faction.Blue, enemy, SensorTrack.Unknown, 300);
        lost = memory.Get(Faction.Blue, enemy)!;
        Check(lost.State == observed.State && !lost.State!.Destroyed, "Hidden destruction does not alter remembered damage status");
        Check(lost.DisplayPosition(enemy.Position) == observed.Position && lost.LastSeenAt == 3, "Lost contact neither follows the ship nor expires with time");
        var radar = TacticalRadar.Locate(me, me.Position, me.Orientation, enemy, enemy.Position, SensorTrack.Unknown, lost);
        Check(radar is { SignalLost: true, Identified: true } && radar.Value.Local.IsEqualApprox((observed.Position - me.Position).ToVector3()), "Radar keeps gray contact at last known location after hidden destruction");
        Vec3d movedObserver = new(1e6, 0, 0);
        var movedRadar = TacticalRadar.Locate(me, movedObserver, me.Orientation, enemy, enemy.Position, SensorTrack.Unknown, lost)!.Value;
        Check(movedRadar.Local.IsEqualApprox((observed.Position - movedObserver).ToVector3()), "Observer motion changes bearing to a fixed ghost, not its world location");
        Check(new ContactMemory().Get(Faction.Blue, enemy) is null, "A new battle does not inherit previous contacts");

        enemy.Damage.Reset(); enemy.Place(new(4000, -1000, -18000), Quaternion.Identity);
        world.Sensors.Update(world.Ships, 6, force: true);
        SensorTrack recoveredTrack = world.Sensors.Track(Faction.Blue, enemy);
        memory.Observe(Faction.Blue, enemy, recoveredTrack, 6);
        ContactSnapshot recovered = memory.Get(Faction.Blue, enemy)!;
        Check(!recovered.SignalLost && recovered.Position != lost.Position && recovered.LastSeenAt == 6, "Reacquisition updates the same contact and clears signal loss");
        enemy.Damage.Breakup(7, "observed damage");
        memory.Observe(Faction.Blue, enemy, recoveredTrack, 7);
        Check(memory.Get(Faction.Blue, enemy)!.State!.Destroyed, "New identified observation updates damage state");
        memory.Observe(Faction.Blue, enemy, unknown, 8);
        Check(memory.Get(Faction.Blue, enemy)!.State!.Destroyed, "Weak contact retains previously identified status instead of inventing new information");

        enemy.Damage.Reset(); enemy.Place(new(0, 0, -15000), Quaternion.Identity);
        world.Sensors.Update(world.Ships, 9, force: true);
        memory.Observe(Faction.Blue, enemy, world.Sensors.Track(Faction.Blue, enemy), 9);
        // 센서만 잃었고 실제 적은 사거리 안에 남아 있어도 기억으로 잠금을 대체하지 않는다.
        world.Sensors.Update(new[] { enemy }, 10, force: true);
        SensorTrack noLock = world.Sensors.Track(Faction.Blue, enemy);
        memory.Observe(Faction.Blue, enemy, noLock, 10);
        Check(memory.Get(Faction.Blue, enemy)!.SignalLost && noLock.Level == TrackLevel.None, "Presentation memory does not promote live sensor level");
        Check(!FireControl.Solve(me, enemy, 10, track: noLock).Valid, "Lost contact cannot provide an assisted firing solution");
        Check(!world.TryAutoFire(me, enemy, null, 30, out _) && !world.LaunchMissile(me, enemy).Fired, "Ghost cannot authorize automatic railgun or missile launch");
        memory.Observe(Faction.Blue, me, world.Sensors.Track(Faction.Blue, me), 10);
        Check(memory.Get(Faction.Blue, me) is { SignalLost: false, Identified: true }, "Friendly datalink stays live");
    }
}

public partial class ScaleTest
{
    internal void CheckContactSelection(Action<bool, string> check)
    {
        var target = Views.First(v => v.Body.Callsign == "BB-X1");
        Vec3d original = target.Body.Position;
        target.Body.Place(Controlled!.Body.Position + Vec3d.From(Controlled.Body.Forward) * 30000, target.Body.Orientation);
        target.Body.Power.Reset(); World.Sensors.Update(World.Ships, World.Time, force: true); UpdateContacts();
        SelectEnemy(target); Gunnery!.Doctrine = FireDoctrine.Focus;
        var observed = ContactOf(target)!;
        check(observed.Identified, "Chosen target is identified before loss");
        target.Body.Teleport(new(1e9, 0, 0));
        World.Sensors.Update(World.Ships, World.Time, force: true); UpdateContacts();
        StepOnce();
        check(InspectTarget == target && Gunnery.Target == target.Body, "Signal loss preserves inspection and selected fire target");
        check(ContactOf(target) is { SignalLost: true } lost && lost.Position == observed.Position, "Scene contact memory freezes position");
        check(FireTarget is null && Gunnery.Solution is null, "Selected ghost supplies no live firing solution");
        target.Body.Place(original, target.Body.Orientation);
        World.Sensors.Update(World.Ships, World.Time, force: true); UpdateContacts();
        check(InspectTarget == target && ContactOf(target) is { SignalLost: false }, "Reacquisition updates the selected target without reselection");
    }

    private void PreviewLostContact()
    {
        if (_shot is null || _frame is not (30 or 32) || !BattleArgs.Parse(OS.GetCmdlineUserArgs()).ContainsKey("lost-contact-preview")) return;
        var target = Views.First(v => v.Body.Callsign == "BB-X1");
        if (_frame == 32)
        {
            if (!WorldMapOpen) return;
            var surface = _worldMap.GetChild<WorldMapSurface>(0);
            Vector3 projected = WorldMap.Project(ContactOf(target)!.Position);
            Vector2 point = surface.Position + surface.Size * .5f
                + new Vector2(projected.X, projected.Y) * Mathf.Min(surface.Size.X, surface.Size.Y) * .46f;
            _worldMap.HandleInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point });
            _worldMap.HandleInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = point });
            return;
        }
        target.Body.Place(Controlled!.Body.Position + Vec3d.From(Controlled.Body.Forward) * 40000, target.Body.Orientation);
        target.Body.Power.Reset(); World.Sensors.Update(World.Ships, World.Time, force: true); UpdateContacts();
        SelectEnemy(target);
        target.Body.Teleport(new(1e9, 0, 0));
        World.Sensors.Update(World.Ships, World.Time, force: true); UpdateContacts();
        target.Body.Damage.Breakup(World.Time, "hidden preview damage");
    }
}
