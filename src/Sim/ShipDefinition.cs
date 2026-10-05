using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace SpaceFleet.Sim;

public enum ArmorSide { Fore, Aft, Port, Starboard, Dorsal, Ventral }
public enum ModuleKind { Sensor, Gun, Magazine, Generator, Reactor, Cooling, Thruster, ManeuverThruster, PowerBus, ShieldEmitter }
public enum PowerGrid { Port, Starboard, Shared }

public sealed record ShieldDefinition(float Capacity, float RechargePerSecond, float RechargeDelay);
public sealed record ArmorPlate(float ThicknessMm, float SlopeDegrees = 0f);
public sealed record HullSection(string Id, string Name, Vector3 Center, Vector3 HalfSize,
    ArmorPlate Armor, Dictionary<ArmorSide, ArmorPlate>? Faces = null)
{
    public ArmorPlate Plate(ArmorSide side) => Faces is not null && Faces.TryGetValue(side, out ArmorPlate? plate) ? plate : Armor;
}
public sealed record ModuleDefinition(string Id, string Name, ModuleKind Kind, Vector3 Center, Vector3 HalfSize,
    float HitPoints, float ResistanceMm, PowerGrid Grid = PowerGrid.Shared, float Capacity = 1f,
    PowerGrid[]? Feeds = null, int VisualEngineIndex = -1, float CriticalChance = 0f, float CriticalEnergy = 300f);

/// <summary>모델 교체 시 함께 조정하는 함선 데이터. 위치·크기는 로컬 좌표(m), 장갑은 게임 기준 등가 mm.</summary>
public sealed class ShipDefinition
{
    public required string Id { get; init; }
    public required HullKind Kind { get; init; }
    public required ShipClass Flight { get; init; }
    public required ShieldDefinition Shield { get; init; }
    public required PowerDefinition Power { get; init; }
    public required HullSection[] HullSections { get; init; }
    public required ModuleDefinition[] Modules { get; init; }
    public RailgunDefinition? Railgun { get; init; }
    public CollisionHull Hull { get; private set; } = null!;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false), new VectorConverter() },
    };

    public static ShipDefinition Parse(string json)
    {
        ShipDefinition definition = JsonSerializer.Deserialize<ShipDefinition>(json, Options)
            ?? throw new InvalidDataException("Empty ship definition");
        definition.Validate();
        definition.Hull = new CollisionHull(definition.HullSections.Select(s => new HullBox(s.Center, s.HalfSize)).ToArray());
        return definition;
    }

    private void Validate()
    {
        void Require([DoesNotReturnIf(false)] bool condition, string message)
        {
            if (!condition) throw new InvalidDataException($"{Id}: {message}");
        }
        bool Positive(float value) => float.IsFinite(value) && value > 0f;
        bool Size(Vector3 value) => value.IsFinite() && value.X > 0 && value.Y > 0 && value.Z > 0;
        bool Plate(ArmorPlate? p) => p is not null && float.IsFinite(p.ThicknessMm) && p.ThicknessMm >= 0
            && float.IsFinite(p.SlopeDegrees) && p.SlopeDegrees >= 0 && p.SlopeDegrees < 85;
        Require(Flight is not null && Shield is not null && Power is not null && HullSections is not null && Modules is not null, "null definition fields");
        Require(Positive(Power.OutputMw) && Positive(Power.Engines) && Positive(Power.Shields) && Positive(Power.Weapons)
            && Positive(Power.Sensors) && Positive(Power.HeatCapacityMj) && float.IsFinite(Power.CoolingMw) && Power.CoolingMw >= 0
            && Power.Engines + Power.Shields + Power.Weapons + Power.Sensors <= Power.OutputMw, "invalid power (balanced draw must fit rated output)");
        Require(!string.IsNullOrWhiteSpace(Id) && Enum.IsDefined(Kind) && Flight.Kind == Kind, "kind/flight mismatch");
        Require(Positive(Flight.MassKg) && Positive(Flight.Length) && Positive(Flight.ForwardAccel)
            && Positive(Flight.StrafeAccel) && Positive(Flight.BrakeAccel) && Positive(Flight.MaxAccelG)
            && Positive(Flight.MaxSpeed) && Positive(Flight.BoostMultiplier) && Positive(Flight.PitchYawRateDeg)
            && Positive(Flight.PitchYawAccelDeg) && Positive(Flight.RollRateDeg) && Positive(Flight.RollAccelDeg)
            && Positive(Flight.CameraDistance) && float.IsFinite(Flight.CameraHeight), "invalid flight parameters");
        Require(float.IsFinite(Shield.Capacity) && Shield.Capacity >= 0 && float.IsFinite(Shield.RechargePerSecond)
            && Shield.RechargePerSecond >= 0 && float.IsFinite(Shield.RechargeDelay) && Shield.RechargeDelay >= 0, "invalid shield");
        Require(HullSections.Length > 0 && Modules.Length > 0, "hull/modules must not be empty");
        Require(HullSections.All(s => s is not null) && Modules.All(m => m is not null), "null hull/module entry");
        Require(HullSections.Select(s => s.Id).Distinct().Count() == HullSections.Length, "duplicate section id");
        Require(Modules.Select(m => m.Id).Distinct().Count() == Modules.Length, "duplicate module id");
        foreach (HullSection s in HullSections)
            Require(!string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.Name) && s.Center.IsFinite() && Size(s.HalfSize) && Plate(s.Armor)
                && (s.Faces is null || s.Faces.All(p => Enum.IsDefined(p.Key) && Plate(p.Value))), $"invalid hull section {s.Id}");
        foreach (ModuleDefinition m in Modules)
        {
            Require(!string.IsNullOrWhiteSpace(m.Id) && !string.IsNullOrWhiteSpace(m.Name) && m.Center.IsFinite() && Size(m.HalfSize) && Positive(m.HitPoints)
                && float.IsFinite(m.ResistanceMm) && m.ResistanceMm >= 0 && Enum.IsDefined(m.Kind) && Enum.IsDefined(m.Grid)
                && Positive(m.Capacity) && float.IsFinite(m.CriticalChance) && m.CriticalChance >= 0 && m.CriticalChance <= 1
                && float.IsFinite(m.CriticalEnergy) && m.CriticalEnergy >= 0
                && (m.VisualEngineIndex == -1 || m.VisualEngineIndex >= 0 && m.Kind == ModuleKind.Thruster)
                && (m.CriticalChance == 0 || m.Kind is ModuleKind.Magazine or ModuleKind.Reactor)
                && (m.Feeds is null || m.Feeds.All(g => Enum.IsDefined(g)))
                && HullSections.Any(s => Contains(s, m.Center, m.HalfSize)), $"invalid/outside module {m.Id}");
        }
        var indices = Modules.Where(m => m.VisualEngineIndex >= 0).Select(m => m.VisualEngineIndex).ToArray();
        Require(indices.Distinct().Count() == indices.Length, "duplicate visual engine index");
        if (Railgun is RailgunDefinition gun)
        {
            Require(Modules.Any(m => m.Id == gun.ModuleId && m.Kind == ModuleKind.Gun), "railgun must reference a gun module");
            Require(gun.Muzzle.IsFinite() && Positive(gun.MuzzleSpeed) && Positive(gun.ReloadSeconds)
                && Positive(gun.MaxRange) && gun.Rounds > 0 && Positive(gun.TraverseDegrees) && gun.TraverseDegrees <= 180
                && float.IsFinite(gun.SensorErrorMeters) && gun.SensorErrorMeters >= 0
                && float.IsFinite(gun.SensorErrorPerKm) && gun.SensorErrorPerKm >= 0
                && float.IsFinite(gun.VelocityError) && gun.VelocityError >= 0
                && float.IsFinite(gun.ShotHeatMj) && gun.ShotHeatMj >= 0, "invalid railgun parameters");
            gun.Packet.Validate();
        }
    }

    private static bool Contains(HullSection section, Vector3 center, Vector3 half) =>
        Mathf.Abs(center.X - section.Center.X) + half.X <= section.HalfSize.X + 0.001f &&
        Mathf.Abs(center.Y - section.Center.Y) + half.Y <= section.HalfSize.Y + 0.001f &&
        Mathf.Abs(center.Z - section.Center.Z) + half.Z <= section.HalfSize.Z + 0.001f;

    private sealed class VectorConverter : JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Vector must contain three numbers");
            reader.Read(); float x = reader.GetSingle(); reader.Read(); float y = reader.GetSingle();
            reader.Read(); float z = reader.GetSingle(); reader.Read();
            if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Vector must contain three numbers");
            return new Vector3(x, y, z);
        }
        public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray(); writer.WriteNumberValue(value.X); writer.WriteNumberValue(value.Y);
            writer.WriteNumberValue(value.Z); writer.WriteEndArray();
        }
    }
}

public static class ShipDefinitions
{
    private static readonly Dictionary<HullKind, ShipDefinition> Definitions = Load();
    public static ShipDefinition For(HullKind kind) => Definitions[kind];

    private static Dictionary<HullKind, ShipDefinition> Load()
    {
        var result = new Dictionary<HullKind, ShipDefinition>();
        foreach (string name in new[] { "battleship", "escort", "interceptor" })
        {
            using Stream stream = typeof(ShipDefinitions).Assembly.GetManifestResourceStream($"SpaceFleet.data.ships.{name}.json")
                ?? throw new InvalidDataException($"Missing ship definition: {name}");
            using var reader = new StreamReader(stream);
            ShipDefinition definition = ShipDefinition.Parse(reader.ReadToEnd());
            result.Add(definition.Kind, definition);
        }
        return result;
    }
}
