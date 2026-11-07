using System.Runtime.InteropServices;

namespace MirrorSound;

/// <summary>
/// Linux audio output through the PulseAudio "simple" API, which PipeWire also provides (pipewire-pulse).
/// Incoming audio waits in a ring buffer; a thread feeds it to the sound server, and pa_simple_write
/// blocks while the server's ~40 ms buffer is full, so the ring holds the backlog the receiver trims.
/// </summary>
public sealed class PulsePlayback : IPlayback
{
    private readonly IntPtr _pa;
    private readonly byte[] _ring;
    private readonly int _chunk;
    private readonly object _lock = new();
    private readonly Thread _thread;
    private int _head, _count;
    private bool _playing, _disposed;
    private volatile float _volume = 1f;

    public PulsePlayback(int sampleRate, int channels)
    {
        int block = channels * 2;
        int bytesPerSecond = sampleRate * block;
        _ring = new byte[bytesPerSecond * 2];
        _chunk = sampleRate / 100 * block; // 10 ms

        var spec = new SampleSpec { Format = SampleS16LE, Rate = (uint)sampleRate, Channels = (byte)channels };
        var attr = new BufferAttr
        {
            MaxLength = uint.MaxValue,
            TLength = (uint)(bytesPerSecond * 40 / 1000),
            PreBuf = uint.MaxValue,
            MinReq = uint.MaxValue,
            FragSize = uint.MaxValue,
        };
        try
        {
            _pa = pa_simple_new(null, "MirrorSound", StreamPlayback, null, "Звук с телефона",
                ref spec, IntPtr.Zero, ref attr, out int error);
            if (_pa == IntPtr.Zero)
                throw new InvalidOperationException(Marshal.PtrToStringUTF8(pa_strerror(error)) ?? $"PulseAudio error {error}");
        }
        catch (DllNotFoundException)
        {
            throw new InvalidOperationException("нет PulseAudio/PipeWire (libpulse-simple)");
        }

        _thread = new Thread(FeedLoop) { IsBackground = true, Name = "PulseAudio feed" };
        _thread.Start();
    }

    public int BufferedBytes
    {
        get { lock (_lock) return _count; }
    }

    public float Volume { set => _volume = value; }

    public void Add(byte[] data, int count)
    {
        lock (_lock)
        {
            int overflow = _count + count - _ring.Length;
            if (overflow > 0) Drop(overflow);
            int tail = (_head + _count) % _ring.Length;
            int first = Math.Min(count, _ring.Length - tail);
            Buffer.BlockCopy(data, 0, _ring, tail, first);
            Buffer.BlockCopy(data, first, _ring, 0, count - first);
            _count += count;
            Monitor.PulseAll(_lock);
        }
    }

    public void Skip(int bytes)
    {
        lock (_lock) Drop(Math.Min(bytes, _count));
    }

    public void Play()
    {
        lock (_lock)
        {
            _playing = true;
            Monitor.PulseAll(_lock);
        }
    }

    private void Drop(int bytes)
    {
        _head = (_head + bytes) % _ring.Length;
        _count -= bytes;
    }

    private void FeedLoop()
    {
        var chunk = new byte[_chunk];
        while (true)
        {
            lock (_lock)
            {
                while (!_disposed && (!_playing || _count < _chunk))
                    Monitor.Wait(_lock, 100);
                if (_disposed) return;

                int first = Math.Min(_chunk, _ring.Length - _head);
                Buffer.BlockCopy(_ring, _head, chunk, 0, first);
                Buffer.BlockCopy(_ring, 0, chunk, first, _chunk - first);
                Drop(_chunk);
            }

            float volume = _volume;
            if (volume < 0.999f)
            {
                for (int i = 0; i + 1 < chunk.Length; i += 2)
                {
                    int s = (int)((short)(chunk[i] | (chunk[i + 1] << 8)) * volume);
                    chunk[i] = (byte)s;
                    chunk[i + 1] = (byte)(s >> 8);
                }
            }

            if (pa_simple_write(_pa, chunk, (nuint)chunk.Length, out _) < 0) return;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            Monitor.PulseAll(_lock);
        }
        _thread.Join(); // a blocked write returns within the server's ~40 ms buffer
        pa_simple_free(_pa);
    }

    private const int StreamPlayback = 1; // PA_STREAM_PLAYBACK
    private const int SampleS16LE = 3;    // PA_SAMPLE_S16LE

    [StructLayout(LayoutKind.Sequential)]
    private struct SampleSpec
    {
        public int Format;
        public uint Rate;
        public byte Channels;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BufferAttr
    {
        public uint MaxLength, TLength, PreBuf, MinReq, FragSize;
    }

    [DllImport("libpulse-simple.so.0")]
    private static extern IntPtr pa_simple_new(string? server, string name, int dir, string? dev, string streamName,
        ref SampleSpec spec, IntPtr channelMap, ref BufferAttr attr, out int error);

    [DllImport("libpulse-simple.so.0")]
    private static extern int pa_simple_write(IntPtr s, byte[] data, nuint bytes, out int error);

    [DllImport("libpulse-simple.so.0")]
    private static extern void pa_simple_free(IntPtr s);

    [DllImport("libpulse.so.0")]
    private static extern IntPtr pa_strerror(int error);
}
