// Portions adapted from Carbon Trinity, Copyright (c) 2026 Fenris Creations, MIT.
// Original source headers also credit CCP ehf. (2011, 2015, 2016, 2026).
// Original functions, pinned sources and modifications: third_party/carbon-trinity/README.md.
using System;
using Godot;

namespace SpaceFleet.View;

/// <summary>Small, renderer-independent ports of Trinity's effect scheduling and debris scaling.</summary>
internal static class CarbonCombatFx
{
    public sealed record ExplosionTimeline(float[] LocalTimes,float GlobalTime,float EndTime);

    // EveChildExplosion::CalculateExplosionTimes. Preserve the growing, random interval
    // sequence; use a private seed and absolute times so replay, pause and frame skips agree.
    // The original wreck-switch timing is deliberately omitted: our physics owns breakup.
    public static ExplosionTimeline ExplosionTimes(int count,uint seed,float interval=.16f,float factor=1.12f)
    {
        count=Math.Clamp(count,0,12);
        var times=new float[count]; float time=.08f;
        for(int i=0;i<count;i++)
        {
            time+=Mathf.Pow(factor,i)*interval*Random01(ref seed);
            times[i]=time;
        }
        float global=count==0?0:time+.1f;
        return new(times,global,Mathf.Max(time+1.7f,global+1.5f));
    }

    // EveTurretFiringFX::PrepareFiring's per-muzzle selection and accumulated delay,
    // evaluated against the actual simulation fire timestamp rather than a frame timer.
    public static float MuzzleAge(double now,double firedAt,int muzzle,int first,int count,float constantDelay=0)
        => muzzle<first || muzzle>=first+count || !double.IsFinite(firedAt)
            ? -1 : (float)(now-firedAt)-constantDelay;

    // EveImpactOverlay::SpawnImpactDebris / EveDamageOverlay constants. Carbon scales
    // normalized impact size by hull size, then attenuates emission by projected size.
    public static float ImpactScale(float impactSize,float parentSize)
        => Math.Clamp(impactSize,0,10)*Mathf.Max(0,parentSize)/(10f/.0129f);
    public static float EmissionLod(float pixelDiameter) => Mathf.Clamp(pixelDiameter/100f,0,1);

    public static uint Seed(string name)
    {
        uint seed=2166136261;
        foreach(char c in name) seed=unchecked((seed^c)*16777619);
        return seed;
    }
    public static float Random01(ref uint state)
    {
        state=unchecked(state*1664525+1013904223);
        return (state>>8)*(1f/16777216f);
    }
    public static Vector3 Direction(uint seed,int index,Vector3 normal,float spread)
    {
        uint state=unchecked(seed+(uint)index*2654435761);
        Vector3 random=new(Random01(ref state)*2-1,Random01(ref state)*2-1,Random01(ref state)*2-1);
        Vector3 n=normal.LengthSquared()>.001f?normal.Normalized():Vector3.Up;
        if(random.Dot(n)<0) random=-random;
        return (n+random*spread).Normalized();
    }
}
