using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using static SpaceFleet.Game.Cine.CineMath;

namespace SpaceFleet.Game.Cine;

/// <summary>
/// 영상 v4(약 62초). 그동안 바뀐 것을 보여 주는 새 구성:
/// 정박지 → 새 함급 편대 → 머리 위의 적 → 70 km 정찰 → 레이저 방공 → 화성 강습 → 원거리 격침과 전과 → 제목.
/// 함선 배치와 카메라만 연출하고, 탄도·미사일·레이저·근접방어·피해·분해·프로브 비행은 실제 시뮬레이션이다.
/// 전과 배너·리본은 게임 HUD와 같은 모양으로 그린다.
/// </summary>
public static class CineScriptV4
{
    public static List<CineShot> Build() => new()
    {
        Dock(), Lineup(), Overhead(), Probe(), Laser(), Assault(), FarKill(), Title(),
    };

    private static readonly Color Gold = new(1f, 0.8f, 0.3f), Blue = new(0.5f, 0.8f, 1f), Pale = new(0.78f, 0.9f, 1f);

    // 1. 정박지: 격납고 미리보기와 같은 궤도 정박지에 T5 전함이 계류돼 있다. 좌현 앞에서 천천히 물러난다.
    private static CineShot Dock() => new()
    {
        Name = "dock", Duration = 7.5, FadeIn = 1.2, FadeOut = 0.3, Planet = true, Phase = 1,
        Setup = s =>
        {
            ShipBody bb = s.Spawn("BB-01", "battleship_t5", Faction.Blue, V(0, 0, 0), Vector3.Forward, 0, drones: false);
            s.Dock(bb);
        },
        Camera = (s, t) =>
        {
            ShipBody bb = s.Ship("BB-01");
            float l = bb.Definition.Flight.Length;
            double k = Ease(t / 7.5);
            Vector3 cam = new Vector3((float)(-0.42 - 0.6 * k), (float)(-0.06 + 0.32 * k), (float)(-0.62 - 0.75 * k)) * l;
            Vector3 look = new Vector3(0, (float)(0.06 - 0.02 * k), (float)(-0.1 + 0.05 * k)) * l;
            return CamPose.Look(s.Local3(bb, cam) + Drift(t, 1.5f), s.Local3(bb, look), 46);
        },
        Captions = new[] { (1.4, 4.2, "출격 준비", "궤도 정박지 · 격납고"), (4.4, 7.1, "내 편대를 꾸리고", "함급 · 공유 프리셋 · 개별 장비") },
    };

    // 2. 새 함급 편대: 콘셉트 실루엣으로 다시 만든 지구 함선들이 역할별 대열로 나아간다. 측면을 따라 훑는다.
    private static CineShot Lineup() => new()
    {
        Name = "lineup", Duration = 8.0, FadeIn = 0.2, FadeOut = 0.25, Planet = true, Phase = 1,
        Setup = s =>
        {
            s.Spawn("BB-01", "battleship_t5", Faction.Blue, V(0, 0, 0), Vector3.Forward, 120);
            s.Spawn("AA-11", "earth_air_defense_cruiser_t5", Faction.Blue, V(700, 260, -350), Vector3.Forward, 120);
            s.Spawn("CA-12", "earth_gun_cruiser_t5", Faction.Blue, V(-850, 150, -900), Vector3.Forward, 120);
            s.Spawn("CA-13", "earth_gun_cruiser_t5", Faction.Blue, V(950, -120, -1100), Vector3.Forward, 120);
            for (int i = 0; i < 4; i++)
                s.Spawn($"IC-2{i + 1}", "interceptor_t5", Faction.Blue, V(-260 + i * 170, 330 + (i % 2) * 60, -1750 - i * 70), Vector3.Forward, 120);
            s.Spawn("RC-31", "earth_recon_t5", Faction.Blue, V(-1700, 480, -1600), Vector3.Forward, 120);
        },
        Camera = (s, t) =>
        {
            ShipBody bb = s.Ship("BB-01");
            double k = Smooth(t / 8.0);
            Vector3 cam = new((float)(820 - 520 * k), (float)(300 - 80 * k), (float)(-2700 + 250 * k));
            Vector3 look = new((float)(-60 - 120 * k), 60, (float)(-1000 + 250 * k));
            return CamPose.Look(s.Local3(bb, cam) + Drift(t, 2f), s.Local3(bb, look), 48);
        },
        Captions = new[] { (0.8, 3.8, "새로 그린 함선", "콘셉트 실루엣 · 34개 함급 · T1~T5"), (4.2, 7.6, "역할별 전개", "전함 후열 · 순양함 중열 · 요격·정찰 선두") },
    };

    // 3. 머리 위의 적: 같은 방향으로 나아가는 우리 전함 아래에서 올려다보면, 화성 함대가 뒤집힌 채 머리 위로 다가온다.
    private static CineShot Overhead() => new()
    {
        Name = "overhead", Duration = 7.0, FadeIn = 0.2, FadeOut = 0.25, Phase = 2,
        Setup = s =>
        {
            s.Spawn("BB-01", "battleship_t5", Faction.Blue, V(0, 0, 0), Vector3.Forward, 80);
            s.Spawn("CA-12", "earth_gun_cruiser_t5", Faction.Blue, V(-1500, 150, -1400), Vector3.Forward, 80);
            // 연출상 거리를 줄였다(실제 개전은 150 km). 위쪽 함대는 머리를 우리 쪽으로, 등을 아래로.
            // 해(+X·+Y·+Z 쪽)를 받는 옆면이 보이도록 위쪽 함대를 왼편 위에 두고, 엔진을 켜 윤곽을 살린다.
            foreach (ShipBody x in new[]
            {
                s.Spawn("BB-X1", "mars_battleship_t5", Faction.Red, V(-2300, 3100, -4300), Vector3.Back, 90, roll: 180),
                s.Spawn("CA-X2", "mars_gun_cruiser_t5", Faction.Red, V(-3700, 3400, -3100), Vector3.Back, 90, roll: 180),
                s.Spawn("CM-X3", "mars_missile_cruiser_t5", Faction.Red, V(-900, 2900, -3300), Vector3.Back, 90, roll: 180),
            })
                x.Control = new ShipControl { FlightAssist = false, Thrust = Vector3.Forward * 0.7f };
        },
        Camera = (s, t) =>
        {
            ShipBody bb = s.Ship("BB-01");
            double k = Smooth(t / 7.0);
            Vec3d cam = s.Local3(bb, new Vector3((float)(650 - 100 * k), (float)(-120 - 60 * k), (float)(420 - 200 * k))) + Drift(t, 1.5f);
            Vec3d look = Lerp(s.Local3(bb, new Vector3(-300, 200, -500)), s.At(s.Ship("BB-X1")), 0.6 + 0.35 * k);
            return CamPose.Look(cam, look, (float)(56 - 8 * k));
        },
        Captions = new[] { (0.8, 6.0, "머리 위의 적", "대향 · 동항 · 반항 × 평면 · 머리 위") },
    };

    // 4. 정찰: 지구 정찰함이 프로브를 70 km 밖 적 전위 쪽으로 쏜다. 빠르게 감아 따라가다가, 적 앞에서 멈추고 펼친다.
    private static CineShot Probe() => new()
    {
        Name = "probe", Duration = 9.5, FadeIn = 0.15, FadeOut = 0.3, Phase = 3, Planet = true,
        Rate = t => t < 2.4 ? 1 : t < 5.6 ? 12 : t < 6.4 ? 12 - (t - 5.6) / 0.8 * 11 : 1,
        Setup = s =>
        {
            s.Spawn("RC-31", "earth_recon_t5", Faction.Blue, V(0, 0, 0), Vector3.Forward, 200);
            s.Spawn("RC-32", "earth_recon_t5", Faction.Blue, V(90, -30, 120), Vector3.Forward, 200);
            s.Spawn("CA-X2", "mars_gun_cruiser_t5", Faction.Red, V(2600, 1300, -94000), Vector3.Back, 100);
            s.Spawn("DD-X4", "mars_escort", Faction.Red, V(-2400, 400, -96500), Vector3.Back, 100);
        },
        Tick = (s, t) =>
        {
            ShipBody rc = s.Ship("RC-31");
            if (s.Cue("probe", t, 0.5)) s.World.LaunchProbeAt(rc, rc.Position + new Vec3d(1600, 900, -70000));
            s.ProbeModel.Probe = s.World.Probes.FirstOrDefault();
            s.Ordnance.DrawProbes = false;
        },
        Camera = (s, t) =>
        {
            ShipBody rc = s.Ship("RC-31");
            Vec3d near = s.Local3(rc, new Vector3(22, 8, 52));
            var probe = s.World.Probes.FirstOrDefault();
            if (probe is null) return CamPose.Look(near + Drift(t, 0.3f), s.Local3(rc, new Vector3(0, 0, -300)), 54);
            Vec3d p = Vec3d.Lerp(probe.PreviousPosition, probe.Position, s.Alpha);
            if (t < 2.4)
            {
                double z = Smooth((t - 0.7) / 1.7);
                return CamPose.Look(near + Drift(t, 0.3f), p, (float)(54 - 30 * z));
            }
            // 컷: 프로브 뒤에서 적 쪽을 본다. 멈추면 옆으로 돌아 펼쳐지는 날개와 저 멀리 적 전위를 함께 담는다.
            Vec3d enemy = s.At(s.Ship("CA-X2"));
            Vector3 dir = (enemy - p).ToVector3().Normalized();
            Vector3 side = dir.Cross(Vector3.Up).Normalized();
            double since = s.ProbeModel.SinceStation(s.World.Time);
            double orbit = since < 0 ? 0 : Smooth(since / 3.0) * 0.9;
            Vector3 offset = (-dir * (float)Math.Cos(orbit) + side * (float)Math.Sin(orbit)) * 13f + Vector3.Up * 4f;
            return CamPose.Look(p + Vec3d.From(offset) + Drift(t, 0.15f), Lerp(p + Vec3d.From(dir * 30), enemy, 0.0), since < 0 ? 44 : 48);
        },
        Captions = new[] { (0.8, 2.4, "정찰 프로브", "70 km 밖 적 전위로"), (6.5, 9.2, "들키기 전에 멈춘다", "적 호위함 탐지 거리 바로 밖에서 식별") },
    };

    // 5. 레이저 방공: 화성 미사일 순양함의 일제사격을 지구 방공 순양함의 레이저 포대가 이어서 태운다.
    private static CineShot Laser() => new()
    {
        Name = "laser", Duration = 7.5, FadeIn = 0.15, FadeOut = 0.2, Phase = 4, Planet = true,
        Setup = s =>
        {
            ShipBody aa = s.Spawn("AA-11", "earth_air_defense_cruiser_t5", Faction.Blue, V(0, 0, 0), Vector3.Forward, 30);
            ShipBody bb = s.Spawn("BB-01", "battleship_t5", Faction.Blue, V(-900, -250, 2200), Vector3.Forward, 30);
            s.World.AttachBrain(aa, ShipOrder.HoldAt(aa.Position));
            // 오른쪽 앞 사분면에서 들어오게 한다(두 포탑이 함께 겨눌 수 있는 방향).
            var launchers = Enumerable.Range(1, 2).Select(i =>
                s.Spawn($"CM-X{i}", "mars_missile_cruiser_t5", Faction.Red, V(23600 + i * 2400, 1800, -27500 + i * 1500), Vector3.Back, 0, drones: false)).ToArray();
            s.Settle(1);
            for (int k = 0; k < 16; k++) s.Missile(launchers[k % 2], k % 3 == 2 ? aa : bb);
            // 적 미사일이 9 km 안에 들 때까지 감는다(아군 요격 미사일은 세지 않는다). 그동안 포탑을 선두 미사일 쪽으로 돌려 둔다.
            bool Incoming(Missile m) => m.Faction != aa.Faction;
            for (int i = 0; i < SimWorld.TickRate * 40 && !s.World.Missiles.Any(m => Incoming(m) && (m.Position - aa.Position).Length() < 9000); i++)
            {
                if (s.World.Missiles.Where(Incoming).OrderBy(m => (m.Position - aa.Position).LengthSquared()).FirstOrDefault() is { } lead)
                    foreach (RailgunState gun in aa.Railguns) gun.Aim((lead.Position - gun.MuzzlePosition).ToVector3().Normalized());
                s.World.Step();
            }
        },
        Camera = (s, t) =>
        {
            ShipBody aa = s.Ship("AA-11");
            double k = Smooth((t - 3.4) / 4.1);
            Vec3d cam = s.Local3(aa, new Vector3((float)(-150 - 250 * k), (float)(70 + 140 * k), (float)(260 + 380 * k))) + Drift(t, 0.8f);
            Vec3d look = Lerp(s.Local3(aa, new Vector3(2200, 500, -2200)), s.Local3(aa, new Vector3(500, 100, -700)), k);
            return CamPose.Look(cam, look, (float)(46 + 10 * k));
        },
        Captions = new[] { (0.8, 5.0, "레이저 방공", "지속 조사로 미사일을 태운다 · 포탑마다 다른 표적") },
    };

    // 6. 화성 강습: 데이모스 강습 편대가 지구 전함 후미 아래로 파고들어 선두기가 반물질 어뢰를 추진부에 꽂는다.
    private static CineShot Assault() => new()
    {
        Name = "assault", Duration = 8.0, PreRoll = 3.0, FadeIn = 0.15, FadeOut = 0.2, Phase = 5,
        Rate = t => t < 4.2 ? 1 : t < 6.2 ? 0.35 : 0.7,
        Setup = s =>
        {
            ShipBody bb = s.Spawn("BB-01", "battleship_t5", Faction.Blue, V(0, 0, 0), Vector3.Forward, 50);
            float l = bb.Definition.Flight.Length;
            for (int i = 0; i < 3; i++)
                s.Spawn($"AS-X{i + 1}", "mars_assault_t3", Faction.Red, V(-40 + i * 70, -380 - i * 30, l * 0.5 + 3700 + i * 90), Vector3.Forward, 300);
            s.Ship("AS-X1").Ordnance.Antimatter.BeginArming();
        },
        Tick = (s, t) =>
        {
            ShipBody lead = s.Ship("AS-X1"), bb = s.Ship("BB-01");
            float l = bb.Definition.Flight.Length;
            Vector3 aim = new(0, -l * 0.022f, l * 0.39f);
            Vec3d engines = bb.Position + Vec3d.From(bb.Orientation * aim);
            foreach (string c in new[] { "AS-X1", "AS-X2", "AS-X3" })
            {
                ShipBody w = s.Ship(c);
                w.Control = new ShipControl { FlightAssist = false, HelmForward = (engines - w.Position).ToVector3().Normalized() };
            }
            if (s.Cue("wing-guns", t, 1.0)) { s.Duel(s.Ship("AS-X2"), bb); s.Duel(s.Ship("AS-X3"), bb); }
            foreach (OrdnanceEvent e in s.World.OrdnanceEvents)
                if (e.Weapon == BattleWeapon.Antimatter && e.Kind == OrdnanceEventKind.Detonation && s.Cue($"am-blast-{e.Time:0.000}", t, 0))
                    s.Fx.Detonate(e.Position, bb.Velocity, s.World.Time, 38f);
            if (lead.Ordnance.Antimatter.Ready && s.Cue("am-fire", t, 2.0))
                GD.Print($"cine AM launch: {s.World.LaunchAntimatter(lead, bb, aim).Reason} at {(engines - lead.Position).Length():0} m");
        },
        Camera = (s, t) =>
        {
            ShipBody lead = s.Ship("AS-X1"), bb = s.Ship("BB-01");
            float l = bb.Definition.Flight.Length;
            Vec3d engines = s.Local3(bb, new Vector3(0, -l * 0.022f, l * 0.39f));
            if (t >= 3.9)
            {
                double k = Smooth((t - 3.9) / 4.0);
                Vec3d cam2 = s.Local3(bb, new Vector3((float)(l * 0.28 + 60 * k), (float)(-120 + 40 * k), (float)(l * 0.75 + 150 * k))) + Drift(t, 3f);
                return CamPose.Look(cam2, engines, 50);
            }
            Quaternion q = s.Orientation(lead);
            Vec3d cam = s.At(lead) + Vec3d.From(q * new Vector3(10, 7, 48)) + Drift(t, 0.5f);
            Vec3d nose = s.At(lead) + Vec3d.From(q * new Vector3(0, 0, -600));
            return CamPose.Look(cam, Lerp(nose, engines, 0.45), 58, q * Vector3.Up);
        },
        Captions = new[] { (0.6, 3.8, "화성 강습 편대", "반물질 4발 · 공격원 60% 집결 후 돌입") },
    };

    // 7. 원거리 격침: 지구 순양함이 30 km 밖 화성 호위함을 두드린다. 멀어서 작아도 격침 섬광은 또렷하고, 전과가 바로 뜬다.
    private static CineShot FarKill() => new()
    {
        Name = "farkill", Duration = 7.5, PreRoll = 0.5, FadeIn = 0.15, FadeOut = 0.01, Phase = 6, Planet = true,
        Setup = s =>
        {
            ShipBody ca = s.Spawn("CA-12", "earth_gun_cruiser_t5", Faction.Blue, V(0, 0, 0), Vector3.Forward, 60);
            ShipBody dd = s.Spawn("DD-X2", "mars_escort", Faction.Red, V(4200, 1700, -30000), Vector3.Left, 80);
            ca.Power.SetPips(1, 1, 4, 2);
            s.Duel(ca, dd);
        },
        Tick = (s, t) =>
        {
            if (s.Cue("kill", t, 3.4)) s.Ship("DD-X2").Damage.Catastrophe(s.World.Time, "teaser");
        },
        Camera = (s, t) =>
        {
            ShipBody ca = s.Ship("CA-12");
            Vec3d target = s.At(s.Ship("DD-X2"));
            Vec3d cam = s.Local3(ca, new Vector3(-150, 95, 420)) + Drift(t, 0.8f);
            return CamPose.Look(cam, Lerp(s.Local3(ca, new Vector3(0, 0, -1500)), target, 0.92), 40);
        },
        Banners = new[] { (3.5, "격침", "DD-X2 · 30 km", Gold) },
        Ribbons = new[]
        {
            (0.9, "명중", Pale), (1.5, "명중", Pale), (1.8, "관통", Blue), (2.3, "명중", Pale), (2.6, "관통", Blue), (2.9, "명중", Pale),
            (3.5, "격침", Gold),
        },
    };

    // 8. 제목: 원거리 격침 장면에서 이어서 물러나며 둥근 가스 행성 위로 제목.
    private static CineShot Title() => new()
    {
        Name = "title", Duration = 7.0, Continue = true, FadeIn = 0.01, FadeOut = 1.6, Planet = true,
        Rate = t => 0.8,
        Camera = (s, t) =>
        {
            double k = Ease(t / 7.0);
            ShipBody ca = s.Ship("CA-12");
            Vec3d at = s.At(ca);
            Vec3d cam = at + V(-500 - 1400 * k, 300 + 1300 * k, 900 + 2600 * k);
            Vec3d look = Lerp(at + V(0, 0, -1500), at + V(-4000, -900, -9000), k);
            return CamPose.Look(cam, look, 44);
        },
    };
}
