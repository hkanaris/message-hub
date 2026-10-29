using System.Windows;

namespace MessageHub;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // One instance only: two would fight over the same WebView2 profile. Starting it again
        // brings the running one out of the tray instead. A restart waits briefly for the old
        // instance to let go.
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "MessageHub.Show");
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
            showSignal.Set();
            return;
        }

        // The tray menu is Windows Forms; keep it dark like the rest.
        System.Windows.Forms.Application.SetColorMode(System.Windows.Forms.SystemColorMode.Dark);

        // Windows 11 Fluent styling for menus, dialogs and controls, always dark to match the panes.
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose, ThemeMode = ThemeMode.Dark };
        app.SessionEnding += (_, _) => MainWindow.Quit();

        var window = new MainWindow(HubConfig.Load(), HubState.Load(), startHidden: args.Contains("--tray"));
        new Thread(() =>
        {
            while (showSignal.WaitOne())
                window.Dispatcher.InvokeAsync(window.ShowFromTray);
        }) { IsBackground = true }.Start();

        app.Run(window);
        mutex.ReleaseMutex();
    }
}
