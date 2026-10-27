using LuxEngine.Core.Engine;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LuxEngine.Control;

public enum AudioSource
{
    /// <summary>Whatever the computer is playing (Spotify, a DJ app…).</summary>
    SystemSound,
    /// <summary>The default microphone or line input.</summary>
    Microphone,
}

/// <summary>
/// Listens to audio and feeds a smoothed 0–1 level into <see cref="LightingEngine.SoundLevel"/>,
/// so lights pump with the music. Windows only (WASAPI).
/// </summary>
public sealed class SoundToLight : IDisposable
{
    private readonly LightingEngine _engine;
    private IWaveIn? _capture;
    private double _envelope;
    private double _peak = 0.05;

    public SoundToLight(LightingEngine engine) => _engine = engine;

    public bool IsRunning => _capture is not null;

    /// <summary>Most recent level, 0–1, for a meter in the UI.</summary>
    public double Level => _envelope;

    public void Start(AudioSource source)
    {
        Stop();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Sound-to-light needs Windows.");
        _capture = source == AudioSource.SystemSound
            ? new WasapiLoopbackCapture()
            : new WasapiCapture(new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia));
        _capture.DataAvailable += OnData;
        _capture.StartRecording();
    }

    public void Stop()
    {
        if (_capture is null) return;
        _capture.DataAvailable -= OnData;
        try { _capture.StopRecording(); } catch (Exception) { /* device already gone */ }
        _capture.Dispose();
        _capture = null;
        _envelope = 0;
        _engine.SoundLevel = 0;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var format = _capture?.WaveFormat;
        if (format is null || e.BytesRecorded == 0) return;

        double sum = 0;
        int count = 0;
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            for (int i = 0; i + 4 <= e.BytesRecorded; i += 4)
            {
                float s = BitConverter.ToSingle(e.Buffer, i);
                sum += s * s;
                count++;
            }
        }
        else if (format.BitsPerSample == 16)
        {
            for (int i = 0; i + 2 <= e.BytesRecorded; i += 2)
            {
                double s = BitConverter.ToInt16(e.Buffer, i) / 32768.0;
                sum += s * s;
                count++;
            }
        }
        if (count == 0) return;

        double rms = Math.Sqrt(sum / count);
        // Automatic gain: follow the loudest recent level and decay slowly.
        _peak = Math.Max(rms, _peak * 0.995);
        double normalized = Math.Clamp(rms / Math.Max(_peak, 0.01), 0, 1);
        // Fast attack, slower release, so lights punch on beats.
        _envelope = normalized > _envelope ? normalized : _envelope * 0.85 + normalized * 0.15;
        _engine.SoundLevel = _envelope;
    }

    public void Dispose() => Stop();
}
