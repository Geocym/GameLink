using GameLink.Models;
using GameLink.Services;
using GameLink.Providers.Steam;

namespace GameLink;

public sealed class MainForm : Form
{
    private readonly SettingsStore _store = new();
    private readonly LogService _log = new();
    private AppSettings _settings;
    private StreamerBotClient? _client;
    private ProcessMonitor? _processMonitor;
    private readonly TextBox _logBox = new();
    private readonly Label _status = new();
    private readonly NotifyIcon _tray = new();
    private bool _allowExit;
    private readonly CardSurface _activity=new(){Dock=DockStyle.Fill};
    private readonly CardSurface _current=new(){Dock=DockStyle.Top,Height=245};
    private readonly Label _dashboardStats=new(){Dock=DockStyle.Top,Height=48,Font=new Font("Segoe UI",12)};
    private readonly System.Windows.Forms.Timer _dashboardTimer=new(){Interval=3000};
    private readonly ListBox _history=new(){Dock=DockStyle.Left,Width=340};
    private readonly TextBox _payload=new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false};
    private readonly Label _playing=new(){Dock=DockStyle.Top,Height=90,Font=new Font("Segoe UI",16)};
    private readonly List<GameEvent> _recent=new();
    private SteamProvider? _steam;
    private readonly Label _steamStatus = new();
    private bool _connecting, _draining, _settingsChanged;
    private readonly EventQueue _queue;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly System.Windows.Forms.Timer _reconnect = new() { Interval = 15000 };

    public MainForm()
    {
        _settings = _store.Load();
        _log.Enabled=_settings.EnableFileLogging; _reconnect.Interval=Math.Clamp(_settings.RetryIntervalSeconds,5,300)*1000;
        _queue = new EventQueue(_log);
        Text = "GameLink v0.2.5.2 - Game Monitoring Middleware";
        Width = 1120; Height = 720;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 540);
        Font = new Font("Segoe UI", 10);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8), WrapContents = false };
        var settingsButton = new Button { Text = "Settings", AutoSize = true };
        var connectButton = new Button { Text = "Connect to Streamer.bot", AutoSize = true };
        var testButton = new Button { Text = "Send test event", AutoSize = true };
        var monitorButton = new Button { Text = "Start game monitor", AutoSize = true };
        settingsButton.Click += (_, _) => EditSettings();
        connectButton.Click += async (_, _) => await ConnectAsync();
        testButton.Click += async (_, _) => await SendTestAsync();
        monitorButton.Click += (_, _) => StartProcessMonitor();
        toolbar.Controls.AddRange([settingsButton, connectButton, testButton, monitorButton]);

        _status.Dock = DockStyle.Top;
        _status.Height = 28;
        _status.Padding = new Padding(10, 6, 0, 0);
        _status.Text = "Disconnected";

        _logBox.Dock = DockStyle.Fill;
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.Font = new Font("Consolas", 9);
        var tabs=new TabControl{Dock=DockStyle.Fill};
        TabPage Page(string name){var page=new TabPage(name){Padding=new Padding(12)};tabs.TabPages.Add(page);return page;}
        var dashboard=Page("Dashboard"); var browser=Page("Achievements"); var history=Page("Event history"); var platforms=Page("Platforms"); var diagnostics=Page("Diagnostics"); var settingsPage=Page("Settings");
        dashboard.Controls.Add(_activity); dashboard.Controls.Add(new Label{Text="Recent gaming activity",Dock=DockStyle.Top,Height=32,Font=new Font("Segoe UI",14,FontStyle.Bold)}); dashboard.Controls.Add(_dashboardStats); dashboard.Controls.Add(_current); dashboard.Controls.Add(new Label{Text="Currently playing",Dock=DockStyle.Top,Height=32,Font=new Font("Segoe UI",14,FontStyle.Bold)});
        _dashboardTimer.Tick+=(_,_)=>RefreshDashboard();_dashboardTimer.Start();RefreshDashboard();
        var achievements=new AchievementPage(()=>_settings,_log){Dock=DockStyle.Fill}; browser.Controls.Add(achievements);
        tabs.SelectedIndexChanged+=async (_,_)=>{if(tabs.SelectedTab==browser)await achievements.RefreshAsync();};
        history.Controls.Add(_payload);history.Controls.Add(_history);
        _history.SelectedIndexChanged+=(_,_)=>{var i=_history.SelectedIndex;_payload.Text=i>=0&&i<_recent.Count?System.Text.Json.JsonSerializer.Serialize(_recent[i],new System.Text.Json.JsonSerializerOptions(GameEvent.JsonOptions){WriteIndented=true}):"";};
        var copy=new Button{Text="Copy JSON",Dock=DockStyle.Bottom};copy.Click+=(_,_)=>{if(_payload.TextLength>0)Clipboard.SetText(_payload.Text);};history.Controls.Add(copy);
        diagnostics.Controls.Add(_logBox);
        var edit=new Button{Text="Open configuration",AutoSize=true,Dock=DockStyle.Top};edit.Click+=(_,_)=>EditSettings();settingsPage.Controls.Add(edit);
        var storage=new Button{Text="Open GameLink data folder",AutoSize=true,Dock=DockStyle.Top};storage.Click+=(_,_)=>{try{Directory.CreateDirectory(LocalStorage.Root);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LocalStorage.Root){UseShellExecute=true});}catch(Exception ex){_log.Error(ex.Message);}};settingsPage.Controls.Add(storage);
        Controls.Add(tabs);
        _steamStatus.Dock = DockStyle.Top;
        _steamStatus.Height = 65;
        _steamStatus.Padding = new Padding(10, 6, 10, 0);
        _steamStatus.Text = "Discovering Steam...";
        platforms.Controls.Add(_steamStatus);
        _steamStatus.Height=150;
        Controls.Add(_status);
        Controls.Add(toolbar);

        _log.Message += AddLog;
        _tray.Text = "GameLink";
        _tray.Icon = SystemIcons.Application;
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; };
        _tray.ContextMenuStrip = new ContextMenuStrip();
        _tray.ContextMenuStrip.Items.Add("Open", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; });
        _tray.ContextMenuStrip.Items.Add("Test artwork cache", null, async (_, _) => {
            if(!_settings.EnableOnlineMetadata) { _log.Info("Artwork test: online metadata disabled."); return; }
            var path = await new SteamMetadata(_log).CacheArtworkAsync("445980", "https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/445980/d1c08aabf952603a1f50f696c90ea45d7074c3ac.jpg");
            _log.Info(path == null ? "Artwork test: failed; see diagnostics." : "Artwork test: passed: " + path);
        });
        _tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) => { _allowExit = true; Close(); });
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        FormClosing += (_, e) =>
        {
            if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                _tray.ShowBalloonTip(1500, "GameLink", "GameLink is still running in the system tray.", ToolTipIcon.Info);
            }
        };
        Shown += async (_, _) => { StartSteam(); await ConnectAsync(); _reconnect.Start(); };
        _reconnect.Tick += async (_, _) => { if (_settingsChanged || _client?.IsConnected != true) await ConnectAsync(); else await DrainAsync(); };
    }

    private void AddLog(string message)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke((Action)(() => AddLog(message))); return; }
        if(_logBox.TextLength>200000) { _logBox.Text=_logBox.Text[^100000..]; _logBox.SelectionStart=_logBox.TextLength; }
        _logBox.AppendText(message + Environment.NewLine);
        UpdateStatus();
    }

    private async Task ConnectAsync()
    {
        if (_connecting || _draining || _shutdown.IsCancellationRequested) return;
        _connecting = true;
        _settingsChanged=false;
        try
        {
            if (_client is not null) await _client.DisposeAsync();
            _client = new StreamerBotClient(_settings, _log);
            await _client.ConnectAsync();
            _status.Text = "Connected to Streamer.bot";
        }
        catch (Exception ex)
        {
            _status.Text = "Connection failed";
            _log.Error(ex.Message);
        }
        finally { _connecting = false; UpdateStatus(); }
        await DrainAsync();
    }

    private Task SendTestAsync()
    {
        try
        {
            ForwardEvent(new GameEvent
            {
                Event = "achievement.unlocked",
                Platform = "GameLink",
                Game = new GameIdentity { Name = "GameLink Test" },
                Data = new Dictionary<string, object?> { ["achievementName"] = "Test achievement", ["achievementDescription"] = "Synthetic test event", ["testMode"] = true }
            });
        }
        catch (Exception ex) { _log.Error($"Test event failed: {ex.Message}"); }
        return Task.CompletedTask;
    }

    private void StartSteam()
    {
        if (_steam != null) return;
        if (!_settings.EnableSteam) { _steamStatus.Text = "Steam monitoring disabled"; return; }
        _steam = new SteamProvider(_log, _settings);
        _steam.StatusChanged += status => { _steamStatus.Text = status; _playing.Text = status.Contains("Running: ") ? "Currently playing: " + status.Split("Running: ")[^1] : status; };
        _steam.EventDetected += ForwardEvent;
        _steam.Start();
    }

    private void StartProcessMonitor()
    {
        if (_processMonitor is not null) return;
        _processMonitor = new ProcessMonitor(_log, _settings.ProcessScanIntervalSeconds, _settings.GameProcesses);
        _processMonitor.GameStarted += ForwardEvent;
        _processMonitor.GameStopped += ForwardEvent;
        _processMonitor.Start();
    }

    private void UpdateStatus() => _status.Text = (_client?.IsConnected == true ? "Connected to Streamer.bot" : "Disconnected from Streamer.bot") + $" | Pending: {_queue.Count}";
    private void ForwardEvent(GameEvent evt)
    {
        if(InvokeRequired) { Invoke((Action)(()=>ForwardEvent(evt))); return; }
        try { _queue.Enqueue(evt,_settings);
            _recent.Insert(0,evt); _history.Items.Insert(0,$"{evt.Timestamp.LocalDateTime:HH:mm:ss}  {evt.Event} — {evt.Game.Name}");
            RefreshDashboard();
            if(_recent.Count>500){_recent.RemoveAt(500);_history.Items.RemoveAt(500);}
            
             UpdateStatus(); _=DrainAsync(); }
        catch(Exception ex) { _log.Error("Queue write failed: "+ex.Message); }
    }
    private static string Value(GameEvent e,string key)=>e.Data.TryGetValue(key,out var v)?v?.ToString()??"":"";
    private void RefreshDashboard()
    {
        var current=new List<VisualCard>();
        if(_steam!=null)foreach(var g in _steam.DashboardGames()){
            var elapsed=g.Start==default?TimeSpan.Zero:DateTime.UtcNow-g.Start;
            current.Add(new(g.Game.Name,$"Steam · Session {(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00} · {g.Count} unlocked",g.Total is >0?$"{g.Unlocked}/{g.Total} achievements · {100.0*g.Unlocked/g.Total:0.0}%":"Achievement progress unavailable",g.Total is >0?(double?)g.Unlocked/g.Total:null,Artwork.Header(g.Game.Id),g.Game.Id));}
        if(current.Count==0)current.Add(new("No game currently detected","Steam games appear here automatically","Your recent activity is below",null,null,""));
        _current.SetCards(current,true,_settings.EnableOnlineMetadata,_log);
        var real=_recent.Where(e=>e.Platform!="GameLink"&&Value(e,"testMode")!="True").ToList();
        _dashboardStats.Text=$"  {real.Count(e=>e.Event=="achievement.unlocked")} achievements detected  ·  {real.Count(e=>e.Event=="game.session_summary")} completed sessions  ·  {_queue.Count} pending events  (current app session)";
        var activity=new List<VisualCard>();
        foreach(var e in real.Take(50)){
            string title,description;string? art=null;var id=e.Game.Id??"";
            switch(e.Event){
                case "achievement.unlocked":title=Value(e,"achievementName");description=e.Game.Name+" · "+Value(e,"achievementDescription");art=Value(e,"achievementIcon");if(string.IsNullOrEmpty(art))art=null;break;
                case "game.started":title="Started playing "+e.Game.Name;description=e.Platform;break;
                case "game.stopped":if(real.Any(x=>x.Event=="game.session_summary"&&x.Game.Id==e.Game.Id&&Math.Abs((x.Timestamp-e.Timestamp).TotalSeconds)<5))continue;title="Finished playing "+e.Game.Name;description=e.Platform;break;
                case "game.session_summary":title="Finished playing "+e.Game.Name;description=$"{Value(e,"gameSessionDurationSeconds")} seconds played · {Value(e,"sessionAchievementsUnlocked")} achievements unlocked";break;
                case "achievement.game_completed":title="All achievements unlocked — "+e.Game.Name;description="100% complete";break;
                default:continue;
            }
            if(art==null&&e.Platform=="Steam"&&uint.TryParse(id,out _))art=Artwork.Header(id);
            var footer=e.Timestamp.LocalDateTime.ToString("g");if(Value(e,"achievementRarity") is {Length:>0} rarity)footer+=" · "+rarity+"% global";if(Value(e,"isRecoveredAchievement")=="True")footer+=" · Recovered achievement";
            activity.Add(new(title,description,footer,null,art,id));
        }
        _activity.SetCards(activity,false,_settings.EnableOnlineMetadata,_log);
    }
    private async Task DrainAsync()
    {
        if(_draining || _connecting || _client?.IsConnected!=true || _shutdown.IsCancellationRequested) return;
        _draining=true;
        try { await _queue.Drain(_client,_shutdown.Token); }
        catch(Exception ex) { if(!_shutdown.IsCancellationRequested) _log.Error("Queue retained for retry: "+ex.Message); }
        finally { _draining=false; if(!IsDisposed) UpdateStatus(); }
    }

    private void EditSettings()
    {
        using var dialog = new SettingsForm(_settings);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { _store.Save(dialog.Settings); } catch(Exception ex) { _log.Error("Settings save: "+ex.Message); MessageBox.Show(this,ex.Message); return; }
        _processMonitor?.Dispose();
        _processMonitor = null;
        _settings = dialog.Settings;
        _log.Enabled=_settings.EnableFileLogging; _reconnect.Interval=Math.Clamp(_settings.RetryIntervalSeconds,5,300)*1000;
        if (!_settings.EnableSteam) { _steam?.Dispose(); _steam = null; _steamStatus.Text = "Steam monitoring disabled"; }
        else { StartSteam(); if(_steam != null) _steam.Settings = _settings; }

        _log.Info("Settings saved. Reconnecting with updated settings...");
        _settingsChanged=true;
        _ = ConnectAsync();
    }

    protected override async void OnFormClosed(FormClosedEventArgs e)
    {
        _dashboardTimer.Dispose();
        _shutdown.Cancel();
        _steam?.Dispose();
        _reconnect.Dispose();
        _processMonitor?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        if (_client is not null) await _client.DisposeAsync();
        base.OnFormClosed(e);
    }
}
