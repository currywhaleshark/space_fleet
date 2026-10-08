using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>식별할 수 있을 때만 복사하는 상태. 가변 ShipDamage를 보관하지 않는다.</summary>
public sealed record ContactState(string Callsign, string ClassName, HullKind Kind, bool Destroyed, bool Disabled,
    float Shield, float Propulsion, float Maneuver, float Weapons, float Sensors, float Cooling, int LostModules, bool Damaged)
{
    public string Status => Destroyed ? "격침" : Disabled ? "무력화" : Damaged ? "손상" : "활동 중";
    public static ContactState Capture(ShipBody ship)
    {
        ShipDamage d = ship.Damage;
        return new(ship.Callsign, ship.Class.DisplayName, ship.Class.Kind, d.Destroyed, d.Disabled,
            d.Shield / Math.Max(1, ship.Definition.Shield.Capacity), d.PropulsionFraction, d.ManeuverFraction,
            d.WeaponsFraction, d.SensorFraction, d.CoolingFraction, d.Modules.Count(m => m.Destroyed), d.Modules.Any(m => m.HealthFraction < .999f));
    }
}

public sealed record ContactSnapshot(Vec3d Position, SensorTrack Track, double LastSeenAt,
    ContactState? State, double LastIdentifiedAt, bool SignalLost = false)
{
    public bool Identified => State is not null;
    public string Name => State?.Callsign ?? "미식별 접촉";
    public Vec3d DisplayPosition(Vec3d interpolated) => SignalLost ? Position : interpolated + Track.Offset;
}

/// <summary>화면용 관측 기록. 사격통제에는 여전히 실시간 SensorNet만 사용한다.</summary>
public sealed class ContactMemory
{
    public static readonly Color LostColor = new(.6f, .62f, .65f, .9f);
    private readonly Dictionary<(Faction, ShipBody), ContactSnapshot> _contacts = new();
    public ContactSnapshot? Get(Faction observer, ShipBody ship) => _contacts.GetValueOrDefault((observer, ship));

    public void Observe(Faction observer, ShipBody ship, SensorTrack track, double time)
    {
        var key = (observer, ship);
        ContactSnapshot? previous = Get(observer, ship);
        if (ship.Faction != observer && track.Level == TrackLevel.None)
        {
            if (previous is { SignalLost: false }) _contacts[key] = previous with { SignalLost = true };
            return;
        }
        bool identified = ship.Faction == observer || track.Level >= TrackLevel.Identified;
        _contacts[key] = new(ship.Position + track.Offset, track, time,
            identified ? ContactState.Capture(ship) : previous?.State,
            identified ? time : previous?.LastIdentifiedAt ?? 0);
    }
}

public partial class ScaleTest
{
    private readonly ContactMemory _contacts = new();
    public ContactSnapshot? ContactOf(ShipView view) => _contacts.Get(Controlled?.Body.Faction ?? Faction.Blue, view.Body);
    private void UpdateContacts()
    {
        Faction side = Controlled?.Body.Faction ?? Faction.Blue;
        foreach (ShipBody ship in World.Ships) _contacts.Observe(side, ship, World.Sensors.Track(side, ship), World.Time);
    }
}
