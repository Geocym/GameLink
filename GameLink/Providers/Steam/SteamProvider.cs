using GameLink.Models;
using GameLink.Services;
namespace GameLink.Providers.Steam;
internal sealed class SteamProvider(LogService log, AppSettings settings) : IGameProvider
{
    public event Action<GameEvent>? EventDetected;
    public event Action<string>? StatusChanged;
    private readonly System.Windows.Forms.Timer timer=new(){Interval=3000};
    private SteamSnapshot? snapshot;
    private readonly Dictionary<string,SteamGame> active=new();
    private DateTime refreshAt=DateTime.MinValue;
    private bool busy, disposed, baseline;
    private readonly SteamAchievementMonitor achievements = new();
    private string? lastStatus;
    private readonly SteamMetadata metadata=new(log);
    private readonly Dictionary<string,(DateTime Start,int Count,int Rare)> sessions=new();
    internal AppSettings Settings { get; set; } = settings;
    private readonly HashSet<string> warnings=new();
    internal IEnumerable<(SteamGame Game, DateTime Start, int Count, int? Total, int? Unlocked)> DashboardGames()
    {
        foreach(var g in active.Values) { var session=sessions.GetValueOrDefault(g.Id); bool known=achievements.Progress.TryGetValue(g.Id,out var p); yield return (g,session.Start,session.Count,known?p.Total:null,known?p.Unlocked:null); }
    }
    public void Start()
    {
        timer.Tick += async (_,_) => await Scan();
        timer.Start();
        _=Scan();
    }
    private async Task Scan()
    {
        if(busy || disposed) return;
        busy=true;
        try {
            var previous=snapshot;
            bool refresh=DateTime.UtcNow>=refreshAt;
            var result=await Task.Run(()=>{
                var notes=new List<string>();
                var ids=SteamDiscovery.RunningIds();
                var snap=refresh || previous==null ? SteamDiscovery.Scan(notes.Add) : previous;
                var unlocked = achievements.Scan(snap, ids, notes.Add, Settings.RecoverOfflineAchievements);
                return (snap, ids, notes, unlocked);
            });
            if(disposed) return;
            snapshot=result.snap;
            if(refresh) refreshAt=DateTime.UtcNow.AddSeconds(30);
            foreach(var note in result.notes) if(note.StartsWith("Achievement unlocked:") || note.StartsWith("Steam stats:") || note.StartsWith("Steam cache changed:") || warnings.Add(note)) log.Info(note,note.StartsWith("Achievement unlocked:")?LogCategory.Achievement:LogCategory.Steam);
            var current=new Dictionary<string,SteamGame>();
            foreach(var id in result.ids)
            {
                if(snapshot.Games.TryGetValue(id,out var game)) current[id]=game;
                else if(active.TryGetValue(id,out game)) current[id]=game;
            }
            foreach(var pair in current) if(!sessions.ContainsKey(pair.Key)) sessions[pair.Key]=(DateTime.UtcNow,0,0);
            foreach(var evt in result.unlocked) {
                try { await metadata.Enrich(evt,Settings); } catch(Exception ex) { log.Error("Optional metadata: "+ex.Message); }
                if(disposed) return;
                if(!(evt.Data.TryGetValue("isRecoveredAchievement",out var recovered) && recovered is true) && evt.Event=="achievement.unlocked" && evt.Game.Id is string id && sessions.TryGetValue(id,out var session))
                    sessions[id]=(session.Start,session.Count+1,session.Rare+(evt.Data.TryGetValue("isRareAchievement",out var rare) && rare is true?1:0));
            }
            // Existing games are shown on startup but do not trigger launch alerts.
            if(baseline)
            {
                
                foreach(var pair in current) if(!active.ContainsKey(pair.Key)) Emit("game.started",pair.Value);
            }
            foreach(var evt in result.unlocked) EventDetected?.Invoke(evt);
            foreach(var pair in active) if(!result.ids.Contains(pair.Key)) {
                Emit("game.stopped",pair.Value);
                if(sessions.Remove(pair.Key,out var session)) {
                    var summary=new GameEvent { Event="game.session_summary",Platform="Steam",Game=new GameIdentity { Id=pair.Key,Name=pair.Value.Name },Data=new() { ["steamAppId"]=pair.Key,["testMode"]=false,["detectionSource"]="steam.registry",["gameSessionDurationSeconds"]=(long)(DateTime.UtcNow-session.Start).TotalSeconds,["sessionAchievementsUnlocked"]=session.Count,["sessionRareAchievementsUnlocked"]=session.Rare } };
                    if(achievements.Progress.TryGetValue(pair.Key,out var progress)) SteamAchievementMonitor.AddProgress(summary.Data,progress.Total,progress.Unlocked);
                    EventDetected?.Invoke(summary);
                }
            }
            active.Clear();
            foreach(var pair in current) active.Add(pair.Key,pair.Value);
            baseline=true;
            var status=snapshot.Path==null ? "Steam not found (checking automatically)" :
                $"Steam: {snapshot.Path} | Account: {snapshot.AccountName ?? snapshot.AccountId ?? "Not signed in"}\n"+
                $"Libraries: {snapshot.Libraries} | Installed apps: {snapshot.Games.Count} | Running: "+
                (active.Count==0 ? "None" : string.Join(", ",active.Values.Select(g=>$"{g.Name} ({g.Id})")));
            if(status!=lastStatus) { lastStatus=status; StatusChanged?.Invoke(status); log.Info(status.Replace("\n"," | ")); }
        } catch(Exception ex) { if(!disposed && warnings.Add(ex.Message)) log.Error("Steam scan: "+ex.Message); }
        finally { busy=false; }
    }
    private void Emit(string type, SteamGame game)
    {
        log.Info($"{type}: {game.Name} ({game.Id})",LogCategory.Steam);
        EventDetected?.Invoke(new GameEvent { Event=type, Platform="Steam",
            Game=new GameIdentity { Name=game.Name, Id=game.Id },
            Data=new() { ["steamAppId"]=game.Id, ["testMode"]=false, ["detectionSource"]="steam.registry" } });
    }
    public void Dispose() { disposed=true; timer.Stop(); timer.Dispose(); }
}
