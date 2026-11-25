using Microsoft.Win32;
namespace GameLink.Services;
internal static class StartupRegistration
{
    public static void Set(bool enabled){using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(enabled)key.SetValue("GameLink","\""+Application.ExecutablePath+"\"");else key.DeleteValue("GameLink",false);}
}
