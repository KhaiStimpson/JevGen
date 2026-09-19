using System.Net;
using System.Text;

namespace JevGen.IntegrationTests;

/// <summary>
/// A real HTTP server speaking the Jev System One protocol, so provider tests exercise the
/// whole transport: headers, status codes, serialization and error bodies.
/// </summary>
/// <remarks>
/// Every request is validated against <see cref="SystemOneSchema"/> before <see cref="Respond"/>
/// sees it, and a request that does not conform is rejected with the validation body the live
/// API returns. A mock that answers whatever it is sent is how 1.0.0-preview.1 shipped a wire
/// format no host accepts.
/// </remarks>
public sealed class MockJevServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _loop;

    public MockJevServer()
    {
        var port = FindFreePort();
        BaseAddress = new Uri($"http://127.0.0.1:{port}/");

        _listener = new HttpListener();
        _listener.Prefixes.Add(BaseAddress.ToString());
        _listener.Start();

        _loop = Task.Run(AcceptAsync);
    }

    /// <summary>Where the server is listening.</summary>
    public Uri BaseAddress { get; }

    /// <summary>The requests the server has received, in order.</summary>
    public List<ReceivedRequest> Requests { get; } = [];

    /// <summary>Produces the response for a request. Replaced per test.</summary>
    public Func<ReceivedRequest, MockResponse> Respond { get; set; } =
        static _ => MockResponse.Json("""{"model":"jev-latest","answers":{}}""");

    /// <summary>
    /// Whether requests are held to the System One schema. Off only for tests that deliberately
    /// send something else.
    /// </summary>
    public bool ValidateSchema { get; set; } = true;

    /// <summary>The schema failures of the requests received, keyed by arrival order.</summary>
    public List<string> SchemaFailures { get; } = [];

    /// <summary>One request as the server saw it.</summary>
    public sealed record ReceivedRequest(string Method, string Path, string Body, IReadOnlyDictionary<string, string> Headers)
    {
        /// <summary>The value of a header, or null when it was not sent.</summary>
        public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>A canned response.</summary>
    public sealed record MockResponse(int StatusCode, string Body, IReadOnlyDictionary<string, string>? Headers = null)
    {
        /// <summary>A 200 with a JSON body.</summary>
        public static MockResponse Json(string body, IReadOnlyDictionary<string, string>? headers = null)
            => new(200, body, headers);

        /// <summary>A failure with a JSON body.</summary>
        public static MockResponse Error(int statusCode, string body = """{"error":{"message":"failed"}}""")
            => new(statusCode, body);

        /// <summary>The bare 404 OpenRouter returns for a path it does not serve.</summary>
        public static MockResponse NotFound() => new(404, """{"error":{"message":"Not Found","code":404}}""");
    }

    private async Task AcceptAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);

                var headers = context.Request.Headers.AllKeys
                    .Where(key => key is not null)
                    .ToDictionary(key => key!, key => context.Request.Headers[key] ?? string.Empty, StringComparer.OrdinalIgnoreCase);

                var received = new ReceivedRequest(
                    context.Request.HttpMethod,
                    context.Request.Url?.AbsolutePath ?? "/",
                    body,
                    headers);

                lock (Requests)
                {
                    Requests.Add(received);
                }

                var response = Answer(received);

                context.Response.StatusCode = response.StatusCode;
                context.Response.ContentType = "application/json";

                if (response.Headers is not null)
                {
                    foreach (var (name, value) in response.Headers)
                    {
                        context.Response.Headers[name] = value;
                    }
                }

                var payload = Encoding.UTF8.GetBytes(response.Body);
                context.Response.ContentLength64 = payload.Length;
                await context.Response.OutputStream.WriteAsync(payload).ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                // The client disconnected; nothing useful to do.
            }
            finally
            {
                context.Response.Close();
            }
        }
    }

    /// <summary>Rejects a request that does not conform before letting the test answer it.</summary>
    private MockResponse Answer(ReceivedRequest received)
    {
        if (!ValidateSchema)
        {
            return Respond(received);
        }

        var failures = SystemOneSchema.Validate(received.Body);

        if (failures.Count == 0)
        {
            return Respond(received);
        }

        lock (Requests)
        {
            SchemaFailures.AddRange(failures);
        }

        return new MockResponse(422, SystemOneSchema.ToValidationBody(failures));
    }

    private static int FindFreePort()
    {
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);

        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        _listener.Close();

        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }

        _shutdown.Dispose();
    }
}
