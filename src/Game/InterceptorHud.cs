using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

public readonly record struct ProbeMarker(Vec3d Position, string Label, bool Enemy, bool Lost, bool Observing)
{ public object? Key { get; init; } }

public partial class ScaleTest
{
    public SmallContact? SmallAimTarget { get; private set; }
    public Vector3 SmallAimDirection { get; private set; }
    public Vec3d SmallAimPoint { get; private set; }
    public bool CorrectingSmallAim { get; private set; }
    private void UpdateSmallAim(ShipBody ship)
    {
        SmallAimTarget = null; CorrectingSmallAim = false; SmallAimSolved = false; SmallAimStatus = ""; SmallApproachSeconds = null;
        if (SelectedSmallContact is { } selected)
        {
            selected = World.Sensors.SmallTrack(ship.Faction, selected.Target) ?? selected;
            SelectedSmallContact = selected; SmallAimTarget = selected; UpdateSmallApproach(ship, selected);
            SmallAimPoint = selected.Predict(World.Time);
            if (!selected.Fresh(World.Time)) { SmallAimStatus = "신호 소실"; return; }
            if (SelectedWeapon == PlayerWeapon.Missile && selected.Target is Missile targetMissile)
            {
                SmallAimSolved = World.CounterMissileSolution(ship, targetMissile, out var launchDirection, out var status);
                SmallAimDirection = launchDirection;
                SmallAimStatus = SmallAimSolved && !ship.Ordnance.MissileReady ? ship.Ordnance.MissileStatus : status;
                // Counter missiles steer themselves; no rail-gun correction marker or nose tracking.
                return;
            }
            if (SelectedWeapon != PlayerWeapon.MainGun) { SmallAimStatus = "1 주포 / 2 미사일로 요격"; return; }
            if (!FireAssist && Scheme == ControlScheme.Pilot) { SmallAimStatus = "사격 보조 꺼짐 · T"; return; }
            SmallAimSolved = World.SmallTargetAim(ship, selected, out var direction, out var aim, playerAssist: true);
            SmallAimDirection = direction; SmallAimPoint = aim;
            if (!SmallAimSolved) { SmallAimStatus = "요격 사거리/사격통제 확인"; return; }
            var line = SimWorld.RailLine(ship, direction);
            CorrectingSmallAim = line == FireFailure.None;
            SmallAimStatus = CorrectingSmallAim ? (AutoSmallBattery ? "자동 요격" : TrackingSmallAim ? "기수 추적 · 근접신관 요격" : "좌클릭 유지 · 요격 보정") : line == FireFailure.HullBlocked
                ? "선체 가림" : Scheme == ControlScheme.Pilot ? (TrackingSmallAim ? "기수 정렬 중 · 발사 대기" : "좌클릭 유지 · 기수 추적") : "포각 밖";
            return;
        }
        if (SelectedWeapon != PlayerWeapon.MainGun || !FireAssist || _explicitShipTarget || Scheme != ControlScheme.Pilot) return;
        float best = Mathf.DegToRad(18);
        foreach (var track in World.Sensors.SmallContacts(ship.Faction))
        {
            if (!World.SmallTargetAim(ship, track, out var direction, out var aim, playerAssist: true)) continue;
            float angle = Camera.AimForward.AngleTo((track.Predict(World.Time) - ship.Position).ToVector3());
            if (angle >= best) continue;
            best = angle; SmallAimTarget = track; SmallAimDirection = direction; SmallAimPoint = aim;
        }
        SmallAimSolved = SmallAimTarget is not null;
        CorrectingSmallAim = SmallAimSolved && best < Mathf.DegToRad(6)
            && SimWorld.RailLine(ship, SmallAimDirection) == FireFailure.None;
        SmallAimStatus = "R 표적 지정";
    }

    private bool AutoSmallBattery => Scheme == ControlScheme.Helm && !Camera.TelescopeHeld
        && Gunnery?.Doctrine is FireDoctrine.Free or FireDoctrine.Focus or FireDoctrine.Disable;

    private void StepSmallTargetBattery(ShipBody ship)
    {
        FiringSolution = null; CorrectingAim = false;
        if (MenuOpen || RadarPointerCaptured || SelectedWeapon != PlayerWeapon.MainGun) return;
        bool firing = !_fireReleaseGuard && (Input.IsActionPressed(InputSetup.Fire) || AutoSmallBattery);
        if (ScopeHullBlocked) { SimWorld.StopLasers(ship); if (firing) Notify("선체에 시야 가림", true); return; }
        if (!SmallAimSolved || SelectedSmallContact is not { } target)
        { SimWorld.StopLasers(ship); if (firing && Input.IsActionPressed(InputSetup.Fire)) Notify(SmallAimStatus, true); return; }
        bool fired = false; string status = SmallAimStatus;
        foreach (var gun in ship.Railguns)
        {
            if (!World.SmallTargetAim(ship, target, out var direction, out var point, gun, playerAssist: true))
            { gun.Laser?.Stop(); continue; }
            gun.Aim(direction);
            if (!firing) { gun.Laser?.Stop(); continue; }
            if (AutoSmallBattery && World.AutoLaserCooling(ship, gun)) { status = "냉각 중"; continue; }
            if (World.Obstacles.SegmentBlocked(gun.MuzzlePosition, point, out _))
            { gun.Laser?.Stop(); status = "지형 가림"; continue; }
            float range = (float)(point - gun.MuzzlePosition).Length();
            if (World.FriendlyInLine(ship, gun.MuzzlePosition, direction, range)
                || World.FriendlyInLine(ship, gun.MuzzlePosition, gun.Direction, range))
            { gun.Laser?.Stop(); status = "아군 사선"; continue; }
            var shot = World.FireRailgun(ship, gun, direction);
            fired |= shot.Fired; status = shot.Reason;
        }
        if (firing)
        {
            SmallAimStatus = fired ? (ship.Railgun?.IsLaser == true ? "레이저 요격" : "요격 주포 발사") : status;
            if (Input.IsActionPressed(InputSetup.Fire)) Notify(SmallAimStatus, !fired);
        }
    }

    public IEnumerable<ProbeMarker> ProbeMarkers()
    {
        Faction faction = Controlled?.Body.Faction ?? Faction.Blue;
        foreach (var probe in World.Probes)
            if (probe.Faction == faction)
                yield return new(probe.Position, $"P{probe.Id} · {probe.Remaining:0}s", false, false, probe.Observing > 0) { Key=probe };
        foreach (var track in World.Sensors.SmallContacts(faction))
            if (track.Kind == SmallContactKind.Probe)
                yield return new(track.Fresh(World.Time) ? track.Predict(World.Time) : track.Position,
                    track.Fresh(World.Time) ? "적 프로브" : "프로브 · 신호 소실", true, !track.Fresh(World.Time), false) { Key=track.Target };
    }

    public void SelectMapContact(ShipView? view)
    {
        if (view is null) return;
        if (view.Body.Faction == Controlled?.Body.Faction) SelectedFriendly = view;
        else if (Known(view)) SelectEnemy(view);
    }
    public void OpenMapWingMenu(ShipView? view)
    {
        SelectMapContact(view);
        CloseWorldMap();
        OpenSquadMenu();
    }
}

public partial class Hud
{
    private void DrawInterceptorStatus(ShipView controlled, Vector2 screen, float squadTop)
    {
        var wing = Game.CommandWing;
        if (wing is null && controlled.Body.Definition.ReconProbes is null) return;
        var ship = controlled.Body.Definition.ReconProbes is not null ? controlled.Body : wing?.Leader ?? controlled.Body;
        var equipment = ship.Ordnance.Interceptor;
        // Keep probe/mission detail beside the radar, reserving the strip beneath it for combat ribbons.
        var at = new Vector2(284,116);
        InterceptorStatusBounds = new Rect2(at.X, at.Y - HudFont(14) - 2, 250, HudFont(14) + 72 + HudFont(12));
        Label(at, wing is null ? "정찰 프로브" : $"{wing.Name} · {InterceptorMissions.Label(wing.Mission)} · {(wing.OrderedMission is null ? "자동" : "명령")}", 14, Friendly);
        if (wing is not null)
        {
            Label(at + new Vector2(0, 23), $"생존 {wing.ActiveMembers.Count()}/{wing.Members.Count} · 미사일 요격 {wing.Members.Sum(s => s.Ordnance.Interceptor.MissileKills)}", 12, Text);
            if (wing.Mission == InterceptorMission.Assault)
                Label(at + new Vector2(0, 45), wing.AssaultAvailable ? "강습 기회 · 호위망 틈 발견" : "강습 위험 · 호위망 유지", 12, wing.AssaultAvailable ? Good : Motion);
            else if (wing.Mission == InterceptorMission.AirDefense)
                Label(at + new Vector2(0, 45), $"보호 대상 {wing.ProtectedShip?.Callsign ?? "—"}", 12, Dim);
        }
        var probes = Game.World.Probes.Where(p => p.Owner == ship).ToArray();
        Label(at + new Vector2(0, 68), $"프로브 {equipment.Probes}/{ship.Definition.ReconProbes?.Count ?? 0} · 관측 {probes.Count(p => p.Observing > 0)} · N 임무", 12, Text);
        foreach (var (probe, index) in probes.Select((p, i) => (p, i)))
            Label(at + new Vector2(0, 88 + index * 18), $"P{probe.Id}  {(probe.Position - ship.Position).Length() / 1000:0.0}km · {probe.Remaining:0}s · {(probe.Observing > 0 ? "관측 중" : probe.OnStation ? "전개" : "이동")}", 11, Dim);
    }

    private void DrawSmallTarget(Camera3D camera)
    {
        if (Game.SmallAimTarget is not { } target) return;
        bool fresh = target.Fresh(Game.World.Time), selected = Game.SelectedSmallContact is not null;
        Vector3 position = ((fresh ? target.Predict(Game.World.Time) : target.Position) - Game.RenderOrigin).ToVector3();
        bool missileReady = Game.SelectedWeapon == PlayerWeapon.Missile && Game.SmallAimSolved
            && Game.Controlled?.Body.Ordnance.MissileReady == true;
        Color color = !fresh ? ContactMemory.LostColor : Game.CorrectingSmallAim || missileReady ? Lead : Motion;
        Vector2 size = GetViewportRect().Size;
        Vector2 p = camera.UnprojectPosition(position);
        bool behind = camera.IsPositionBehind(position);
        if (behind || !new Rect2(Vector2.One * 24, size - Vector2.One * 48).HasPoint(p))
        {
            if (!selected) return;
            Vector2 delta = p - size * .5f;
            if (behind) delta = -delta;
            if (delta.LengthSquared() < 1) delta = Vector2.Down;
            delta = delta.Normalized();
            float scale = Mathf.Min((size.X * .5f - 110) / Mathf.Max(.001f, Mathf.Abs(delta.X)),
                (size.Y * .5f - 100) / Mathf.Max(.001f, Mathf.Abs(delta.Y)));
            p = size * .5f + delta * scale;
        }
        if (selected) p = SelectionMarkerPosition;
        else DrawDiamond(p, 7, color);
        string kind = target.Kind == SmallContactKind.Missile ? "적 미사일" : "적 프로브";
        double range = Game.Controlled is { } me ? (target.Predict(Game.World.Time) - me.Body.Position).Length() / 1000 : 0;
        if (!selected) CenteredLabel(p + new Vector2(0, -22), $"{kind} {range:0.0}km", 12, color);
        CenteredLabel(p + new Vector2(0, 30), Game.SmallAimStatus, 12, color);
        if (Game.SmallApproachSeconds is { } seconds)
            CenteredLabel(p + new Vector2(0, 48), $"최근접 {seconds:0.0}초", 12, seconds < 2 ? Hostile : Motion);
        if (!fresh || !Game.SmallAimSolved || behind || Game.SelectedWeapon != PlayerWeapon.MainGun) return;
        Vector3 lead = (Game.SmallAimPoint - Game.RenderOrigin).ToVector3();
        if (camera.IsPositionBehind(lead)) return;
        Vector2 a = camera.UnprojectPosition(lead);
        DrawLine(p, a, new Color(color, .4f), 1);
        DrawArc(a, 8, 0, Mathf.Tau, 24, color, 1.5f);
    }
}
