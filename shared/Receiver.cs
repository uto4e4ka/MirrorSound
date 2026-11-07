using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MirrorSound;

/// <summary>Platform audio output fed by <see cref="Receiver"/>: a FIFO of 16-bit PCM that plays once started.</summary>
public interface IPlayback : IDisposable
{
    int BufferedBytes { get; }
    float Volume { set; }
    void Add(byte[] data, int count);
    /// <summary>Drops the oldest <paramref name="bytes"/> of buffered audio.</summary>
    void Skip(int bytes);
    void Play();
}

/// <summary>
/// Answers discovery broadcasts from the phone and plays the PCM stream it sends.
/// Protocol: phone connects over TCP, sends "MSND" + int32 sampleRate + int16 channels (little endian),
/// then raw 16-bit PCM.
/// </summary>
public sealed class Receiver : IDisposable
{
    public const int TcpPort = 47800;
    public const int DiscoveryPort = 47801;

    public event Action<string>? Status;

    /// <summary>Opens the platform audio output for (sampleRate, channels).</summary>
    private readonly Func<int, int, IPlayback> _createPlayback;

    public Receiver(Func<int, int, IPlayback> createPlayback) => _createPlayback = createPlayback;
    public float Volume { get; set; } = 1f;

    /// <summary>Address of the phone currently streaming, or null.</summary>
    public string? Device => _device;
    private volatile string? _device;

    private volatile float _peak;

    /// <summary>Loudest sample (0..1) since the last call; drives the level meter.</summary>
    public float TakePeak()
    {
        var p = _peak;
        _peak = 0;
        return p;
    }

    private readonly CancellationTokenSource _cts = new();
    private CancellationTokenSource? _current;

    public void Start()
    {
        _ = Task.Run(() => DiscoveryLoop(_cts.Token));
        _ = Task.Run(() => AcceptLoop(_cts.Token));
    }

    private async Task DiscoveryLoop(CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            var reply = Encoding.UTF8.GetBytes("MIRRORSOUND!" + Environment.MachineName);

            while (!ct.IsCancellationRequested)
            {
                var request = await udp.ReceiveAsync(ct);
                if (Encoding.UTF8.GetString(request.Buffer) == "MIRRORSOUND?")
                    await udp.SendAsync(reply, request.RemoteEndPoint, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Status?.Invoke("Автопоиск не работает: " + e.Message);
        }
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        TcpListener listener;
        try
        {
            listener = new TcpListener(IPAddress.Any, TcpPort);
            listener.Start();
        }
        catch (Exception e)
        {
            Status?.Invoke($"Порт {TcpPort} занят: {e.Message}");
            return;
        }

        Status?.Invoke("Ожидание телефона…");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                client.NoDelay = true;

                // A new connection replaces the old one (e.g. the phone reconnected after Wi-Fi hiccup).
                _current?.Cancel();
                var connection = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _current = connection;

                _ = Task.Run(async () =>
                {
                    var who = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
                    bool audioFailed = false;
                    using (client)
                    {
                        try
                        {
                            await Play(client.GetStream(), who, connection.Token);
                        }
                        catch (AudioException e)
                        {
                            audioFailed = true;
                            Status?.Invoke("Не удалось открыть звук: " + e.Message);
                        }
                        catch (Exception) { }
                    }
                    if (_current == connection && !ct.IsCancellationRequested)
                        _device = null;
                    if (_current == connection && !ct.IsCancellationRequested && !audioFailed)
                    {
                        _device = null;
                        Status?.Invoke("Ожидание телефона…");
                    }
                });
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            listener.Stop();
        }
    }

    private async Task Play(NetworkStream stream, string who, CancellationToken ct)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idle.CancelAfter(TimeSpan.FromSeconds(5));

        var header = new byte[10];
        await stream.ReadExactlyAsync(header, idle.Token);
        if (Encoding.ASCII.GetString(header, 0, 4) != "MSND") return;

        int rate = BitConverter.ToInt32(header, 4);
        int channels = BitConverter.ToInt16(header, 8);
        if (rate is < 8000 or > 192000 || channels is < 1 or > 8) return;
        int block = channels * 2;
        int bytesPerSecond = rate * block;
        int Align(int bytes) => bytes - bytes % block;

        IPlayback output;
        try
        {
            output = _createPlayback(rate, channels);
        }
        catch (Exception e)
        {
            throw new AudioException(e.Message);
        }
        using var _ = output;
        output.Volume = Volume;

        // Start once ~60 ms is buffered; if the backlog grows past 150 ms, skip back to 60 ms
        // so latency stays low even when Wi-Fi delivers audio in bursts.
        int target = Align(bytesPerSecond * 60 / 1000);
        int maxLag = Align(bytesPerSecond * 150 / 1000);
        var data = new byte[Align(bytesPerSecond / 25)];
        int fill = 0;
        bool playing = false;

        _device = who;
        Status?.Invoke("Играет звук с телефона");

        while (true)
        {
            idle.CancelAfter(TimeSpan.FromSeconds(5));
            int n = await stream.ReadAsync(data.AsMemory(fill), idle.Token);
            if (n == 0) break;
            fill += n;

            int usable = Align(fill);
            if (usable > 0)
            {
                int peak = 0;
                for (int i = 0; i + 1 < usable; i += 8) // every other stereo frame is plenty for a meter
                {
                    int sample = Math.Abs((int)(short)(data[i] | (data[i + 1] << 8)));
                    if (sample > peak) peak = sample;
                }
                float level = peak / 32768f;
                if (level > _peak) _peak = level;

                output.Add(data, usable);
                Buffer.BlockCopy(data, usable, data, 0, fill - usable);
                fill -= usable;
            }

            output.Volume = Volume;

            if (!playing && output.BufferedBytes >= target)
            {
                output.Play();
                playing = true;
            }
            if (output.BufferedBytes > maxLag)
                output.Skip(Align(output.BufferedBytes - target));
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _current?.Cancel();
    }

    private sealed class AudioException(string message) : Exception(message);
}
