using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace MirrorSound;

/// <summary>
/// Tiny HTTP server the QR code points to: a page with "Connect" (opens the app via mirrorsound://)
/// and "Download app" (serves the APK embedded in this exe).
/// </summary>
public sealed class WebServer : IDisposable
{
    public const int Port = 47802;

    private readonly CancellationTokenSource _cts = new();
    private readonly byte[]? _apk;

    /// <summary>Extra addresses to hand to the phone after the one it reached us on.</summary>
    public Func<IEnumerable<string>> FallbackHosts { get; set; } = () => [];

    public WebServer()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MirrorSound.apk");
        if (s != null)
        {
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            _apk = ms.ToArray();
        }
    }

    public void Start() => _ = Task.Run(() => Loop(_cts.Token));

    public static string ConnectLink(IEnumerable<string> hosts) =>
        "mirrorsound://connect?h=" + string.Join(",", hosts) +
        "&amp;n=" + Uri.EscapeDataString(Environment.MachineName);

    private async Task Loop(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, Port);
        try { listener.Start(); } catch { return; }
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => Handle(client, ct));
            }
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    private async Task Handle(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var stream = client.GetStream();

                // Read the request head; we only need the path from the first line.
                var head = new StringBuilder();
                var buf = new byte[2048];
                while (!head.ToString().Contains("\r\n\r\n") && head.Length < 16384)
                {
                    int n = await stream.ReadAsync(buf, timeout.Token);
                    if (n == 0) return;
                    head.Append(Encoding.ASCII.GetString(buf, 0, n));
                }
                var parts = head.ToString().Split(' ', 3);
                var path = parts.Length > 1 ? parts[1].Split('?')[0] : "/";

                if (path == "/MirrorSound.apk" && _apk != null)
                {
                    await Send(stream, "200 OK", "application/vnd.android.package-archive", _apk,
                        "Content-Disposition: attachment; filename=\"MirrorSound.apk\"\r\n", timeout.Token);
                }
                else if (path == "/")
                {
                    var local = ((IPEndPoint)client.Client.LocalEndPoint!).Address.MapToIPv4().ToString();
                    var hosts = new[] { local }.Concat(FallbackHosts()).Distinct().Take(4);
                    var html = Page(ConnectLink(hosts), _apk != null);
                    await Send(stream, "200 OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html), "", timeout.Token);
                }
                else
                {
                    await Send(stream, "404 Not Found", "text/plain", "Not found"u8.ToArray(), "", timeout.Token);
                }
            }
            catch { }
        }
    }

    private static async Task Send(NetworkStream stream, string status, string type, byte[] body, string extra, CancellationToken ct)
    {
        var header = $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\n" +
                     $"Cache-Control: no-store\r\n{extra}Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);
        await stream.WriteAsync(body, ct);
    }

    private static string Page(string connectLink, bool hasApk) => $$"""
        <!doctype html>
        <html lang="ru"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta name="theme-color" content="#15131F">
        <title>MirrorSound</title>
        <style>
          * { box-sizing: border-box; }
          body { font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; margin: 0; min-height: 100vh;
                 padding: 40px 20px 28px; color: #ECEEF2;
                 background: radial-gradient(120% 60% at 50% 0%, #2A1F55 0%, #15131F 45%, #0B0C10 100%); }
          .wrap { max-width: 420px; margin: 0 auto; display: flex; flex-direction: column; align-items: center; text-align: center; }
          .logo { width: 96px; height: 96px; border-radius: 28px; display: grid; place-items: center;
                  background: linear-gradient(135deg, #8B5CF6, #3B82F6); box-shadow: 0 16px 48px rgba(124, 92, 246, .45); }
          h1 { font-size: 28px; margin: 22px 0 6px; letter-spacing: -.3px; }
          .sub { color: #9AA0AA; margin: 0 0 6px; font-size: 16px; }
          .pc { color: #C4B5FD; font-size: 14px; margin: 0 0 30px; }
          a.btn { display: block; width: 100%; text-align: center; text-decoration: none; padding: 18px; border-radius: 18px;
                  font-size: 18px; font-weight: 600; margin: 8px 0; }
          .main { background: linear-gradient(135deg, #8B5CF6, #3B82F6); color: #fff; box-shadow: 0 10px 30px rgba(99, 102, 241, .35); }
          .second { background: rgba(255,255,255,.06); color: #ECEEF2; border: 1px solid rgba(255,255,255,.12); }
          .card { width: 100%; margin-top: 28px; padding: 18px; border-radius: 18px; text-align: left;
                  background: rgba(255,255,255,.04); border: 1px solid rgba(255,255,255,.08); }
          .card p { color: #9AA0AA; margin: 0 0 12px; font-size: 14px; line-height: 1.45; }
          .step { display: flex; gap: 10px; align-items: baseline; }
          .num { flex: none; width: 22px; height: 22px; border-radius: 50%; display: grid; place-items: center; font-size: 12px;
                 font-weight: 700; background: rgba(139, 92, 246, .25); color: #C4B5FD; }
        </style></head>
        <body><div class="wrap">
          <div class="logo">
            <svg width="56" height="56" viewBox="0 0 72 72" fill="none" stroke="#fff" stroke-width="6" stroke-linecap="round">
              <circle cx="24" cy="36" r="6" fill="#fff" stroke="none"/>
              <path d="M33.64 24.51A15 15 0 0 1 33.64 47.49M40.07 16.85A25 25 0 0 1 40.07 55.15M46.5 9.19A35 35 0 0 1 46.5 62.81"/>
            </svg>
          </div>
          <h1>MirrorSound</h1>
          <p class="sub">Звук телефона на компьютере</p>
          <p class="pc">Компьютер {{WebUtility.HtmlEncode(Environment.MachineName)}}</p>
          <a class="btn main" href="{{connectLink}}">Подключить</a>
          {{(hasApk ? """
          <div class="card">
            <p class="step"><span class="num">1</span><span>Приложения ещё нет? Скачайте и установите его.</span></p>
            <p class="step"><span class="num">2</span><span>Вернитесь сюда и нажмите «Подключить».</span></p>
            <a class="btn second" href="/MirrorSound.apk">Скачать приложение</a>
          </div>
          """ : "")}}
        </div></body></html>
        """;

    public void Dispose() => _cts.Cancel();
}
