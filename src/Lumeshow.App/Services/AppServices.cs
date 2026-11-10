using System.Runtime.InteropServices;
using Lumeshow.Control;
using Lumeshow.Control.Remote;
using Lumeshow.Core.Engine;
using Lumeshow.Core.Fixtures;
using Lumeshow.Output;

namespace Lumeshow.App.Services;

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
    public static PresetEditor PresetEditor { get; } = new();
    public static MidiController Midi { get; } = new(Engine);
    public static SoundToLight Sound { get; } = new(Engine);
    public static RemoteApi Remote { get; } = new(Engine);
    public static RemoteServer RemoteServer { get; } = new(Remote);
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
        if (Settings.RemoteEnabled) StartRemote();
        KeepAwake(true);
    }

    /// <summary>
    /// Starts (or restarts) the phone remote with the port and PIN from the settings.
    /// Returns what went wrong in plain words, or null when it runs.
    /// </summary>
    public static string? StartRemote()
    {
        Remote.Pin = Settings.RemotePin;
        if (RemoteServer.IsRunning && RemoteServer.Port == Settings.RemotePort) return null;
        try
        {
            RemoteServer.Start(Settings.RemotePort);
            return null;
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentOutOfRangeException)
        {
            return $"Port {Settings.RemotePort} cannot be used, probably because another program has it. Choose another port.";
        }
    }

    public static void Shutdown()
    {
        RemoteServer.Dispose();
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
