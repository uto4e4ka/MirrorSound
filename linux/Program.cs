using Avalonia;

namespace MirrorSound;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // A second launch (e.g. from the app menu) just brings the running window to the front.
        if (SingleInstance.SignalExisting()) return 0;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
