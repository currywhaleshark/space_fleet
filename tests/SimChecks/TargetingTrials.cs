using SpaceFleet.Sim;
static class TargetingTrials
{
    public static void Run(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path,"ships\tfield\trepeat\tmean_ms\tp99_ms\tmax_ms\tbytes_per_tick\tstate_hash\n");
        foreach(int count in new[]{24,80})foreach(var field in Enum.GetValues<Battlefield>())
        {
            string? hash=null;
            for(int repeat=0;repeat<2;repeat++)
            {
                var result=TacticalTrials.Battle(new BattleConfig{Seed=91,StartDistance=45000,FieldSeed=4,Battlefield=field,
                    Objective=BattleObjective.Annihilation,Engagement=Engagement.HeadOn,Elevation=Elevation.Level,Match=TacticalTrials.Lineup(count)},30,5);
                if(hash is not null&&hash!=result.Hash)throw new Exception("Non-deterministic targeting trial");
                hash=result.Hash;
                string line=FormattableString.Invariant($"{count}\t{field}\t{repeat}\t{result.Mean:F3}\t{result.P99:F3}\t{result.Max:F3}\t{result.Bytes:F0}\t{hash}");
                File.AppendAllText(path,line+"\n");Console.WriteLine(line);
            }
        }
    }
}
