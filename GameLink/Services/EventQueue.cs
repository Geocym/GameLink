using System.Text.Json;
using GameLink.Models;
namespace GameLink.Services;

public sealed class PendingEvent
{
    public GameEvent Event { get; set; } = new();
    public DateTimeOffset Queued { get; set; } = DateTimeOffset.UtcNow;
    public List<string> Targets { get; set; } = new();
    public int Attempts { get; set; }
    public bool Recovered { get; set; }
    public List<string> ReplaySources { get; set; } = new();
}
internal sealed class EventQueue
{
    private readonly string dir = Path.Combine(LocalStorage.Root,"queue");
    private readonly List<PendingEvent> items = new();
    private readonly LogService log;
    private readonly Dictionary<string,DateTime> headerRetryAfter = new();
    public EventHistory? History { get; set; }
    public int Count => items.Count;
    public EventQueue(LogService log)
    {
        this.log=log; Directory.CreateDirectory(dir);
        foreach(var file in Directory.GetFiles(dir,"*.json")) try {
            var item=JsonSerializer.Deserialize<PendingEvent>(File.ReadAllText(file),GameEvent.JsonOptions) ?? throw new InvalidDataException();
            if(!Guid.TryParse(item.Event.EventId,out _) || Path.GetFileNameWithoutExtension(file)!=item.Event.EventId) throw new InvalidDataException("Invalid queue ID");
            item.Recovered=true; items.Add(item);
        } catch(Exception ex) { log.Error("Queue file retained: "+file+": "+ex.Message); }
        items.Sort((a,b)=>a.Queued.CompareTo(b.Queued));
    }
    private string FileFor(PendingEvent item)=>Path.Combine(dir,item.Event.EventId+".json");
    private void Save(PendingEvent item)=>LocalStorage.Write(FileFor(item),JsonSerializer.Serialize(item,GameEvent.JsonOptions));
    public bool Enqueue(GameEvent evt, AppSettings settings)
    {
        if(DeliveryReceipts.Read(evt.EventId).Queued)return false;
        if(items.Any(i=>i.Event.EventId==evt.EventId)){DeliveryReceipts.MarkQueued(evt.EventId);return false;}
        GameHeaderPath.Refresh(evt);
        var targets=StreamerBotClient.Targets(evt,settings);
        if(targets.Count==0) { History?.Attempt(evt,"","No route"); log.Info("No route for "+evt.Event); DeliveryReceipts.MarkQueued(evt.EventId); return true; }
        var item=new PendingEvent { Event=evt,Targets=targets }; Save(item); items.Add(item); DeliveryReceipts.MarkQueued(evt.EventId); foreach(var target in targets) History?.Attempt(evt,target,"Queued");
        log.Info($"Queued {evt.Event} ({Count} pending).");
        return true;
    }
    public List<string> ReplayTargets(GameEvent evt, AppSettings settings, bool skipAccepted)
    {
        var targets=StreamerBotClient.Targets(evt,settings);
        if(skipAccepted) { var accepted=new HashSet<string>(DeliveryReceipts.Read(evt.EventId).Accepted,StringComparer.OrdinalIgnoreCase); if(History is not null) accepted.UnionWith(History.AcceptedTargets(evt.EventId)); targets.RemoveAll(accepted.Contains); }
        return targets;
    }
    public void Replay(GameEvent evt, AppSettings settings, bool skipAccepted, IEnumerable<string> rejectedPaths)
    {
        if(!Guid.TryParse(evt.EventId,out _))throw new InvalidDataException("Invalid rejected event");
        if(items.Any(i=>i.Event.EventId==evt.EventId))throw new InvalidOperationException("This event already has pending delivery. Wait until it finishes.");
        var targets=ReplayTargets(evt,settings,skipAccepted);
        if(targets.Count==0)throw new InvalidOperationException("No current destinations remain for replay. Check routing settings.");
        var copy=JsonSerializer.Deserialize<GameEvent>(evt.ToJson(),GameEvent.JsonOptions)!;
        foreach(var key in new[]{"eventQueuedTimestamp","eventDeliveredTimestamp","eventDeliveryAttempts","eventDeliveryDelaySeconds","isDelayedEvent","isRecoveredEvent"})copy.Data.Remove(key);
        int count=copy.Data.TryGetValue("eventReplayCount",out var value)&&int.TryParse(value?.ToString(),out var n)?n:0;
        copy.Data["isReplayedEvent"]=true;copy.Data["eventReplayCount"]=count+1;copy.Data["eventReplayTimestamp"]=DateTimeOffset.UtcNow.ToString("O");copy.Data["originalEventId"]=evt.EventId;
        GameHeaderPath.Refresh(copy);
        var item=new PendingEvent{Event=copy,Targets=targets,ReplaySources=rejectedPaths.Select(p=>Path.GetFileName(p)!).ToList()};
        Save(item);items.Add(item);foreach(var target in targets)History?.Attempt(copy,target,"Queued","Manual replay using current routing");
    }
    public async Task Drain(StreamerBotClient client, CancellationToken ct, bool onlineMetadata)
    {
        while(items.Count>0 && !ct.IsCancellationRequested) {
            var item=items[0];
            DeliveryReceipts.MarkQueued(item.Event.EventId);
            foreach(var source in item.ReplaySources) { if(Path.GetFileName(source)!=source || !source.StartsWith(item.Event.EventId+"-",StringComparison.Ordinal))throw new InvalidDataException("Invalid replay source"); File.Delete(Path.Combine(LocalStorage.Root,"rejected",source)); }
            if(item.ReplaySources.Count>0){item.ReplaySources.Clear();Save(item); }
            if(item.Targets.Count==0) { File.Delete(FileFor(item)); items.RemoveAt(0); continue; }
            GameHeaderPath.Refresh(item.Event);
            if(onlineMetadata && !item.Event.Data.ContainsKey("gameHeaderLocalPath") &&
                string.Equals(item.Event.Platform,"Steam",StringComparison.OrdinalIgnoreCase) &&
                uint.TryParse(item.Event.Game.Id,out var appId) && appId > 0)
            {
                var id=appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if(!headerRetryAfter.TryGetValue(id,out var retryAt) || retryAt<=DateTime.UtcNow)
                {
                    headerRetryAfter[id]=DateTime.UtcNow.AddMinutes(2);
                    await new GameLink.Providers.Steam.SteamMetadata(log).CacheArtworkAsync(id,
                        $"https://cdn.cloudflare.steamstatic.com/steam/apps/{id}/header.jpg",2,ct);
                    GameHeaderPath.Refresh(item.Event);
                }
            }
            ct.ThrowIfCancellationRequested();
            item.Attempts++; Save(item);
            var now=DateTimeOffset.UtcNow; var delay=Math.Max(0,(now-item.Event.Timestamp).TotalSeconds);
            var data=item.Event.Data;
            data["eventQueuedTimestamp"]=item.Queued.ToString("O"); data["eventDeliveredTimestamp"]=now.ToString("O");
            data["eventDeliveryAttempts"]=item.Attempts; data["eventDeliveryDelaySeconds"]=delay;
            data["isDelayedEvent"]=delay>=5 || item.Attempts>1 || item.Recovered; data["isRecoveredEvent"]=item.Recovered;
            var target=item.Targets[0];
            try { await client.SendEventAsync(item.Event,ct,target); DeliveryReceipts.Accept(item.Event.EventId,target); History?.Attempt(item.Event,target,"Accepted"); }
            catch(RequestRejectedException ex) {
                var rejected=Path.Combine(LocalStorage.Root,"rejected",item.Event.EventId+"-"+Guid.NewGuid().ToString("N")+".json");
                LocalStorage.Write(rejected,JsonSerializer.Serialize(new { item.Event, Target=target, item.Attempts, Error=ex.Message },GameEvent.JsonOptions));
                History?.Attempt(item.Event,target,"Rejected",ex.Message);
                log.Error("Rejected destination archived: "+target+"; "+ex.Message);
            }
            catch(Exception ex) { History?.Attempt(item.Event,target,"Retry pending",ex.Message); throw; }
            item.Targets.RemoveAt(0);
            try { Save(item); } catch { item.Targets.Insert(0,target); throw; }
            if(item.Targets.Count==0) { File.Delete(FileFor(item)); items.RemoveAt(0); }
        }
    }
}
