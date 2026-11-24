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
}
internal sealed class EventQueue
{
    private readonly string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameLink", "queue");
    private readonly List<PendingEvent> items = new();
    private readonly LogService log;
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
    public void Enqueue(GameEvent evt, AppSettings settings)
    {
        var targets=StreamerBotClient.Targets(evt,settings);
        if(targets.Count==0) { log.Info("No route for "+evt.Event); return; }
        var item=new PendingEvent { Event=evt,Targets=targets }; Save(item); items.Add(item);
        log.Info($"Queued {evt.Event} ({Count} pending).");
    }
    public async Task Drain(StreamerBotClient client, CancellationToken ct)
    {
        while(items.Count>0 && !ct.IsCancellationRequested) {
            var item=items[0];
            if(item.Targets.Count==0) { File.Delete(FileFor(item)); items.RemoveAt(0); continue; }
            item.Attempts++; Save(item);
            var now=DateTimeOffset.UtcNow; var delay=Math.Max(0,(now-item.Event.Timestamp).TotalSeconds);
            var data=item.Event.Data;
            data["eventQueuedTimestamp"]=item.Queued.ToString("O"); data["eventDeliveredTimestamp"]=now.ToString("O");
            data["eventDeliveryAttempts"]=item.Attempts; data["eventDeliveryDelaySeconds"]=delay;
            data["isDelayedEvent"]=delay>=5 || item.Attempts>1 || item.Recovered; data["isRecoveredEvent"]=item.Recovered;
            var target=item.Targets[0];
            try { await client.SendEventAsync(item.Event,ct,target); }
            catch(RequestRejectedException ex) {
                var rejected=Path.Combine(LocalStorage.Root,"rejected",item.Event.EventId+"-"+Guid.NewGuid().ToString("N")+".json");
                LocalStorage.Write(rejected,JsonSerializer.Serialize(new { item.Event, Target=target, item.Attempts, Error=ex.Message },GameEvent.JsonOptions));
                log.Error("Rejected destination archived: "+target+"; "+ex.Message);
            }
            item.Targets.RemoveAt(0);
            try { Save(item); } catch { item.Targets.Insert(0,target); throw; }
            if(item.Targets.Count==0) { File.Delete(FileFor(item)); items.RemoveAt(0); }
        }
    }
}
