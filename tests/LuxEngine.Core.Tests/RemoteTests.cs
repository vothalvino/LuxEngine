using System.Net;
using System.Text.Json;
using LuxEngine.Control.Remote;
using LuxEngine.Core.Cues;
using LuxEngine.Core.Engine;

namespace LuxEngine.Core.Tests;

public class RemoteTests
{
    private static readonly Dictionary<string, string> None = new();

    private static Dictionary<string, string> Query(params (string Name, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Name, p => p.Value);

    private static JsonElement Body(RemoteResponse response) => JsonDocument.Parse(response.Body).RootElement;

    private static CueButton AddButton(TestShow t, string name, CueButtonMode mode, int column)
    {
        var button = new CueButton { Name = name, Mode = mode, Row = 0, Column = column, FadeIn = 0, FadeOut = 0 };
        t.Show.ButtonPage.Buttons.Add(button);
        return button;
    }

    [Fact]
    public void The_page_is_served_without_a_pin_but_commands_need_it()
    {
        var api = new RemoteApi(new LightingEngine(new TestShow().Show)) { Pin = "1234" };

        var page = api.Handle("GET", "/", None);
        Assert.Equal(200, page.Status);
        Assert.StartsWith("text/html", page.ContentType);
        Assert.Contains("LuxEngine remote", page.Body);

        Assert.Equal(401, api.Handle("GET", "/api/state", None).Status);
        Assert.Equal(401, api.Handle("POST", "/api/go", Query(("pin", "0000"))).Status);
        Assert.Equal(200, api.Handle("GET", "/api/state", Query(("pin", "1234"))).Status);
        Assert.Equal(404, api.Handle("GET", "/somewhere", None).Status);
    }

    [Fact]
    public void Go_back_and_state_follow_the_cue_list()
    {
        var t = new TestShow();
        t.AddCue(0, t.AddPreset("A", (t.Par1, "Intensity", 255)));
        t.AddCue(0, t.AddPreset("B", (t.Par2, "Intensity", 255)));
        var engine = new LightingEngine(t.Show);
        var api = new RemoteApi(engine);
        int actions = 0;
        api.ActionPerformed += () => actions++;

        var before = Body(api.Handle("GET", "/api/state", None));
        Assert.Equal(JsonValueKind.Null, before.GetProperty("cue").ValueKind);
        Assert.Equal("1. Cue 1", before.GetProperty("next").GetString());
        Assert.Equal(0, actions);

        var first = Body(api.Handle("POST", "/api/go", None));
        Assert.Equal("1. Cue 1", first.GetProperty("cue").GetString());
        Assert.Equal("2. Cue 2", first.GetProperty("next").GetString());

        var second = Body(api.Handle("POST", "/api/go", None));
        Assert.Equal("2. Cue 2", second.GetProperty("cue").GetString());

        var back = Body(api.Handle("POST", "/api/back", None));
        Assert.Equal("1. Cue 1", back.GetProperty("cue").GetString());
        Assert.Equal(3, actions);

        // Changing something needs POST, so a link or a page preview cannot start a cue.
        Assert.Equal(405, api.Handle("GET", "/api/go", None).Status);
        Assert.Equal(404, api.Handle("POST", "/api/nothing", None).Status);
    }

    [Fact]
    public void Master_blackout_and_tap()
    {
        double now = 0;
        var engine = new LightingEngine(new TestShow().Show, () => now);
        var api = new RemoteApi(engine);

        Assert.Equal(40, Body(api.Handle("POST", "/api/master", Query(("value", "40")))).GetProperty("master").GetDouble());
        Assert.Equal(0.4, engine.GrandMaster, 3);
        api.Handle("POST", "/api/master", Query(("value", "250")));
        Assert.Equal(1, engine.GrandMaster, 3);
        Assert.Equal(400, api.Handle("POST", "/api/master", Query(("value", "loud"))).Status);
        Assert.Equal(400, api.Handle("POST", "/api/master", None).Status);

        Assert.True(Body(api.Handle("POST", "/api/blackout", None)).GetProperty("blackout").GetBoolean());
        Assert.False(Body(api.Handle("POST", "/api/blackout", None)).GetProperty("blackout").GetBoolean());
        api.Handle("POST", "/api/blackout", Query(("on", "1")));
        api.Handle("POST", "/api/blackout", Query(("on", "1")));
        Assert.True(engine.Blackout);

        JsonElement state = default;
        for (int i = 0; i < 4; i++)
        {
            state = Body(api.Handle("POST", "/api/tap", None));
            now += 0.4;
        }
        Assert.Equal(150, state.GetProperty("bpm").GetDouble());
    }

    [Fact]
    public void Cue_buttons_toggle_and_flash()
    {
        var t = new TestShow();
        var toggle = AddButton(t, "Blue", CueButtonMode.Toggle, 0);
        var flash = AddButton(t, "Blinder", CueButtonMode.Flash, 2);
        var engine = new LightingEngine(t.Show);
        var api = new RemoteApi(engine);

        var listed = Body(api.Handle("GET", "/api/state", None)).GetProperty("buttons");
        Assert.Equal(2, listed.GetArrayLength());
        Assert.Equal("Blue", listed[0].GetProperty("name").GetString());
        Assert.False(listed[0].GetProperty("flash").GetBoolean());
        Assert.True(listed[1].GetProperty("flash").GetBoolean());

        api.Handle("POST", "/api/button", Query(("id", toggle.Id.ToString())));
        Assert.True(engine.IsButtonOn(toggle.Id));
        api.Handle("POST", "/api/button", Query(("id", toggle.Id.ToString())));
        Assert.False(engine.IsButtonOn(toggle.Id));

        var held = Body(api.Handle("POST", "/api/button", Query(("id", flash.Id.ToString()), ("on", "1"))));
        Assert.True(held.GetProperty("buttons")[1].GetProperty("on").GetBoolean());
        api.Handle("POST", "/api/button", Query(("id", flash.Id.ToString()), ("on", "0")));
        Assert.False(engine.IsButtonOn(flash.Id));

        Assert.Equal(400, api.Handle("POST", "/api/button", Query(("id", flash.Id.ToString()))).Status);
        Assert.Equal(404, api.Handle("POST", "/api/button", Query(("id", Guid.NewGuid().ToString()))).Status);
        Assert.Equal(404, api.Handle("POST", "/api/button", None).Status);

        api.Handle("POST", "/api/button", Query(("id", toggle.Id.ToString())));
        api.Handle("POST", "/api/release", None);
        Assert.False(engine.IsButtonOn(toggle.Id));
    }

    [Fact]
    public void Wrong_pins_lock_the_remote_for_a_while()
    {
        double now = 100;
        var engine = new LightingEngine(new TestShow().Show, () => now);
        var api = new RemoteApi(engine) { Pin = "4321" };

        // The page asking without a PIN is not a guess.
        for (int i = 0; i < 20; i++) Assert.Equal(401, api.Handle("GET", "/api/state", None).Status);

        for (int i = 0; i < RemoteApi.MaxWrongPins; i++)
            Assert.Equal(401, api.Handle("GET", "/api/state", Query(("pin", "111" + i))).Status);

        // Locked: even the right PIN has to wait.
        Assert.Equal(429, api.Handle("GET", "/api/state", Query(("pin", "4321"))).Status);
        now += RemoteApi.LockSeconds + 1;
        Assert.Equal(200, api.Handle("GET", "/api/state", Query(("pin", "4321"))).Status);
    }

    [Fact]
    public void New_pins_are_four_digits()
    {
        for (int i = 0; i < 50; i++)
        {
            string pin = RemoteApi.NewPin();
            Assert.Equal(4, pin.Length);
            Assert.All(pin, c => Assert.True(char.IsAsciiDigit(c)));
        }
    }

    [Fact]
    public void Url_targets_are_split_and_decoded()
    {
        var (path, query) = RemoteServer.ParseTarget("/api/button?id=abc&on=1&name=a%20b+c&flag");
        Assert.Equal("/api/button", path);
        Assert.Equal("abc", query["id"]);
        Assert.Equal("1", query["on"]);
        Assert.Equal("a b c", query["name"]);
        Assert.Equal("", query["flag"]);
        Assert.Empty(RemoteServer.ParseTarget("/").Query);
    }

    [Fact]
    public async Task The_server_answers_real_http_requests()
    {
        var t = new TestShow();
        t.AddCue(0, t.AddPreset("A", (t.Par1, "Intensity", 255)));
        var engine = new LightingEngine(t.Show);
        using var server = new RemoteServer(new RemoteApi(engine) { Pin = "2468" });
        server.Start(0);
        Assert.True(server.IsRunning);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}") };

        string page = await http.GetStringAsync("/");
        Assert.Contains("<title>LuxEngine remote</title>", page);

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/state")).StatusCode);

        var go = await http.PostAsync("/api/go?pin=2468", new StringContent("ignored"));
        Assert.Equal(HttpStatusCode.OK, go.StatusCode);
        Assert.Equal("application/json", go.Content.Headers.ContentType?.MediaType);
        Assert.Equal("1. Cue 1", JsonDocument.Parse(await go.Content.ReadAsStringAsync()).RootElement.GetProperty("cue").GetString());
        Assert.Equal(0, engine.CuePlayer.CurrentIndex);

        var master = await http.PostAsync("/api/master?pin=2468&value=25", null);
        Assert.Equal(HttpStatusCode.OK, master.StatusCode);
        Assert.Equal(0.25, engine.GrandMaster, 3);

        // Rubbish instead of HTTP gets a 400, and the server keeps running.
        using (var raw = new System.Net.Sockets.TcpClient())
        {
            await raw.ConnectAsync(IPAddress.Loopback, server.Port);
            await raw.GetStream().WriteAsync("hello\r\n\r\n"u8.ToArray());
            using var reader = new StreamReader(raw.GetStream());
            Assert.StartsWith("HTTP/1.1 400", await reader.ReadLineAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/state?pin=2468")).StatusCode);

        server.Stop();
        Assert.False(server.IsRunning);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetAsync("/api/state?pin=2468"));
    }

    [Fact]
    public void A_taken_port_is_reported()
    {
        using var first = new RemoteServer(new RemoteApi(new LightingEngine()));
        first.Start(0);
        using var second = new RemoteServer(new RemoteApi(new LightingEngine()));
        Assert.Throws<System.Net.Sockets.SocketException>(() => second.Start(first.Port));
        Assert.False(second.IsRunning);
    }
}
