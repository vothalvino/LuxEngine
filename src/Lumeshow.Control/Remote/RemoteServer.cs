using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Lumeshow.Control.Remote;

/// <summary>
/// A very small web server for the phone remote. It uses plain sockets, so it needs no administrator rights,
/// and answers one request per connection.
/// </summary>
public sealed class RemoteServer : IDisposable
{
    public const int DefaultPort = 8080;
    private const int MaxHeaderBytes = 8192, MaxBodyBytes = 65536, MaxConnections = 32;

    private readonly RemoteApi _api;
    private readonly SemaphoreSlim _slots = new(MaxConnections);
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;

    public RemoteServer(RemoteApi api) => _api = api;

    public bool IsRunning => _listener is not null;

    /// <summary>The port in use, or 0 when the server is off.</summary>
    public int Port { get; private set; }

    /// <summary>Raised (on a network thread) when something unexpected goes wrong.</summary>
    public event Action<Exception>? Error;

    /// <summary>Starts listening. Throws <see cref="SocketException"/> when the port is taken. Port 0 picks a free one.</summary>
    public void Start(int port = DefaultPort)
    {
        Stop();
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _stop = new CancellationTokenSource();
        _ = AcceptLoopAsync(listener, _stop.Token);
    }

    public void Stop()
    {
        _stop?.Cancel();
        _stop = null;
        try { _listener?.Stop(); } catch (SocketException) { }
        _listener = null;
        Port = 0;
    }

    /// <summary>Addresses to type into the phone, e.g. "http://192.168.1.20:8080". The most likely one comes first.</summary>
    public static IReadOnlyList<string> Addresses(int port)
    {
        var found = new List<(int Rank, string Url)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var properties = nic.GetIPProperties();
                foreach (var address in properties.UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var bytes = address.Address.GetAddressBytes();
                    if (bytes[0] == 169 && bytes[1] == 254) continue;   // no network: self-assigned
                    // A network with a gateway (the Wi-Fi router) is the one a phone is most likely on.
                    int rank = properties.GatewayAddresses.Count > 0 ? 0 : 1;
                    found.Add((rank, $"http://{address.Address}:{port}"));
                }
            }
        }
        catch (NetworkInformationException)
        {
            // No list of network cards: the dialog then shows no address.
        }
        return found.OrderBy(f => f.Rank).Select(f => f.Url).Distinct().ToList();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stop).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;   // one failed connection must not stop the remote
            }

            if (!_slots.Wait(0))
            {
                client.Dispose();   // too many at once: drop it, the page tries again
                continue;
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await ServeAsync(client, stop).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
                {
                    // The phone went away mid-request.
                }
                catch (Exception ex)
                {
                    Error?.Invoke(ex);
                }
                finally
                {
                    client.Dispose();
                    _slots.Release();
                }
            }, CancellationToken.None);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken stop)
    {
        client.NoDelay = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var stream = client.GetStream();

        // Read up to the blank line that ends the headers.
        var buffer = new byte[MaxHeaderBytes];
        int length = 0, headerEnd = -1;
        while (headerEnd < 0)
        {
            if (length == buffer.Length)
            {
                await WriteAsync(stream, RemoteResponse.Error(431, "Request too large"), timeout.Token).ConfigureAwait(false);
                return;
            }
            int read = await stream.ReadAsync(buffer.AsMemory(length), timeout.Token).ConfigureAwait(false);
            if (read == 0) return;
            length += read;
            headerEnd = buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8);
        }

        var lines = Encoding.ASCII.GetString(buffer, 0, headerEnd).Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length != 3 || !requestLine[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
        {
            await WriteAsync(stream, RemoteResponse.Error(400, "Bad request"), timeout.Token).ConfigureAwait(false);
            return;
        }

        // The commands carry everything in the URL; a body is read only to throw it away.
        long bodyLength = 0;
        foreach (var line in lines.Skip(1))
        {
            int colon = line.IndexOf(':');
            if (colon > 0 && line.AsSpan(0, colon).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                long.TryParse(line.AsSpan(colon + 1).Trim(), out bodyLength);
        }
        if (bodyLength is < 0 or > MaxBodyBytes)
        {
            await WriteAsync(stream, RemoteResponse.Error(413, "Request too large"), timeout.Token).ConfigureAwait(false);
            return;
        }
        long remaining = bodyLength - (length - headerEnd - 4);
        while (remaining > 0)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), timeout.Token).ConfigureAwait(false);
            if (read == 0) return;
            remaining -= read;
        }

        var (path, query) = ParseTarget(requestLine[1]);
        var response = _api.Handle(requestLine[0].ToUpperInvariant(), path, query);
        await WriteAsync(stream, response, timeout.Token).ConfigureAwait(false);
    }

    /// <summary>Splits "/api/master?value=50" into the path and its parameters.</summary>
    public static (string Path, Dictionary<string, string> Query) ParseTarget(string target)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        int mark = target.IndexOf('?');
        string path = mark < 0 ? target : target[..mark];
        if (mark >= 0)
            foreach (var pair in target[(mark + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = pair.IndexOf('=');
                string name = equals < 0 ? pair : pair[..equals], value = equals < 0 ? "" : pair[(equals + 1)..];
                query[WebUtility.UrlDecode(name)] = WebUtility.UrlDecode(value);
            }
        return (WebUtility.UrlDecode(path), query);
    }

    private static async Task WriteAsync(NetworkStream stream, RemoteResponse response, CancellationToken cancel)
    {
        byte[] body = Encoding.UTF8.GetBytes(response.Body);
        string reason = response.Status switch
        {
            200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 404 => "Not Found", 405 => "Method Not Allowed",
            413 => "Content Too Large", 429 => "Too Many Requests", 431 => "Request Header Fields Too Large", _ => "Error",
        };
        string head = $"HTTP/1.1 {response.Status} {reason}\r\n" +
                      $"Content-Type: {response.ContentType}\r\n" +
                      $"Content-Length: {body.Length}\r\n" +
                      "Cache-Control: no-store\r\n" +
                      "X-Content-Type-Options: nosniff\r\n" +
                      "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancel).ConfigureAwait(false);
        await stream.WriteAsync(body, cancel).ConfigureAwait(false);
        await stream.FlushAsync(cancel).ConfigureAwait(false);
    }

    public void Dispose() => Stop();
}
