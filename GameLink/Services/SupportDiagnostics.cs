using System.IO.Compression;
using System.Text.Json;
namespace GameLink.Services;
internal static class SupportDiagnostics
{
    internal static void Crash(Exception exception)
    {
        try {
            var report=new { timestamp=DateTimeOffset.UtcNow, version=typeof(SupportDiagnostics).Assembly.GetName().Version?.ToString(), exception=exception.ToString() };
            LocalStorage.Write(Path.Combine(LocalStorage.Root,"crashes",Guid.NewGuid().ToString("N")+".json"),JsonSerializer.Serialize(report));
        } catch { /* Never mask the original failure. */ }
    }
    internal static void Export(string path, bool connected, int pending, bool monitoring)
    {
        // Explicit allowlist: no raw settings, log messages, paths, account IDs or payloads.
        var report=new { version=typeof(SupportDiagnostics).Assembly.GetName().Version?.ToString(), os=Environment.OSVersion.VersionString, runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), portable=LocalStorage.Portable, connected,pending,monitoring, achievementJournal=File.Exists(Path.Combine(LocalStorage.Root,"achievement-scan.pending.json")), crashReports=Count("crashes"), rejectedRecords=Count("rejected") };
        using var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None);
        using var zip=new ZipArchive(stream,ZipArchiveMode.Create);
        using var writer=new StreamWriter(zip.CreateEntry("diagnostics.json").Open());
        writer.Write(JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    }
    private static int Count(string folder){var dir=Path.Combine(LocalStorage.Root,folder);return Directory.Exists(dir)?Directory.GetFiles(dir,"*.json").Length:0;}
}
