using System.Text.Json;
namespace GameLink.Services;
internal static class DeliveryReceipts
{
    private static string PathFor(string id) => Path.Combine(LocalStorage.Root,"receipts",Guid.Parse(id).ToString("N")+".json");
    internal sealed class Receipt { public bool Queued {get;set;} public HashSet<string> Accepted {get;set;}=new(StringComparer.OrdinalIgnoreCase); }
    internal static Receipt Read(string id) => File.Exists(PathFor(id)) ? JsonSerializer.Deserialize<Receipt>(File.ReadAllText(PathFor(id))) ?? throw new InvalidDataException("Invalid delivery receipt") : new();
    internal static void MarkQueued(string id) { var r=Read(id);r.Queued=true;Save(id,r); }
    internal static void Accept(string id,string target) { var r=Read(id);r.Accepted=new HashSet<string>(r.Accepted,StringComparer.OrdinalIgnoreCase);r.Accepted.Add(target);Save(id,r); }
    private static void Save(string id,Receipt r)=>LocalStorage.Write(PathFor(id),JsonSerializer.Serialize(r));
}
