namespace GameLink.Models;

public sealed class AppSettings
{
    public Dictionary<string,string> ActionDisplayNames { get; set; } = new();
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool AutoConnect { get; set; } = true;
    public bool AutoMonitor { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool ConnectionNotifications { get; set; }
    public bool EnableHistory { get; set; } = true;
    public int HistoryDays { get; set; } = 90;
    public int HistoryLimit { get; set; } = 50000;
    public string LogLevel { get; set; } = "Information";
    public int LogSizeMB { get; set; } = 2;
    public int LogArchives { get; set; } = 4;
    public string Theme { get; set; } = "System";
    public string StreamerBotWebSocketUrl { get; set; } = "ws://127.0.0.1:8080/";
    public string StreamerBotPassword { get; set; } = "";
    public string GenericActionName { get; set; } = "Game Monitor - Receive Event";
    public string AchievementActionName { get; set; } = "";
    public string GameLifecycleActionName { get; set; } = "";
    public string TelemetryActionName { get; set; } = "";
    public bool UseGenericAction { get; set; } = true;
    public bool UseSpecificActions { get; set; } = false;
    public bool EnableOnlineMetadata { get; set; } = true;
    public double UncommonThreshold { get; set; } = 25;
    public double RareThreshold { get; set; } = 10;
    public double EpicThreshold { get; set; } = 5;
    public double LegendaryThreshold { get; set; } = 1;
    public bool RecoverOfflineAchievements { get; set; } = false;
    public bool SummarizeOfflineAchievements { get; set; } = false;
    public bool EnableFileLogging { get; set; } = true;
    public int RetryIntervalSeconds { get; set; } = 15;
    public bool EnableSteam { get; set; } = true;
    public bool EnableProcessMonitor { get; set; } = true;
    public int ProcessScanIntervalSeconds { get; set; } = 3;
    public string GameProcesses { get; set; } = "";
}
