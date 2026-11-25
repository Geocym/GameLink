using GameLink.Models;
using System.Text.Json;
namespace GameLink.Providers.Steam;

internal static class OfflineRecoveryEvents
{
    private static bool Recovered(GameEvent e) => e.Data.TryGetValue("isRecoveredAchievement", out var value) && value is true;
    internal static IEnumerable<GameEvent> Prepare(List<GameEvent> events, bool summarize)
    {
        if (!summarize) return events;
        var result = new List<GameEvent>();
        var groups = events.Where(e => e.Event == "achievement.unlocked" && Recovered(e)).GroupBy(e => e.Game.Id).ToDictionary(g => g.Key ?? "", g => g.ToArray());
        var emitted = new HashSet<string>();
        foreach (var e in events)
        {
            var id = e.Game.Id ?? "";
            if (!Recovered(e) || !groups.TryGetValue(id, out var group)) { result.Add(e); continue; }
            if (e.Event != "achievement.unlocked" && e.Event != "achievement.game_completed") { result.Add(e); continue; }
            if (!emitted.Add(id)) continue;
            var last = group[^1];
            var data = new Dictionary<string, object?> {
                ["steamAppId"] = id, ["testMode"] = false, ["detectionSource"] = "steam.binary_cache",
                ["isRecoveredAchievement"] = true, ["recoveredAchievementCount"] = group.Length,
                ["recoveredAchievementsJson"] = JsonSerializer.Serialize(group.Select(x => x.Data), GameEvent.JsonOptions),
                ["recoveredAchievementNames"] = string.Join("\n", group.Select(x => x.Data.GetValueOrDefault("achievementName")?.ToString() ?? "Unknown")),
                ["recoveredGameCompleted"] = events.Any(x => x.Game.Id == e.Game.Id && x.Event == "achievement.game_completed" && Recovered(x))
            };
            foreach (var key in new[] { "achievementsTotal", "achievementsUnlocked", "achievementsRemaining", "achievementCompletionPercentage", "isGameCompleted", "gameStoreUrl", "gameHeaderImage" })
                if (last.Data.TryGetValue(key, out var value)) data[key] = value;
            result.Add(new GameEvent { EventId = group[0].EventId, Timestamp = group[0].Timestamp, Event = "achievement.recovered_summary", Platform = e.Platform, Game = e.Game, Data = data });
        }
        return result;
    }
}
