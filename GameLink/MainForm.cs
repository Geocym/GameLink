using GameLink.Models;
using GameLink.Services;

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
    private bool _connecting;
    private readonly System.Windows.Forms.Timer _reconnect = new() { Interval = 15000 };

    public MainForm()
    {
        _settings = _store.Load();
        Text = "GameLink - Game Monitoring Middleware";
        Width = 850; Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(680, 420);

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
        Controls.Add(_logBox);
        Controls.Add(_status);
        Controls.Add(toolbar);

        _log.Message += AddLog;
        _tray.Text = "GameLink";
        _tray.Icon = SystemIcons.Application;
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; };
        _tray.ContextMenuStrip = new ContextMenuStrip();
        _tray.ContextMenuStrip.Items.Add("Open", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; });
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
        Shown += async (_, _) => { await ConnectAsync(); _reconnect.Start(); };
        _reconnect.Tick += async (_, _) => { if (_client?.IsConnected != true) await ConnectAsync(); };
    }

    private void AddLog(string message)
    {
        if (InvokeRequired) { BeginInvoke((Action)(() => AddLog(message))); return; }
        _logBox.AppendText(message + Environment.NewLine);
        _status.Text = _client?.IsConnected == true ? "Connected to Streamer.bot" : "Disconnected from Streamer.bot";
    }

    private async Task ConnectAsync()
    {
        if (_connecting) return;
        _connecting = true;
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
        finally { _connecting = false; }
    }

    private async Task SendTestAsync()
    {
        if (_client is null) { _log.Error("Connect to Streamer.bot first."); return; }
        try
        {
            await _client.SendEventAsync(new GameEvent
            {
                Event = "achievement.unlocked",
                Platform = "GameLink",
                Game = new GameIdentity { Name = "GameLink Test" },
                Data = new Dictionary<string, object?> { ["achievementName"] = "Test achievement", ["achievementDescription"] = "Synthetic test event", ["testMode"] = true }
            });
        }
        catch (Exception ex) { _log.Error($"Test event failed: {ex.Message}"); }
    }

    private void StartProcessMonitor()
    {
        if (_processMonitor is not null) return;
        _processMonitor = new ProcessMonitor(_log, _settings.ProcessScanIntervalSeconds, _settings.GameProcesses);
        _processMonitor.GameStarted += ForwardEvent;
        _processMonitor.GameStopped += ForwardEvent;
        _processMonitor.Start();
    }

    private async void ForwardEvent(GameEvent evt)
    {
        try
        {
            if (_client is null) return;
            await _client.SendEventAsync(evt);
        }
        catch (Exception ex) { _log.Error($"Could not forward {evt.Event}: {ex.Message}"); }
    }

    private void EditSettings()
    {
        using var dialog = new SettingsForm(_settings);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _processMonitor?.Dispose();
        _processMonitor = null;
        _settings = dialog.Settings;
        _store.Save(_settings);
        _log.Info("Settings saved. Reconnecting with updated settings...");
        _ = ConnectAsync();
    }

    protected override async void OnFormClosed(FormClosedEventArgs e)
    {
        _reconnect.Dispose();
        _processMonitor?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        if (_client is not null) await _client.DisposeAsync();
        base.OnFormClosed(e);
    }
}
