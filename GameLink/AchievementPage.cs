using GameLink.Providers.Steam;
using GameLink.Services;
using GameLink.Models;
using System.Text.Json;
namespace GameLink;
internal sealed class AchievementPage : UserControl
{
    private record Entry(string Game,string Id,string Name,string Description,string State,DateTimeOffset? Date,double? Rarity,string? Icon,int Total,int Unlocked,bool Hidden,string Key);
    private List<Entry> entries=new();
    private readonly HashSet<string> revealed=new();
    private string? gameId;
    private readonly TextBox search=new(){PlaceholderText="Search games or achievements",Width=290};
    private readonly ComboBox filter=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=110};
    private readonly ComboBox sort=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=145};
    private readonly Label status=new(){Dock=DockStyle.Top,Height=48};
    private readonly Button back=new(){Text="‹ All games",AutoSize=true};
    private readonly Button revealAll=new(){Text="Reveal hidden",AutoSize=true};
    private readonly CardSurface cards=new(){Dock=DockStyle.Fill};
    private readonly Func<AppSettings> settings;
    private readonly LogService log;
    private bool busy;
    private readonly Button hideAll=new(){Text="Hide all",AutoSize=true};
    private readonly List<Entry> displayed=new();
    private readonly Dictionary<string,DateTime> copied=new();
    private readonly System.Windows.Forms.Timer copyTimer=new(){Interval=250};
    private string sourceStatus="";
    public AchievementPage(Func<AppSettings> settings,LogService log)
    {
        this.settings=settings;this.log=log;
        var bar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=76,WrapContents=true,Padding=new Padding(4)};
        filter.Items.AddRange(["All","Unlocked","Locked","Unknown"]);filter.SelectedIndex=0;
        sort.Items.AddRange(["Name","Completion","Rarest first","Newest unlock"]);sort.SelectedIndex=0;
        var refresh=new Button{Text="Refresh",AutoSize=true};refresh.Click+=async (_,_)=>await RefreshAsync();
        back.Click+=(_,_)=>{gameId=null;revealed.Clear();search.Clear();Render();};
        revealAll.Click+=(_,_)=>{foreach(var e in displayed.Where(e=>e.Hidden))revealed.Add(Key(e));UpdateDisplayed();};
        hideAll.Click+=(_,_)=>{revealed.Clear();UpdateDisplayed();};
        cards.Concealed+=i=>{if(i>=0&&i<displayed.Count){revealed.Remove(Key(displayed[i]));cards.UpdateCard(i,MakeCard(displayed[i]));}};
        copyTimer.Tick+=(_,_)=>{foreach(var key in copied.Where(x=>x.Value<=DateTime.UtcNow).Select(x=>x.Key).ToArray()){copied.Remove(key);for(int i=0;i<displayed.Count;i++)if(Key(displayed[i])==key)cards.UpdateCard(i,MakeCard(displayed[i]));}};copyTimer.Start();
        bar.Controls.AddRange([back,search,filter,sort,revealAll,hideAll,refresh]);
        Controls.Add(cards);Controls.Add(status);Controls.Add(bar);
        search.TextChanged+=(_,_)=>Render();filter.SelectedIndexChanged+=(_,_)=>Render();sort.SelectedIndexChanged+=(_,_)=>Render();
        cards.Activated+=i=>{if(i>=0&&i<actions.Count)actions[i]();};
        Render();
    }
    private readonly List<Action> actions=new();
    internal async Task RefreshAsync()
    {
        if(busy)return;busy=true;status.Text="Reading local Steam achievement caches…";
        try{var result=await Task.Run(Read);if(IsDisposed)return;entries=result.Item1;sourceStatus=result.Item2;Render();}
        catch(Exception ex){if(!IsDisposed)status.Text="Unable to read Steam data: "+ex.Message;}
        finally{busy=false;}
    }
    internal async Task OpenGameAsync(string id){gameId=id;revealed.Clear();search.Clear();filter.SelectedIndex=0;cards.ResetScroll();if(!busy)await RefreshAsync();Render();}
    private static (List<Entry>,string) Read()
    {
        var snap=SteamDiscovery.Scan(_=>{}); var rows=new List<Entry>();
        if(snap.Path==null || !ulong.TryParse(snap.AccountId,out var account)||account<76561197960265728UL)return(rows,"Sign in to Steam to view your achievements.");
        var dir=Path.Combine(snap.Path,"appcache","stats"); int unavailable=0,errors=0;
        foreach(var game in snap.Games.Values){
            var schemaPath=Path.Combine(dir,$"UserGameStatsSchema_{game.Id}.bin");
            if(!File.Exists(schemaPath)){unavailable++;continue;}
            try{
                var schema=BinaryKeyValues.Load(schemaPath).Node(game.Id)?.Node("stats")??throw new InvalidDataException();
                var cachePath=Path.Combine(dir,$"UserGameStats_{account-76561197960265728UL}_{game.Id}.bin");
                var cache=File.Exists(cachePath)?BinaryKeyValues.Load(cachePath).Node("cache"):null;
                Dictionary<string,double> rarity=new(); try{var p=Path.Combine(LocalStorage.Root,"metadata",game.Id+".json");if(File.Exists(p))rarity=JsonSerializer.Deserialize<SteamMetadata.RarityCache>(File.ReadAllText(p))?.Values??new();}catch{}
                var gameRows=new List<Entry>();
                foreach(var group in schema){if(group.Value is not BinaryKeyValues def || def.Node("bits") is not {} bits)continue;
                    var data=cache?.Node(group.Key);var mask=data?.Number("data")??0;
                    foreach(var bit in bits){if(!int.TryParse(bit.Key,out var index)||index<0||index>31||bit.Value is not BinaryKeyValues a)continue;
                        bool unlocked=(mask&(1u<<index))!=0;var state=cache==null?"Unknown":unlocked?"Unlocked":"Locked";
                        var display=a.Node("display");string? Local(string field)=>display?.Text(field)??display?.Node(field)?.Text("english");
                        var id=a.Text("name")??group.Key+":"+bit.Key;var stamp=data?.Node("AchievementTimes")?.Number(bit.Key);
                        var hidden=display?.Number("hidden")==1||display?.Text("hidden")=="1";
                        var icon=display?.Text(unlocked?"icon":"icon_gray");
                        gameRows.Add(new(game.Name,game.Id,Local("name")??id,Local("desc")??"",state,unlocked&&stamp is >0?DateTimeOffset.FromUnixTimeSeconds(stamp.Value):null,rarity.TryGetValue(id,out var r)?r:null,icon,0,0,hidden&&!unlocked,id));
                    }
                }
                int count=gameRows.Count(e=>e.State=="Unlocked");rows.AddRange(gameRows.Select(e=>e with{Total=gameRows.Count,Unlocked=cache==null?-1:count}));
            }catch{errors++;}
        }
        return(rows,$"{snap.AccountName??snap.AccountId} | {unavailable} installed apps without schema; {errors} unreadable caches");
    }
    private bool VisibleDetails(Entry e)=>!e.Hidden||revealed.Contains(e.Id+":"+e.Key);
    private string Name(Entry e)=>VisibleDetails(e)?e.Name:"Hidden achievement";
    private string Description(Entry e)=>VisibleDetails(e)?e.Description:"Click to reveal details (spoilers).";
    private void Render()
    {
        var q=search.Text.Trim();var f=filter.Text;
        var matching=entries.Where(e=>(gameId==null||e.Id==gameId)&&(f=="All"||e.State==f)&&(e.Game.Contains(q,StringComparison.OrdinalIgnoreCase)||Name(e).Contains(q,StringComparison.OrdinalIgnoreCase)||Description(e).Contains(q,StringComparison.OrdinalIgnoreCase))).ToList();
        var items=new List<VisualCard>();actions.Clear();displayed.Clear();back.Visible=gameId!=null;revealAll.Visible=gameId!=null;hideAll.Visible=gameId!=null;
        if(gameId==null){
            var groups=matching.GroupBy(e=>e.Id).Select(g=>g.First());
            groups=sort.SelectedIndex==1?groups.OrderByDescending(e=>e.Unlocked<0?-1:(double)e.Unlocked/e.Total):groups.OrderBy(e=>e.Game);
            foreach(var e in groups){var count=matching.Count(x=>x.Id==e.Id);items.Add(new(e.Game,e.Unlocked<0?"Progress unknown":$"{e.Unlocked:N0} / {e.Total:N0} unlocked · {100.0*e.Unlocked/e.Total:0.0}%",$"{count:N0} matching achievements  ›",e.Unlocked<0?null:(double)e.Unlocked/e.Total,Artwork.Header(e.Id),e.Id));actions.Add(()=>{gameId=e.Id;cards.ResetScroll();Render();});}
            status.Text=$"Your games · {items.Count:N0} matching games\n{sourceStatus}";
        }else{
            var g=entries.FirstOrDefault(e=>e.Id==gameId);
            status.Text=g==null?"Game data unavailable":$"{g.Game} · {(g.Unlocked<0?"Progress unknown":$"{g.Unlocked:N0}/{g.Total:N0} unlocked · {100.0*g.Unlocked/g.Total:0.0}%")}\n{matching.Count:N0} matching achievements";
            IEnumerable<Entry> ordered=sort.SelectedIndex switch{2=>matching.OrderBy(e=>e.Rarity??double.MaxValue),3=>matching.OrderByDescending(e=>e.Date),_=>matching.OrderBy(Name)};
            foreach(var e in ordered){int index=items.Count;displayed.Add(e);items.Add(MakeCard(e));actions.Add(()=>Activate(e,index));}
        }
        cards.SetCards(items,gameId==null,settings().EnableOnlineMetadata,log);
    }
    private void UpdateDisplayed(){for(int i=0;i<displayed.Count;i++)cards.UpdateCard(i,MakeCard(displayed[i]));}
    private static string Key(Entry e)=>e.Id+":"+e.Key;
    private VisualCard MakeCard(Entry e){bool visible=VisibleDetails(e);var art=visible&&e.Icon is string icon?Artwork.Achievement(e.Id,icon):null;return new(Name(e),Description(e),$"{e.State} · {(e.Rarity is double r?$"{r:0.##}% global":"Rarity unknown")} · {e.Date?.LocalDateTime.ToString("g")??""}",null,art,e.Id,!visible?"Reveal":copied.ContainsKey(Key(e))?"Copied!":"Copy",!visible,e.Hidden&&visible);}
    private void Activate(Entry e,int index){if(!VisibleDetails(e)){revealed.Add(Key(e));cards.UpdateCard(index,MakeCard(e));return;}try{Clipboard.SetText($"{e.Game} — {e.Name}\n{e.Description}\n{e.State} · {e.Date?.LocalDateTime:g}\n{(e.Rarity is double r?$"{r:0.##}% global":"Rarity unknown")}");copied[Key(e)]=DateTime.UtcNow.AddSeconds(2);cards.UpdateCard(index,MakeCard(e));}catch(Exception ex){log.Error("Copy achievement: "+ex.Message);status.Text="Unable to copy achievement; please try again.";}}
    protected override void Dispose(bool disposing){if(disposing)copyTimer.Dispose();base.Dispose(disposing);}

}
