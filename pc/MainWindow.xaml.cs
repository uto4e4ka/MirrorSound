using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using QRCoder;
using Forms = System.Windows.Forms;

namespace MirrorSound;

public partial class MainWindow : Window
{
    private const int BarCount = 28;
    private const double BarWidth = 6, BarGap = 4, MeterHeight = 70;

    private static readonly Brush Green = Frozen(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly Brush Amber = Frozen(Color.FromRgb(0xF5, 0x9E, 0x0B));

    private readonly Receiver _receiver = new();
    private readonly WebServer _web = new();
    private readonly List<NetworkCandidate> _candidates = NetworkInfo.Candidates();
    private readonly Forms.NotifyIcon _tray = new();
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

    public MainWindow(EventWaitHandle showSignal)
    {
        InitializeComponent();

        _selected = _candidates.FirstOrDefault(c => c.Address == _settings.Network) ?? _candidates.FirstOrDefault();
        BuildMeter();
        UpdateNetwork();

        VolumeSlider.Value = Math.Clamp(_settings.Volume, 0, 100);
        _receiver.Volume = (float)(VolumeSlider.Value / 100);
        AutostartToggle.IsChecked = Autostart.IsEnabled;
        _saveDelay.Tick += (_, _) =>
        {
            _saveDelay.Stop();
            _settings.Save();
        };

        _web.FallbackHosts = () => _candidates.Where(c => c.Score > 0).Select(c => c.Address);
        _receiver.Status += text => Dispatcher.BeginInvoke(() => StatusText.Text = text);
        _timer.Tick += (_, _) => Tick();

        SetupTray();
        SourceInitialized += (_, _) => DarkTitleBar();
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) Hide();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _settings.Save();
            _tray.Visible = false;
            _tray.Dispose();
            _receiver.Dispose();
            _web.Dispose();
        };

        // Another launch of the exe asks us to come to the front.
        ThreadPool.RegisterWaitForSingleObject(showSignal,
            (_, _) => Dispatcher.BeginInvoke(ShowFromTray), null, Timeout.Infinite, false);

        // Start right away: with autostart the window may stay hidden in the tray.
        _receiver.Start();
        _web.Start();
        _timer.Start();
        _ready = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_exiting) return;

        // The close button keeps MirrorSound running in the tray, so the phone can still connect.
        e.Cancel = true;
        Hide();
        if (!_settings.TrayHintShown)
        {
            _settings.TrayHintShown = true;
            _settings.Save();
            _tray.ShowBalloonTip(4000, "MirrorSound работает в трее",
                "Телефон может подключиться в любой момент. Выйти можно через значок в трее.",
                Forms.ToolTipIcon.None);
        }
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
        Application.Current.Shutdown();
    }

    private void AutostartChanged(object sender, RoutedEventArgs e)
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
            PulseRing.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
            DeviceText.Text = playing ? $"Телефон {device}" : "Телефон ещё не подключён";
            _tray.Text = playing ? "MirrorSound: играет звук" : "MirrorSound: ожидание телефона";
        }

        if (!IsVisible) return; // nothing to animate while hidden in the tray

        bool showQr = !playing || _qrWhilePlaying;
        QrPanel.Visibility = showQr ? Visibility.Visible : Visibility.Collapsed;
        PlayingPanel.Visibility = showQr ? Visibility.Collapsed : Visibility.Visible;
        HideQrButton.Visibility = playing && _qrWhilePlaying ? Visibility.Visible : Visibility.Collapsed;

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
        var brush = (Brush)FindResource("Accent");
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

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(png);
        image.EndInit();
        image.Freeze();
        QrImage.Source = image;
    }

    private void ShowNetworks(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("DarkMenu"),
            ItemContainerStyle = (Style)FindResource("DarkMenuItem"),
            PlacementTarget = NetworkButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
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
        menu.IsOpen = true;
    }

    private void ToggleQr(object sender, RoutedEventArgs e) => _qrWhilePlaying = !_qrWhilePlaying;

    private void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeText == null) return; // fires once during InitializeComponent
        _receiver.Volume = (float)(e.NewValue / 100);
        VolumeText.Text = $"{e.NewValue:0}%";
        _settings.Volume = Math.Round(e.NewValue);
        _saveDelay.Stop();
        _saveDelay.Start();
    }

    private void SetupTray()
    {
        _tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        _tray.Text = "MirrorSound";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowFromTray();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => ShowFromTray());
        menu.Items.Add("Выход", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    // Dark title bar that blends with the window background (Windows 11).
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void DarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));        // DWMWA_USE_IMMERSIVE_DARK_MODE
        int caption = 0x00130F0E;                                      // #0E0F13 as COLORREF (BGR)
        DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));   // DWMWA_CAPTION_COLOR
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
