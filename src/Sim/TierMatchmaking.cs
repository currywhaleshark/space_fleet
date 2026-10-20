using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpaceFleet.Sim;

public static partial class Matchmaking
{
    public static ResolvedSquadron ValidateResearchLoadout(SquadronLoadout squad,IReadOnlySet<string> owned,
        IReadOnlyDictionary<string,IReadOnlySet<string>> upgrades)
    {
        var resolved=FleetLoadouts.Resolve(squad,owned);var tree=ResearchTree.Catalog;
        for(int i=0;i<squad.Members.Count;i++)
        {
            var fit=squad.Members[i];var node=tree.ByHull(fit.HullId);
            if(fit.Upgrades?.Any(id=>!upgrades.TryGetValue(fit.HullId,out var unlocked)||!unlocked.Contains(id))==true)
                throw new InvalidDataException("연구하지 않은 부품입니다.");
            if(fit.Components?.Ids.Any(id=>!upgrades.TryGetValue(fit.HullId,out var unlocked)||!unlocked.Contains(ShipComponents.UnlockKey(id)))==true)
                throw new InvalidDataException("연구하지 않은 교체 부품입니다.");
            if(fit.Components?.PartIds.Any(id=>MainGunParts.Research(id) is not {Stock:true}
                &&(!upgrades.TryGetValue(fit.HullId,out var unlocked)||!unlocked.Contains(MainGunParts.UnlockKey(id))))==true)
                throw new InvalidDataException("연구하지 않은 부품입니다.");
            if(resolved.Leader.MatchClass==MatchWeightClass.Large&&node.Tier>tree.ByHull(resolved.Leader.Id).Tier)
                throw new InvalidDataException("호위 함선의 티어는 기함 이하여야 합니다.");
        }
        tree.Bracket(squad);return resolved;
    }
    /// <summary>Pairs the full member tier/class vectors and research strength, including the human squad.</summary>
    public static MatchLineup CreateTiered(int seed,SquadronLoadout player,IReadOnlySet<string> owned,
        IReadOnlyDictionary<string,IReadOnlySet<string>> upgrades)
    {
        var validated=ValidateResearchLoadout(player,owned,upgrades);var tree=ResearchTree.Catalog;
        var bracket=tree.Bracket(player);var random=new Random(seed);
        // Fit choices draw from their own stream so the hull/tier lineup for a seed stays as before.
        var fitRandom=new Random(unchecked(seed*7919+17));
        var pool=tree.Playable.Where(n=>n.Tier>=bracket.Low&&n.Tier<=bracket.High).ToArray();
        ShipDefinition Hull(ResearchNode n)=>ShipDefinitions.ById(n.HullId!);
        ResearchNode Pick(IEnumerable<ResearchNode> candidates){var list=candidates.ToArray();if(list.Length==0)throw new InvalidDataException("이 티어의 편성을 만들 수 없습니다.");return list[random.Next(list.Length)];}
        ShipFit Fit(ResearchNode node,int preset,IReadOnlyList<string>? research=null)
        {
            var equipment=FleetLoadouts.AiPreset(node.HullId!,preset).Members[0].Equipment;
            return new(node.HullId!,equipment,research);
        }
        SquadronLoadout Fresh(MatchWeightClass kind)
        {
            var leader=Pick(pool.Where(n=>Hull(n).MatchClass==kind));var hull=Hull(leader);int preset=random.Next(3);
            var fits=new List<ShipFit>();
            for(int i=0;i<hull.Squadron.Members.Count;i++)
            {
                var node=i==0?leader:Pick(pool.Where(n=>FleetLoadouts.Allows(hull,i,Hull(n))&&(kind!=MatchWeightClass.Large||n.Tier<=leader.Tier)));
                int count=random.Next(node.Upgrades.Length+1);var fit=Fit(node,preset,node.Upgrades.Take(count).Select(u=>u.Id).ToArray());
                // AI squads field unlocked main gun mixes too (§8), stock half of the time.
                fits.Add(fit with{Components=FitScores.RandomParts(fit,fitRandom)});
            }
            return new(fits);
        }
        SquadronLoadout Counterpart(SquadronLoadout source)
        {
            var original=ShipDefinitions.ById(source.Members[0].HullId);var sourceNode=tree.ByHull(original.Id);
            var possible=pool.Where(n=>n.Tier==sourceNode.Tier&&Hull(n).MatchClass==original.MatchClass).Where(n=>
                source.Members.Select((m,i)=>pool.Any(p=>p.Tier==tree.ByHull(m.HullId).Tier&&FleetLoadouts.Allows(Hull(n),i,Hull(p)))).All(x=>x)).ToArray();
            var leader=Pick(possible);var def=Hull(leader);var fits=new List<ShipFit>();int preset=random.Next(3);
            for(int i=0;i<source.Members.Count;i++)
            {
                var old=source.Members[i];var node=i==0?leader:Pick(pool.Where(n=>n.Tier==tree.ByHull(old.HullId).Tier&&FleetLoadouts.Allows(def,i,Hull(n))));
                var research=node.Upgrades.Take(old.Upgrades?.Count??0).Select(u=>u.Id).ToArray();var fit=Fit(node,preset,research);
                // Mirror the member's fitted score, not just its tier and research count.
                fits.Add(fit with{Components=FitScores.ClosestParts(fit,FitScores.Score(old),fitRandom)});
            }
            return new(fits);
        }
        var classes=pool.Select(n=>Hull(n).MatchClass).Distinct().ToArray();var sizes=new List<MatchWeightClass>{validated.Leader.MatchClass};
        foreach(var kind in classes)while(sizes.Count(k=>k==kind)<2)sizes.Add(kind);
        while(sizes.Count<MatchLineup.TeamSize)
        {var choices=classes.Where(k=>sizes.Count(s=>s==k)<(classes.Length==3?4:MatchLineup.TeamSize)).ToArray();sizes.Add(choices[random.Next(choices.Length)]);}
        var blue=new List<SquadronLoadout>{player};
        foreach(var kind in sizes.Skip(1))blue.Add(Fresh(kind));
        var red=blue.Select(Counterpart).ToArray();
        // Where a mirror could not reach its member's fitted score (few weaker parts on that side), refit the AI
        // member instead; the player's own squad is never changed.
        // Controlled forced-part trials keep their forced fits (FitScores.ForcedPart).
        for(int i=1;i<blue.Count&&FitScores.ForcedPart is null;i++)
        {
            var members=blue[i].Members.ToArray();bool changed=false;
            for(int j=0;j<members.Length;j++)
            {
                double target=FitScores.Score(red[i].Members[j]),own=FitScores.Score(members[j]);
                if(Math.Abs(own-target)<=.02*target)continue;
                members[j]=members[j] with{Components=FitScores.ClosestParts(members[j] with{Components=null},target,fitRandom)};changed=true;
            }
            if(changed)blue[i]=new(members);
        }
        Shuffle(red,0,random);
        var participants=new List<MatchParticipant>();
        void Add(SquadronLoadout fit,Faction team,int slot,bool human)
        {
            var resolved=human?validated:FleetLoadouts.Resolve(fit);
            participants.Add(new(slot,team,resolved.Leader.Id,human,resolved,fit.Members.Sum(FitScores.Score),
                tree.ByHull(resolved.Leader.Id).Tier,Array.AsReadOnly(fit.Members.Select(m=>tree.ByHull(m.HullId).Tier).ToArray())));
        }
        for(int i=0;i<blue.Count;i++)Add(blue[i],Faction.Blue,i+1,i==0);
        for(int i=0;i<red.Length;i++)Add(red[i],Faction.Red,i+1,false);
        return new(participants){Tier=bracket.Center,LowTier=bracket.Low,HighTier=bracket.High};
    }
}
