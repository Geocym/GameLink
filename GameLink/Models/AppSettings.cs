namespace GameLink.Models;

public sealed class AppSettings
{
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
    public bool EnableFileLogging { get; set; } = true;
    public int RetryIntervalSeconds { get; set; } = 15;
    public bool EnableSteam { get; set; } = true;
    public bool EnableProcessMonitor { get; set; } = true;
    public int ProcessScanIntervalSeconds { get; set; } = 3;
    public string GameProcesses { get; set; } = "";
}
