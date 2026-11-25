namespace GameLink.Services;
public enum LogSeverity { Debug, Information, Warning, Error }
public enum LogCategory { Application, Steam, Achievement, StreamerBot, Network, Artwork, Storage }
public record LogEntry(DateTime Time,LogSeverity Severity,LogCategory Category,string Text){public override string ToString()=>$"[{Time:HH:mm:ss}] [{Severity}] [{Category}] {Text}";}
public sealed class LogService
{
    private readonly object gate=new();
    public volatile bool Enabled=true;
    public LogSeverity Minimum=LogSeverity.Information;
    public int SizeMB=2, Archives=4;
    public event Action<LogEntry>? Message;
    public void Info(string message,LogCategory category=LogCategory.Application)=>Write(message,LogSeverity.Information,category);
    public void Debug(string message,LogCategory category=LogCategory.Application)=>Write(message,LogSeverity.Debug,category);
    public void Warn(string message,LogCategory category=LogCategory.Application)=>Write(message,LogSeverity.Warning,category);
    public void Error(string message,LogCategory category=LogCategory.Application)=>Write(message,LogSeverity.Error,category);
    public void ClearFiles(){lock(gate){var dir=Path.Combine(LocalStorage.Root,"logs");if(Directory.Exists(dir))foreach(var file in Directory.GetFiles(dir,"gamelink.log*"))File.Delete(file);}}
    private void Write(string message,LogSeverity severity,LogCategory category) {
        var entry=new LogEntry(DateTime.Now,severity,category,message);
        if(Enabled&&(int)severity>=(int)Minimum) lock(gate) try {
            var dir=Path.Combine(LocalStorage.Root,"logs"); Directory.CreateDirectory(dir);
            var path=Path.Combine(dir,"gamelink.log");var count=Math.Clamp(Archives,0,20);
            if(File.Exists(path) && new FileInfo(path).Length>=Math.Clamp(SizeMB,1,100)*1_000_000L) {
                foreach(var file in Directory.GetFiles(dir,"gamelink.log.*"))if(int.TryParse(System.IO.Path.GetExtension(file).TrimStart('.'),out int n)&&n>=count)File.Delete(file);
                if(count==0)File.Delete(path);else {for(int i=count-1;i>=1;i--){var old=path+"."+i;if(File.Exists(old))File.Move(old,path+"."+(i+1),true);}File.Move(path,path+".1",true);}
            }
            File.AppendAllText(path,$"{entry.Time:yyyy-MM-dd} {entry}"+Environment.NewLine);
        } catch(IOException) {} catch(UnauthorizedAccessException) {}
        Message?.Invoke(entry);
    }
}
