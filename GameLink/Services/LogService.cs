namespace GameLink.Services;

public sealed class LogService
{
    public event Action<string>? Message;
    public void Info(string message) => Message?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
    public void Error(string message) => Message?.Invoke($"[{DateTime.Now:HH:mm:ss}] ERROR: {message}");
}
