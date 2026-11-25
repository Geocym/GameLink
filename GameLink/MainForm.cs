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
    private readonly RichTextBox _logBox = new();
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
    private readonly EventHistory _archive;
    private readonly List<GameEvent> _shown=new();
    private readonly TextBox _search=new(){Width=200,PlaceholderText="Search history"};
    private readonly List<LogEntry> _logs=new();
    private readonly ComboBox _severity=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly ComboBox _category=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly CancellationTokenSource _shutdown = new();
    private readonly System.Windows.Forms.Timer _reconnect = new() { Interval = 15000 };

    public MainForm()
    {
        _settings = _store.Load();
        _log.Enabled=_settings.EnableFileLogging; _reconnect.Interval=Math.Clamp(_settings.RetryIntervalSeconds,5,300)*1000;
        _archive=new EventHistory(_log); ConfigureStorage(); _archive.Prune();
        _queue = new EventQueue(_log){History=_archive};
        Text = "GameLink v0.2.7 - Game Monitoring Middleware";
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
        toolbar.Controls.AddRange([settingsButton, connectButton, monitorButton]);

        _status.Dock = DockStyle.Top;
        _status.Height = 28;
        _status.Padding = new Padding(10, 6, 0, 0);
        _status.Text = "Disconnected";

        _logBox.Dock = DockStyle.Fill;
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = RichTextBoxScrollBars.Vertical;
        _logBox.Font = new Font("Consolas", 9);
        var tabs=new Panel{Dock=DockStyle.Fill};
        var nav=new FlowLayoutPanel{Dock=DockStyle.Top,Height=44,Padding=new Padding(8,3,8,3),WrapContents=false};
        Panel Page(string name){var page=new Panel{Dock=DockStyle.Fill,Padding=new Padding(12),Visible=false,Tag=name};tabs.Controls.Add(page);var button=new Button{Text=name,AutoSize=true,Height=34};nav.Controls.Add(button);button.Click+=(_,_)=>ShowPage(page);return page;}
        void ShowPage(Panel page){foreach(Control c in tabs.Controls)c.Visible=c==page;page.BringToFront();foreach(Button b in nav.Controls){var style=b.Text==(string?)page.Tag?FontStyle.Bold:FontStyle.Regular;if(b.Font.Style!=style)b.Font=new Font(Font,style);}}
        var dashboard=Page("Dashboard"); var browser=Page("Achievements"); var history=Page("Event history"); var platforms=Page("Platforms"); var diagnostics=Page("Diagnostics");
        ShowPage(dashboard);
        dashboard.Controls.Add(_activity); dashboard.Controls.Add(new Label{Text="Recent gaming activity",Dock=DockStyle.Top,Height=32,Font=new Font("Segoe UI",14,FontStyle.Bold)}); dashboard.Controls.Add(_dashboardStats); dashboard.Controls.Add(_current); dashboard.Controls.Add(new Label{Text="Currently playing",Dock=DockStyle.Top,Height=32,Font=new Font("Segoe UI",14,FontStyle.Bold)});
        _dashboardTimer.Tick+=(_,_)=>RefreshDashboard();_dashboardTimer.Start();RefreshDashboard();
        var achievements=new AchievementPage(()=>_settings,_log){Dock=DockStyle.Fill}; browser.Controls.Add(achievements);
        browser.VisibleChanged+=async (_,_)=>{if(browser.Visible)await achievements.RefreshAsync();};
        async void OpenGame(string id){if(!uint.TryParse(id,out _))return;ShowPage(browser);await achievements.OpenGameAsync(id);}
        _current.Activated+=i=>{var id=_current.CardId(i);if(id!=null)OpenGame(id);};
        _activity.Activated+=i=>{var id=_activity.CardId(i);if(id!=null)OpenGame(id);};
        var detail=new Panel{Dock=DockStyle.Fill,Padding=new Padding(10,0,0,0)};detail.Controls.Add(_payload);history.Controls.Add(detail);history.Controls.Add(_history);
        _history.SelectedIndexChanged+=(_,_)=>{var i=_history.SelectedIndex;_payload.Text=i>=0&&i<_shown.Count?System.Text.Json.JsonSerializer.Serialize(_shown[i],new System.Text.Json.JsonSerializerOptions(GameEvent.JsonOptions){WriteIndented=true}):"";};
        var copy=new Button{Text="Copy JSON",AutoSize=true,Height=34};copy.Click+=(_,_)=>{if(_payload.TextLength>0)Clipboard.SetText(_payload.Text);};var outcomes=new TextBox{Dock=DockStyle.Bottom,Height=100,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};detail.Controls.Add(outcomes);_history.SelectedIndexChanged+=(_,_)=>outcomes.Text=_history.SelectedIndex>=0&&_history.SelectedIndex<_shown.Count?_archive.Outcomes(_shown[_history.SelectedIndex].EventId):"";
        var copyBar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=46,Padding=new Padding(0,0,0,6)};copyBar.Controls.Add(copy);copyBar.Controls.Add(_search);var refresh=new Button{Text="Refresh",AutoSize=true};refresh.Click+=(_,_)=>RefreshHistory();copyBar.Controls.Add(refresh);var clear=new Button{Text="Clear history",AutoSize=true};clear.Click+=(_,_)=>{if(MessageBox.Show(this,"Clear historical records? Pending events are unaffected.","Clear history",MessageBoxButtons.YesNo)==DialogResult.Yes){_archive.Clear();_recent.Clear();RefreshHistory();}};copyBar.Controls.Add(clear);_search.TextChanged+=(_,_)=>RefreshHistory();history.VisibleChanged+=(_,_)=>{if(history.Visible)RefreshHistory();};detail.Controls.Add(copyBar);
        diagnostics.Controls.Add(_logBox);var diagnosticBar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=42};diagnosticBar.Controls.Add(testButton);_severity.Items.Add("All levels");_severity.Items.AddRange(Enum.GetNames<LogSeverity>());_severity.SelectedIndex=0;_category.Items.Add("All categories");_category.Items.AddRange(Enum.GetNames<LogCategory>());_category.SelectedIndex=0;diagnosticBar.Controls.Add(_severity);diagnosticBar.Controls.Add(_category);_severity.SelectedIndexChanged+=(_,_)=>RenderLogs();_category.SelectedIndexChanged+=(_,_)=>RenderLogs();diagnostics.Controls.Add(diagnosticBar);
        Controls.Add(tabs);Controls.Add(nav);
        _steamStatus.Dock = DockStyle.Top;
        _steamStatus.Height = 65;
        _steamStatus.Padding = new Padding(10, 6, 10, 0);
        _steamStatus.Text = "Discovering Steam...";
        platforms.Controls.Add(_steamStatus);
        _steamStatus.Height=150;
        Controls.Add(_status);
        Controls.Add(toolbar);

        ThemePalette.Set(_settings.Theme);ThemePalette.Apply(this);
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

    private void AddLog(LogEntry message)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke((Action)(() => AddLog(message))); return; }
        _logs.Add(message);if(_logs.Count>2000){_logs.RemoveRange(0,500);RenderLogs();}else AppendLog(message);
        UpdateStatus();
    }
    private void AppendLog(LogEntry entry){if(_severity.SelectedIndex>0&&_severity.Text!=entry.Severity.ToString()||_category.SelectedIndex>0&&_category.Text!=entry.Category.ToString())return;var start=_logBox.SelectionStart;var length=_logBox.SelectionLength;bool follow=start+length>=_logBox.TextLength;_logBox.Select(_logBox.TextLength,0);_logBox.SelectionColor=entry.Severity switch{LogSeverity.Error=>ThemePalette.Dark?Color.LightCoral:Color.Firebrick,LogSeverity.Warning=>ThemePalette.Dark?Color.Orange:Color.SaddleBrown,LogSeverity.Debug=>ThemePalette.Muted,_=>entry.Category==LogCategory.Achievement?ThemePalette.Rarity(3):entry.Category==LogCategory.Steam?ThemePalette.Accent:ThemePalette.Ink};_logBox.AppendText(entry+Environment.NewLine);if(follow)_logBox.ScrollToCaret();else _logBox.Select(start,length);}
    private void RenderLogs(){_logBox.Clear();foreach(var entry in _logs)AppendLog(entry);}
    private void ConfigureStorage(){_archive.Enabled=_settings.EnableHistory;_archive.Days=_settings.HistoryDays;_archive.Limit=_settings.HistoryLimit;_log.Enabled=_settings.EnableFileLogging;_log.Minimum=Enum.TryParse<LogSeverity>(_settings.LogLevel,out var level)?level:LogSeverity.Information;_log.SizeMB=_settings.LogSizeMB;_log.Archives=_settings.LogArchives;}
    private void RefreshHistory(){var id=_history.SelectedIndex>=0&&_history.SelectedIndex<_shown.Count?_shown[_history.SelectedIndex].EventId:null;var events=_archive.Enabled?_archive.Search(_search.Text):_recent.Where(e=>e.ToJson().Contains(_search.Text,StringComparison.OrdinalIgnoreCase)).ToList();_history.BeginUpdate();try{_history.Items.Clear();_shown.Clear();_shown.AddRange(events);foreach(var e in _shown)_history.Items.Add($"{e.Timestamp.LocalDateTime:g}  {e.Event} — {e.Game.Name}");if(id!=null)_history.SelectedIndex=_shown.FindIndex(e=>e.EventId==id);}finally{_history.EndUpdate();}}

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
            _recent.Insert(0,evt); _archive.Prune(); RefreshHistory();
            RefreshDashboard();
            if(_recent.Count>500){_recent.RemoveAt(500);}
            
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
        finally { _draining=false; if(!IsDisposed) {UpdateStatus();if(_history.Visible)RefreshHistory();} }
    }

    private void EditSettings()
    {
        using var window=new Form{Text="GameLink Settings",Size=new Size(900,620),MinimumSize=new Size(740,500),StartPosition=FormStartPosition.CenterParent,Font=Font,ShowInTaskbar=false};
        var panel=new SettingsForm(_settings,()=>_client);window.Controls.Add(panel);
        panel.Saved+=settings=>{if(ApplySettings(settings)){ThemePalette.Apply(window);return true;}return false;};
        panel.Cancelled+=()=>window.Close();ThemePalette.Apply(window);window.ShowDialog(this);
    }
    private bool ApplySettings(AppSettings settings)
    {
        try { _store.Save(settings); } catch(Exception ex) { _log.Error("Settings save: "+ex.Message); MessageBox.Show(this,ex.Message); return false; }
        bool reconnect=_settings.StreamerBotWebSocketUrl!=settings.StreamerBotWebSocketUrl||_settings.StreamerBotPassword!=settings.StreamerBotPassword;
        bool wasMonitoring=_processMonitor!=null;
        bool processes=_settings.GameProcesses!=settings.GameProcesses||_settings.ProcessScanIntervalSeconds!=settings.ProcessScanIntervalSeconds;
        if(processes&&_processMonitor!=null){_processMonitor.Dispose();_processMonitor=null;}
        _settings=settings;
        ThemePalette.Set(_settings.Theme);ThemePalette.Apply(this);RenderLogs();ConfigureStorage();_archive.Prune();
        _log.Enabled=_settings.EnableFileLogging; _reconnect.Interval=Math.Clamp(_settings.RetryIntervalSeconds,5,300)*1000;
        if (!_settings.EnableSteam) { _steam?.Dispose(); _steam = null; _steamStatus.Text = "Steam monitoring disabled"; }
        else { StartSteam(); if(_steam != null) _steam.Settings = _settings; }
        if(processes&&wasMonitoring)StartProcessMonitor();
        _log.Info("Settings saved.");if(reconnect){_settingsChanged=true;_=ConnectAsync();}return true;
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
