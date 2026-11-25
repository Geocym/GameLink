using GameLink.Models;
using GameLink.Services;

namespace GameLink;

public sealed class SettingsForm : UserControl
{
    private readonly CheckBox[] startup=Enumerable.Range(0,6).Select(_=>new CheckBox()).ToArray();
    private readonly CheckBox historyEnabled=new(){Text="Keep persistent event history"};
    private readonly NumericUpDown historyDays=new(){Minimum=1,Maximum=3650};
    private readonly NumericUpDown historyLimit=new(){Minimum=100,Maximum=1000000};
    private readonly NumericUpDown logSize=new(){Minimum=1,Maximum=100};
    private readonly NumericUpDown logArchives=new(){Minimum=0,Maximum=20};
    private readonly ComboBox logLevel=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    public AppSettings Settings { get; private set; }
    private readonly NumericUpDown[] _thresholds = Enumerable.Range(0,4).Select(_=>new NumericUpDown { Minimum=0,Maximum=100,DecimalPlaces=1,Increment=0.5m,Width=65 }).ToArray();
    private readonly CheckBox _online = new() { Text = "Fetch public Steam metadata", AutoSize = true };
    private readonly ComboBox _recover = new() { DropDownStyle=ComboBoxStyle.DropDownList };
    private readonly CheckBox _fileLog = new() { Text="Enable persistent log files", AutoSize=true };
    private readonly NumericUpDown _retry = new() { Minimum=5, Maximum=300 };
    private readonly CheckBox _steamEnabled = new();
    private readonly TextBox _processes = new();
    private readonly TextBox _url = new();
    private readonly TextBox _password = new();
    private readonly TextBox _generic = new();
    private readonly TextBox _achievement = new();
    private readonly TextBox _lifecycle = new();
    private readonly TextBox _telemetry = new();
    private readonly CheckBox _genericEnabled = new();
    private readonly CheckBox _specificEnabled = new();

    public event Func<AppSettings,bool>? Saved;
    public event Action? Cancelled;
    private readonly ComboBox theme=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly System.Windows.Forms.Timer connectionTimer=new(){Interval=500};
    internal SettingsForm(AppSettings current, Func<StreamerBotClient?> client, EventHistory archive, LogService log)
    {
        Settings = new AppSettings
        {
            StartWithWindows=current.StartWithWindows,StartMinimized=current.StartMinimized,AutoConnect=current.AutoConnect,AutoMonitor=current.AutoMonitor,CloseToTray=current.CloseToTray,ConnectionNotifications=current.ConnectionNotifications,EnableHistory=current.EnableHistory,HistoryDays=current.HistoryDays,HistoryLimit=current.HistoryLimit,LogLevel=current.LogLevel,LogSizeMB=current.LogSizeMB,LogArchives=current.LogArchives,Theme=current.Theme, ActionDisplayNames=new(current.ActionDisplayNames),
            StreamerBotWebSocketUrl = current.StreamerBotWebSocketUrl,
            StreamerBotPassword = current.StreamerBotPassword,
            GenericActionName = current.GenericActionName,
            AchievementActionName = current.AchievementActionName,
            TelemetryActionName = current.TelemetryActionName,
            GameLifecycleActionName = current.GameLifecycleActionName,
            UseGenericAction = current.UseGenericAction,
            UseSpecificActions = current.UseSpecificActions,
            EnableSteam = current.EnableSteam,
            RecoverOfflineAchievements=current.RecoverOfflineAchievements, SummarizeOfflineAchievements=current.SummarizeOfflineAchievements, EnableFileLogging=current.EnableFileLogging, RetryIntervalSeconds=current.RetryIntervalSeconds,
            EnableOnlineMetadata = current.EnableOnlineMetadata,
            UncommonThreshold = current.UncommonThreshold, RareThreshold = current.RareThreshold, EpicThreshold = current.EpicThreshold, LegendaryThreshold = current.LegendaryThreshold,
            EnableProcessMonitor = current.EnableProcessMonitor,
            ProcessScanIntervalSeconds = current.ProcessScanIntervalSeconds,
            GameProcesses = current.GameProcesses
        };

        Dock=DockStyle.Fill;
        var pages=new Panel{Dock=DockStyle.Fill,Padding=new Padding(12)};
        var sidebar=new FlowLayoutPanel{Dock=DockStyle.Left,Width=160,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(8)};
        Action? refreshRoutes=null;
        TableLayoutPanel Page(string title){var page=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Visible=pages.Controls.Count==0};pages.Controls.Add(page);var table=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Padding=new Padding(12)};table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,210));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));page.Controls.Add(table);var button=new Button{Text=title,Width=140,Height=38,TextAlign=ContentAlignment.MiddleLeft};sidebar.Controls.Add(button);button.Click+=(_,_)=>{foreach(Control c in pages.Controls)c.Visible=c==page;page.BringToFront();if(title=="Event routing")refreshRoutes?.Invoke();};return table;}
        var general=Page("General");var startupNames=new[]{"Start with Windows","Start minimised to tray","Connect automatically","Start monitoring automatically","Close to system tray","Notify when connection is lost"};var startupValues=new[]{Settings.StartWithWindows,Settings.StartMinimized,Settings.AutoConnect,Settings.AutoMonitor,Settings.CloseToTray,Settings.ConnectionNotifications};for(int i=0;i<startup.Length;i++){startup[i].Text=startupNames[i];startup[i].Checked=startupValues[i];AddRow(general,i,"",startup[i]);}
        var appearance=Page("Appearance");theme.Items.AddRange(["System","Light","Dark"]);theme.SelectedItem=Settings.Theme;if(theme.SelectedIndex<0)theme.SelectedIndex=0;AddRow(appearance,0,"Application theme",theme);
        var connection=Page("Streamer.bot");AddRow(connection,0,"WebSocket URL",_url);AddRow(connection,1,"Password (encrypted on disk)",_password);_password.UseSystemPasswordChar=true;_retry.Value=Math.Clamp(Settings.RetryIntervalSeconds,5,300);AddRow(connection,2,"Retry interval (seconds)",_retry);
        var routing=Page("Event routing");var browseButtons=new List<Button>();bool discovering=false;
        var links=new Dictionary<TextBox,string>();var labels=new Dictionary<TextBox,Label>();bool updating=false;
        void Link(TextBox input,string id,string name){updating=true;links[input]=id;input.Text=name;labels[input].Text="Linked ID: "+id;updating=false;}
        Control Route(TextBox input){var panel=new TableLayoutPanel{ColumnCount=2,Dock=DockStyle.Fill,RowCount=2};panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,32));panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));input.Dock=DockStyle.Fill;var browse=new Button{Text="Browse…",Dock=DockStyle.Fill,Enabled=false};var label=new Label{Dock=DockStyle.Fill,AutoEllipsis=true,Font=new Font("Segoe UI",8)};labels[input]=label;panel.Controls.Add(label,0,1);panel.SetColumnSpan(label,2);browseButtons.Add(browse);panel.Controls.Add(input,0,0);panel.Controls.Add(browse,1,0);input.TextChanged+=(_,_)=>{if(!updating){links.Remove(input);label.Text="Manual action reference";}};browse.Click+=async (_,_)=>{var c=client();if(c?.IsConnected!=true||discovering)return;discovering=true;foreach(var b in browseButtons)b.Enabled=false;try{var actions=await c.GetActionsAsync();if(IsDisposed)return;using var picker=new ActionPicker(actions);if(picker.ShowDialog(FindForm())==DialogResult.OK&&picker.Selected!=null)Link(input,picker.Selected.Id,picker.Selected.Name);}catch(Exception ex){if(!IsDisposed)MessageBox.Show(FindForm(),ex.Message,"Action discovery");}finally{discovering=false;if(!IsDisposed)foreach(var b in browseButtons)b.Enabled=client()?.IsConnected==true;}};return panel;}
        refreshRoutes=async ()=>{var c=client();if(c?.IsConnected!=true||discovering)return;discovering=true;try{var actions=await c.GetActionsAsync();if(IsDisposed)return;foreach(var pair in links.ToArray()){if(!links.TryGetValue(pair.Key,out var currentId)||currentId!=pair.Value)continue;var match=actions.FirstOrDefault(a=>string.Equals(a.Id,pair.Value,StringComparison.OrdinalIgnoreCase));if(match!=null)Link(pair.Key,match.Id,match.Name);else labels[pair.Key].Text="Missing action: "+pair.Value;}}catch{ }finally{discovering=false;}};
        AddRow(routing,0,"Generic action",Route(_generic));AddRow(routing,1,"Achievement action",Route(_achievement));AddRow(routing,2,"Game lifecycle action",Route(_lifecycle));AddRow(routing,3,"Telemetry action",Route(_telemetry));_genericEnabled.Text="Use generic action";_specificEnabled.Text="Use event-specific actions";AddRow(routing,4,"",_genericEnabled);AddRow(routing,5,"",_specificEnabled);
        connectionTimer.Tick+=(_,_)=>{foreach(var b in browseButtons)b.Enabled=!discovering&&client()?.IsConnected==true;};connectionTimer.Start();
        var platforms=Page("Platforms");_steamEnabled.Text="Enable Steam monitoring";_steamEnabled.Checked=Settings.EnableSteam;AddRow(platforms,0,"Steam",_steamEnabled);_recover.Items.AddRange(["Disabled","Individual events","Summary per game"]);_recover.SelectedIndex=!Settings.RecoverOfflineAchievements?0:Settings.SummarizeOfflineAchievements?2:1;AddRow(platforms,1,"Offline achievement recovery",_recover);AddRow(platforms,2,"Game processes (comma separated)",_processes);
        var achievements=Page("Achievements");_online.Checked=Settings.EnableOnlineMetadata;AddRow(achievements,0,"Metadata",_online);var thresholds=new[]{Settings.UncommonThreshold,Settings.RareThreshold,Settings.EpicThreshold,Settings.LegendaryThreshold};var names=new[]{"Uncommon","Rare","Epic","Legendary"};for(int i=0;i<4;i++){_thresholds[i].Value=(decimal)Math.Clamp(thresholds[i],0,100);AddRow(achievements,i+1,names[i]+" (global unlock % ≤)",_thresholds[i]);((Label)achievements.GetControlFromPosition(0,i+1)!).Tag="rarity"+i;}
        var storage=Page("Storage");var open=new Button{Text="Open GameLink data folder"};open.Click+=(_,_)=>{try{Directory.CreateDirectory(LocalStorage.Root);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LocalStorage.Root){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(ex.Message);}};AddRow(storage,0,"Local data",open);historyEnabled.Checked=Settings.EnableHistory;historyDays.Value=Math.Clamp(Settings.HistoryDays,1,3650);historyLimit.Value=Math.Clamp(Settings.HistoryLimit,100,1000000);AddRow(storage,1,"History",historyEnabled);AddRow(storage,2,"Retention days",historyDays);AddRow(storage,3,"Maximum events",historyLimit);var manage=new Button{Text="Manage storage…"};manage.Click+=(_,_)=>{using var dialog=new StorageForm(archive,log);dialog.ShowDialog(FindForm());};AddRow(storage,4,"Storage usage",manage);
        var logging=Page("Logging");_fileLog.Checked=Settings.EnableFileLogging;AddRow(logging,0,"",_fileLog);logLevel.Items.AddRange(Enum.GetNames<LogSeverity>());logLevel.SelectedItem=Settings.LogLevel;if(logLevel.SelectedIndex<0)logLevel.SelectedIndex=1;logSize.Value=Math.Clamp(Settings.LogSizeMB,1,100);logArchives.Value=Math.Clamp(Settings.LogArchives,0,20);AddRow(logging,1,"Minimum file logging level",logLevel);AddRow(logging,2,"Maximum log size (MB)",logSize);AddRow(logging,3,"Retained archives",logArchives);
        MigrationRequest? pending=null;try{pending=DataMigration.Read();}catch(Exception ex){MessageBox.Show(ex.Message,"Migration request");}
        var mode=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=175};mode.Items.AddRange(["Standard (AppData)","Portable"]);mode.SelectedIndex=(pending?.TargetPortable??LocalStorage.Portable)?1:0;var modeStatus=new Label{AutoSize=true};bool changingMode=false;void ModeStatus(){modeStatus.Text=pending==null?"Data mode":"Restart required (active: "+(LocalStorage.Portable?"Portable":"Standard")+")";}ModeStatus();mode.SelectedIndexChanged+=(_,_)=>{if(changingMode)return;var target=mode.SelectedIndex==1;if(target==LocalStorage.Portable){pending=null;ModeStatus();return;}try{var choice=DataMigration.Choose(FindForm()!,target);if(choice!=null){pending=choice;ModeStatus();return;}}catch(Exception ex){MessageBox.Show(ex.Message,"Data mode");}changingMode=true;mode.SelectedIndex=(pending?.TargetPortable??LocalStorage.Portable)?1:0;changingMode=false;};
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,FlowDirection=FlowDirection.RightToLeft};var save=new Button{Text="Save & Close",AutoSize=true};var apply=new Button{Text="Apply",AutoSize=true};var cancel=new Button{Text="Cancel",AutoSize=true};void Save(bool close){if(_thresholds[0].Value<_thresholds[1].Value||_thresholds[1].Value<_thresholds[2].Value||_thresholds[2].Value<_thresholds[3].Value){MessageBox.Show("Thresholds must descend from Uncommon to Legendary.");return;}ReadSettings();foreach(var pair in links)Settings.ActionDisplayNames[pair.Value]=pair.Key.Text;Settings.GenericActionName=links.GetValueOrDefault(_generic,_generic.Text.Trim());Settings.AchievementActionName=links.GetValueOrDefault(_achievement,_achievement.Text.Trim());Settings.GameLifecycleActionName=links.GetValueOrDefault(_lifecycle,_lifecycle.Text.Trim());Settings.TelemetryActionName=links.GetValueOrDefault(_telemetry,_telemetry.Text.Trim());var snapshot=System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(Settings))!;if(Saved?.Invoke(snapshot)==true){try{DataMigration.Schedule(pending);}catch(Exception ex){MessageBox.Show(ex.Message,"Unable to schedule data mode change");return;}if(close)Cancelled?.Invoke();}}save.Click+=(_,_)=>Save(true);apply.Click+=(_,_)=>Save(false);cancel.Click+=(_,_)=>Cancelled?.Invoke();buttons.Controls.AddRange([save,apply,cancel]);Controls.Add(pages);Controls.Add(sidebar);var footer=new Panel{Dock=DockStyle.Bottom,Height=62};buttons.Dock=DockStyle.Right;buttons.Width=270;buttons.Height=48;var modePanel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(8,0,0,0)};modePanel.Controls.Add(modeStatus);modePanel.Controls.Add(mode);footer.Controls.Add(modePanel);footer.Controls.Add(buttons);Controls.Add(footer);
        _processes.Text = Settings.GameProcesses;
        _url.Text = Settings.StreamerBotWebSocketUrl;
        _password.Text = Settings.StreamerBotPassword;
        _generic.Text = Settings.GenericActionName;
        _achievement.Text = Settings.AchievementActionName;
        _telemetry.Text = Settings.TelemetryActionName;
        _lifecycle.Text = Settings.GameLifecycleActionName;
        _genericEnabled.Checked = Settings.UseGenericAction;
        _specificEnabled.Checked = Settings.UseSpecificActions;
        foreach(var input in new[]{_generic,_achievement,_lifecycle,_telemetry})if(Guid.TryParse(input.Text,out _)){var id=input.Text;Link(input,id,Settings.ActionDisplayNames.GetValueOrDefault(id,id));}


    }

    private static void AddRow(TableLayoutPanel panel, int row, string label, Control control)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, control is TableLayoutPanel ? 62 : 42));
        panel.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(control, 1, row);
    }

    protected override void Dispose(bool disposing){if(disposing)connectionTimer.Dispose();base.Dispose(disposing);}
    private void ReadSettings()
    {
        Settings.StartWithWindows=startup[0].Checked;Settings.StartMinimized=startup[1].Checked;Settings.AutoConnect=startup[2].Checked;Settings.AutoMonitor=startup[3].Checked;Settings.CloseToTray=startup[4].Checked;Settings.ConnectionNotifications=startup[5].Checked;Settings.Theme=theme.Text;Settings.EnableHistory=historyEnabled.Checked;Settings.HistoryDays=(int)historyDays.Value;Settings.HistoryLimit=(int)historyLimit.Value;Settings.LogLevel=logLevel.Text;Settings.LogSizeMB=(int)logSize.Value;Settings.LogArchives=(int)logArchives.Value;
        Settings.RecoverOfflineAchievements=_recover.SelectedIndex>0; Settings.SummarizeOfflineAchievements=_recover.SelectedIndex==2; Settings.EnableFileLogging=_fileLog.Checked; Settings.RetryIntervalSeconds=(int)_retry.Value;
        Settings.EnableSteam = _steamEnabled.Checked;
        Settings.EnableOnlineMetadata = _online.Checked;
        Settings.UncommonThreshold=(double)_thresholds[0].Value; Settings.RareThreshold=(double)_thresholds[1].Value;
        Settings.EpicThreshold=(double)_thresholds[2].Value; Settings.LegendaryThreshold=(double)_thresholds[3].Value;
        Settings.GameProcesses = _processes.Text;
        Settings.StreamerBotWebSocketUrl = _url.Text.Trim();
        Settings.StreamerBotPassword = _password.Text;
        Settings.GenericActionName = _generic.Text.Trim();
        Settings.AchievementActionName = _achievement.Text.Trim();
        Settings.TelemetryActionName = _telemetry.Text.Trim();
        Settings.GameLifecycleActionName = _lifecycle.Text.Trim();
        Settings.UseGenericAction = _genericEnabled.Checked;
        Settings.UseSpecificActions = _specificEnabled.Checked;
    }
}
