namespace GameLink;
internal static class TrayBrand
{
    internal static Icon[] Load()=>new[]{"GameLink","Paused","Active","Offline"}.Select(name=>{using var stream=typeof(TrayBrand).Assembly.GetManifestResourceStream("GameLink.Assets."+name+".ico")!;using var icon=new Icon(stream);return (Icon)icon.Clone();}).ToArray();
}
