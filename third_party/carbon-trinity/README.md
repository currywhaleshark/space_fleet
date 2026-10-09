# Carbon Trinity effect ports

Source: https://github.com/carbonengine/trinity

Pinned revision: `c4fd6af4fcda38416cdd6a044a04740f6e06aad6`.
The unmodified source snapshots, paths and SHA-256 hashes are recorded in `sources.json`.
Retain `LICENSE.md` when distributing the adapted code. Upstream `NOTICE.md` is also retained;
the third-party hash/parser implementations it describes are not part of these ports.

| Upstream function | Space Fleet adaptation |
| --- | --- |
| `EveChildExplosion::CalculateExplosionTimes` | `CarbonCombatFx.ExplosionTimes`: interval-factor sequence, local/global durations; deterministic private RNG and absolute times replace `rand()` and countdown state. `WreckView` applies the schedule to moving wreck pieces. |
| `EveImpactOverlay::SpawnImpactDebris` | `CarbonCombatFx.ImpactScale` and `EmissionLod`, used by `BallisticsView`: hull-size scaling, projected-size emission budget, surface direction and inherited world velocity. Real hit records replace authored damage locators. |
| `EveTurretFiringFX::PrepareFiring` | `CarbonCombatFx.MuzzleAge`, used by `ShipView` and `BallisticsView`: per-muzzle selection and delay, evaluated at actual simulation fire timestamps. Simultaneous physical rounds retain simultaneous visual cues. |

These are modified C# ports, not a native Trinity integration. Godot draws the effects.
`CombatBurstBatch` and `combat_burst.gdshader` are original Space Fleet implementations;
no EVE game textures, models, sounds or effect presets are included.

The effect scheduler never changes damage, ammunition, hit timing or wreck collision geometry.
Physics splits the hull immediately according to the existing simulation; secondary explosions
follow those moving pieces rather than delaying or hiding their physical breakup.
