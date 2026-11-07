namespace MirrorSound;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "MirrorSoundReceiver", out bool firstInstance);
        if (!firstInstance) return;

        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose };
        app.Run(new MainWindow());
    }
}
