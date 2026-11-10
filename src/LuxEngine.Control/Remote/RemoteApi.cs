using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LuxEngine.Core.Cues;
using LuxEngine.Core.Engine;

namespace LuxEngine.Control.Remote;

/// <summary>What the remote sends back for one request.</summary>
public readonly record struct RemoteResponse(int Status, string ContentType, string Body)
{
    public static RemoteResponse Json(object value, int status = 200) =>
        new(status, "application/json; charset=utf-8", JsonSerializer.Serialize(value, RemoteApi.JsonOptions));

    public static RemoteResponse Error(int status, string message) => Json(new { error = message }, status);
}

/// <summary>
/// The phone remote's commands, without any networking: GO, back, pause, release, blackout, master, tap tempo
/// and the cue buttons. <see cref="RemoteServer"/> puts this on the network.
/// </summary>
public sealed class RemoteApi
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>After this many wrong PINs in a row, nobody can try again for <see cref="LockSeconds"/>.</summary>
    public const int MaxWrongPins = 5;
    public const double LockSeconds = 30;

    private readonly LightingEngine _engine;
    private readonly object _pinLock = new();
    private int _wrongPins;
    private double _lockedUntil = double.NegativeInfinity;

    public RemoteApi(LightingEngine engine) => _engine = engine;

    /// <summary>The PIN a phone has to give. Empty means anyone on the network can use the remote.</summary>
    public string Pin { get; set; } = "";

    /// <summary>Raised (on a network thread) after a command changed something.</summary>
    public event Action? ActionPerformed;

    public static string NewPin() => RandomNumberGenerator.GetInt32(0, 10000).ToString("0000", CultureInfo.InvariantCulture);

    /// <summary>Answers one request. <paramref name="path"/> is the URL path without the query, e.g. "/api/go".</summary>
    public RemoteResponse Handle(string method, string path, IReadOnlyDictionary<string, string> query)
    {
        if (path is "/" or "/index.html")
            return method == "GET" ? new RemoteResponse(200, "text/html; charset=utf-8", RemotePage.Html) : Error(405);
        if (!path.StartsWith("/api/", StringComparison.Ordinal)) return Error(404);

        if (CheckPin(query.GetValueOrDefault("pin", "")) is { } refused) return refused;

        string command = path["/api/".Length..];
        if (command == "state") return method == "GET" ? State() : Error(405);
        if (method != "POST") return Error(405);

        switch (command)
        {
            case "go": _engine.Go(); break;
            case "back": _engine.Back(); break;
            case "pause": _engine.TogglePause(); break;
            case "release":
                _engine.Release();
                _engine.ReleaseButtons();
                break;
            case "blackout":
                _engine.Blackout = query.TryGetValue("on", out var on) ? on is "1" or "true" : !_engine.Blackout;
                break;
            case "tap": _engine.Tap(); break;
            case "master":
                if (!query.TryGetValue("value", out var text)
                    || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double percent)
                    || double.IsNaN(percent))
                    return RemoteResponse.Error(400, "master needs value=0…100");
                _engine.GrandMaster = Math.Clamp(percent, 0, 100) / 100;
                break;
            case "button":
                if (!Guid.TryParse(query.GetValueOrDefault("id"), out var id) || _engine.Show.ButtonPage.Find(id) is not { } button)
                    return RemoteResponse.Error(404, "No such cue button");
                // A flash button is on only while it is held: the page sends on=1 when touched and on=0 when let go.
                if (query.TryGetValue("on", out var state)) _engine.SetButton(id, state is "1" or "true");
                else if (button.Mode == CueButtonMode.Toggle) _engine.ToggleButton(id);
                else return RemoteResponse.Error(400, "A flash button needs on=1 or on=0");
                break;
            default:
                return Error(404);
        }
        ActionPerformed?.Invoke();
        return State();
    }

    private RemoteResponse? CheckPin(string given)
    {
        if (Pin.Length == 0) return null;
        lock (_pinLock)
        {
            double now = _engine.Now;
            if (now < _lockedUntil) return RemoteResponse.Error(429, "Too many wrong PINs. Wait half a minute.");
            bool right = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(Pin));
            if (right)
            {
                _wrongPins = 0;
                return null;
            }
            // A request without a PIN is just the page asking for one; only real guesses count.
            if (given.Length > 0 && ++_wrongPins >= MaxWrongPins)
            {
                _wrongPins = 0;
                _lockedUntil = now + LockSeconds;
            }
            return RemoteResponse.Error(401, "PIN needed");
        }
    }

    private RemoteResponse State()
    {
        lock (_engine.SyncRoot)
        {
            var show = _engine.Show;
            var player = _engine.CuePlayer;
            var cues = show.CueList.Cues;
            int next = player.CurrentIndex + 1;
            if (next >= cues.Count) next = show.CueList.Loop && cues.Count > 0 ? 0 : -1;
            return RemoteResponse.Json(new
            {
                cue = player.CurrentCue is { } cue ? $"{player.CurrentIndex + 1}. {cue.Name}" : null,
                next = next < 0 ? null : $"{next + 1}. {cues[next].Name}",
                fading = player.IsFading,
                paused = player.IsPaused,
                progress = Math.Round(player.Progress, 2),
                blackout = _engine.Blackout,
                master = Math.Round(_engine.GrandMaster * 100),
                bpm = Math.Round(_engine.Bpm),
                buttons = show.ButtonPage.Buttons.OrderBy(b => b.Row).ThenBy(b => b.Column).Select(b => new
                {
                    id = b.Id,
                    name = string.IsNullOrWhiteSpace(b.Name) ? $"Button {b.Row + 1}.{b.Column + 1}" : b.Name,
                    color = b.Color,
                    row = b.Row,
                    column = b.Column,
                    flash = b.Mode == CueButtonMode.Flash,
                    on = _engine.IsButtonOn(b.Id),
                }).ToList(),
            });
        }
    }

    private static RemoteResponse Error(int status) => RemoteResponse.Error(status, status switch
    {
        404 => "Not found",
        405 => "Method not allowed",
        _ => "Error",
    });
}
