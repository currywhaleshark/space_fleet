using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using static SpaceFleet.Game.Cine.CineMath;

namespace SpaceFleet.Game.Cine;

/// <summary>
/// 티저 v2 장면 목록. 약 68초: 지구 함대 → 화성 함대 → 탐지 → 미사일·근접방어 → 포격 → 반물질 강습 → 난전·선체 분해 → 제목.
/// 청군은 지구 설계, 적군은 화성 설계. 함선 배치와 카메라만 연출하고, 탄도·미사일·근접방어·드론·피해·분해는 실제 시뮬레이션이다.
/// </summary>
public static class CineScript
{
    public static List<CineShot> Build() => new()
    {
        Earth(), Mars(), Probe(), Contact(), Salvo(), Defense(), Broadside(), Impact(), Strike(), Brawl(), Title(),
    };

    // 1. 지구 함대: 시안의 3/4 정면 구도에서 전함 옆구리를 따라 함미 쪽으로 미끄러진다. 요격 편대가 머리 위로 앞질러 간다.
    private static CineShot Earth() => new()
    {
        Name = "earth", Duration = 8.0, FadeIn = 1.4, FadeOut = 0.25, Planet = true,
        Setup = s =>
        {
            s.Spawn("BB-01", ShipClass.Battleship, Faction.Blue, V(0, 0, 0), Vector3.Forward, 70);
            for (int i = 0; i < 5; i++)
                s.Spawn($"IC-2{i + 1}", ShipClass.Interceptor, Faction.Blue, V(470 + i * 20, 195 + i * 12, 2100 + i * 110), Vector3.Forward, 560);
        },
        Camera = (s, t) =>
        {
            ShipBody bb = s.Ship("BB-01");
            double k = Ease(t / 8.0);
            // 해가 비추는 우현(+X) 위쪽에서 함수 앞 → 함교 옆으로. 방열판(우현 x≈340)보다 바깥을 지난다.
            Vector3 cam = new((float)(560 - 20 * k), (float)(240 - 70 * k), (float)(-1100 + 820 * k));
            Vector3 look = new(0, (float)(40 - 10 * k), (float)(-450 + 420 * k));
            return CamPose.Look(s.Local3(bb, cam) + Drift(t, 2f), s.Local3(bb, look), 44);
        },
        Captions = new[] { (1.6, 4.4, "지구 함대", "Bastion급 전함 · 1,200 m"), (4.4, 7.5, "주포탑 3 · 근접방어 8", "방어 드론 8기 호위") },
    };

    // 2. 화성 함대: 아래 앞쪽에서 아레스 전함과 데이모스 호위함 두 척이 다가온다. 방열 격자가 붉게 빛난다.
    private static CineShot Mars() => new()
    {
        Name = "mars", Duration = 7.0, FadeIn = 0.2, FadeOut = 0.25, PreRoll = 0.5,
        Setup = s =>
        {
            s.Spawn("BB-X1", ShipClass.Battleship, Faction.Red, V(0, 0, 0), Vector3.Forward, 60);
            s.Spawn("DD-X1", ShipClass.Escort, Faction.Red, V(560, 120, 520), Vector3.Forward, 60);
            s.Spawn("DD-X2", ShipClass.Escort, Faction.Red, V(-600, -140, 700), Vector3.Forward, 60);
        },
        Camera = (s, t) =>
        {
            ShipBody bb = s.Ship("BB-X1");
            double k = Smooth(t / 7.0);
            Vector3 cam = new((float)(560 + 80 * k), (float)(-200 + 260 * k), (float)(-980 + 560 * k));
            Vector3 look = new(0, 30, (float)(-80 + 160 * k));
            return CamPose.Look(s.Local3(bb, cam) + Drift(t, 2.5f), s.Local3(bb, look), 46);
        },
        Captions = new[] { (0.8, 3.6, "화성 함대", "Ares급 전함 · 1,080 m"), (4.0, 6.7, "데이모스 호위함", "정찰 · 전자전 · 신속 대응") },
    };

    // 2b. 정찰: 지구 요격기가 정찰 프로브를 사출해 멀어지는 것을 보고(망원), 컷 — 프로브 곁에서 따라가다가
    //     목표 지점에 멈춰 날개를 펴고 탐지 파동을 내기 시작하는 순간까지.
    private static CineShot Probe() => new()
    {
        Name = "probe", Duration = 10.0, Phase = 1, FadeIn = 0.15, FadeOut = 0.3,
        // 사출(1배속) → 컷 뒤 비행은 빠르게 감아서(3.2배) → 정지·전개는 다시 1배속.
        Rate = t => t < 3.0 ? 1 : t < 4.2 ? 3.2 : t < 4.8 ? 3.2 - (t - 4.2) / 0.6 * 2.2 : 1,
        Setup = s =>
        {
            s.Spawn("IC-21", ShipClass.Interceptor, Faction.Blue, V(0, 0, 0), Vector3.Forward, 250);
            s.Spawn("IC-22", ShipClass.Interceptor, Faction.Blue, V(60, -18, 70), Vector3.Forward, 250);
        },
        Tick = (s, t) =>
        {
            ShipBody ic = s.Ship("IC-21");
            if (s.Cue("probe", t, 0.5))
                s.World.LaunchProbeAt(ic, ic.Position + new Vec3d(350, 160, -4200));
            s.ProbeModel.Probe = s.World.Probes.FirstOrDefault();
            s.Ordnance.DrawProbes = false;
        },
        Camera = (s, t) =>
        {
            ShipBody ic = s.Ship("IC-21");
            Vec3d near = s.Local3(ic, new Vector3(16, 6, 38));
            var probe = s.World.Probes.FirstOrDefault();
            if (probe is null) return CamPose.Look(near + Drift(t, 0.3f), s.Local3(ic, new Vector3(0, 0, -200)), 54);
            Vec3d p = Vec3d.Lerp(probe.PreviousPosition, probe.Position, s.Alpha);
            if (t < 3.0)
            {
                // 요격기 곁에서 멀어지는 프로브를 망원으로.
                double z = Smooth((t - 0.8) / 2.0);
                return CamPose.Look(near + Drift(t, 0.3f), p, (float)(54 - 36 * z));
            }
            // 컷: 프로브 바로 옆. 비행 중엔 뒤쪽 비스듬히, 멈추면 천천히 돌아 앞쪽으로.
            double since = s.ProbeModel.SinceStation(s.World.Time);
            Vector3 dir = probe.Direction.LengthSquared() > .5f ? probe.Direction : Vector3.Forward;
            Vector3 side = dir.Cross(Vector3.Up).Normalized();
            double orbit = since < 0 ? 0 : Smooth(since / 3.5) * 1.6;
            double rise = since < 0 ? 0 : Smooth((since - 0.6) / 2.5);
            Vector3 offset = (-dir * (float)Math.Cos(orbit) + side * (float)Math.Sin(orbit)) * 11f + side * 5f + Vector3.Up * (float)(3.2 + 9 * rise);
            float fov = since < 0 ? 46 : (float)(46 + 10 * Smooth((since - 1.2) / 2.0));   // 파동이 퍼지면 조금 넓게
            float back = since < 0 ? 1 : (float)(1 + 0.7 * Smooth((since - 1.2) / 2.0));
            return CamPose.Look(p + Vec3d.From(offset * back) + Drift(t, 0.15f), p + Vec3d.From(dir * 6), fov);
        },
        Captions = new[] { (0.9, 2.9, "정찰 프로브", "사출"), (5.8, 9.6, "전개 · 탐지 개시", "센서망을 적 쪽으로 밀어낸다") },
    };

    // 3. 탐지: 150 km 밖의 적은 점과 오차 원뿐이다.
    private static CineShot Contact() => new()
    {
        Name = "contact", Duration = 4.6, Card = true, FadeIn = 0.2, FadeOut = 0.3, Phase = 1,
    };

    // 4. 일제사격: 지구 호위 전대가 수직 발사관에서 차례로 쏘아 올린다.
    private static CineShot Salvo() => new()
    {
        Name = "salvo", Duration = 5.5, Phase = 2, PreRoll = 1.0, FadeIn = 0.15, FadeOut = 0.15,
        Setup = s =>
        {
            for (int i = 0; i < 4; i++)
                s.Spawn($"DD-3{i + 1}", ShipClass.Escort, Faction.Blue, V(-i * 420, -i * 50, i * 380), Vector3.Forward, 180);
            s.Spawn("BB-X1", ShipClass.Battleship, Faction.Red, V(3000, 2500, -60000), Vector3.Back, 60);
        },
        Tick = (s, t) =>
        {
            for (int k = 0; k < 16; k++)
                if (s.Cue($"salvo{k}", t, 0.4 + k * 0.2))
                    s.Missile(s.Ship($"DD-3{k % 4 + 1}"), s.Ship("BB-X1"));
        },
        Camera = (s, t) =>
        {
            ShipBody dd = s.Ship("DD-31");
            double k = Smooth((t - 1.2) / 3.5);
            Vec3d cam = s.Local3(dd, new Vector3(95, 26, 160)) + Drift(t, 1f);
            Vec3d ahead = s.Local3(dd, new Vector3(-120, 40, -380));
            Vec3d up = s.Local3(dd, new Vector3(-300, 650, -1200));
            return CamPose.Look(cam, Lerp(ahead, up, k), (float)(52 - 8 * k));
        },
        Captions = new[] { (0.9, 4.3, "일제사격", "근접방어를 포화시킨다") },
    };

    // 5. 근접방어: 화성 전함의 포대와 방어 드론이 날아든 미사일을 긁어낸다. 디코이 섬광.
    private static CineShot Defense() => new()
    {
        Name = "defense", Duration = 7.0, Phase = 2, FadeIn = 0.15, FadeOut = 0.2, Planet = true,
        Setup = s =>
        {
            ShipBody bb = s.Spawn("BB-X1", ShipClass.Battleship, Faction.Red, V(0, 0, 0), Vector3.Back, 40);
            s.Spawn("DD-X1", ShipClass.Escort, Faction.Red, V(900, 240, 500), Vector3.Back, 40);
            s.Spawn("DD-X2", ShipClass.Escort, Faction.Red, V(-1000, -200, 300), Vector3.Back, 40);
            // 포보스 요격 편대가 방공 임무로 전함 앞을 가로막고 미사일을 주포·요격 미사일로 떨어뜨린다.
            var wing = Enumerable.Range(1, 4).Select(i =>
                s.Spawn($"IC-X{i}", ShipClass.Interceptor, Faction.Red, V(-450 + i * 220, 200 + (i % 2) * 120, 2600 + i * 150), Vector3.Back, 200)).ToArray();
            foreach (ShipBody ic in wing) s.World.AttachBrain(ic, ShipOrder.EscortOf(bb));
            var squadron = s.World.AddSquadron("화성 요격 편대", Faction.Red, SquadronRole.InterceptorWing, wing);
            squadron.PlayerLed = true;
            s.World.OrderWing(squadron, InterceptorMission.AirDefense, bb);
            var launchers = Enumerable.Range(1, 4).Select(i =>
                s.Spawn($"DD-3{i}", ShipClass.Escort, Faction.Blue, V(-3000 + i * 1500, 1500, 34000), Vector3.Forward, 0, drones: false)).ToArray();
            s.Settle(1);
            for (int k = 0; k < 18; k++) s.Missile(launchers[k % 4], k % 5 == 4 ? s.Ship("DD-X1") : bb);
            for (int i = 0; i < 60 * 40 && !s.World.Missiles.Any(m => (m.Position - bb.Position).Length() < 8000); i++) s.World.Step();
        },
        Tick = (s, t) =>
        {
            if (s.Cue("decoy", t, 1.4)) s.World.LaunchDecoys(s.Ship("BB-X1"));
            if (s.Cue("decoy2", t, 3.2)) s.World.LaunchDecoys(s.Ship("DD-X1"));
        },
        Camera = (s, t) =>
        {
            ShipBody bb = s.Ship("BB-X1");
            if (t < 3.4)
            {
                // 요격 편대 바로 뒤에서 날아오는 미사일 쪽을 본다.
                ShipBody ic = s.Ship("IC-X2");
                Vec3d icAt = s.At(ic);
                return CamPose.Look(icAt + V(30, 12, -70) + Drift(t, 0.6f), icAt + V(0, 0, 1500), 50);
            }
            double k = Smooth((t - 3.4) / 3.6);
            Vec3d cam = s.At(bb) + V(640 - 100 * k, 260 - 40 * k, 1150 - 200 * k) + Drift(t, 2.5f);
            Vec3d look = s.At(bb) + V(-80, 60, 150);
            return CamPose.Look(cam, look, 54);
        },
        Captions = new[] { (0.8, 4.5, "다층 방어", "요격기 · 포대 · 방어 드론 · 디코이") },
    };

    // 6. 포격: 지구 전함 주포탑들이 따로 선회해 21 km 밖 데이모스를 쏜다.
    private static CineShot Broadside() => new()
    {
        Name = "broadside", Duration = 5.5, Phase = 3, PreRoll = 1.0, FadeIn = 0.15, FadeOut = 0.1,
        Setup = s =>
        {
            ShipBody bb = s.Spawn("BB-01", ShipClass.Battleship, Faction.Blue, V(0, 0, 0), Vector3.Forward, 30);
            ShipBody dd = s.Spawn("DD-11", ShipClass.Escort, Faction.Blue, V(-1300, 150, 900), Vector3.Forward, 30);
            ShipBody dd2 = s.Spawn("DD-12", ShipClass.Escort, Faction.Blue, V(1500, -200, 1400), Vector3.Forward, 30);
            s.Spawn("DD-X3", ShipClass.Escort, Faction.Red, V(6500, 3000, -21000), Vector3.Left, 60);
            foreach (ShipBody ship in new[] { bb, dd, dd2 }) ship.Power.SetPips(1, 1, 4, 2);
        },
        Tick = (s, t) =>
        {
            if (s.Cue("open-fire", t, 0.2))
                foreach (string c in new[] { "BB-01", "DD-11", "DD-12" }) s.Duel(s.Ship(c), s.Ship("DD-X3"));
        },
        Camera = (s, t) =>
        {
            ShipBody bb = s.Ship("BB-01");
            double k = Smooth(t / 5.5);
            Vec3d cam = s.Local3(bb, new Vector3((float)(600 - 220 * k), (float)(190 - 70 * k), (float)(-1020 + 330 * k))) + Drift(t, 1.2f);
            Vec3d look = s.Local3(bb, new Vector3(-40, 50, (float)(-60 + 60 * k)));
            return CamPose.Look(cam, look, 48, s.Orientation(bb) * Vector3.Up);
        },
        Captions = new[] { (0.6, 5.1, "레일건", "포탑마다 따로 선회 · 선행 조준") },
    };

    // 7. 명중: 21 km를 건너온 탄이 실드를 벗기고 장갑을 뚫는다(같은 월드를 이어 본다).
    private static CineShot Impact() => new()
    {
        Name = "impact", Duration = 6.0, Phase = 3, Continue = true, FadeIn = 0.1, FadeOut = 0.25, Planet = true,
        Rate = t => t < 1.0 ? 1 : 0.75,
        Camera = (s, t) =>
        {
            ShipBody dd = s.Ship("DD-X3");
            double a = 0.15 + t * 0.06;
            Vec3d cam = s.At(dd) + V(Math.Sin(a) * 360, 110 - t * 5, Math.Cos(a) * 360) + Drift(t, 1.2f);
            return CamPose.Look(cam, s.At(dd) + V(0, 10, 0), 44);
        },
        Captions = new[] { (1.0, 5.2, "실드 → 장갑 → 내부 모듈", "부위마다 따로 부서진다") },
    };

    // 8. 반물질 강습: 요격 편대가 화성 전함 후미로 파고들고, 선두기가 반물질 어뢰를 추진기에 꽂는다.
    private static CineShot Strike() => new()
    {
        Name = "strike", Duration = 8.0, Phase = 4, PreRoll = 3.0, FadeIn = 0.15, FadeOut = 0.2,
        Rate = t => t < 4.2 ? 1 : t < 6.2 ? 0.35 : 0.7,
        Setup = s =>
        {
            ShipBody bb = s.Spawn("BB-X1", ShipClass.Battleship, Faction.Red, V(0, 0, 0), Vector3.Forward, 50);
            for (int i = 0; i < 3; i++)
            {
                ShipBody ic = s.Spawn($"IC-2{i + 1}", ShipClass.Interceptor, Faction.Blue, V(-40 + i * 70, -380 - i * 30, 4300 + i * 90), Vector3.Forward, 300);
            }
            s.Ship("IC-21").Ordnance.Antimatter.BeginArming();
        },
        Tick = (s, t) =>
        {
            ShipBody ic = s.Ship("IC-21"), bb = s.Ship("BB-X1");
            // 편대 전원이 추진기 쪽으로 기수를 맞춘다. 선두기만 어뢰를 쏜다(호위기는 주포로 엄호).
            Vec3d engines = bb.Position + Vec3d.From(bb.Orientation * new Vector3(0, -30, 470));
            foreach (string c in new[] { "IC-21", "IC-22", "IC-23" })
            {
                ShipBody w = s.Ship(c);
                w.Control = new ShipControl { FlightAssist = false, HelmForward = (engines - w.Position).ToVector3().Normalized() };
            }
            if (s.Cue("wing-guns", t, 1.0)) { s.Duel(s.Ship("IC-22"), bb); s.Duel(s.Ship("IC-23"), bb); }
            foreach (OrdnanceEvent e in s.World.OrdnanceEvents)
                if (e.Weapon == BattleWeapon.Antimatter && e.Kind == OrdnanceEventKind.Detonation && s.Cue($"am-blast-{e.Time:0.000}", t, 0))
                    s.Fx.Detonate(e.Position, bb.Velocity, s.World.Time, 38f);
            if (ic.Ordnance.Antimatter.Ready && s.Cue("am-fire", t, 2.0))
                GD.Print($"cine AM launch: {s.World.LaunchAntimatter(ic, bb, new Vector3(0, -26, 468)).Reason} at {(engines - ic.Position).Length():0} m");
        },
        Camera = (s, t) =>
        {
            ShipBody ic = s.Ship("IC-21"), bb = s.Ship("BB-X1");
            Vec3d engines = s.Local3(bb, new Vector3(0, -30, 470));
            if (t >= 3.9)
            {
                // 컷: 화성 전함 함미 옆에서 어뢰가 추진기에 꽂히는 순간을 본다.
                double k = Smooth((t - 3.9) / 4.0);
                Vec3d cam2 = s.Local3(bb, new Vector3((float)(330 + 60 * k), (float)(-120 + 40 * k), (float)(900 + 150 * k))) + Drift(t, 3f);
                return CamPose.Look(cam2, engines, 50);
            }
            Quaternion q = s.Orientation(ic);
            Vec3d cam = s.At(ic) + Vec3d.From(q * new Vector3(9, 6, 44)) + Drift(t, 0.5f);
            Vec3d nose = s.At(ic) + Vec3d.From(q * new Vector3(0, 0, -600));
            return CamPose.Look(cam, Lerp(nose, engines, 0.45), 58, q * Vector3.Up);
        },
        Captions = new[] { (0.6, 3.8, "반물질 강습 어뢰", "후미 아래 사각에서") },
    };

    // 9. 난전: 두 전투단이 3.5 km 간격으로 나란히 달리며 서로를 두드린다. 화성 호위함이 갈라지며 느려진다.
    private static CineShot Brawl() => new()
    {
        Name = "brawl", Duration = 9.0, Phase = 5, PreRoll = 3.0, FadeIn = 0.15, FadeOut = 0.01,
        Rate = t => t < 4.4 ? 1 : t < 5.0 ? 1 - (t - 4.4) / 0.6 * 0.7 : t < 7.2 ? 0.3 : 0.3 + (t - 7.2) / 1.8 * 0.5,
        Setup = s =>
        {
            ShipBody bb = s.Spawn("BB-01", ShipClass.Battleship, Faction.Blue, V(0, 0, 0), Vector3.Forward, 90);
            ShipBody d11 = s.Spawn("DD-11", ShipClass.Escort, Faction.Blue, V(200, 300, 1700), Vector3.Forward, 90);
            ShipBody d12 = s.Spawn("DD-12", ShipClass.Escort, Faction.Blue, V(-150, -250, -1700), Vector3.Forward, 90);
            ShipBody rbb = s.Spawn("BB-X1", ShipClass.Battleship, Faction.Red, V(-3600, 350, -900), Vector3.Forward, 90, roll: 25);
            ShipBody x1 = s.Spawn("DD-X1", ShipClass.Escort, Faction.Red, V(-3250, 650, 1100), Vector3.Forward, 90, roll: 20);
            ShipBody x2 = s.Spawn("DD-X2", ShipClass.Escort, Faction.Red, V(-3900, -150, -2900), Vector3.Forward, 90, roll: 15);
            foreach (ShipBody ship in new[] { bb, d11, d12, rbb, x1, x2 }) ship.Power.SetPips(1, 1, 4, 2);
            s.Duel(bb, rbb); s.Duel(d11, x1); s.Duel(d12, x2);
            s.Duel(rbb, bb); s.Duel(x1, d11); s.Duel(x2, d12);
        },
        Tick = (s, t) =>
        {
            if (s.Cue("focus", t, 2.2)) { s.Duel(s.Ship("DD-11"), s.Ship("BB-X1")); s.Duel(s.Ship("DD-12"), s.Ship("BB-X1")); }
            // 구조 붕괴: 선체가 가운데서 두 동강 나며 떨어져 나간다(WreckLayout 두 조각).
            if (s.Cue("kill", t, 4.6)) s.Ship("BB-X1").Damage.Breakup(s.World.Time, "teaser");
        },
        Camera = (s, t) =>
        {
            ShipBody x1 = s.Ship("BB-X1");
            Vec3d anchor = s.At(s.Ship("BB-01"));
            double push = Smooth((t - 2.2) / 2.4);
            Vec3d wide = anchor + V(-1700 - 80 * t, 420 - 20 * t, 1900 - 100 * t);
            Vec3d close = s.At(x1) + V(1900, 620, 1500);
            Vec3d cam = Lerp(wide, close, push) + Drift(t, 2.5f);
            Vec3d along = anchor + V(-1900, 150, -2200);
            Vec3d look = Lerp(along, s.At(x1) + V(0, 0, 60), push);
            return CamPose.Look(cam, look, (float)(54 - 6 * push));
        },
    };

    // 10. 제목: 잔해와 남은 함선 위로 천천히 물러나며 제목.
    private static CineShot Title() => new()
    {
        Name = "title", Duration = 7.5, Continue = true, FadeIn = 0.01, FadeOut = 1.6, Planet = true,
        Rate = t => 0.8,
        Camera = (s, t) =>
        {
            double k = Ease(t / 7.5);
            ShipBody bb = s.Ship("BB-01");
            // 두 동강 난 화성 전함 잔해를 중심에 두고 물러난다.
            Vec3d wreck = s.At(s.Ship("BB-X1"));
            Vec3d cam = wreck + V(1700 + 600 * k, 700 + 1500 * k, 1900 + 3600 * k);
            Vec3d look = Lerp(wreck, wreck + V(1200, 0, -600), k);
            return CamPose.Look(cam, look, 44);
        },
    };
}
