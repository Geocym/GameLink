namespace GameLink;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex=new Mutex(true,"Local\\GameLink.SingleInstance",out bool first);
        if(!first) { MessageBox.Show("GameLink is already running."); return; }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
