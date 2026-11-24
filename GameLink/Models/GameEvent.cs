using System.Text.Json;

namespace GameLink.Models;

public sealed class GameEvent
{
    public string Event { get; set; } = "game.unknown";
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");
    public string Platform { get; set; } = "Unknown";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public GameIdentity Game { get; set; } = new();
    public Dictionary<string, object?> Data { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

public sealed class GameIdentity
{
    public string Name { get; set; } = "Unknown";
    public string? ProcessName { get; set; }
    public string? Id { get; set; }
}
