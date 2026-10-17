using System;
using Godot;

namespace SpaceFleet.Sim;

public sealed record FiringSolution(bool Valid, string Reason, Vector3 Direction, Vec3d AimPoint,
    double FlightTime, float ErrorMeters, double Range)
{
    public bool ViaDatalink { get; init; }
}

/// <summary>
/// 등속 선행 조준. 센서망의 관측 표본에 재현 가능한 사격통제 오차를 더한다.
/// 센서망 추적(track)을 넘기면 잠금 단계일 때만 해를 내고, ECM·신호 세기에 따라 오차가 커지거나 줄어든다.
/// </summary>
public static class FireControl
{
    public const double MaxObservationAge = SensorNet.UpdateInterval * 2;
    public const double PredictionGraceSeconds = 2;
    public const double PredictionErrorPerSecondSquared = 1.25;
    /// <param name="localAim">표적 로컬 좌표의 조준점(부위 조준). null이면 함선 중심.</param>
    public static FiringSolution Solve(ShipBody shooter, ShipBody target, double time, bool sensorError = true, SensorTrack? track = null,
        Vector3? localAim = null, RailgunState? weapon = null)
    {
        weapon ??= shooter.Railgun;
        if (weapon is null)
            return new(false, "주포 없음", Vector3.Zero, target.Position, 0, 0, (target.Position - shooter.Position).Length());
        Vec3d muzzle = weapon.MuzzlePosition;
        // No track is the local-only path used by isolated physics tools. Runtime callers supply the faction track.
        Vec3d position = track?.EstimatedPosition ?? target.Position;
        Vector3 velocity = track.HasValue ? Vector3.Zero : target.Velocity;
        Quaternion orientation = track.HasValue ? Quaternion.Identity : target.Orientation;
        float observationQuality = shooter.Damage.SensorFraction * shooter.Power.SensorEffect;
        bool linked = false;
        double range = (position - muzzle).Length();
        FiringSolution Invalid(string reason) => new(false, reason, Vector3.Zero, position, 0, 0, range);
        if (target == shooter || target.Damage.Destroyed) return Invalid("유효 표적 없음");
        if (shooter.Damage.Destroyed || shooter.Damage.GenerationFraction <= .001f || shooter.Power.SensorEffect <= .001f)
            return Invalid("사격통제 전원 상실");
        RailgunDefinition gun = weapon.Definition;
        float trackScale = 1f;
        if (track is SensorTrack t)
        {
            if (t.Level < TrackLevel.Locked)
                return Invalid(t.Level switch
                {
                    TrackLevel.None => "탐지 안 됨",
                    TrackLevel.Contact => "접촉만 · 식별 전",
                    _ => "식별 · 잠금 전",
                });
            trackScale = t.FireControlScale;
            observationQuality = 1f;
            if (t.Observation is { } sample)
            {
                double age = time - sample.Time;
                if (age < -1e-6 || age > MaxObservationAge) return Invalid("추적 갱신 대기");
                if (sample.Observer.Faction != shooter.Faction) return Invalid("아군 관측 정보 없음");
                position = sample.Position + Vec3d.From(sample.Velocity) * Math.Max(0, age);
                velocity = sample.Velocity;
                orientation = sample.Orientation;
                observationQuality = sample.Quality;
                linked = sample.Observer != shooter;
            }
        }
        else if (shooter.Damage.SensorFraction <= .02f) return Invalid("조준 센서 비활성 · 아군 잠금 필요");
        range = (position - muzzle).Length();
        if (range > gun.MaxRange) return Invalid("사거리 밖");
        // The observing ship supplies measurement quality. Receiver sensor destruction does not erase the link.
        // Signal strength and ECM still degrade accuracy; firing hardware/power are checked separately.
        float quality = Mathf.Max(0.05f, observationQuality) / trackScale;
        // A current lock cannot predict future course changes indefinitely. Long
        // flight adds uncertainty; short-range interception keeps its existing accuracy.
        double predictionSeconds = Math.Max(0, range / gun.MuzzleSpeed - PredictionGraceSeconds);
        float predictionError = (float)(PredictionErrorPerSecondSquared * predictionSeconds * predictionSeconds);
        // Weapon-specific uncertainty scales the observed aim, never a hit/miss roll.
        // A receiving ship keeps its own firing calibration when using an allied track.
        float errorScale = sensorError ? gun.AimErrorScale : 0;
        float error = (gun.SensorErrorMeters + gun.SensorErrorPerKm * (float)(range / 1000) + predictionError) / quality * errorScale;
        float velocityError = gun.VelocityError / quality * errorScale;
        uint seed = Hash(shooter.Callsign) ^ Hash(target.Callsign);
        Vec3d aimBase = localAim is Vector3 local ? position + Vec3d.From(orientation * local) : position;
        Vec3d observedPosition = aimBase + SmoothNoise(seed, time) * error;
        Vector3 observedVelocity = velocity + SmoothNoise(seed ^ 0x9e3779b9, time).ToVector3() * velocityError;
        Vec3d relative = observedPosition - muzzle;
        Vec3d relativeVelocity = Vec3d.From(observedVelocity - shooter.Velocity);
        if (!Intercept(relative, relativeVelocity, gun.MuzzleSpeed, gun.MaxRange / gun.MuzzleSpeed, out double flight))
            return Invalid("선행 계산 불가");
        Vec3d aim = relative + relativeVelocity * flight;
        Vector3 direction = aim.ToVector3().Normalized();
        return new(true, linked ? "아군 링크 사격" : "선행 계산", direction, muzzle + aim, flight,
            error + velocityError * (float)flight, range) { ViaDatalink = linked };
    }

    public static bool Intercept(Vec3d relative, Vec3d velocity, double speed, double maxTime, out double time)
    {
        time = 0;
        if (!double.IsFinite(speed) || speed <= 0 || !double.IsFinite(maxTime) || maxTime <= 0
            || !double.IsFinite(relative.LengthSquared()) || !double.IsFinite(velocity.LengthSquared())) return false;
        double c = relative.LengthSquared();
        if (c < 1e-12) return false;
        double vv = velocity.LengthSquared(), a = vv - speed * speed, b = 2 * relative.Dot(velocity);
        double result;
        if (Math.Abs(a) < Math.Max(vv, speed * speed) * 1e-12)
        {
            if (Math.Abs(b) < 1e-12) return false;
            result = -c / b;
        }
        else
        {
            double discriminant = b * b - 4 * a * c;
            if (discriminant < 0) return false;
            double q = -0.5 * (b + Math.CopySign(Math.Sqrt(discriminant), b));
            double t1 = q / a, t2 = q != 0 ? c / q : double.PositiveInfinity;
            result = Math.Min(t1 > 0 ? t1 : double.PositiveInfinity, t2 > 0 ? t2 : double.PositiveInfinity);
        }
        if (!double.IsFinite(result) || result <= 0 || result > maxTime) return false;
        time = result;
        return true;
    }

    // 관측 오차를 rate(Hz)로 부드럽게 보간한다(기본 4Hz). 프레임별 랜덤 흔들림은 조준에 넣지 않는다.
    internal static Vec3d SmoothNoise(uint seed, double time, double rate = 4)
    {
        double sample = Math.Max(0, time) * rate;
        uint index = (uint)(Math.Floor(sample) % uint.MaxValue);
        double t = sample - Math.Floor(sample); t = t * t * (3 - 2 * t);
        return Vec3d.Lerp(Noise(seed ^ index), Noise(seed ^ (index + 1)), t);
    }
    private static Vec3d Noise(uint seed)
    {
        double Value(uint v) { v ^= v >> 16; v *= 0x7feb352d; v ^= v >> 15; v *= 0x846ca68b; v ^= v >> 16; return (v & 0xffffff) / 8388607.5 - 1; }
        return new(Value(seed), Value(seed ^ 0x6a09e667), Value(seed ^ 0xbb67ae85));
    }
    private static uint Hash(string text)
    {
        uint value = 2166136261;
        foreach (char c in text) { value ^= c; value *= 16777619; }
        return value;
    }
}
