using System.Diagnostics;
using GameLink.Models;

namespace GameLink.Services;

/// <summary>
/// Basic process discovery only. It deliberately does not guess game titles
/// or read process memory. A later game catalogue/profile provider can enrich this.
/// </summary>
public sealed class ProcessMonitor : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly Dictionary<int,string> _knownPids = new();
    private readonly HashSet<string> _names;
    private readonly LogService _log;

    public event Action<GameEvent>? GameStarted;
    public event Action<GameEvent>? GameStopped;

    public ProcessMonitor(LogService log, int intervalSeconds, string names)
    {
        _log = log;
        _names = names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _timer.Interval = Math.Max(1, intervalSeconds) * 1000;
        _timer.Tick += (_, _) => Scan();
    }

    public void Start()
    {
        Scan();
        _timer.Start();
        _log.Info("Process monitor started.");
    }

    private void Scan()
    {
        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch (Exception ex) { _log.Error($"Process scan failed: {ex.Message}"); return; }

        var current = new HashSet<int>();
        foreach (var process in processes)
        {
            try
            {
                if (!_names.Contains(process.ProcessName)) continue;
                current.Add(process.Id);
                if (_knownPids.TryAdd(process.Id, process.ProcessName))
                {
                    var name = process.ProcessName;
                    GameStarted?.Invoke(new GameEvent
                    {
                        Event = "game.process.started",
                        Platform = "Windows",
                        Game = new GameIdentity { Name = name, ProcessName = name, Id = process.Id.ToString() },
                        Data = new Dictionary<string, object?> { ["processId"] = process.Id }
                    });
                }
            }
            catch { /* Process may exit while enumerating. */ }
            finally { process.Dispose(); }
        }

        foreach (var pid in _knownPids.Keys.Except(current).ToArray())
        {
            var name = _knownPids[pid];
            _knownPids.Remove(pid);
            GameStopped?.Invoke(new GameEvent
            {
                Event = "game.process.stopped",
                Platform = "Windows",
                Game = new GameIdentity { Name = name, ProcessName = name, Id = pid.ToString() },
                Data = new Dictionary<string, object?> { ["processId"] = pid }
            });
        }
    }

    public void Dispose() => _timer.Dispose();
}
