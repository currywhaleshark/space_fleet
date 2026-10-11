# Battle objectives: Annihilation / Flagship / Domination

PR #1, merged with main (damage control / emergency repair, interceptor wing missions). World simulation remains at 60 Hz, coordinates in meters using Vec3d, Blue/Red teams independent from Earth/Mars hull design.

## Mode select
Sortie (ship choice) screen: **전투 목표** selector cycles:
- **섬멸전** (Annihilation) — current legacy combat victory logic, unchanged by default
- **기함격침전** (Flagship) — destroy *or disable* the opposing BB flagship; simultaneous losses draw. A whole-force loss also loses. A flagship that is disabled but inside its emergency-repair window (`DamageControl.RecoveringDisable`) is not yet lost; it is lost once the repair window closes or repairs are held
- **점령전** (Domination) — hold 3 spherical zones and accumulate 500 score before the opponent

CLI direct-play override: `--battle --objective=Domination --seed=1` (also `Annihilation`, `Flagship`). The selected mode persists for restarting a match and remains independent of Blue/Red ship design.

## Capture rules
- Zones: **A/B/C**, 12 km radius each, side by side across the approach axis at the midpoint (equal distance to both fleets), 60 km apart (`BattleRules.ZoneSpacing`), heights A +14 km / B −9 km / C +5 km
- Only combat-capable ships (not destroyed/disabled) count. Ships recovering from disable still count as remaining strength for the wipe-out check
- Inside influence weight: battleship 2, escort 1.5, interceptor 1; influence rate capped at 3
- An initially neutral zone fills from 0 to +100 or -100; progress reverses if a different team arrives
- Both teams inside the same volume => **contested**, neither progress nor points
- A captured zone retains ownership if left vacant, until the opposing team completes the recapture
- A held, uncontested zone awards one point per second; attackers immediately prevent the current owner from scoring
- Score goal: 500 points; max 25 minutes. On timer, compare score, then owned zone count, then remaining active fleet fraction, then draw
- Destroying an entire team awards the surviving team a victory even without capture points

## AI
- Flagship objective: on *identified* enemy flagship, focus attacks toward it while the battle-group escorts protect their own flagship; interceptor wings only commit within 40 km. Otherwise use normal sensor-respecting combat behaviour
- Domination objective: send battle groups to B, escort squadrons toward their near-side peripheral objective, interceptor wings to B, and redeploy toward unfinished zones after capture
- AI may fire at detected enemies while holding objective points
- Ships hold `SimWorld.HoldClearance` (4 km) from the zone centre on their own side, so they do not sit inside the structure (still well inside the 12 km capture radius)
- Squadrons given objective orders are flagged (`Squadron.ObjectiveOrders`, reset every 2 s command cycle) so interceptor wing missions do not overwrite them
- Player-led squadrons retain manual orders; commander does not override them

## HUD / map / 3D
- Mode selection in sortie screen and mode label in start/result
- Combat HUD (under the strength bars, shapes only):
  - Flagship: both flagship silhouettes with shield/system bars; enemy not identified = dashed outline + `?`; disabled = red bar, recovering = blinking amber, destroyed = X
  - Domination: score race bars toward 500 from the centre, three zone discs (owner fill, capture-progress arc, blinking amber ring when contested)
- Full map shows objective centres and approximate circular projected influence areas; Fit All includes objective coordinates
- 3D scene renders three axis-aligned translucent circle outlines around each spherical capture area using floating-origin rendering
- Each zone centre has a procedural structure (`src/View/ZoneStructures.cs`, visual only — no collision, no cover, not targetable):
  - A 자원기지: asteroid with refinery, silos, drill tower and a loading truss
  - B 정거장: hub with a slowly rotating habitat ring, spokes and solar wings
  - C 연료 저장고: truss spine with spherical tank clusters, control module and radiators
  - Beacon lights take the owner colour and blink while contested
  - Preview: `.	ools\gallery.ps1 zone-B` (a battleship is placed alongside for scale)

## Validation
Checks run inside `.	ools\check-sim.ps1` (19 objective checks). Batches: `.	oolsattle-batch.ps1 -Seeds (1..5) -Mirror -Parallel -Objective Domination`.

The checks cover default legacy rules, flagship loss and simultaneous destruction, flagship emergency-repair grace, zone layout (side by side, equidistant, distinct heights), spherical capture status, contest, red recapture, scoring, score freeze, and standard three-zone setup.

Measured 2026-10-11 (seeds 1–5 × mirror, Earth/Earth): Flagship 10/10 by 기함 상실 (median 671 s, Blue 4 : Red 6); Domination 10/10 by 거점 점수 달성 (median 597 s, Blue 6 : Red 4; after the structure standoff: median 665 s, Blue 3 : Red 7 — 10 battles, within noise, recheck with seeds 1–20). Annihilation is unchanged from main.

Known cost: long domination battles let wrecks age near the holding fleets; aged wreck hulls grow to ~900 m and dominate collision time (see `docs/WORKORDER_sim_performance.md` 2.5차).

Before merging, run:
```powershell
.\tools\build.ps1
.\tools\check-sim.ps1
.\tools\run.ps1 --flow-test
.\tools\run.ps1 --battle --objective=Flagship --autoplay --seed=1
.\tools\run.ps1 --battle --objective=Domination --autoplay --seed=1
.\tools\run.ps1 --battle --objective=Annihilation --autoplay --seed=1
```
Verify all three modes end reliably and the selected role, 3D rendering, capture labels and AI movement are readable. Tune fairness and capture pacing only after gameplay measurements; parameters are provisional. Preserve legacy Earth/Earth deterministic test summaries.
