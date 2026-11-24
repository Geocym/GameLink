namespace GameLink.Services;
public sealed class LogService
{
    private readonly object gate=new();
    public volatile bool Enabled=true;
    public event Action<string>? Message;
    public void Info(string message)=>Write(message);
    public void Error(string message)=>Write("ERROR: "+message);
    private void Write(string message) {
        var line=$"[{DateTime.Now:HH:mm:ss}] {message}";
        if(Enabled) lock(gate) try {
            var dir=Path.Combine(LocalStorage.Root,"logs"); Directory.CreateDirectory(dir);
            var path=Path.Combine(dir,"gamelink.log");
            if(File.Exists(path) && new FileInfo(path).Length>=2_000_000) {
                for(int i=3;i>=1;i--) { var old=path+"."+i; if(File.Exists(old)) File.Move(old,path+"."+(i+1),true); }
                File.Move(path,path+".1",true);
            }
            File.AppendAllText(path,$"{DateTime.Now:yyyy-MM-dd} {line}"+Environment.NewLine);
        } catch(IOException) {} catch(UnauthorizedAccessException) {}
        Message?.Invoke(line);
    }
}
