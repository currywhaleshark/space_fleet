using System.Collections.Generic;
using Godot;
using SpaceFleet.Sim;

namespace SpaceFleet.Game;

/// <summary>Small cached textures; menus never instantiate a 3D scene per card.</summary>
internal static class MenuArt
{
    private static readonly Dictionary<string,Texture2D> Textures=new();
    private static Texture2D Load(string path)
    {
        if(!Textures.TryGetValue(path,out var texture))Textures[path]=texture=GD.Load<Texture2D>(path);
        return texture;
    }
    internal static Texture2D Ship(ShipDefinition hull)=>Load($"res://assets/ui/ships/{hull.ModelId}.png");
    internal static Texture2D Icon(string name)=>Load($"res://assets/ui/equipment/{name}.svg");
    internal static Texture2D Component(ComponentSlot slot,string? id=null,ShipDefinition? hull=null)=>Icon(id switch
    {
        "main-single"=>"main-single","main-triple"=>"main-triple",
        "main-precision" or "main-rapid"=>"forward-gun",
        _ when slot==ComponentSlot.MainGun&&hull?.Railgun?.Mounts is null&&hull is not null=>"forward-gun",
        _=>slot switch{ComponentSlot.MainGun=>"main-twin",ComponentSlot.Secondary=>"point-defense",
            ComponentSlot.Engine=>"engine",ComponentSlot.Sensor=>"sensor",ComponentSlot.Generator=>"generator",_=>"shield"}
    });
    internal static Texture2D Equipment(EquipmentCategory category)=>Icon(category switch
    {EquipmentCategory.Shield=>"shield",EquipmentCategory.Sensor=>"sensor",EquipmentCategory.Ecm=>"ecm",_=>"drone"});
}
