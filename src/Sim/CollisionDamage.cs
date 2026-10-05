using Godot;

namespace SpaceFleet.Sim;

/// <summary>
/// 충돌 피해. 비탄성 충돌로 사라지는 운동에너지(½·환산질량·접근속도²)가 양쪽 선체에 모두 들어간다.
/// - 선체 구조 한계(질량 × StructureJoulesPerKg)를 넘으면 그 함선은 붕괴한다.
/// - 넘지 않으면 접촉점에서 선체 안쪽으로 파쇄 피해가 들어간다. 장갑·모듈 관통은 사격과 같은 계산이고 실드는 무시한다.
/// - 같은 에너지라도 작은 함선일수록 크게 부서진다(ReferenceLength / 길이 배율).
/// 상수는 게임 조정용이다.
/// </summary>
public static class CollisionDamage
{
    /// <summary>구조가 흡수할 수 있는 kg당 에너지. 고정된 벽에 약 141 m/s로 부딪히면 붕괴하는 수준.</summary>
    public const double StructureJoulesPerKg = 1e4;
    /// <summary>파쇄 피해 1단위(사격 패킷 에너지·관통 mm와 같은 척도)에 해당하는 J.</summary>
    public const double JoulesPerUnit = 6e6;
    /// <summary>이 길이(호위함)를 기준으로 작은 함선은 같은 에너지에 더 크게, 큰 함선은 작게 부서진다.</summary>
    public const float ReferenceLength = 300f;
    /// <summary>이보다 약한 파쇄는 무시한다(스치거나 밀어붙이는 접촉).</summary>
    public const float MinimumUnits = 5f;
    private const float ModuleDamagePerUnit = 0.2f;

    /// <summary>비탄성 충돌의 소산 에너지(J).</summary>
    public static double DissipatedEnergy(double massA, double massB, double closingSpeed, double restitution)
    {
        double reducedMass = massA * massB / (massA + massB);
        return 0.5 * reducedMass * closingSpeed * closingSpeed * (1.0 - restitution * restitution);
    }

    /// <param name="worldArm">함선 중심에서 접촉점까지(월드 방향, m).</param>
    /// <param name="inward">접촉점에서 이 함선 안쪽을 향하는 방향.</param>
    public static void Apply(ShipBody ship, ShipBody other, Vector3 worldArm, Vector3 inward, double energy, double time)
    {
        if (ship.Damage.Destroyed || energy <= 0 || inward.LengthSquared() < 1e-8f)
            return;
        if (energy >= ship.Class.MassKg * StructureJoulesPerKg)
        {
            ship.Damage.Breakup(time, other.Callsign);
            return;
        }

        float units = (float)(energy / JoulesPerUnit) * ReferenceLength / ship.Class.Length;
        if (units < MinimumUnits)
            return;
        var packet = new DamagePacket(units, units, units * ModuleDamagePerUnit, ship.Class.Length * 1.5f);
        uint sequence = (uint)(time * 240.0);
        Vec3d contact = ship.Position + Vec3d.From(worldArm);
        inward = inward.Normalized();
        ship.Damage.Report(time, $"충돌 충격 ({other.Callsign})");

        // 접촉점 조금 바깥에서 법선 방향으로 파쇄한다. 모서리 접촉처럼 법선 방향에 선체가 없으면 중심 쪽으로 다시 겨눈다.
        ShotResult result = DamageRay.ApplyAtPose(ship, contact - Vec3d.From(inward * 2f), inward, packet,
            time, sequence, ship.Position, ship.Orientation, shieldApplies: false);
        if (result.Target is null)
        {
            Vector3 toCenter = (ship.Position - contact).ToVector3();
            if (toCenter.LengthSquared() > 1e-6f)
                DamageRay.ApplyAtPose(ship, contact, toCenter.Normalized(), packet,
                    time, sequence, ship.Position, ship.Orientation, shieldApplies: false);
        }
    }
}
