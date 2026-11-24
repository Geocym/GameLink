using GameLink.Models;

namespace GameLink;

public sealed class SettingsForm : Form
{
    public AppSettings Settings { get; private set; }
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
            EnableProcessMonitor = current.EnableProcessMonitor,
            ProcessScanIntervalSeconds = current.ProcessScanIntervalSeconds,
            GameProcesses = current.GameProcesses
        };

        Text = "GameLink Settings";
        Width = 620; Height = 520;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 12 };
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
            Text = "Use a local WebSocket server. Password is stored locally in settings.json. Both routes can be enabled together.",
            Dock = DockStyle.Fill, AutoSize = false
        };
        _steamEnabled.Text = "Enable automatic Steam monitoring";
        _steamEnabled.Checked = Settings.EnableSteam;
        layout.Controls.Add(_steamEnabled, 1, 9);
        layout.Controls.Add(note, 0, 10); layout.SetColumnSpan(note, 2);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        save.Click += (_, _) => ReadSettings();
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 11); layout.SetColumnSpan(buttons, 2);

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
        Settings.EnableSteam = _steamEnabled.Checked;
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
