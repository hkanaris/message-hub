using System.Windows;

namespace MessageHub;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // One instance only: two would fight over the same WebView2 profile.
        // A restart waits briefly for the old instance to let go.
        using var mutex = new Mutex(false, "MessageHub.SingleInstance");
        bool owned;
        try
        {
            owned = mutex.WaitOne(args.Contains("--restart") ? 10_000 : 0);
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }

        if (!owned)
        {
            MessageBox.Show("Message Hub is already running.", "Message Hub");
            return;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(new MainWindow(HubConfig.Load(), HubState.Load()));
        mutex.ReleaseMutex();
    }
}
