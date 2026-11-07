using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using QRCoder;

namespace MirrorSound;

public partial class MainWindow : Window
{
    private const int BarCount = 28;
    private const double BarWidth = 6, BarGap = 4, MeterHeight = 70;

    private static readonly IBrush Green = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly IBrush Amber = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));

    private readonly Receiver _receiver = new((rate, channels) => new PulsePlayback(rate, channels));
    private readonly WebServer _web = new();
    private readonly List<NetworkCandidate> _candidates = NetworkInfo.Candidates();
    private readonly TrayIcon _tray = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly double[] _barLevels = new double[BarCount];
    private readonly Random _random = new();

    private readonly Settings _settings = Settings.Load();
    private readonly DispatcherTimer _saveDelay = new() { Interval = TimeSpan.FromMilliseconds(600) };

    private NetworkCandidate? _selected;
    private bool _qrWhilePlaying;
    private bool _wasPlaying;
    private double _level;
    private bool _exiting;
    private bool _ready;

    public MainWindow()
    {
        InitializeComponent();

        _selected = _candidates.FirstOrDefault(c => c.Address == _settings.Network) ?? _candidates.FirstOrDefault();
        BuildMeter();
        UpdateNetwork();

        VolumeSlider.Value = Math.Clamp(_settings.Volume, 0, 100);
        _receiver.Volume = (float)(VolumeSlider.Value / 100);
        VolumeText.Text = $"{VolumeSlider.Value:0}%";
        VolumeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) VolumeChanged(VolumeSlider.Value);
        };
        // Clicking anywhere on the track jumps the knob there, as in the Windows app.
        VolumeSlider.AddHandler(PointerPressedEvent, (_, e) =>
        {
            const double knob = 18;
            double x = e.GetPosition(VolumeSlider).X;
            double width = VolumeSlider.Bounds.Width - knob;
            if (width > 0)
                VolumeSlider.Value = Math.Round(Math.Clamp((x - knob / 2) / width, 0, 1) * 100);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);

        AutostartToggle.IsChecked = Autostart.IsEnabled;
        AutostartToggle.IsCheckedChanged += (_, _) => AutostartChanged();

        _saveDelay.Tick += (_, _) =>
        {
            _saveDelay.Stop();
            _settings.Save();
        };

        _web.FallbackHosts = () => _candidates.Where(c => c.Score > 0).Select(c => c.Address);
        _receiver.Status += text => Dispatcher.UIThread.Post(() => StatusText.Text = text);
        _timer.Tick += (_, _) => Tick();

        SetupTray();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _settings.Save();
            _tray.IsVisible = false;
            _tray.Dispose();
            _receiver.Dispose();
            _web.Dispose();
        };

        // Start right away: with autostart the window may stay hidden in the tray.
        _receiver.Start();
        _web.Start();
        _timer.Start();
        _ready = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_exiting || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
            return;

        // The close button keeps MirrorSound running in the background, so the phone can still connect.
        e.Cancel = true;
        Hide();
        if (!_settings.TrayHintShown)
        {
            _settings.TrayHintShown = true;
            _settings.Save();
            Notify("MirrorSound работает в фоне",
                "Телефон может подключиться в любой момент. Открыть окно — значок в трее или снова запустите MirrorSound.");
        }
    }

    private static void Notify(string title, string body)
    {
        try
        {
            Process.Start(new ProcessStartInfo("notify-send", ["-a", "MirrorSound", "-i", "mirrorsound", title, body])
            {
                UseShellExecute = false,
            })?.Dispose();
        }
        catch { } // no notification daemon: not worth bothering the user about
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void AutostartChanged()
    {
        if (!_ready) return;
        try
        {
            Autostart.Set(AutostartToggle.IsChecked == true);
        }
        catch
        {
            AutostartToggle.IsChecked = Autostart.IsEnabled;
        }
    }

    private void Tick()
    {
        var device = _receiver.Device;
        bool playing = device != null;
        if (playing != _wasPlaying)
        {
            _wasPlaying = playing;
            _qrWhilePlaying = false;
            StatusDot.Fill = playing ? Green : Amber;
            PulseRing.IsVisible = playing;
            DeviceText.Text = playing ? $"Телефон {device}" : "Телефон ещё не подключён";
            _tray.ToolTipText = playing ? "MirrorSound: играет звук" : "MirrorSound: ожидание телефона";
        }

        if (!IsVisible) return; // nothing to animate while hidden

        bool showQr = !playing || _qrWhilePlaying;
        QrPanel.IsVisible = showQr;
        PlayingPanel.IsVisible = !showQr;
        HideQrButton.IsVisible = playing && _qrWhilePlaying;

        if (!playing || showQr) return;

        // Smooth peak level with a quick attack and slow release, then spread it into bars
        // that are taller in the middle and jitter a little so the meter looks alive.
        double peak = Math.Min(1, _receiver.TakePeak() * VolumeSlider.Value / 100 * 1.4);
        _level = Math.Max(peak, _level * 0.88);
        double mid = (BarCount - 1) / 2.0;
        for (int i = 0; i < BarCount; i++)
        {
            double shape = 1 - Math.Abs(i - mid) / mid * 0.65;
            double target = _level * shape * (0.6 + 0.4 * _random.NextDouble());
            _barLevels[i] += (target - _barLevels[i]) * 0.4;
            double h = 6 + _barLevels[i] * (MeterHeight - 6);
            _bars[i].Height = h;
            Canvas.SetTop(_bars[i], (MeterHeight - h) / 2);
        }
    }

    private void BuildMeter()
    {
        var brush = (IBrush)Resources["Accent"]!;
        Meter.Width = BarCount * BarWidth + (BarCount - 1) * BarGap;
        for (int i = 0; i < BarCount; i++)
        {
            var bar = new Rectangle { Width = BarWidth, Height = 6, RadiusX = 3, RadiusY = 3, Fill = brush };
            Canvas.SetLeft(bar, i * (BarWidth + BarGap));
            Canvas.SetTop(bar, (MeterHeight - 6) / 2);
            Meter.Children.Add(bar);
            _bars[i] = bar;
        }
    }

    private void UpdateNetwork()
    {
        if (_selected == null)
        {
            NetworkButton.Content = "нет подключения ▾";
            QrImage.Source = null;
            return;
        }
        NetworkButton.Content = $"{_selected.Address} · {_selected.Adapter} ▾";

        // The phone camera opens the PC's page; the app's own scanner reads the h= list directly.
        var hosts = new[] { _selected.Address }
            .Concat(_candidates.Where(c => c.Score > 0).Select(c => c.Address))
            .Distinct().Take(4);
        var url = $"http://{_selected.Address}:{WebServer.Port}/?h={string.Join(",", hosts)}" +
                  $"&n={Uri.EscapeDataString(Environment.MachineName)}";

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(10, [0x15, 0x13, 0x1F, 0xFF], [0xFF, 0xFF, 0xFF, 0xFF], false);
        QrImage.Source = new Bitmap(new MemoryStream(png));
    }

    private void ShowNetworks(object? sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (var candidate in _candidates)
        {
            var item = new MenuItem { Header = $"{candidate.Address}   {candidate.Adapter}" };
            item.Click += (_, _) =>
            {
                _selected = candidate;
                _settings.Network = candidate.Address;
                _settings.Save();
                UpdateNetwork();
            };
            menu.Items.Add(item);
        }
        menu.ShowAt(NetworkButton);
    }

    private void ToggleQr(object? sender, RoutedEventArgs e) => _qrWhilePlaying = !_qrWhilePlaying;

    private void VolumeChanged(double value)
    {
        _receiver.Volume = (float)(value / 100);
        VolumeText.Text = $"{value:0}%";
        _settings.Volume = Math.Round(value);
        _saveDelay.Stop();
        _saveDelay.Start();
    }

    private void SetupTray()
    {
        _tray.Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://MirrorSound/Assets/icon.png")));
        _tray.ToolTipText = "MirrorSound";
        _tray.Clicked += (_, _) => ShowFromTray();

        var open = new NativeMenuItem("Открыть");
        open.Click += (_, _) => ShowFromTray();
        var exit = new NativeMenuItem("Выход");
        exit.Click += (_, _) => ExitApp();
        _tray.Menu = [open, exit];

        if (Application.Current is { } app)
            TrayIcon.SetIcons(app, [_tray]);
        _tray.IsVisible = true;
    }

    public void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}
