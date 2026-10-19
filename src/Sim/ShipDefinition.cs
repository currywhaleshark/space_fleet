using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Godot;

namespace SpaceFleet.Sim;

public enum ArmorSide { Fore, Aft, Port, Starboard, Dorsal, Ventral }
public enum ModuleKind { Sensor, Gun, Magazine, Generator, Reactor, Cooling, Thruster, ManeuverThruster, PowerBus, ShieldEmitter, AntimatterContainment, ShieldBooster, SensorBooster, EcmSuite, DroneBay }
public enum PowerGrid { Port, Starboard, Shared }

public sealed record ShieldDefinition(float Capacity, float RechargePerSecond, float RechargeDelay);
/// <summary>
/// 센서·전자전 데이터. Strength = 센서 세기, Signature = 기본 신호 크기(함선 크기), Jammer = ECM 방해 세기(2핍 기준).
/// 신호 = 관측 세기 × 표적 신호 ÷ 거리(km)²로 계산한다(SensorNet).
/// </summary>
public sealed record SensorDefinition(float Strength, float Signature, float Jammer);
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
    public DesignFamily Design { get; init; } = DesignFamily.Earth;
    public required HullKind Kind { get; init; }
    public required ShipBranch Branch { get; init; }
    public required string ModelId { get; init; }
    public required MatchWeightClass MatchClass { get; init; }
    public required SquadronDefinition Squadron { get; init; }
    public required ShipAiDefinition Ai { get; init; }
    public required BattleValueDefinition BattleValue { get; init; }
    public required ShipClass Flight { get; init; }
    public required ShieldDefinition Shield { get; init; }
    public required PowerDefinition Power { get; init; }
    public required SensorDefinition Sensors { get; init; }
    public required HullSection[] HullSections { get; init; }
    public required ModuleDefinition[] Modules { get; init; }
    public EquipmentBay[] Bays { get; init; } = Array.Empty<EquipmentBay>();
    public InstalledEquipment[] InstalledEquipment { get; init; } = Array.Empty<InstalledEquipment>();
    public float StructureHitPoints { get; init; } = 1000f;
    private RailgunDefinition? _legacyRailgun, _compiledRailgun;
    /// <summary>Legacy input and compiled compatibility view for firing, AI, HUD and model rigs.</summary>
    public RailgunDefinition? Railgun { get => _compiledRailgun ?? _legacyRailgun; init => _legacyRailgun = value; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MainBatteryDefinition? MainBattery { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShipComponentSelection? Components { get; init; }
    // Applied after research by ShipLoadouts; null selections mean the hull's factory weapon.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultMainGun { get; init; }
    /// <summary>Hull upgrades to the main gun container (§7); null = none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WeaponBudgetDefinition? WeaponBudget { get; init; }
    public MissileDefinition? Missiles { get; init; }
    public AntimatterDefinition? Antimatter { get; init; }
    public DefenseDroneDefinition? DefenseDrones { get; init; }
    public PointDefenseDefinition? PointDefense { get; init; }
    public ReconProbeDefinition? ReconProbes { get; init; }
    public DecoyDefinition? Decoys { get; init; }
    public DamageControlDefinition DamageControl { get; init; } = new();
    [JsonIgnore] public CollisionHull Hull { get; private set; } = null!;
    [JsonIgnore] public ShieldEnvelope ShieldEnvelope { get; private set; } = null!;
    [JsonIgnore] public MainBatteryDefinition? Battery { get; private set; }
    [JsonIgnore] public ResolvedWeaponMount[] PrimaryMounts { get; private set; } = Array.Empty<ResolvedWeaponMount>();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        IgnoreReadOnlyProperties = true,
        TypeInfoResolver = CreateResolver(strict: false),
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false), new VectorConverter() },
    };

    private static readonly JsonSerializerOptions StrictOptions = CreateStrictOptions();

    private static JsonSerializerOptions CreateStrictOptions()
    {
        return new(Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, TypeInfoResolver = CreateResolver(strict: true) };
    }

    private static DefaultJsonTypeInfoResolver CreateResolver(bool strict)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Kind != JsonTypeInfoKind.Object) return;
            if (info.Type == typeof(ShipDefinition))
                info.Properties.Single(p => p.Name == nameof(Railgun)).ShouldSerialize =
                    (owner, _) => ((ShipDefinition)owner).MainBattery is null;
            // Computed packets, transforms and collision caches are not editable fields.
            if (!strict) return;
            for (int i = info.Properties.Count - 1; i >= 0; i--)
                if (info.Properties[i].Set is null) info.Properties.RemoveAt(i);
        });
        return resolver;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);
    internal static string SerializeData<T>(T value) => JsonSerializer.Serialize(value, Options);
    internal static T ParseData<T>(string json) => JsonSerializer.Deserialize<T>(json, StrictOptions)
        ?? throw new InvalidDataException("Empty data definition");

    public static ShipDefinition Parse(string json) => Parse(json, strict: false);

    internal static ShipDefinition Parse(string json, bool strict)
    {
        ShipDefinition definition = JsonSerializer.Deserialize<ShipDefinition>(json, strict ? StrictOptions : Options)
            ?? throw new InvalidDataException("Empty ship definition");
        if (definition.MainBattery is { } battery)
        {
            if (definition._legacyRailgun is not null)
                throw new InvalidDataException("Author mainBattery or railgun, not both");
            MainBatteries.Validate(battery, definition);
            definition._compiledRailgun = MainBatteries.Compile(battery);
        }
        definition.Validate();
        definition.Battery = definition.MainBattery ?? MainBatteries.FromLegacy(definition);
        definition.PrimaryMounts = MainBatteries.Bind(definition.Battery, definition.Railgun);
        definition.Hull = new CollisionHull(definition.HullSections.Select(s => new HullBox(s.Center, s.HalfSize)).ToArray());
        definition.ShieldEnvelope = new ShieldEnvelope(definition.HullSections, definition.Flight.Length);
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
        ShipRoleValidation.Validate(this);
        Require(DefaultMainGun is null or "main-laser", "invalid factory primary");
        Require(WeaponBudget is null || float.IsFinite(WeaponBudget.VolumeBonus) && WeaponBudget.VolumeBonus is >= 0 and <= 20
            && float.IsFinite(WeaponBudget.SupplyScale) && WeaponBudget.SupplyScale is >= 1 and <= 1.5f
            && (WeaponBudget.EnlargedSockets ?? Array.Empty<string>()).All(id => !string.IsNullOrWhiteSpace(id))
            && (WeaponBudget.EnlargedSockets ?? Array.Empty<string>()).Distinct().Count() == (WeaponBudget.EnlargedSockets?.Length ?? 0),
            "invalid weapon budget");
        Require(Positive(StructureHitPoints), "invalid structure hit points");
        Require(DamageControl is not null && DamageControl.Kits >= 0 && DamageControl.Kits <= 32
            && Positive(DamageControl.RepairSeconds) && Positive(DamageControl.RestoreFraction)
            && DamageControl.RestoreFraction <= .5f && Positive(DamageControl.QuietSeconds), "invalid damage control");
        Require(Flight is not null && Shield is not null && Power is not null && Sensors is not null && HullSections is not null && Modules is not null, "null definition fields");
        Require(Positive(Sensors.Strength) && Positive(Sensors.Signature) && float.IsFinite(Sensors.Jammer) && Sensors.Jammer >= 0, "invalid sensors");
        Require(Positive(Power.OutputMw) && Positive(Power.Engines) && Positive(Power.Shields) && Positive(Power.Weapons)
            && Positive(Power.Sensors) && Positive(Power.Ecm) && Positive(Power.HeatCapacityMj) && float.IsFinite(Power.CoolingMw) && Power.CoolingMw >= 0
            && Power.Engines + Power.Shields + Power.Weapons + Power.Sensors <= Power.OutputMw, "invalid power (balanced draw must fit rated output)");
        Require(!string.IsNullOrWhiteSpace(Id) && Enum.IsDefined(Design) && Enum.IsDefined(Kind) && Flight.Kind == Kind, "design/kind/flight mismatch");
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
                && (m.CriticalChance == 0 || m.Kind is ModuleKind.Magazine or ModuleKind.Reactor or ModuleKind.DroneBay)
                && (m.Feeds is null || m.Feeds.All(g => Enum.IsDefined(g)))
                && HullSections.Any(s => Contains(s, m.Center, m.HalfSize)), $"invalid/outside module {m.Id}");
        }
        var indices = Modules.Where(m => m.VisualEngineIndex >= 0).Select(m => m.VisualEngineIndex).ToArray();
        Require(indices.Distinct().Count() == indices.Length, "duplicate visual engine index");
        if (Railgun is RailgunDefinition gun)
        {
            Require(Modules.Any(m => m.Id == gun.ModuleId && m.Kind == ModuleKind.Gun), "railgun must reference a gun module");
            Require(gun.Muzzle.IsFinite() && Positive(gun.MuzzleSpeed) && Positive(gun.ReloadSeconds)
                && Positive(gun.MaxRange) && (MainBattery?.Weapon.System == PrimaryWeaponSystem.Laser ? gun.Rounds == 0 : gun.Rounds > 0) && float.IsFinite(gun.TraverseDegrees) && gun.TraverseDegrees >= 0 && gun.TraverseDegrees <= 180
                && float.IsFinite(gun.SensorErrorMeters) && gun.SensorErrorMeters >= 0
                && float.IsFinite(gun.SensorErrorPerKm) && gun.SensorErrorPerKm >= 0
                && float.IsFinite(gun.VelocityError) && gun.VelocityError >= 0
                && float.IsFinite(gun.ShotHeatMj) && gun.ShotHeatMj >= 0
                && Positive(gun.AimErrorScale), "invalid railgun parameters");
            gun.Packet.Validate();
            if (gun.Mounts is { } mounts)
            {
                Require(mounts.Length > 0 && mounts.All(m => m is not null), "empty/null turret mounts");
                Require(mounts.Select(m => m.ModuleId).Distinct().Count() == mounts.Length, "duplicate turret module");
                Require(mounts[0].ModuleId == gun.ModuleId, "first turret must be the primary gun");
                foreach (TurretDefinition m in mounts)
                {
                    Require(Modules.Any(module => module.Id == m.ModuleId && module.Kind == ModuleKind.Gun), "turret must reference a gun module");
                    Require(m.Pivot.IsFinite() && m.Trunnion.IsFinite() && m.Muzzles is { Length: > 0 }
                        && m.Muzzles.All(p => p.IsFinite() && p.Z < 0), "invalid turret geometry");
                    Require(m.HousingCenter.IsFinite() && m.HousingHalfSize.IsFinite()
                        && (m.HousingHalfSize == Vector3.Zero || m.HousingHalfSize.X > 0 && m.HousingHalfSize.Y > 0 && m.HousingHalfSize.Z > 0),
                        "invalid turret obstruction bounds");
                    Require(Positive(m.YawDegrees) && m.YawDegrees <= 180 && float.IsFinite(m.MinElevation)
                        && float.IsFinite(m.MaxElevation) && m.MinElevation >= -90 && m.MinElevation <= 0
                        && m.MaxElevation > 0 && m.MaxElevation <= 90 && Positive(m.YawRate) && Positive(m.ElevationRate)
                        && Positive(m.ToleranceDegrees) && m.ToleranceDegrees <= 1
                        && (MainBattery?.Weapon.System == PrimaryWeaponSystem.Laser ? m.Rounds == 0 : m.Rounds > 0), "invalid turret drive/ammunition");
                }
                Require(mounts.Sum(m => m.Rounds) == gun.Rounds, "turret ammunition must equal ship magazine loadout");
            }
        }
        if (Antimatter is { } am)
        {
            Require(Kind == HullKind.Interceptor && Modules.Any(m => m.Id == am.ModuleId && m.Kind == ModuleKind.AntimatterContainment), "AM requires interceptor containment module");
            Require(am.Rounds > 0 && Positive(am.ArmingSeconds) && Positive(am.RecommendedMinMeters)
                && Positive(am.RecommendedMaxMeters) && Positive(am.MaxTravelMeters)
                && am.RecommendedMaxMeters >= am.RecommendedMinMeters && am.MaxTravelMeters > am.RecommendedMaxMeters
                && Positive(am.TerminalAccelG) && Positive(am.LaunchConeDegrees) && am.LaunchConeDegrees <= 30
                && Positive(am.DamageDepthMeters) && Positive(am.StructuralDamageMultiplier)
                && am.SafeFailureHealth > 0 && am.SafeFailureHealth < am.ArmedFailureHealth
                && am.ArmedFailureHealth < am.WarningHealth && am.WarningHealth <= 1 && am.Flight is not null,
                "invalid AM configuration");
            ValidateMissile(am.Flight!);
            Require(float.IsFinite(am.Flight!.ModuleDamage * am.StructuralDamageMultiplier), "invalid AM structural damage");
            Require(am.Flight!.Rounds == am.Rounds && am.Flight.MaxFlightSeconds <= 8 && am.Flight.SeekerRangeMeters <= am.MaxTravelMeters
                && am.TerminalAccelG <= am.Flight.AccelG, "invalid AM lifetime/guidance");
        }
        if (ReconProbes is { } probe)
            Require(Kind == HullKind.Interceptor && Enum.IsDefined(probe.Kind) && probe.Count > 0 && probe.Count <= 8
                && Positive(probe.LifetimeSeconds) && Positive(probe.DeploymentMeters) && Positive(probe.Acceleration)
                && Positive(probe.BurnSeconds) && Positive(probe.SensorStrength) && Positive(probe.Signature)
                && Positive(probe.HitPoints) && Positive(probe.Radius) && Positive(probe.CooldownSeconds)
                && (probe.LaunchPoints is null || probe.LaunchPoints.Length > 0 && probe.LaunchPoints.All(p => p.IsFinite())), "invalid recon probe");
        if (Missiles is MissileDefinition ms) ValidateMissile(ms);
        void ValidateMissile(MissileDefinition ms)
        {
            Require(ms.Rounds > 0 && Positive(ms.ReloadSeconds) && ms.LaunchPoint.IsFinite() && float.IsFinite(ms.EjectSpeed) && ms.EjectSpeed >= 0
                && Positive(ms.AccelG) && Positive(ms.BurnSeconds) && Positive(ms.MaxFlightSeconds) && Positive(ms.SeekerRangeMeters)
                && Positive(ms.SeekerFovDegrees) && ms.SeekerFovDegrees <= 180 && Positive(ms.SeekerStrength) && Positive(ms.FuzeMeters)
                && Positive(ms.HitPoints) && float.IsFinite(ms.LaunchHeatMj) && ms.LaunchHeatMj >= 0
                && Positive(ms.TurnRateDegrees) && ms.TurnRateDegrees <= (ms.CanInterceptMissiles ? 360 : 180)
                && Positive(ms.MaxSteeringAngleDegrees) && ms.MaxSteeringAngleDegrees<=90
                && float.IsFinite(ms.ThrustGimbalDegrees) && ms.ThrustGimbalDegrees>=0 && ms.ThrustGimbalDegrees<=45, "invalid missiles");
            ms.Packet.Validate();
            Require(Positive(ms.BodyScale) && ms.BodyScale <= 2, "invalid missile scale");
            Require(float.IsFinite(ms.LaunchSignatureFraction) && ms.LaunchSignatureFraction >= 0 && ms.LaunchSignatureFraction <= 10
                && float.IsFinite(ms.LaunchSignatureSeconds) && ms.LaunchSignatureSeconds >= 0
                && (ms.LaunchSignatureFraction == 0 || ms.LaunchSignatureSeconds > 0), "invalid missile launch signature");
            if (ms.CanInterceptMissiles)
                Require((Kind == HullKind.Interceptor || Branch == ShipBranch.AirDefenseCruiser) && Positive(ms.InterceptRangeMeters)
                    && Positive(ms.InterceptFuzeMeters) && ms.InterceptFuzeMeters <= 100 && Positive(ms.InterceptDamage), "invalid counter-missile equipment");
        }
        if (PointDefense is PointDefenseDefinition pd)
            Require(pd.Mounts is { Length: > 0 } && pd.Mounts.All(v => v.IsFinite()) && Positive(pd.RangeMeters) && Positive(pd.ShotsPerSecond)
                && pd.HitChance > 0 && pd.HitChance <= 1 && Positive(pd.DamagePerHit)
                && (pd.MissileDamagePerHit is null || Positive(pd.MissileDamagePerHit.Value))
                && (pd.Normals is null || pd.Normals.Length == pd.Mounts.Length && pd.Normals.All(n => n.IsFinite() && n.LengthSquared() > 1e-6f))
                && Positive(pd.ArcDegrees) && pd.ArcDegrees <= 180
                && Positive(pd.YawDegrees) && pd.YawDegrees <= 180
                && float.IsFinite(pd.MinElevation) && pd.MinElevation >= -90 && pd.MinElevation <= 0
                && Positive(pd.MaxElevation) && pd.MaxElevation <= 90
                && Positive(pd.YawRate) && Positive(pd.ElevationRate)
                && Positive(pd.ToleranceDegrees) && pd.ToleranceDegrees <= 5
                && Positive(pd.MuzzleSpeed) && Positive(pd.TrackingErrorMeters), "invalid point defense");
        if (PointDefense?.ShipPacket is { } shipPacket)
        {
            try{shipPacket.Validate();}catch(ArgumentException e){throw new InvalidDataException("invalid PD ship packet",e);}
            Require(shipPacket.Range>=PointDefense.RangeMeters,"PD ship packet must reach its engagement range");
        }
        if (Decoys is DecoyDefinition dc)
            Require(dc.Count > 0 && dc.PerLaunch > 0 && Positive(dc.CooldownSeconds) && Positive(dc.SignatureFactor)
                && Positive(dc.LifetimeSeconds) && float.IsFinite(dc.EjectSpeed) && dc.EjectSpeed >= 0, "invalid decoys");
        if (DefenseDrones is { } drones)
            Require(drones.Count > 0 && drones.Count <= 64 && Positive(drones.OrbitMeters) && Positive(drones.RangeMeters)
                && Positive(drones.ShotsPerSecond) && Positive(drones.HitChance) && drones.HitChance <= 1
                && Positive(drones.DamagePerHit) && drones.RoundsPerDrone > 0 && Positive(drones.RepositionSpeed)
                && Positive(drones.HitPoints) && Positive(drones.RadiusMeters) && Positive(drones.ShipEnergy)
                && Positive(drones.ShipPenetrationMm) && Positive(drones.ShipModuleDamage), "invalid defense drones");
        EquipmentRules.Validate(this);
        ShipComponents.ValidateSelection(this, Components);
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
    private static readonly Dictionary<string, string> Sources = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ShipDefinition> Definitions = Load();
    public static IReadOnlyCollection<ShipDefinition> All => Definitions.Values;
    public static ShipDefinition ById(string id) => Definitions.TryGetValue(id, out var definition)
        ? definition : ShipVariants.TryGet(id, out var variant) ? variant.Definition
        : throw new ArgumentException($"Unknown ship ID: {id}", nameof(id));
    public static string SourceJson(string id) => Sources.TryGetValue(id, out var json)
        ? json : throw new ArgumentException($"Unknown ship ID: {id}", nameof(id));
    public static ShipDefinition For(HullKind kind, DesignFamily design = DesignFamily.Earth) =>
        Definitions.Values.Single(d => d.Kind == kind && d.Design == design);
    public static ShipDefinition For(ShipClass flight) => Definitions.Values.FirstOrDefault(d => ReferenceEquals(d.Flight, flight))
        ?? For(flight.Kind);

    private static Dictionary<string, ShipDefinition> Load()
    {
        var result = new Dictionary<string, ShipDefinition>(StringComparer.Ordinal);
        const string prefix = "SpaceFleet.data.ships.";
        foreach (string resource in typeof(ShipDefinitions).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
        {
            using Stream stream = typeof(ShipDefinitions).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidDataException($"Missing ship definition: {resource}");
            using var reader = new StreamReader(stream);
            string json = reader.ReadToEnd();
            ShipDefinition definition = ShipDefinition.Parse(json);
            if (resource != prefix + definition.Id + ".json") throw new InvalidDataException($"Ship ID/file mismatch: {resource}");
            result.Add(definition.Id, definition);
            Sources.Add(definition.Id, json);
        }
        foreach (var definition in result.Values)
            ShipRoleValidation.ValidateReferences(definition, id => result.TryGetValue(id, out var member)
                ? member : throw new ArgumentException($"Unknown ship ID: {id}"));
        return result;
    }
}
