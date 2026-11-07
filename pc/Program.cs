using System.Windows;

namespace MirrorSound;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // A second launch (e.g. the Start menu shortcut while we sit in the tray) just asks
        // the running copy to show its window.
        using var mutex = new Mutex(true, "MirrorSoundReceiver", out bool firstInstance);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "MirrorSoundShowWindow");
        if (!firstInstance)
        {
            showSignal.Set();
            return;
        }

        bool startHidden = args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow(showSignal);
        if (!startHidden) window.Show();
        app.Run();
    }
}
