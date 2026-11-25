using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
namespace GameLink.Services;

internal sealed record MigrationRequest(bool SourcePortable, bool TargetPortable, bool Copy, string DestinationStamp);
internal static class DataMigration
{
    private static string Flag => Path.Combine(AppContext.BaseDirectory,"portable.flag");
    private static string Pending => Path.Combine(AppContext.BaseDirectory,"migration.pending.json");
    private static string Journal => Path.Combine(AppContext.BaseDirectory,"migration.recovery.json");
    private sealed record Recovery(bool TargetPortable,string Stage,string Backup,bool HadDestination);
    private static string? RecoverInterrupted(){
        if(!File.Exists(Journal))return null;
        var r=JsonSerializer.Deserialize<Recovery>(File.ReadAllText(Journal))??throw new IOException("Invalid migration recovery journal");
        var dest=Root(r.TargetPortable);
        if(!r.Stage.StartsWith(dest+".staging-",StringComparison.Ordinal)||!r.Backup.StartsWith(dest+".backup-",StringComparison.Ordinal))throw new IOException("Invalid migration recovery paths");
        bool activated=File.Exists(Flag)==r.TargetPortable;
        if(!activated){
            if(Directory.Exists(r.Backup)){
                if(Directory.Exists(dest))Directory.Move(dest,dest+".interrupted-"+Guid.NewGuid().ToString("N"));
                Directory.Move(r.Backup,dest);
            }else if(!r.HadDestination&&!Directory.Exists(r.Stage)&&Directory.Exists(dest))Directory.Move(dest,dest+".interrupted-"+Guid.NewGuid().ToString("N"));
        }else if(File.Exists(Path.Combine(dest,Inactive)))File.Delete(Path.Combine(dest,Inactive));
        if(Directory.Exists(r.Stage))Directory.Delete(r.Stage,true);
        if(File.Exists(Pending))File.Move(Pending,Pending+".interrupted-"+Guid.NewGuid().ToString("N"));
        File.Delete(Journal);
        return activated?"Interrupted migration activation recovered.":"Interrupted migration rolled back; schedule again if needed.";
    }
    private const string Inactive = ".migration-inactive";
    internal static string Root(bool portable) => portable ? Path.Combine(AppContext.BaseDirectory,"Data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"GameLink");
    internal static MigrationRequest? Read() => File.Exists(Pending) ? JsonSerializer.Deserialize<MigrationRequest>(File.ReadAllText(Pending)) : null;
    private static IEnumerable<string> Files(string root) => Directory.Exists(root) ? Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories) : Enumerable.Empty<string>();
    private static string Stamp(string root) => string.Join("\n",Files(root).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).Select(x=>{var f=new FileInfo(x);return Path.GetRelativePath(root,x)+"|"+f.Length+"|"+f.LastWriteTimeUtc.Ticks;}));
    internal static MigrationRequest? Choose(IWin32Window owner,bool target)
    {
        var source=Root(LocalStorage.Portable);var dest=Root(target);
        var answer=MessageBox.Show(owner,$"Current: {source}\nNew: {dest}\n\nCopy current data?\nYes = copy (recommended)\nNo = use destination data\nCancel = keep current selection.\n\nApplies on next startup.","Change data mode",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);
        if(answer==DialogResult.Cancel)return null;
        var stamp=Stamp(dest);
        if(answer==DialogResult.Yes&&stamp.Length>0&&MessageBox.Show(owner,"Destination contains data. Back it up and replace it with a copy of current data?","Destination data",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return null;
        if(answer==DialogResult.No&&File.Exists(Path.Combine(dest,Inactive))&&MessageBox.Show(owner,"This destination is an inactive migration backup. Old pending events may be delivered again and achievement snapshots may be stale. Use it anyway?","Old data warning",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return null;
        return new(LocalStorage.Portable,target,answer==DialogResult.Yes,stamp);
    }
    internal static void Schedule(MigrationRequest? request)
    {
        if(request==null){if(File.Exists(Pending))File.Delete(Pending);return;}
        LocalStorage.Write(Pending,JsonSerializer.Serialize(request));
    }
    private static void RewritePaths(JsonNode? node,string source,string dest)
    {
        if(node is JsonObject obj){foreach(var pair in obj.ToArray()){if((pair.Key is "achievementIconLocalPath" or "gameHeaderLocalPath")&&pair.Value is JsonValue value&&value.TryGetValue<string>(out var path)&&path.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))obj[pair.Key]=dest+path[source.Length..];else if(pair.Key=="recoveredAchievementsJson"&&pair.Value is JsonValue embedded&&embedded.TryGetValue<string>(out var json)){var nested=JsonNode.Parse(json);RewritePaths(nested,source,dest);obj[pair.Key]=nested?.ToJsonString();}else RewritePaths(pair.Value,source,dest);}}
        else if(node is JsonArray array)foreach(var child in array)RewritePaths(child,source,dest);
    }
    internal static string? ApplyAtStartup()
    {
        if(File.Exists(Journal))return RecoverInterrupted();
        if(!File.Exists(Pending))return null;
        string? stage=null,backup=null,dest=null;bool installed=false,activated=false;
        try {
            var r=Read()??throw new IOException("Invalid migration request.");
            if(File.Exists(Flag)==r.TargetPortable){File.Delete(Pending);return "Data mode already applied.";}
            if(r.SourcePortable==r.TargetPortable)throw new IOException("Invalid migration modes.");
            var source=Root(r.SourcePortable);dest=Root(r.TargetPortable);
            if(Stamp(dest)!=r.DestinationStamp)throw new IOException("Destination changed since confirmation. Schedule the migration again.");
            if(r.Copy){
                stage=dest+".staging-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(stage);
                foreach(var file in Files(source)){
                    var rel=Path.GetRelativePath(source,file);if(rel==Inactive||file.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase))continue;
                    var output=Path.Combine(stage,rel);Directory.CreateDirectory(Path.GetDirectoryName(output)!);File.Copy(file,output);
                    if(new FileInfo(file).Length!=new FileInfo(output).Length)throw new IOException("Copy verification failed: "+rel);
                }
                foreach(var folder in new[]{"queue","rejected"})foreach(var file in Files(Path.Combine(stage,folder)).Where(f=>f.EndsWith(".json",StringComparison.OrdinalIgnoreCase))){var node=JsonNode.Parse(File.ReadAllText(file));RewritePaths(node,source,dest);LocalStorage.Write(file,node!.ToJsonString());}
                var db=Path.Combine(stage,"history.db");if(File.Exists(db)){using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=db,Pooling=false}.ToString());c.Open();using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA quick_check";if(!Equals(cmd.ExecuteScalar(),"ok"))throw new IOException("Copied history database failed integrity check.");}
                var backupPath=dest+".backup-"+Guid.NewGuid().ToString("N");
                LocalStorage.Write(Journal,JsonSerializer.Serialize(new Recovery(r.TargetPortable,stage,backupPath,Directory.Exists(dest))));
                if(Directory.Exists(dest)){backup=backupPath;Directory.Move(dest,backup);}
                Directory.Move(stage,dest);installed=true;
            } else {Directory.CreateDirectory(dest);}
            var probe=Path.Combine(dest,".write-test-"+Guid.NewGuid().ToString("N"));File.WriteAllText(probe,"");File.Delete(probe);
            // Mark the old root before activation; never remove its data.
            if(Directory.Exists(source))LocalStorage.Write(Path.Combine(source,Inactive),DateTimeOffset.UtcNow.ToString("O"));
            if(r.TargetPortable)LocalStorage.Write(Flag,"");else File.Delete(Flag);
            activated=true;
            if(File.Exists(Path.Combine(dest,Inactive)))File.Delete(Path.Combine(dest,Inactive));
            File.Delete(Pending);
            File.Delete(Journal);
            return "Data mode changed successfully. Original data preserved."+(backup==null?"":" Destination backup: "+backup);
        } catch(Exception ex){
            if(File.Exists(Journal)){try{return RecoverInterrupted()+" "+ex.Message;}catch(Exception recovery){throw new IOException("Migration recovery failed; startup stopped to protect data.",recovery);}}
            if(!activated){try{if(installed&&dest!=null)Directory.Move(dest,dest+".failed-"+Guid.NewGuid().ToString("N"));if(backup!=null&&dest!=null&&!Directory.Exists(dest))Directory.Move(backup,dest);}catch(Exception rollback){ex=new IOException(ex.Message+"; rollback: "+rollback.Message);}}
            try{if(stage!=null&&Directory.Exists(stage))Directory.Delete(stage,true);}catch{}
            try{File.Move(Pending,Pending+".failed-"+Guid.NewGuid().ToString("N"));}catch{}
            return (activated?"Data mode activated with cleanup warning: ":"Data migration failed; original mode retained: ")+ex.Message;
        }
    }
}
