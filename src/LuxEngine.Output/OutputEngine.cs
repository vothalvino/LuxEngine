using System.Diagnostics;
using System.Runtime.InteropServices;
using LuxEngine.Core.Engine;
using LuxEngine.Core.Shows;
using LuxEngine.Output.Drivers;

namespace LuxEngine.Output;

/// <summary>
/// Runs a dedicated high-priority thread that asks the <see cref="LightingEngine"/> for a frame
/// and sends it to every output, about 40 times a second.
/// </summary>
public sealed class OutputEngine : IDisposable
{
    private readonly LightingEngine _engine;
    private readonly object _outputsLock = new();
    private List<IDmxOutput> _outputs = new();
    private Thread? _thread;
    private volatile bool _running;

    public OutputEngine(LightingEngine engine) => _engine = engine;

    /// <summary>Frames per second. DMX allows up to about 44 for a full universe.</summary>
    public double RefreshRate { get; set; } = 40;

    /// <summary>The most recently sent frame, for the universe monitor.</summary>
    public DmxFrame? LastFrame { get; private set; }

    public bool IsRunning => _running;

    public IReadOnlyList<IDmxOutput> Outputs
    {
        get { lock (_outputsLock) return _outputs.ToList(); }
    }

    /// <summary>Replaces all outputs with ones built from the show's output settings.</summary>
    public void Configure(IEnumerable<OutputConfig> configs)
    {
        var created = new List<IDmxOutput>();
        foreach (var config in configs.Where(c => c.Enabled))
        {
            IDmxOutput output;
            try
            {
                output = Create(config);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                output = new InvalidOutput(config, ex.Message);
            }
            output.Open();
            created.Add(output);
        }

        List<IDmxOutput> old;
        lock (_outputsLock)
        {
            old = _outputs;
            _outputs = created;
        }
        foreach (var o in old) o.Dispose();
    }

    public static IDmxOutput Create(OutputConfig c) => c.Type switch
    {
        OutputType.ArtNet => new ArtNetOutput(c.Universe, c.Target, c.NetworkUniverse, c.LocalAddress),
        OutputType.Sacn => new SacnOutput(c.Universe, c.Target, c.NetworkUniverse, c.LocalAddress),
        OutputType.EnttecPro => new EnttecProOutput(c.Universe, c.Target),
        OutputType.OpenDmx => new OpenDmxOutput(c.Universe, c.Target),
        _ => throw new ArgumentException($"Unknown output type {c.Type}"),
    };

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(Run) { Name = "LuxEngine DMX output", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(1000);
        _thread = null;
    }

    private void Run()
    {
        using var timer = HighResolutionTimer.Begin();
        var clock = Stopwatch.StartNew();
        double next = 0;

        while (_running)
        {
            try
            {
                var frame = _engine.ComputeFrame();
                LastFrame = frame;
                List<IDmxOutput> outputs;
                lock (_outputsLock) outputs = _outputs;
                foreach (var output in outputs)
                {
                    var data = frame.Universes.TryGetValue(output.Universe, out var u) ? u : Empty;
                    output.Send(data);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"DMX output frame failed: {ex}");
            }

            next += 1.0 / Math.Clamp(RefreshRate, 1, 44);
            double wait = next - clock.Elapsed.TotalSeconds;
            if (wait > 0) Thread.Sleep(TimeSpan.FromSeconds(wait));
            else next = clock.Elapsed.TotalSeconds; // running late: don't try to catch up
        }
    }

    private static readonly byte[] Empty = new byte[PatchHelper.UniverseSize];

    public void Dispose()
    {
        Stop();
        lock (_outputsLock)
        {
            foreach (var o in _outputs) o.Dispose();
            _outputs.Clear();
        }
    }

    /// <summary>Placeholder for an output whose settings are wrong, so the error shows in the UI.</summary>
    private sealed class InvalidOutput : IDmxOutput
    {
        public InvalidOutput(OutputConfig config, string error)
        {
            Description = $"{config.Type} ({config.Target})";
            Universe = config.Universe;
            LastError = error;
        }

        public string Description { get; }
        public int Universe { get; }
        public OutputState State => OutputState.Error;
        public string? LastError { get; }
        public void Open() { }
        public void Send(ReadOnlySpan<byte> data) { }
        public void Dispose() { }
    }
}

/// <summary>Asks Windows for 1 ms timer resolution while output runs, so frames go out evenly.</summary>
internal sealed class HighResolutionTimer : IDisposable
{
    private readonly bool _active;

    private HighResolutionTimer(bool active) => _active = active;

    public static HighResolutionTimer Begin()
    {
        if (!OperatingSystem.IsWindows()) return new HighResolutionTimer(false);
        try
        {
            return new HighResolutionTimer(timeBeginPeriod(1) == 0);
        }
        catch (DllNotFoundException)
        {
            return new HighResolutionTimer(false);
        }
    }

    public void Dispose()
    {
        if (_active) timeEndPeriod(1);
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint period);
}
