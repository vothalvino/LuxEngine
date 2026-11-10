namespace LuxEngine.Core.Effects;

/// <summary>The beat that synced effects follow: beats per minute and the time (seconds) a beat fell on.</summary>
public readonly record struct Tempo(double Bpm, double Origin)
{
    public const double MinBpm = 20, MaxBpm = 300;

    /// <summary>Beats since <see cref="Origin"/> at the given time.</summary>
    public double BeatsAt(double time) => (time - Origin) * Bpm / 60;
}

/// <summary>Works out the tempo from taps: the average gap between the last few taps.</summary>
public sealed class TapTempo
{
    /// <summary>A longer pause than this starts a new count.</summary>
    public const double ResetSeconds = 2;
    private const int MaxTaps = 8;
    private readonly List<double> _taps = new();

    /// <summary>How many taps the current count has (1 right after a pause).</summary>
    public int Count => _taps.Count;

    /// <summary>Registers a tap. Returns the new BPM, or null while there is only one tap to go on.</summary>
    public double? Tap(double now)
    {
        if (_taps.Count > 0 && now - _taps[^1] > ResetSeconds) _taps.Clear();
        _taps.Add(now);
        if (_taps.Count > MaxTaps) _taps.RemoveAt(0);
        if (_taps.Count < 2) return null;
        double interval = (_taps[^1] - _taps[0]) / (_taps.Count - 1);
        return interval <= 0 ? null : Math.Clamp(60 / interval, Tempo.MinBpm, Tempo.MaxBpm);
    }
}
