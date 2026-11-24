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
    public bool EnableSteam { get; set; } = true;
    public bool EnableProcessMonitor { get; set; } = true;
    public int ProcessScanIntervalSeconds { get; set; } = 3;
    public string GameProcesses { get; set; } = "";
}
