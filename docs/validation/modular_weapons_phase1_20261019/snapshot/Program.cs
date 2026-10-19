using System.Security.Cryptography;
using System.Text;
using SpaceFleet.Sim;
// Definition snapshot: stock, every catalog component, and fully researched fits of every playable hull.
var lines = new List<string>();
string H(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..16];
foreach (var hull in FleetLoadouts.Hulls.OrderBy(h => h.Id, StringComparer.Ordinal))
{
    var stock = FleetLoadouts.ResolveFit(FleetLoadouts.Empty(hull.Id));
    lines.Add($"{hull.Id}\tstock\t{stock.LoadoutId}\t{H(stock.DefinitionJson)}");
    foreach (var c in ShipComponents.All.Where(c => ShipComponents.Available(hull, c)))
    {
        var r = FleetLoadouts.ResolveFit(FleetLoadouts.Empty(hull.Id) with { Components = new ShipComponentSelection().With(c.Slot, c.Id) });
        lines.Add($"{hull.Id}\t{c.Id}\t{r.LoadoutId}\t{H(r.DefinitionJson)}");
    }
    var all = ResearchTree.Catalog.ByHull(hull.Id).Upgrades.Select(u => u.Id).ToArray();
    if (all.Length > 0)
    {
        var r = FleetLoadouts.ResolveFit(FleetLoadouts.Empty(hull.Id) with { Upgrades = all });
        lines.Add($"{hull.Id}\tresearched\t{r.LoadoutId}\t{H(r.DefinitionJson)}");
    }
    // Runtime per-gun definitions as the firing path sees them.
    var body = new ShipBody("S", stock.Definition, Faction.Blue);
    foreach (var g in body.Railguns)
        lines.Add($"{hull.Id}\tgun:{g.Definition.ModuleId}\t{H(ShipDefinition.SerializeData(g.Definition))}\t{H(ShipDefinition.SerializeData(g.Weapon))}");
}
File.WriteAllLines(args[0], lines);
Console.WriteLine($"{lines.Count} rows");
