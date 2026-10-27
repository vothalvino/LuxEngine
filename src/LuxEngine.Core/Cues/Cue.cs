namespace LuxEngine.Core.Cues;

/// <summary>A step in the show. A cue combines several presets and fades to them.</summary>
public sealed class Cue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";

    /// <summary>Presets that make up this cue, in order. For color and position later presets win;
    /// for dimmers the brightest value wins.</summary>
    public List<Guid> PresetIds { get; set; } = new();

    /// <summary>Seconds to fade up and to move to new color/position.</summary>
    public double FadeIn { get; set; } = 2.0;

    /// <summary>Seconds for lights that get darker to fade down.</summary>
    public double FadeOut { get; set; } = 2.0;

    /// <summary>Seconds to wait after GO before the fade starts.</summary>
    public double Delay { get; set; }

    /// <summary>When true, the next cue starts automatically after this one completes.</summary>
    public bool AutoFollow { get; set; }

    /// <summary>Extra wait before an auto-follow, after the fade finished.</summary>
    public double FollowDelay { get; set; }
}

public sealed class CueList
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Main";
    public List<Cue> Cues { get; set; } = new();

    /// <summary>When true, GO after the last cue starts again at the first cue.</summary>
    public bool Loop { get; set; }
}
