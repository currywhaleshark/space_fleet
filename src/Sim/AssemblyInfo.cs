using System.Runtime.CompilerServices;

// The game layer owns input, rendering and native integration checks. Simulation
// still contains no Godot nodes; batch/check runners compile the same sources.
[assembly: InternalsVisibleTo("SpaceFleet")]
