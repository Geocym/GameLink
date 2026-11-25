namespace GameLink;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex=new Mutex(true,"Local\\GameLink.SingleInstance",out bool first);
        if(!first) { MessageBox.Show("GameLink is already running."); return; }
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        AppDomain.CurrentDomain.UnhandledException += (_,e)=> { if(e.ExceptionObject is Exception ex)Services.SupportDiagnostics.Crash(ex); };
        var migration=Services.DataMigration.ApplyAtStartup();
        if (!FirstRunSetup.RunIfNeeded()) return;
        Application.Run(new MainForm(migration));
    }
}
