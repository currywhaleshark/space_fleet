using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceFleet.Sim;
using SpaceFleet.View;

namespace SpaceFleet.Game;

/// <summary>
/// 미사일 표시. 내 진영 미사일은 파란 갈매기(탐색기 잠금이면 채움), 적 미사일은 빨간 삼각형.
/// 나를 노리는 미사일은 크게, 화면 밖이면 가장자리에 깜박이는 빨간 삼각형(OffscreenHud). 조준선 아래 호 = 미사일 잔량, 위 호 = 디코이 잔량.
/// </summary>
public partial class Hud
{
    private static readonly Color MissileOwn = new(0.55f, 0.8f, 1f, 0.95f);
    private static readonly Color DecoyColor = new(1f, 0.95f, 0.8f, 0.9f);
    /// <summary>이 거리 안의 적 미사일은 경보 수신기로 보인다(m).</summary>
    private const float MissileWarningRange = 60_000f;

    /// <summary>나를 노리는 적 미사일 수(계기판 칩용).</summary>
    public int IncomingMissiles { get; private set; }

    private readonly List<(Missile Missile,Vector2 Point,bool Own,bool Threat)> _missileMarks=new();
    private readonly Dictionary<Vector2I,int> _friendlyMissileDensity=new();
    internal int DrawnMissileSymbols { get; private set; }
    internal int CompactMissileSymbols { get; private set; }

    internal static bool ApproachesAlly(SmallContact observed,ShipBody ally,double time)
    {
        Vec3d relative=observed.Predict(time)-ally.Position;
        Vector3 velocity=observed.Velocity-ally.Velocity;
        double speed2=velocity.LengthSquared();
        if(speed2<1)return false;
        double t=-relative.Dot(Vec3d.From(velocity))/speed2;
        return t>=0&&t<=12&&(relative+Vec3d.From(velocity)*t).Length()<ally.Class.Length*.5+600;
    }
    private void DrawMissileMarkers(Camera3D cam, ShipView controlled, Vector2 screen)
    {
        ShipBody me=controlled.Body;
        int incoming=0;DrawnMissileSymbols=0;CompactMissileSymbols=0;
        _missileMarks.Clear();_friendlyMissileDensity.Clear();
        ShipBody? anchor=Game.SelectedFriendly?.Body??me.Squadron?.SupportGroup?.Leader
            ??Game.World.Ships.Where(s=>s!=me&&s.Faction==me.Faction&&!s.Damage.Destroyed&&s.Class.Kind==HullKind.Battleship)
                .MinBy(s=>(s.Position-me.Position).LengthSquared());
        foreach(Missile m in Game.World.Missiles)
        {
            bool own=m.Faction==me.Faction,threat=!own&&m.Target==me;
            if(threat)incoming++;
            if(!own&&(m.Position-me.Position).Length()>MissileWarningRange)continue;
            Vector3 render=(m.Position-Game.RenderOrigin).ToVector3();
            bool behind=cam.IsPositionBehind(render);
            Vector2 p=behind?Vector2.Zero:cam.UnprojectPosition(render);
            if(behind||!new Rect2(Vector2.Zero,screen).HasPoint(p))
            {
                if(threat)DrawOffscreenMissile(cam,(m.Position-me.Position).ToVector3().Normalized(),screen);
                continue;
            }
            if(!own&&anchor is not null&&Game.World.Sensors.SmallTrack(me.Faction,m) is { } observed&&observed.Fresh(Game.World.Time))
                threat|=ApproachesAlly(observed,anchor,Game.World.Time);
            _missileMarks.Add((m,p,own,threat));
            _markerLabels.ProtectSymbol(p,threat?10:7);
            if(own)
            {
                var cell=new Vector2I((int)(p.X/18),(int)(p.Y/18));
                _friendlyMissileDensity[cell]=_friendlyMissileDensity.GetValueOrDefault(cell)+1;
            }
        }
        var hovered=_missileMarks.Count==0?null:_missileMarks.MinBy(mark=>mark.Point.DistanceSquaredTo(MarkerPointer)).Missile;
        foreach(var (m,p,own,threat) in _missileMarks)
        {
            bool selected=Game.SelectedSmallContact?.Target==m,hover=m==hovered&&p.DistanceSquaredTo(MarkerPointer)<=18*18;
            if(own)
            {
                var cell=new Vector2I((int)(p.X/18),(int)(p.Y/18));int nearby=0;
                for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)nearby+=_friendlyMissileDensity.GetValueOrDefault(cell+new Vector2I(x,y));
                bool compact=nearby>=4&&m.Shooter!=me&&!selected&&!hover;
                float size=compact?3.5f:6;
                Color color=compact?new Color(MissileOwn,.65f):MissileOwn;
                Vector2[] chevron={p+new Vector2(-size,size*.67f),p+new Vector2(0,-size*.84f),p+new Vector2(size,size*.67f),p};
                if(m.SeekerLocked&&!compact)DrawColoredPolygon(chevron,color);
                else DrawPolyline(new[]{chevron[0],chevron[1],chevron[2]},color,1.5f);
                if(compact)CompactMissileSymbols++;
                if(hover)_markerLabels.Add(m,p,new Vector2(12,-10),$"아군 미사일 · {FormatDistance((m.Position-me.Position).Length())}",Friendly,90,12);
            }
            else
            {
                float size=selected?11:threat?9:6;
                Vector2[] tri={p+new Vector2(0,-size),p+new Vector2(size*.9f,size*.7f),p+new Vector2(-size*.9f,size*.7f)};
                if(threat)DrawColoredPolygon(tri,new Color(Hostile,.85f));
                else DrawPolyline(new[]{tri[0],tri[1],tri[2],tri[0]},Hostile,1.5f);
                // The selected small-target overlay already provides range and intercept status.
                if(!selected&&hover)_markerLabels.Add(m,p,new Vector2(12,-12),
                    (m.Target==me?"자함 접근":threat?"아군 접근":"적 미사일")+" · "+FormatDistance((m.Position-me.Position).Length()),Hostile,hover?90:80,12);
            }
            DrawnMissileSymbols++;
        }
        IncomingMissiles=incoming;
    }

    /// <summary>조준선 아래·위 호: 미사일·디코이 잔량.</summary>
    private void DrawOrdnanceArcs(ShipView controlled, Vector2 c, float radius = CrosshairRadius + 6f)
    {
        OrdnanceState o = controlled.Body.Ordnance;
        if (o.MissileDefinition is MissileDefinition md)
        {
            Color color = o.MissileReady ? MissileOwn : new Color(MissileOwn, 0.45f);
            ArcGauge(c, radius, Mathf.DegToRad(120), Mathf.DegToRad(-60), o.Missiles / (float)md.Rounds, color, 3f);
        }
        if (o.DecoyDefinition is DecoyDefinition dd)
            ArcGauge(c, radius, Mathf.DegToRad(240), Mathf.DegToRad(60), o.Decoys / (float)dd.Count,
                o.DecoyReady ? DecoyColor : new Color(DecoyColor, 0.4f), 3f);
    }
}
