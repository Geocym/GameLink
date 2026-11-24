using Microsoft.Win32;
using System.Diagnostics;
namespace GameLink.Providers.Steam;
internal record SteamGame(string Id, string Name, string Directory);
internal record SteamSnapshot(string? Path, string? AccountId, string? AccountName,
    bool ClientRunning, int Libraries, Dictionary<string,SteamGame> Games);
internal static class SteamDiscovery
{
    internal const string Key = @"Software\Valve\Steam";
    internal static string? FindPath()
    {
        using var key=Registry.CurrentUser.OpenSubKey(Key);
        using var machine=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
        foreach(var candidate in new[]{key?.GetValue("SteamPath") as string,
            machine?.GetValue("InstallPath") as string,
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam")})
            if(!string.IsNullOrWhiteSpace(candidate) && File.Exists(System.IO.Path.Combine(candidate,"steam.exe")))
                return System.IO.Path.GetFullPath(candidate);
        return null;
    }
    internal static bool ClientRunning()
    {
        var processes=Process.GetProcessesByName("steam");
        try { return processes.Length>0; } finally { foreach(var p in processes) p.Dispose(); }
    }
    internal static SteamSnapshot Scan(Action<string> warn)
    {
        var path=FindPath();
        var games=new Dictionary<string,SteamGame>();
        bool running=ClientRunning();
        if(path==null) return new(null,null,null,running,0,games);
        string? id=null,name=null;
        using(var active=Registry.CurrentUser.OpenSubKey(Key+@"\ActiveProcess"))
        {
            if(running && active?.GetValue("ActiveUser") is int raw && raw!=0)
                id=(76561197960265728UL+unchecked((uint)raw)).ToString();
        }
        try {
            if(id!=null) {
                var users=Vdf.Parse(File.ReadAllText(System.IO.Path.Combine(path,"config","loginusers.vdf"))).Section("users");
                var user=users?.Section(id);
                name=user?.Value("PersonaName") ?? user?.Value("AccountName");
            }
        } catch(Exception ex) { warn("Steam account metadata: "+ex.Message); }
        var libraries=new HashSet<string>(StringComparer.OrdinalIgnoreCase){path};
        try {
            var folders=Vdf.Parse(File.ReadAllText(System.IO.Path.Combine(path,"steamapps","libraryfolders.vdf"))).Section("libraryfolders");
            if(folders!=null) foreach(var pair in folders)
                if(uint.TryParse(pair.Key,out _)) {
                    var folder=pair.Value is Vdf v ? v.Value("path") : pair.Value as string;
                    if(!string.IsNullOrWhiteSpace(folder)) libraries.Add(folder);
                }
        } catch(Exception ex) { warn("Steam libraries: "+ex.Message); }
        foreach(var library in libraries)
        {
            try {
                foreach(var file in Directory.EnumerateFiles(System.IO.Path.Combine(library,"steamapps"),"appmanifest_*.acf"))
                {
                    try {
                        var v=Vdf.Parse(File.ReadAllText(file)).Section("AppState");
                        var appId=v?.Value("appid"); var title=v?.Value("name"); var dir=v?.Value("installdir");
                        if(appId!=null && uint.TryParse(appId,out _) && title!=null && dir!=null)
                        {
                            var install=System.IO.Path.Combine(library,"steamapps","common",dir);
                            if(Directory.Exists(install)) games[appId]=new(appId,title,install);
                        }
                    } catch(Exception ex) { warn("Manifest "+System.IO.Path.GetFileName(file)+": "+ex.Message); }
                }
            } catch(Exception ex) { warn("Library unavailable: "+library+" ("+ex.Message+")"); }
        }
        return new(path,id,name,running,libraries.Count,games);
    }
    internal static HashSet<string> RunningIds()
    {
        var ids=new HashSet<string>();
        if(!ClientRunning()) return ids;
        using var apps=Registry.CurrentUser.OpenSubKey(Key+@"\Apps");
        if(apps!=null) foreach(var id in apps.GetSubKeyNames())
        {
            if(!uint.TryParse(id,out _)) continue;
            using var app=apps.OpenSubKey(id);
            if(app?.GetValue("Running") is int value && value==1) ids.Add(id);
        }
        return ids;
    }
}
