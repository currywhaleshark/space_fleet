using System;
using Godot;

namespace SpaceFleet.Sim;

public sealed record FiringSolution(bool Valid, string Reason, Vector3 Direction, Vec3d AimPoint,
    double FlightTime, float ErrorMeters, double Range);

/// <summary>
/// 등속 선행 조준. 실제 위치·속도에 재현 가능한 관측 오차를 더한다.
/// 센서망 추적(track)을 넘기면 잠금 단계일 때만 해를 내고, ECM·신호 세기에 따라 오차가 커지거나 줄어든다.
/// </summary>
public static class FireControl
{
    public static FiringSolution Solve(ShipBody shooter, ShipBody target, double time, bool sensorError = true, SensorTrack? track = null)
    {
        if (shooter.Railgun is not RailgunState weapon)
            return new(false, "주포 없음", Vector3.Zero, target.Position, 0, 0, (target.Position - shooter.Position).Length());
        Vec3d muzzle = weapon.MuzzlePosition;
        double range = (target.Position - muzzle).Length();
        FiringSolution Invalid(string reason) => new(false, reason, Vector3.Zero, target.Position, 0, 0, range);
        if (target == shooter || target.Damage.Destroyed) return Invalid("유효 표적 없음");
        if (shooter.Damage.SensorFraction <= 0.02f) return Invalid("조준 센서 비활성");
        RailgunDefinition gun = weapon.Definition;
        if (range > gun.MaxRange) return Invalid("사거리 밖");
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
        }
        // 관측 품질 = 센서 모듈 상태 × 센서 채널 배율(핍·전압 강하·과열). 센서 핍을 올리면 오차가 줄어든다.
        // 추적 배율(ECM·신호 세기)은 관측 품질을 깎는 것으로 반영한다.
        float quality = Mathf.Max(0.05f, shooter.Damage.SensorFraction * shooter.Power.SensorEffect) / trackScale;
        float error = sensorError ? (gun.SensorErrorMeters + gun.SensorErrorPerKm * (float)(range / 1000)) / quality : 0;
        uint seed = Hash(shooter.Callsign) ^ Hash(target.Callsign);
        Vec3d observedPosition = target.Position + SmoothNoise(seed, time) * error;
        Vector3 observedVelocity = target.Velocity + SmoothNoise(seed ^ 0x9e3779b9, time).ToVector3() * (sensorError ? gun.VelocityError / quality : 0);
        Vec3d relative = observedPosition - muzzle;
        Vec3d velocity = Vec3d.From(observedVelocity - shooter.Velocity);
        if (!Intercept(relative, velocity, gun.MuzzleSpeed, gun.MaxRange / gun.MuzzleSpeed, out double flight))
            return Invalid("선행 계산 불가");
        Vec3d aim = relative + velocity * flight;
        Vector3 direction = aim.ToVector3().Normalized();
        return new(true, "선행 계산", direction, muzzle + aim, flight,
            error + (sensorError ? gun.VelocityError / quality * (float)flight : 0), range);
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

    // 4Hz 관측 오차를 부드럽게 보간한다. 프레임별 랜덤 흔들림은 조준에 넣지 않는다.
    internal static Vec3d SmoothNoise(uint seed, double time)
    {
        double sample = Math.Max(0, time) * 4;
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
