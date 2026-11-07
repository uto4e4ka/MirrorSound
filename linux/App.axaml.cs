using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace MirrorSound;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            // With autostart the app starts hidden in the tray and just listens for the phone.
            if (desktop.Args?.Contains("--minimized") != true)
                window.Show();
            SingleInstance.Listen(() => Dispatcher.UIThread.Post(window.ShowFromTray));
            desktop.Exit += (_, _) => SingleInstance.Release();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
