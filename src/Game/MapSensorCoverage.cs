using System;
using System.Collections.Generic;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

public readonly record struct SensorVolume(Vec3d Position, float Strength, Vector3 Direction,
    ReconProbeDefinition? Probe, bool Emphasized)
{
    public double Radius(Vector3 bearing) => SensorNet.NominalContactRange(Probe is null ? Strength
        : SensorNet.ProbeDirectionalStrength(Probe, Direction.Dot(bearing)));
}

public readonly record struct ProbePreview(Vec3d Requested, Vec3d Destination, Vector3 Direction,
    ReconProbeDefinition Definition, bool Ready, string Status)
{
    public bool Clamped => (Requested - Destination).Length() > 1;
}

public partial class WorldMapOverlay
{
    // Only friendly equipment contributes range overlays, even when an enemy is identified.
    internal IEnumerable<SensorVolume> SensorVolumes()
    {
        var faction = Game.Controlled?.Body.Faction ?? Faction.Blue;
        foreach (var ship in Game.World.Ships)
            if (ShowSensorCoverage && ship.Faction == faction && SensorNet.Strength(ship) > .01f)
                yield return new(ship.Position, SensorNet.Strength(ship), ship.Forward, null,
                    ship == Game.Controlled?.Body || ship == _selected?.Body);
        foreach (var probe in Game.World.Probes)
            if (probe.Faction == faction && probe.Active)
                yield return new(probe.Position, probe.Definition.SensorStrength, probe.Direction, probe.Definition, true);
    }

    /// <summary>
    /// Probe placement on the map:
    /// - Shift+click launches at the cursor, level with the controlled ship.
    /// - Shift+drag keeps that ground point and raises or lowers it; releasing arms the placement.
    /// - Any left click then launches it; right click or Esc cancels.
    /// </summary>
    private enum ProbeGesture { None, Pressing, Adjusting, Armed }
    private ProbeGesture _probeGesture;
    private Vec3d _probeGround;
    private double _probeHeight;
    internal bool ProbeArmed => _probeGesture == ProbeGesture.Armed;
    internal double ProbeHeight => _probeHeight;
    private Vec3d ProbeRequested => _probeGround + new Vec3d(0, _probeHeight, 0);

    internal ProbePreview? ActiveProbePreview => _probeGesture != ProbeGesture.None ? PreviewFor(ProbeRequested)
        : _shiftHeld && _drag == MouseButton.None ? PreviewAt(_pointer) : null;

    /// <summary>The cursor's point on the plane level with the controlled ship.</summary>
    private Vec3d? LevelPoint(Vector2 screen)
        => Game.Controlled?.Body is { } ship && new Rect2(_surface.Position, _surface.Size).HasPoint(screen)
            ? Map.ScreenOnLevel(Normalized(screen), ship.Position.Y) : null;

    internal ProbePreview? PreviewAt(Vector2 screen) => LevelPoint(screen) is { } point ? PreviewFor(point) : null;

    private ProbePreview? PreviewFor(Vec3d requested)
    {
        if (Game.Controlled?.Body is not { Definition.ReconProbes: { } definition } ship) return null;
        if (!SimWorld.TryProbeDestination(ship, requested, out var destination,Game.World.Obstacles)) return null;
        string? problem = !Squadron.Active(ship) || ship.Power.Supply <= .01f ? "사출 계통 정지"
            : ship.Ordnance.Interceptor.Probes <= 0 ? "프로브 소진"
            : Game.World.Time < ship.Ordnance.Interceptor.NextLaunch ? "사출 준비 중" : null;
        string status = problem ?? _probeGesture switch
        {
            ProbeGesture.Adjusting => "놓은 뒤 클릭하면 사출",
            ProbeGesture.Armed => "클릭하면 사출 · 우클릭/Esc 취소",
            _ => "Shift+클릭 사출 · 드래그로 높이",
        };
        return new(requested, destination, (destination - ship.Position).ToVector3().Normalized(), definition, problem is null, status);
    }

    /// <summary>Handles probe placement input; true when the event belonged to it.</summary>
    private bool HandleProbeInput(InputEvent e)
    {
        if (_probeGesture != ProbeGesture.None && (e.IsActionPressed(InputSetup.ReleaseMouse)
            || e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }))
        { CancelProbe(); return true; }
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (button.Pressed && _probeGesture == ProbeGesture.Armed) { LaunchProbe(); return true; }
            if (button.Pressed && button.ShiftPressed && LevelPoint(button.Position) is { } ground)
            {
                _probeGesture = ProbeGesture.Pressing; _probeGround = ground; _probeHeight = 0;
                _press = button.Position; _drag = MouseButton.None; return true;
            }
            if (!button.Pressed && _probeGesture == ProbeGesture.Pressing) { LaunchProbe(); return true; }
            if (!button.Pressed && _probeGesture == ProbeGesture.Adjusting) { _probeGesture = ProbeGesture.Armed; return true; }
        }
        if (e is InputEventMouseMotion motion && _probeGesture is ProbeGesture.Pressing or ProbeGesture.Adjusting)
        {
            if (_probeGesture == ProbeGesture.Pressing && motion.Position.DistanceTo(_press) > 4) _probeGesture = ProbeGesture.Adjusting;
            if (_probeGesture == ProbeGesture.Adjusting) _probeHeight = (_press.Y - motion.Position.Y) * Map.HeightPerPixel(Radius);
            return true;
        }
        return false;
    }
    private void LaunchProbe() { var requested = ProbeRequested; CancelProbe(); Game.LaunchPlayerProbeAt(requested); }
    private void CancelProbe() { _probeGesture = ProbeGesture.None; _probeHeight = 0; }

    private const int MaxSensorVolumes = 64;
    private readonly Vector4[] _coverageVolumes = new Vector4[MaxSensorVolumes];
    private readonly Vector4[] _coverageDirections = new Vector4[MaxSensorVolumes];
    private ShaderMaterial _coverageMaterial = null!;
    private ColorRect _coverage = null!;
    private WorldMapGlobe _globe = null!;

    private void CreateCoverageLayers()
    {
        _globe = new WorldMapGlobe { OwnerMap = this, MouseFilter = MouseFilterEnum.Ignore, ClipContents = true };
        _coverageMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sensor_coverage.gdshader") };
        _coverage = new ColorRect { Color = Colors.White, Material = _coverageMaterial, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_globe); AddChild(_coverage);
    }

    private void UpdateCoverageLayer()
    {
        _globe.Position = _coverage.Position = _surface.Position;
        _globe.Size = _coverage.Size = _surface.Size;
        _globe.QueueRedraw();
        int count = 0;
        void AddVolume(SensorVolume volume, float kind)
        {
            if (count >= MaxSensorVolumes) return;
            Vector3 center = Map.Project(volume.Position);
            float radius = (float)(SensorNet.NominalContactRange(volume.Strength) / Map.HalfSpan);
            if (center.Length() > 1 + radius || radius <= 0) return;
            _coverageVolumes[count] = new(center.X, center.Y, center.Z, radius);
            Vector3 direction = Map.ProjectUnit(volume.Direction);
            _coverageDirections[count++] = new(direction.X, direction.Y, direction.Z, kind);
        }
        foreach (var volume in SensorVolumes()) AddVolume(volume, volume.Probe is null ? 0 : 1);
        if (ActiveProbePreview is { } preview)
            AddVolume(new(preview.Destination, preview.Definition.SensorStrength, preview.Direction, preview.Definition, true), preview.Ready ? 2 : 3);
        _coverageMaterial.SetShaderParameter("volumes", _coverageVolumes);
        _coverageMaterial.SetShaderParameter("directions", _coverageDirections);
        _coverageMaterial.SetShaderParameter("volume_count", count);
        _coverageMaterial.SetShaderParameter("map_scale", _surface.Size / Radius);
    }

    private void DrawSensorCoverage(Control canvas, Func<Vec3d, Vector2> at)
    {
        float x = canvas.Size.X - 245;
        Text(canvas, new(x, canvas.Size.Y - 65), "통합 탐지영역 · 청색 함선 / 녹색 프로브", 12, Blue);
        Text(canvas, new(x, canvas.Size.Y - 45), "접촉 기준 · 표준 신호 / 전자전 없음", 11, new Color(Ink, .65f));
        Text(canvas, new(x, canvas.Size.Y - 25), "표적 신호·전자전에 따라 실제 탐지 변동", 11, new Color(Ink, .55f));
    }

    private void DrawProbePreview(Control canvas, Func<Vec3d, Vector2> at)
    {
        if (_probeGesture == ProbeGesture.None && (!_shiftHeld || _drag != MouseButton.None)) return;
        if (ActiveProbePreview is not { } preview)
        {
            Text(canvas, new(18, 25), "프로브 투사: 요격함을 조종하고 지도 위에서 위치 지정", 13, Amber);
            return;
        }
        var ship = Game.Controlled!.Body;
        var color = preview.Ready ? Amber : Red;

        Vector2 p = at(preview.Destination);
        canvas.DrawDashedLine(at(ship.Position), p, new Color(color, .7f), 1, 8);
        canvas.DrawArc(p, 10, 0, Mathf.Tau, 24, color, 2, true);
        canvas.DrawLine(p - Vector2.Right * 15, p + Vector2.Right * 15, color, 1);
        canvas.DrawLine(p - Vector2.Down * 15, p + Vector2.Down * 15, color, 1);
        if (preview.Clamped)
            canvas.DrawDashedLine(p, at(preview.Requested), new Color(color, .35f), 1, 5);
        // Height stalk to the plane level with the ship, like the contact drop lines.
        double rise = preview.Destination.Y - ship.Position.Y;
        Vector2 foot = at(new Vec3d(preview.Destination.X, ship.Position.Y, preview.Destination.Z));
        if (foot.DistanceTo(p) > 3)
        { canvas.DrawLine(foot, p, new Color(color, .8f), 2, true); canvas.DrawArc(foot, 5, 0, Mathf.Tau, 16, new Color(color, .6f), 1, true); }
        double distance = (preview.Destination - ship.Position).Length() / 1000;
        string limited = preview.Clamped ? " · 투사거리/지형 제한" : "";
        string height = Math.Abs(rise) < 50 ? "자함 높이" : $"높이 {(rise > 0 ? "+" : "−")}{Math.Abs(rise) / 1000:0.0}km";
        Text(canvas, new(18, 25), $"프로브 전개 예정 {distance:0.0}km · {height}{limited} · {preview.Status}", 13, color);
        Text(canvas, new(18, 46), $"전방 접촉 반경 {SensorNet.NominalContactRange(preview.Definition.SensorStrength) / 1000:0.0}km · 이동 후 이 위치에서 관측", 12, color);
        if (Game.World.Time - Game.LastFireTime < 2)
            Text(canvas, new(18, 67), Game.LastFireMessage, 12, Game.LastFireFailed ? Red : Blue);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut) { _shiftHeld = false; _drag = MouseButton.None; }
    }
}

public partial class WorldMapGlobe : Control
{
    public WorldMapOverlay OwnerMap { get; set; } = null!;
    public override void _Draw() => OwnerMap.DrawGlobe(this, Size * .5f);
}
