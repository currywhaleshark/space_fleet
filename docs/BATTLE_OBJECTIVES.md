# Battle objectives: Annihilation / Flagship / Domination

Implemented as a candidate branch (not yet merged). World simulation remains at 60 Hz, coordinates in meters using Vec3d, Blue/Red teams independent from Earth/Mars hull design.

## Mode select
Sortie (ship choice) screen: **전투 목표** selector cycles:
- **섬멸전** (Annihilation) — current legacy combat victory logic, unchanged by default
- **기함격침전** (Flagship) — destroy *or disable* the opposing BB flagship; simultaneous losses draw. A whole-force loss also loses
- **점령전** (Domination) — hold 3 spherical zones and accumulate 500 score before the opponent

CLI direct-play override: `--battle --objective=Domination --seed=1` (also `Annihilation`, `Flagship`). The selected mode persists for restarting a match and remains independent of Blue/Red ship design.

## Capture rules
- Zones: **A/B/C**, 12 km radius each, positioned around the map midpoint, including vertical offsets
- Only combat-capable ships (not destroyed/disabled) count
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
- Player-led squadrons retain manual orders; commander does not override them

## HUD / map / 3D
- Mode selection in sortie screen and mode label in start/result
- Combat HUD shows flagship status or capture score and three ownership labels
- Full map shows objective centres and approximate circular projected influence areas; Fit All includes objective coordinates
- 3D scene renders three axis-aligned translucent circle outlines around each spherical capture area using floating-origin rendering

## Validation
New command: `dotnet run --project tests/SimChecks/SimChecks.csproj -- --objectives`.

The new checks cover default legacy rules, flagship capture and simultaneous destruction, spherical capture status, contest, red recapture, scoring, score freeze, and standard three-zone setup.

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
