using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameLink.Models;

namespace GameLink.Services;

public sealed class StreamerBotClient(AppSettings settings, LogService log) : IAsyncDisposable
{
    private ClientWebSocket? socket;
    private readonly SemaphoreSlim gate = new(1);
    private bool ready, disposed;
    public bool IsConnected => ready && socket?.State == WebSocketState.Open;
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try { using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(10)); await ConnectCore(timeout.Token); }
        catch (Exception ex) { log.Error("WebSocket: " + ex.Message); Reset(); throw; }
        finally { gate.Release(); }
    }
    private async Task ConnectCore(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsConnected) return;
        Reset(); socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        socket.Options.KeepAliveTimeout = Timeout.InfiniteTimeSpan;
        await socket.ConnectAsync(new Uri(settings.StreamerBotWebSocketUrl), ct);
        using var hello = await Receive(ct);
        if (!hello.RootElement.TryGetProperty("request", out var req) || req.GetString() != "Hello") throw new IOException("Expected Streamer.bot Hello.");
        if (hello.RootElement.TryGetProperty("authentication", out var auth) && auth.ValueKind == JsonValueKind.Object)
        {
            if (string.IsNullOrEmpty(settings.StreamerBotPassword)) throw new IOException("Streamer.bot password required.");
            static string Hash(string s) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
            var authentication = Hash(Hash(settings.StreamerBotPassword + auth.GetProperty("salt").GetString()) + auth.GetProperty("challenge").GetString());
            var id = Guid.NewGuid().ToString();
            await Request(new { request = "Authenticate", id, authentication }, id, ct);
        }
        ready = true; log.Info("Connected to Streamer.bot.");
    }
    public async Task SendEventAsync(GameEvent evt, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(15)); var ct = timeout.Token;
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (settings.UseGenericAction && !string.IsNullOrWhiteSpace(settings.GenericActionName)) targets.Add(settings.GenericActionName);
            var specific = evt.Event.StartsWith("achievement.") ? settings.AchievementActionName : evt.Event == "game.telemetry" ? settings.TelemetryActionName : evt.Event is "game.started" or "game.stopped" ? settings.GameLifecycleActionName : "";
            if (settings.UseSpecificActions && !string.IsNullOrWhiteSpace(specific)) targets.Add(specific);
            if (targets.Count == 0) { log.Info($"No route for {evt.Event}."); return; }
            await ConnectCore(ct);
            var args = new Dictionary<string, object?> { ["eventType"] = evt.Event, ["platform"] = evt.Platform, ["gameName"] = evt.Game.Name, ["gameId"] = evt.Game.Id ?? "", ["processName"] = evt.Game.ProcessName ?? "", ["timestamp"] = evt.Timestamp.ToString("O"), ["eventJson"] = evt.ToJson(), ["eventId"] = evt.EventId };
            foreach (var pair in evt.Data) args.TryAdd(pair.Key, pair.Value);
            foreach (var target in targets)
            {
                var id = Guid.NewGuid().ToString();
                var action = Guid.TryParse(target, out _) ? new Dictionary<string,string>{{"id",target}} : new Dictionary<string,string>{{"name",target}};
                await Request(new { request = "DoAction", id, action, args }, id, ct);
                log.Info($"Accepted {evt.Event} by '{target}'.");
            }
        }
        catch (Exception ex) { log.Error("WebSocket: " + ex.Message); Reset(); throw; }
        finally { gate.Release(); }
    }
    private async Task Request(object payload, string id, CancellationToken ct)
    {
        await socket!.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(payload)), WebSocketMessageType.Text, true, ct);
        while (true)
        {
            using var reply = await Receive(ct); var r = reply.RootElement;
            if (!r.TryGetProperty("id", out var rid) || rid.GetString() != id) continue;
            if (!r.TryGetProperty("status", out var status) || status.GetString() != "ok") throw new IOException("Streamer.bot rejected request: " + r.ToString());
            return;
        }
    }
    private async Task<JsonDocument> Receive(CancellationToken ct)
    {
        using var buffer = new MemoryStream(); var bytes = new byte[8192]; WebSocketReceiveResult result;
        do { result = await socket!.ReceiveAsync(new ArraySegment<byte>(bytes), ct); if (result.MessageType != WebSocketMessageType.Text) throw new IOException("Unexpected WebSocket message or disconnect."); buffer.Write(bytes,0,result.Count); if(buffer.Length > 2_000_000) throw new IOException("Response too large."); } while (!result.EndOfMessage);
        return JsonDocument.Parse(buffer.ToArray());
    }
    private void Reset() { ready = false; socket?.Dispose(); socket = null; }
    public async ValueTask DisposeAsync() { await gate.WaitAsync(); try { disposed = true; Reset(); } finally { gate.Release(); } }
}
