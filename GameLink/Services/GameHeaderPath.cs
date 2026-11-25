using GameLink.Models;
using GameLink.Providers.Steam;
namespace GameLink.Services;
internal static class GameHeaderPath
{
    internal static void Refresh(GameEvent evt)
    {
        evt.Data.Remove("gameHeaderLocalPath");
        if(!string.Equals(evt.Platform,"Steam",StringComparison.OrdinalIgnoreCase) || !uint.TryParse(evt.Game.Id,out var id))return;
        evt.Data["gameHeaderImage"]=$"https://cdn.cloudflare.steamstatic.com/steam/apps/{id}/header.jpg";
        var path=Path.GetFullPath(Path.Combine(LocalStorage.Root,"artwork",id.ToString(System.Globalization.CultureInfo.InvariantCulture),"header.jpg"));
        if(SteamMetadata.ValidImage(path))evt.Data["gameHeaderLocalPath"]=path;
    }
}
