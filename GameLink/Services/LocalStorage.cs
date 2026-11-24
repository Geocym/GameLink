namespace GameLink.Services;
internal static class LocalStorage
{
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"GameLink");
    internal static void Write(string path,string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { var bytes=System.Text.Encoding.UTF8.GetBytes(content); stream.Write(bytes); stream.Flush(true); } File.Move(temp,path,true); }
        finally { if(File.Exists(temp)) File.Delete(temp); }
    }
}
