using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using GameLink.Models;
namespace GameLink.Services;
public sealed class SettingsStore
{
    private readonly string _path=Path.Combine(LocalStorage.Root,"settings.json");
    public AppSettings Load()
    {
        if(!File.Exists(_path)) return new AppSettings();
        try {
            var node=JsonNode.Parse(File.ReadAllText(_path))!.AsObject();
            var settings=node.Deserialize<AppSettings>() ?? new AppSettings();
            if(node["EncryptedPassword"]?.GetValue<string>() is string encrypted) {
                settings.StreamerBotPassword=Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted),null,DataProtectionScope.CurrentUser));
            } else if(!string.IsNullOrEmpty(settings.StreamerBotPassword)) Save(settings);
            return settings;
        } catch(Exception ex) {
            MessageBox.Show("Could not load or migrate GameLink settings. Existing file retained. "+ex.Message,"GameLink");
            return new AppSettings();
        }
    }
    public void Save(AppSettings settings)
    {
        var node=JsonSerializer.SerializeToNode(settings)!.AsObject();
        node.Remove(nameof(AppSettings.StreamerBotPassword));
        if(!string.IsNullOrEmpty(settings.StreamerBotPassword)) node["EncryptedPassword"]=Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(settings.StreamerBotPassword),null,DataProtectionScope.CurrentUser));
        LocalStorage.Write(_path,node.ToJsonString(new JsonSerializerOptions { WriteIndented=true }));
    }
}
