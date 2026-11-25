using System.Text.Json;
using GameLink.Models;
using GameLink.Services;
namespace GameLink.Providers.Steam;
internal sealed class SteamMetadata(LogService log)
{
    private static readonly HttpClient Http=new(){Timeout=TimeSpan.FromSeconds(2)};
    private static readonly HttpClient ArtworkHttp = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly Dictionary<string,(DateTime Until,Dictionary<string,double> Values)> rarity=new();
    public sealed record RarityCache(DateTime Until, Dictionary<string,double> Values);
    internal async Task Enrich(GameEvent evt, AppSettings settings)
    {
        if(evt.Game.Id is not string id || !uint.TryParse(id,out _)) return;
        evt.Data["gameStoreUrl"]=$"https://store.steampowered.com/app/{id}/";
        evt.Data["gameHeaderImage"]=$"https://cdn.cloudflare.steamstatic.com/steam/apps/{id}/header.jpg";
        if(!settings.EnableOnlineMetadata || !evt.Data.TryGetValue("achievementId",out var raw) || raw is not string achievement) return;
        var cacheFile=Path.Combine(LocalStorage.Root,"metadata",id+".json");
        if(!rarity.ContainsKey(id)) try {
            if(File.Exists(cacheFile)) { var saved=JsonSerializer.Deserialize<RarityCache>(File.ReadAllText(cacheFile)); if(saved!=null) rarity[id]=(saved.Until,saved.Values); }
        } catch(Exception ex) { log.Error("Rarity cache: "+ex.Message); }
        if(!rarity.TryGetValue(id,out var entry) || entry.Until<DateTime.UtcNow) {
            var values=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
            try {
                using var doc=JsonDocument.Parse(await Http.GetStringAsync($"https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid={id}"));
                foreach(var item in doc.RootElement.GetProperty("achievementpercentages").GetProperty("achievements").EnumerateArray()) {
                    var p=item.GetProperty("percent"); double n;
                    if((p.ValueKind==JsonValueKind.Number && p.TryGetDouble(out n) || p.ValueKind==JsonValueKind.String && double.TryParse(p.GetString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out n)) && n>=0 && n<=100) values[item.GetProperty("name").GetString()!]=n;
                }
            } catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException) { }
            entry=(DateTime.UtcNow.AddMinutes(values.Count>0?60:5),values); rarity[id]=entry;
            if(values.Count>0) try { LocalStorage.Write(cacheFile,JsonSerializer.Serialize(new RarityCache(entry.Until,values))); } catch(Exception ex) { log.Error("Rarity cache: "+ex.Message); }
        }
        if(entry.Values.TryGetValue(achievement,out double percent)) {
            evt.Data["achievementRarity"]=percent;
            evt.Data["achievementRarityLabel"]=percent<=settings.LegendaryThreshold?"Legendary":percent<=settings.EpicThreshold?"Epic":percent<=settings.RareThreshold?"Rare":percent<=settings.UncommonThreshold?"Uncommon":"Common";
            evt.Data["isRareAchievement"]=percent<=settings.RareThreshold;
        }
        if(evt.Data.TryGetValue("achievementIcon",out var icon) && icon is string url)
        {
            var path = await CacheArtworkAsync(id, url);
            if(path != null) evt.Data["achievementIconLocalPath"] = path;
        }
    }

    internal async Task<string?> CacheArtworkAsync(string id, string url)
    {
        if(!uint.TryParse(id,out _) || !Uri.TryCreate(url,UriKind.Absolute,out var uri) ||
            uri.Scheme != "https" || uri.Host != "cdn.cloudflare.steamstatic.com") return null;
        var name = Path.GetFileName(uri.AbsolutePath);
        if(string.IsNullOrEmpty(name) || name.Length > 128 || name.Any(c=>!char.IsAsciiLetterOrDigit(c) && c!='.' && c!='_') || name is "." or "..") return null;
        string? temp = null;
        try {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"GameLink","artwork",id);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir,name);
            if(File.Exists(path) && ValidImage(path)) { log.Debug($"Artwork: cache hit {path}",LogCategory.Artwork); return path; }
            var fallback = new UriBuilder(uri) { Host = "cdn.akamai.steamstatic.com" }.Uri;
            foreach(var candidate in new[] { uri, fallback }) {
                try {
                    log.Debug($"Artwork: downloading {candidate}",LogCategory.Artwork);
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var response = await ArtworkHttp.GetAsync(candidate,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
                    if(!response.IsSuccessStatusCode) { log.Error($"Artwork: HTTP {(int)response.StatusCode} {response.ReasonPhrase}"); continue; }
                    if(response.Content.Headers.ContentLength > 2_000_000) { log.Error("Artwork: response exceeds size limit."); continue; }
                    using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
                    using var bytes = new MemoryStream();
                    var buffer = new byte[8192];
                    int count;
                    while((count = await stream.ReadAsync(buffer.AsMemory(),deadline.Token)) > 0) {
                        if(bytes.Length + count > 2_000_000) throw new InvalidDataException("Artwork exceeds size limit.");
                        bytes.Write(buffer,0,count);
                    }
                    temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    await File.WriteAllBytesAsync(temp,bytes.ToArray(),deadline.Token);
                    if(!ValidImage(temp)) throw new InvalidDataException("Response is not a valid image.");
                    File.Move(temp,path,true);
                    log.Info($"Artwork: cached {path} ({bytes.Length} bytes)",LogCategory.Artwork);
                    return path;
                } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException or ArgumentException or InvalidDataException) {
                    log.Error($"Artwork: {candidate.Host}: {ex.GetType().Name}: {ex.Message}");
                } finally {
                    if(temp != null) { try { File.Delete(temp); } catch(IOException) {} catch(UnauthorizedAccessException) {} temp = null; }
                }
            }
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException) { log.Error($"Artwork: {ex.GetType().Name}: {ex.Message}"); }
        log.Info("Artwork: local cache unavailable; retaining remote URL.");
        return null;
    }
    private static bool ValidImage(string path)
    {
        try {
            var size = new FileInfo(path).Length;
            if(size <= 0 || size > 2_000_000) return false;
            using var stream = File.OpenRead(path);
            using var image = System.Drawing.Image.FromStream(stream,false,true);
            return image.Width > 0 && image.Height > 0;
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.ExternalException or OutOfMemoryException) { return false; }
    }
}
