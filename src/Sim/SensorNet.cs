using System;
using System.Collections.Generic;
using Godot;

namespace SpaceFleet.Sim;

public enum TrackLevel
{
    /// <summary>모른다. 화면에 나오지 않는다.</summary>
    None,
    /// <summary>무언가 있다. 위치 오차가 크고 함종을 모른다.</summary>
    Contact,
    /// <summary>호출부호·함종을 안다. 아직 사격통제 해를 낼 만큼 정밀하지 않다.</summary>
    Identified,
    /// <summary>사격통제 잠금. 선행 조준을 계산할 수 있다.</summary>
    Locked,
}

/// <param name="TrackSnr">식별·잠금에 쓰는 신호(ECM 방해 반영).</param>
/// <param name="DetectSnr">접촉에 쓰는 신호(ECM 방해 전파는 오히려 멀리서 잡힌다).</param>
/// <param name="JamRatio">관측 쪽에서 본 방해 세기(ECM 배율 ÷ 관측 센서 배율). 0이면 방해 없음.</param>
/// <param name="ErrorMeters">진영이 아는 위치의 오차 규모(m).</param>
/// <param name="EstimatedPosition">진영이 아는 위치(갱신 시점의 실제 위치 + Offset).</param>
/// <param name="Offset">추정 오차 벡터(m). 몇 초에 걸쳐 천천히 흘러간다. 화면은 매 프레임 실제 이동 + 이 값으로 그린다.</param>
public readonly record struct SensorTrack(TrackLevel Level, float TrackSnr, float DetectSnr, float JamRatio,
    double Range, float ErrorMeters, Vec3d EstimatedPosition, Vector3 Offset = default)
{
    public static readonly SensorTrack Unknown = new(TrackLevel.None, 0, 0, 0, 0, 0, Vec3d.Zero);

    public bool Jammed => JamRatio > 0.01f;

    /// <summary>
    /// 사격통제 오차 배율. ECM은 오차를 키우고(√(1+방해)), 잠금 기준보다 강한 신호는 오차를 줄인다(최대 절반).
    /// </summary>
    public float FireControlScale => Mathf.Sqrt(1f + JamRatio)
        * Mathf.Clamp(Mathf.Sqrt(SensorNet.LockSnr / Mathf.Max(TrackSnr, 1e-3f)), 0.5f, 1f);
}

/// <summary>
/// 진영별 센서망. 같은 진영 함선끼리는 데이터 링크로 표적 정보를 공유하고, 표적마다 가장 잘 보는 관측함의 값을 쓴다.
/// 신호 = 관측 센서 세기 × 표적 신호 ÷ 거리(km)². 단계 기준은 접촉 1, 식별 3, 잠금 8이며 떨어질 때는 80%까지 버틴다.
/// - 관측 센서 세기 = 데이터 Strength × 센서 모듈 상태 × 센서 채널 배율(핍·전력·과열).
/// - 표적 신호 = 데이터 Signature × (1 + 0.5·엔진 출력 + 0.5·열 비율). 태우고 뜨거우면 잘 보인다.
/// - 표적 ECM: 식별·잠금 신호를 (1 + 방해) 로 나눈다. 방해 = Jammer × ECM 배율 ÷ 관측 센서 배율(ECCM).
///   대신 방해 전파 때문에 접촉 신호는 (1 + 2·ECM 배율) 배로 커진다.
/// </summary>
public sealed class SensorNet
{
    public const float ContactSnr = 1f;
    public const float IdentifySnr = 3f;
    public const float LockSnr = 8f;
    /// <summary>한 번 올라간 단계는 기준의 이 비율 아래로 떨어져야 내려간다(깜박임 방지).</summary>
    public const float DropFactor = 0.8f;
    public const double UpdateInterval = 0.25;
    public const float EngineSignature = 0.5f;
    public const float HeatSignature = 0.5f;
    public const float JamStrobe = 2f;
    /// <summary>위치 오차 = 거리 × 이 비율 ÷ √신호.</summary>
    public const float PositionErrorPerRange = 0.02f;
    /// <summary>
    /// 추정 오차가 바뀌는 빠르기(Hz). 갱신(4Hz)마다 새로 뽑으면 접촉 표시가 수 km씩 순간이동해 보이므로
    /// 몇 초에 걸쳐 천천히 흘러가게 한다.
    /// </summary>
    public const double OffsetDriftRate = 0.15;

    private readonly Dictionary<(Faction, ShipBody), SensorTrack> _tracks = new();
    private double _nextUpdate;

    /// <summary>observer 진영이 target을 아는 정도. 같은 진영은 항상 완전히 안다.</summary>
    public SensorTrack Track(Faction observer, ShipBody target)
    {
        if (target.Faction == observer)
            return new SensorTrack(TrackLevel.Locked, float.PositiveInfinity, float.PositiveInfinity, 0, 0, 0, target.Position);
        return _tracks.TryGetValue((observer, target), out SensorTrack track) ? track : SensorTrack.Unknown;
    }

    public static float Strength(ShipBody observer) => observer.Damage.Destroyed ? 0f
        : observer.Definition.Sensors.Strength * observer.Damage.SensorFraction * observer.Power.SensorEffect;

    public static float Signature(ShipBody target) => target.Definition.Sensors.Signature
        * (1f + EngineSignature * target.EngineOutput + HeatSignature * target.Power.HeatFraction);

    /// <summary>관측함 한 척이 표적을 보는 신호(접촉용·추적용)와 방해 비율.</summary>
    public static (float Track, float Detect, float Jam) Measure(ShipBody observer, ShipBody target)
    {
        double km = Math.Max((target.Position - observer.Position).Length() / 1000.0, 1e-3);
        float baseSnr = (float)(Strength(observer) * Signature(target) / (km * km));
        float jam = target.Power.EcmActive
            ? target.Definition.Sensors.Jammer * target.Power.EcmEffect / Mathf.Max(observer.Power.SensorEffect, 0.05f)
            : 0f;
        float strobe = target.Power.EcmActive ? 1f + JamStrobe * target.Power.EcmEffect : 1f;
        return (baseSnr / (1f + jam), baseSnr * strobe, jam);
    }

    public void Update(IReadOnlyList<ShipBody> ships, double time, bool force = false)
    {
        if (!force && time < _nextUpdate)
            return;
        _nextUpdate = time + UpdateInterval;

        foreach (Faction side in Enum.GetValues<Faction>())
        foreach (ShipBody target in ships)
        {
            if (target.Faction == side)
                continue;
            float bestTrack = 0f, bestDetect = 0f, bestJam = 0f;
            double bestRange = double.PositiveInfinity;
            foreach (ShipBody observer in ships)
            {
                if (observer.Faction != side || observer.Damage.Destroyed)
                    continue;
                var (track, detect, jam) = Measure(observer, target);
                bestDetect = Mathf.Max(bestDetect, detect);
                if (track > bestTrack)
                {
                    bestTrack = track;
                    bestJam = jam;
                    bestRange = (target.Position - observer.Position).Length();
                }
            }
            if (!double.IsFinite(bestRange))
                bestRange = 0;

            TrackLevel previous = Track(side, target).Level;
            TrackLevel level = Classify(bestTrack, bestDetect, previous);
            float error = (float)(bestRange * PositionErrorPerRange / Math.Sqrt(Math.Max(bestTrack, 0.25f)));
            uint seed = Hash(side.ToString()) ^ Hash(target.Callsign);
            Vector3 offset = (FireControl.SmoothNoise(seed, time, OffsetDriftRate) * error).ToVector3();
            _tracks[(side, target)] = level == TrackLevel.None ? SensorTrack.Unknown
                : new SensorTrack(level, bestTrack, bestDetect, bestJam, bestRange, error, target.Position + offset, offset);
        }
    }

    public static TrackLevel Classify(float trackSnr, float detectSnr, TrackLevel previous)
    {
        float Threshold(float value, TrackLevel level) => previous >= level ? value * DropFactor : value;
        if (trackSnr >= Threshold(LockSnr, TrackLevel.Locked)) return TrackLevel.Locked;
        if (trackSnr >= Threshold(IdentifySnr, TrackLevel.Identified)) return TrackLevel.Identified;
        if (Mathf.Max(trackSnr, detectSnr) >= Threshold(ContactSnr, TrackLevel.Contact)) return TrackLevel.Contact;
        return TrackLevel.None;
    }

    private static uint Hash(string text)
    {
        uint value = 2166136261;
        foreach (char c in text) { value ^= c; value *= 16777619; }
        return value;
    }
}
