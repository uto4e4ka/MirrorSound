using System.Net.Sockets;

namespace MirrorSound;

/// <summary>
/// One MirrorSound per user: the first instance listens on a Unix socket, later launches connect to it
/// to ask for the window and exit.
/// </summary>
public static class SingleInstance
{
    private static Socket? _listener;

    private static string SocketPath => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } dir ? dir : Path.GetTempPath(),
        $"mirrorsound-{Environment.UserName}.sock");

    /// <summary>True if another instance is running (and was asked to show itself).</summary>
    public static bool SignalExisting()
    {
        var endpoint = new UnixDomainSocketEndPoint(SocketPath);
        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(endpoint);
            client.Send("show"u8);
            return true;
        }
        catch (SocketException) { }

        try
        {
            File.Delete(SocketPath); // stale socket left by a crash
            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(endpoint);
            _listener.Listen(4);
        }
        catch
        {
            _listener = null; // still run, just without the single-instance guard
        }
        return false;
    }

    public static void Listen(Action onShow)
    {
        if (_listener == null) return;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var client = await _listener.AcceptAsync();
                    onShow();
                }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { }
            }
        });
    }

    public static void Release()
    {
        _listener?.Dispose();
        try { File.Delete(SocketPath); } catch { }
    }
}
