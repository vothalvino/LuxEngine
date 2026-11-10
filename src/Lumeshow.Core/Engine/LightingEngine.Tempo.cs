using Lumeshow.Core.Effects;
using Lumeshow.Core.Fixtures;

namespace Lumeshow.Core.Engine;

/// <summary>The live tempo (tap tempo) and fanning values across lights.</summary>
public sealed partial class LightingEngine
{
    private readonly TapTempo _tapTempo = new();
    private Tempo _tempo = new(120, 0);

    /// <summary>The beat that effects set "to the beat" follow. Live only; it is not saved in the show.</summary>
    public Tempo Tempo { get { lock (_sync) return _tempo; } }

    public double Bpm
    {
        get => Tempo.Bpm;
        set
        {
            lock (_sync)
            {
                // Keep the current place in the beat, so effects change speed without jumping.
                double now = Now, beats = _tempo.BeatsAt(now);
                double bpm = Math.Clamp(value, Tempo.MinBpm, Tempo.MaxBpm);
                _tempo = new Tempo(bpm, now - beats * 60 / bpm);
            }
        }
    }

    /// <summary>Tap along with the music: each tap is a beat. Returns the BPM in use after the tap.</summary>
    public double Tap()
    {
        lock (_sync)
        {
            double now = Now;
            double bpm = _tapTempo.Tap(now) ?? _tempo.Bpm;
            _tempo = new Tempo(bpm, now);   // the beat falls on the tap
            return bpm;
        }
    }

    /// <summary>How far into the current beat we are, 0–1 (for a blinking beat light).</summary>
    public double BeatPhase
    {
        get
        {
            var tempo = Tempo;
            double beats = tempo.BeatsAt(Now);
            return beats - Math.Floor(beats);
        }
    }

    /// <summary>
    /// Fans a kind of channel across the lights: the first light (in stage order) gets <paramref name="from"/>,
    /// the last gets <paramref name="to"/> (0–255), and the ones in between are spread evenly.
    /// </summary>
    public void Fan(IEnumerable<Guid> fixtureIds, ChannelType type, double from, double to)
    {
        lock (_sync)
        {
            EnsurePatch();
            var ids = fixtureIds.Distinct().Where(id => _fixtures.TryGetValue(id, out var f) && f.Has(type))
                .OrderByStage(id => _fixtures[id].Instance).ToList();
            var steps = Effects.Fan.Spread(ids.Count, from, to);
            for (int i = 0; i < ids.Count; i++)
                foreach (var ch in _fixtures[ids[i]].Channels.Where(c => c.Channel.Type == type))
                    Programmer.Set(ids[i], ch.Channel.Name, (byte)Math.Clamp(Math.Round(steps[i]), 0, 255));
        }
    }
}
