using GameLink.Models;
namespace GameLink.Providers.Steam;
internal sealed class SteamAchievementMonitor
{
    private string? context;
    private readonly Dictionary<string,HashSet<string>> states=new();
    private readonly HashSet<string> emitted=new();
    private readonly HashSet<string> tracked=new();
    private readonly Dictionary<string,DateTime> ended=new();
    private readonly Dictionary<string,DateTime> started=new();
    private readonly HashSet<string> errors=new();
    private bool initial=true;
    internal List<GameEvent> Scan(SteamSnapshot snap, HashSet<string> running, Action<string> note)
    {
        var events=new List<GameEvent>();
        if(snap.Path==null || snap.AccountId==null || !ulong.TryParse(snap.AccountId,out var steamId) || steamId<76561197960265728UL) { context=null; return events; }
        var key=snap.Path+"|"+snap.AccountId;
        var now=DateTime.UtcNow;
        if(context!=key) { context=key; states.Clear(); emitted.Clear(); tracked.Clear(); ended.Clear(); started.Clear(); errors.Clear(); initial=true; note("Achievement diagnostics: binary Steam cache monitoring."); }
        var dir=Path.Combine(snap.Path,"appcache","stats");
        var account=steamId-76561197960265728UL;
        foreach(var id in running) if(tracked.Add(id)) started[id]=now;
        foreach(var id in tracked.ToArray()) {
            if(running.Contains(id)) ended.Remove(id);
            else if(!ended.ContainsKey(id)) ended[id]=now;
            else if(now-ended[id]>TimeSpan.FromMinutes(2)) { tracked.Remove(id); ended.Remove(id); started.Remove(id); }
        }
        // Baseline installed caches even before games launch. Continue scanning recently closed games.
        foreach(var id in snap.Games.Keys.Union(tracked)) {
            var file=Path.Combine(dir,$"UserGameStats_{account}_{id}.bin");
            var schemaFile=Path.Combine(dir,$"UserGameStatsSchema_{id}.bin");
            if(!File.Exists(file) || !File.Exists(schemaFile)) continue;
            try {
                var cache=BinaryKeyValues.Load(file).Node("cache") ?? throw new InvalidDataException("Missing cache root");
                var schema=BinaryKeyValues.Load(schemaFile).Node(id)?.Node("stats") ?? throw new InvalidDataException("Missing stats schema");
                var current=new HashSet<string>();
                bool known=states.TryGetValue(id,out var old);
                foreach(var group in schema) {
                    if(group.Value is not BinaryKeyValues definition || definition.Node("bits") is not {} bits) continue;
                    var data=cache.Node(group.Key); uint mask=data?.Number("data") ?? 0;
                    foreach(var bit in bits) {
                        if(!int.TryParse(bit.Key,out int index) || index<0 || index>31 || bit.Value is not BinaryKeyValues achievement || (mask & (1u<<index))==0) continue;
                        string slot=group.Key+":"+bit.Key; current.Add(slot);
                        if(initial || !tracked.Contains(id) || (known && old!.Contains(slot))) continue;
                        uint? stamp=data?.Node("AchievementTimes")?.Number(bit.Key);
                        DateTimeOffset? unlock=stamp is >0 ? DateTimeOffset.FromUnixTimeSeconds(stamp.Value) : null;
                        // Require a recent timestamp for newly discovered caches, to avoid historical alerts.
                        if(!known && (unlock==null || unlock.Value.UtcDateTime<started[id].AddSeconds(-5))) continue;
                        if(unlock!=null && (unlock.Value.UtcDateTime<started[id].AddSeconds(-5) || unlock.Value.UtcDateTime>now.AddMinutes(2))) continue;
                        if(!snap.Games.TryGetValue(id,out var game)) continue;
                        if(!emitted.Add(id+":"+slot)) continue;
                        var internalId=achievement.Text("name") ?? slot;
                        var display=achievement.Node("display");
                        string? Local(string field) => display?.Text(field) ?? display?.Node(field)?.Text("english");
                        var name=Local("name") ?? internalId;
                        var icon=display?.Text("icon");
                        var args=new Dictionary<string,object?> { ["steamAppId"]=id,["achievementId"]=internalId,["achievementName"]=name,["achievementDescription"]=Local("desc") ?? "",["achievementIcon"]=icon==null ? "" : $"https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{id}/{icon}",["testMode"]=false,["detectionSource"]="steam.binary_cache" };
                        if(unlock!=null) args["unlockTimestamp"]=unlock.Value.ToString("O");
                        events.Add(new GameEvent { Event="achievement.unlocked", Platform="Steam", Game=new GameIdentity { Id=id,Name=game.Name }, Data=args });
                        note($"Achievement unlocked: {game.Name} / {name} ({internalId})");
                    }
                }
                if(!known) note($"Achievement baseline: AppID {id}, {current.Count} unlocked.");
                states[id]=current;
            } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException) { if(errors.Add(id+ex.Message)) note($"Achievement cache {id}: {ex.Message}; will retry."); }
        }
        initial=false;
        return events;
    }
}
