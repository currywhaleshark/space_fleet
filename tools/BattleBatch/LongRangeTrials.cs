using System.Text.Json;
using Godot;
using SpaceFleet.Sim;

internal static class LongRangeTrials
{
    // Static, intact hulls: isolate acquisition and firing uncertainty from battle movement/damage.
    public static void Save(string output)
    {
        var rows = new List<object>();
        foreach (bool mars in new[] { false, true })
        foreach (int distance in new[] { 40000, 60000, 80000, 100000, 120000, 140000 })
        foreach (bool eccm in new[] { false, true })
        foreach (bool screen in new[] { false, true })
        {
            string id = mars ? "mars_battleship" : "battleship";
            var w = new SimWorld();
            var ship = w.Add(new ShipBody("SHOOTER", ShipDefinitions.ById(id), Faction.Blue));
            var target = w.Add(new ShipBody("TARGET", ShipDefinitions.ById(mars ? "battleship" : "mars_battleship"), Faction.Red));
            target.Place(new(0, 0, -distance), new Quaternion(Vector3.Up, Mathf.Pi));
            ship.Control = target.Control = new ShipControl { FlightAssist = false };
            SetPips(ship, eccm ? new[] { 2, 1, 1, 4, 0 } : ship.Definition.Ai.DefaultPips);
            SetPips(target, target.Definition.Ai.DefaultPips);
            if (screen)
            {
                var escort = w.Add(new ShipBody("SCREEN", ShipDefinitions.ById("mars_escort"), Faction.Red));
                escort.Place(target.Position + new Vec3d(4000, 0, 0), Quaternion.Identity);
                SetPips(escort, escort.Definition.Ai.DefaultPips);
            }
            w.Sensors.Update(w.Ships, 0, true);
            var track = w.Sensors.Track(ship.Faction, target);
            var solution = FireControl.Solve(ship, target, 0, track: track);
            rows.Add(new { Hull = id, Distance = distance, Eccm = eccm, Screen = screen,
                Track = track.Level.ToString(), track.TrackSnr, track.JamRatio,
                Flight = solution.Valid ? (double?)solution.FlightTime : null,
                Error = solution.Valid ? (float?)solution.ErrorMeters : null });
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Saved {rows.Count} static acquisition/accuracy samples to {output}");
    }
    private static void SetPips(ShipBody ship, IReadOnlyList<int> p) => ship.Power.SetPips(p[0], p[1], p[2], p[3], p[4]);
}
