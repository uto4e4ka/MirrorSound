using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MirrorSound;

/// <summary>Windows audio output: a NAudio buffer played through WASAPI shared mode.</summary>
public sealed class WasapiPlayback : IPlayback
{
    private readonly BufferedWaveProvider _buffer;
    private readonly VolumeWaveProvider16 _volume;
    private readonly WasapiOut _output;
    private byte[] _trash = [];

    public WasapiPlayback(int sampleRate, int channels)
    {
        _buffer = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, channels))
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true,
        };
        _volume = new VolumeWaveProvider16(_buffer);
        _output = new WasapiOut(AudioClientShareMode.Shared, true, 40);
        _output.Init(_volume);
    }

    public int BufferedBytes => _buffer.BufferedBytes;
    public float Volume { set => _volume.Volume = value; }
    public void Add(byte[] data, int count) => _buffer.AddSamples(data, 0, count);

    public void Skip(int bytes)
    {
        if (_trash.Length < bytes) _trash = new byte[bytes];
        _buffer.Read(_trash, 0, bytes);
    }

    public void Play() => _output.Play();
    public void Dispose() => _output.Dispose();
}
