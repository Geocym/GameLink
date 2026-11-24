using GameLink.Models;

namespace GameLink;

public sealed class SettingsForm : Form
{
    public AppSettings Settings { get; private set; }
    private readonly NumericUpDown[] _thresholds = Enumerable.Range(0,4).Select(_=>new NumericUpDown { Minimum=0,Maximum=100,DecimalPlaces=1,Increment=0.5m,Width=65 }).ToArray();
    private readonly CheckBox _online = new() { Text = "Fetch public Steam metadata", AutoSize = true };
    private readonly CheckBox _recover = new() { Text="Recover achievements earned while closed", AutoSize=true };
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

    public SettingsForm(AppSettings current)
    {
        Settings = new AppSettings
        {
            StreamerBotWebSocketUrl = current.StreamerBotWebSocketUrl,
            StreamerBotPassword = current.StreamerBotPassword,
            GenericActionName = current.GenericActionName,
            AchievementActionName = current.AchievementActionName,
            TelemetryActionName = current.TelemetryActionName,
            GameLifecycleActionName = current.GameLifecycleActionName,
            UseGenericAction = current.UseGenericAction,
            UseSpecificActions = current.UseSpecificActions,
            EnableSteam = current.EnableSteam,
            RecoverOfflineAchievements=current.RecoverOfflineAchievements, EnableFileLogging=current.EnableFileLogging, RetryIntervalSeconds=current.RetryIntervalSeconds,
            EnableOnlineMetadata = current.EnableOnlineMetadata,
            UncommonThreshold = current.UncommonThreshold, RareThreshold = current.RareThreshold, EpicThreshold = current.EpicThreshold, LegendaryThreshold = current.LegendaryThreshold,
            EnableProcessMonitor = current.EnableProcessMonitor,
            ProcessScanIntervalSeconds = current.ProcessScanIntervalSeconds,
            GameProcesses = current.GameProcesses
        };

        Text = "GameLink Settings";
        Width = 620; Height = 730;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 17 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(layout, 0, "Streamer.bot WebSocket URL", _url);
        AddRow(layout, 1, "WebSocket password", _password);
        _password.UseSystemPasswordChar = true;
        AddRow(layout, 2, "Generic action name / GUID", _generic);
        AddRow(layout, 3, "Achievement action / GUID", _achievement);
        AddRow(layout, 4, "Telemetry action / GUID", _telemetry);
        _genericEnabled.Text = "Use generic action";
        _specificEnabled.Text = "Also use event-specific actions";
        AddRow(layout, 5, "Game lifecycle action / GUID", _lifecycle);
        AddRow(layout, 6, "Game processes (comma separated)", _processes);
        layout.Controls.Add(_genericEnabled, 1, 7);
        layout.Controls.Add(_specificEnabled, 1, 8);

        var note = new Label
        {
            Text = "Use a local WebSocket server. Password is encrypted for your Windows account. Both routes can be enabled together.",
            Dock = DockStyle.Fill, AutoSize = false
        };
        _steamEnabled.Text = "Enable automatic Steam monitoring";
        _steamEnabled.Checked = Settings.EnableSteam;
        layout.Controls.Add(_steamEnabled, 1, 9);
        _online.Checked = Settings.EnableOnlineMetadata; layout.Controls.Add(_online, 1, 10);
        var rarityPanel = new FlowLayoutPanel { Dock=DockStyle.Fill, AutoSize=true, WrapContents=false };
        var thresholds = new[]{Settings.UncommonThreshold,Settings.RareThreshold,Settings.EpicThreshold,Settings.LegendaryThreshold};
        for(int i=0;i<4;i++) { _thresholds[i].Value=(decimal)Math.Clamp(thresholds[i],0,100); rarityPanel.Controls.Add(_thresholds[i]); }
        AddRow(layout, 11, "Rarity % (U / R / E / L)", rarityPanel);
        _recover.Checked=Settings.RecoverOfflineAchievements; layout.Controls.Add(_recover,1,12);
        _fileLog.Checked=Settings.EnableFileLogging; layout.Controls.Add(_fileLog,1,13);
        _retry.Value=Math.Clamp(Settings.RetryIntervalSeconds,5,300); AddRow(layout,14,"Retry interval (seconds)",_retry);
        layout.Controls.Add(note, 0, 15); layout.SetColumnSpan(note, 2);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        save.Click += (_, _) => {
            if(_thresholds[0].Value<_thresholds[1].Value || _thresholds[1].Value<_thresholds[2].Value || _thresholds[2].Value<_thresholds[3].Value) { DialogResult=DialogResult.None; MessageBox.Show(this,"Thresholds must descend from Uncommon to Legendary."); return; }
            ReadSettings();
        };
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 16); layout.SetColumnSpan(buttons, 2);

        _processes.Text = Settings.GameProcesses;
        _url.Text = Settings.StreamerBotWebSocketUrl;
        _password.Text = Settings.StreamerBotPassword;
        _generic.Text = Settings.GenericActionName;
        _achievement.Text = Settings.AchievementActionName;
        _telemetry.Text = Settings.TelemetryActionName;
        _lifecycle.Text = Settings.GameLifecycleActionName;
        _genericEnabled.Checked = Settings.UseGenericAction;
        _specificEnabled.Checked = Settings.UseSpecificActions;

        Controls.Add(layout);
        AcceptButton = save; CancelButton = cancel;
    }

    private static void AddRow(TableLayoutPanel panel, int row, string label, Control control)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(control, 1, row);
    }

    private void ReadSettings()
    {
        Settings.RecoverOfflineAchievements=_recover.Checked; Settings.EnableFileLogging=_fileLog.Checked; Settings.RetryIntervalSeconds=(int)_retry.Value;
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
