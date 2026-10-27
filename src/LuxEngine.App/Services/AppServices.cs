using System.Runtime.InteropServices;
using LuxEngine.Control;
using LuxEngine.Core.Engine;
using LuxEngine.Core.Fixtures;
using LuxEngine.Output;

namespace LuxEngine.App.Services;

/// <summary>The app-wide objects every view uses.</summary>
public static class AppServices
{
    public static AppSettings Settings { get; private set; } = new();
    public static FixtureLibrary Library { get; } = new();
    public static IReadOnlyList<string> LibraryErrors { get; private set; } = Array.Empty<string>();
    public static LightingEngine Engine { get; } = new();
    public static OutputEngine Output { get; } = new(Engine);
    public static ShowDocument Document { get; } = new(Engine);
    public static FixtureSelection Selection { get; } = new();
    public static MidiController Midi { get; } = new(Engine);
    public static SoundToLight Sound { get; } = new(Engine);
    public static MainWindow? MainWindow { get; set; }

    /// <summary>The preset most recently clicked in the preset panel (used by "Add to cue").</summary>
    public static Guid? SelectedPresetId { get; set; }

    public static event Action? SelectedPresetChanged;

    public static void SelectPreset(Guid? id)
    {
        SelectedPresetId = id;
        SelectedPresetChanged?.Invoke();
    }

    public static void Initialize()
    {
        Settings = AppSettings.Load();

        var errors = new List<string>();
        errors.AddRange(Library.LoadFolder(AppPaths.BundledFixtures));
        foreach (var generic in BuiltInGenerics()) Library.Add(generic);
        errors.AddRange(Library.LoadFolder(AppPaths.UserFixtures, userDefined: true));
        LibraryErrors = errors;

        Output.RefreshRate = Settings.RefreshRate;
        Output.Configure(Engine.Show.Outputs);
        Output.Start();
        Midi.Start();
        KeepAwake(true);
    }

    public static void Shutdown()
    {
        Sound.Dispose();
        Midi.Dispose();
        Output.Dispose();
        KeepAwake(false);
    }

    /// <summary>Simple fixtures that cover most cheap LED pars and dimmer packs.</summary>
    private static IEnumerable<FixtureDefinition> BuiltInGenerics()
    {
        yield return FixtureLibrary.CreateGeneric("Dimmer", ChannelType.Intensity);
        yield return FixtureLibrary.CreateGeneric("RGB Par", ChannelType.Red, ChannelType.Green, ChannelType.Blue);
        yield return FixtureLibrary.CreateGeneric("RGBW Par", ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White);
        yield return FixtureLibrary.CreateGeneric("Dimmer RGB Par", ChannelType.Intensity, ChannelType.Red, ChannelType.Green, ChannelType.Blue);
        yield return FixtureLibrary.CreateGeneric("Dimmer RGBW Par", ChannelType.Intensity, ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White);
        yield return FixtureLibrary.CreateGeneric("Dimmer RGB Strobe Par", ChannelType.Intensity, ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.Strobe);
        yield return FixtureLibrary.CreateGeneric("RGBWA UV Par", ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White, ChannelType.Amber, ChannelType.UV);
    }

    /// <summary>Stops Windows from sleeping or turning off the display while the app runs a show.</summary>
    private static void KeepAwake(bool on)
    {
        const uint EsContinuous = 0x80000000, EsSystemRequired = 0x00000001, EsDisplayRequired = 0x00000002;
        SetThreadExecutionState(on ? EsContinuous | EsSystemRequired | EsDisplayRequired : EsContinuous);
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
}
