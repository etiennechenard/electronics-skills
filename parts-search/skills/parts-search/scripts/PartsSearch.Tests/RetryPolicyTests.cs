using System.Net;
using System.Net.Sockets;
using System.Text;
using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

public class RetryPolicyTests
{
    private sealed class FakeTransport : IRawHttpSender
    {
        private readonly Queue<object> _script = new();
        private readonly TimeSpan _delayBeforeResponse;

        public List<RawHttpRequest> Requests { get; } = [];

        public int SendCount => Requests.Count;

        public FakeTransport(TimeSpan? delayBeforeResponse = null) => _delayBeforeResponse = delayBeforeResponse ?? TimeSpan.Zero;

        public void Enqueue(RawHttpResponse response) => _script.Enqueue(response);

        public void Enqueue(Exception error) => _script.Enqueue(error);

        public async Task<RawHttpResponse> SendAsync(RawHttpRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (_delayBeforeResponse > TimeSpan.Zero)
            {
                await Task.Delay(_delayBeforeResponse, cancellationToken);
            }
            var next = _script.Dequeue();
            return next switch
            {
                RawHttpResponse response => response,
                Exception error => throw error,
                _ => throw new InvalidOperationException("empty transport script"),
            };
        }
    }

    private static (HttpSend Sender, FakeTransport Transport, List<TimeSpan> Waits) Build(
        FakeTransport transport,
        TimeSpan? timeout = null)
    {
        var waits = new List<TimeSpan>();
        var sender = new HttpSend(transport, delay: t =>
        {
            waits.Add(t);
            return Task.CompletedTask;
        }, timeout: timeout);
        return (sender, transport, waits);
    }

    private static RawHttpRequest Get(string url = "https://api.mouser.com/api/v2/search/keyword") =>
        new("GET", url, new Dictionary<string, string>(), null);

    [Fact]
    public async Task FiveHundredThenSuccess_RetriesOnceAndSucceeds()
    {
        var transport = new FakeTransport();
        transport.Enqueue(new RawHttpResponse(503, "down"));
        transport.Enqueue(new RawHttpResponse(200, "{\"ok\":true}"));
        var (sender, _, waits) = Build(transport);

        var result = await sender.SendAsync(VendorId.Mouser, Get());

        Assert.True(result.Succeeded);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("{\"ok\":true}", result.Body);
        Assert.Equal(2, transport.SendCount);
        Assert.Single(waits, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task FiveHundredTwice_BecomesErrorOutcome()
    {
        var transport = new FakeTransport();
        transport.Enqueue(new RawHttpResponse(503, "down"));
        transport.Enqueue(new RawHttpResponse(503, "still down"));
        var (sender, _, waits) = Build(transport);

        var result = await sender.SendAsync(VendorId.Mouser, Get());

        Assert.False(result.Succeeded);
        Assert.Equal("http 503 (1 retry)", result.Error);
        Assert.Equal(2, transport.SendCount);
        Assert.Single(waits, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task NetworkErrorThenSuccess_IsRetried()
    {
        var transport = new FakeTransport();
        transport.Enqueue(new HttpRequestException("connection reset"));
        transport.Enqueue(new RawHttpResponse(200, "{\"ok\":true}"));
        var (sender, _, waits) = Build(transport);

        var result = await sender.SendAsync(VendorId.Mouser, Get());

        Assert.True(result.Succeeded);
        Assert.Equal(2, transport.SendCount);
        Assert.Single(waits, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task FourOhOne_ImmediateErrorWithKeyHint_NoRetry()
    {
        var transport = new FakeTransport();
        transport.Enqueue(new RawHttpResponse(401, "unauthorized"));
        var (sender, _, waits) = Build(transport);

        var result = await sender.SendAsync(VendorId.Mouser, Get());

        Assert.False(result.Succeeded);
        Assert.Contains("MOUSER_API_KEY", result.Error);
        Assert.Contains("parts-search key set mouser", result.Error);
        Assert.Equal(1, transport.SendCount);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task FourOhThree_ImmediateErrorWithKeyHint_NoRetry()
    {
        var transport = new FakeTransport();
        transport.Enqueue(new RawHttpResponse(403, "forbidden"));
        var (sender, _, waits) = Build(transport);

        var result = await sender.SendAsync(
            VendorId.Digikey,
            Get("https://api.digikey.com/products/v4/search/keyword"));

        Assert.False(result.Succeeded);
        Assert.Contains("DIGIKEY_CLIENT_SECRET", result.Error);
        Assert.Contains("parts-search key set digikey", result.Error);
        Assert.Equal(1, transport.SendCount);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task FourTwentyNine_ImmediateError_NoRetry()
    {
        var transport = new FakeTransport();
        transport.Enqueue(new RawHttpResponse(429, "{\"error\":\"quota exceeded\"}"));
        var (sender, _, waits) = Build(transport);

        var result = await sender.SendAsync(VendorId.Mouser, Get());

        Assert.False(result.Succeeded);
        Assert.Equal(429, result.StatusCode);
        Assert.Equal("http 429", result.Error);
        Assert.Equal(1, transport.SendCount);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task Timeout_IsTransientRetriedOnceThenError()
    {
        var transport = new FakeTransport(delayBeforeResponse: TimeSpan.FromSeconds(5));
        var (sender, _, waits) = Build(transport, timeout: TimeSpan.FromMilliseconds(50));

        var result = await sender.SendAsync(VendorId.Newark, Get("https://api.element14.com/catalog/products"));

        Assert.False(result.Succeeded);
        Assert.Contains("timed out", result.Error);
        Assert.Equal(2, transport.SendCount);
        Assert.Single(waits, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task NonOfficialEndpoint_RefusedWithoutSending()
    {
        var transport = new FakeTransport();
        var (sender, _, _) = Build(transport);

        var http = await sender.SendAsync(VendorId.Mouser, Get("http://api.mouser.com/api/v2/search/keyword"));
        var evil = await sender.SendAsync(VendorId.Mouser, Get("https://evil.example.com/steal"));

        Assert.False(http.Succeeded);
        Assert.Contains("refusing", http.Error);
        Assert.False(evil.Succeeded);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task Redirect_NotFollowed_IsAVendorError()
    {
        var transport = new FakeTransport();
        transport.Enqueue(new RawHttpResponse(302, "moved"));
        var (sender, _, _) = Build(transport);

        var result = await sender.SendAsync(VendorId.Mouser, Get());

        // Not followed (one request), and not parsed as a search response either: a
        // redirect on these APIs is anomalous, so it surfaces as that vendor's error.
        Assert.False(result.Succeeded);
        Assert.Equal(302, result.StatusCode);
        Assert.Equal("http 302", result.Error);
        Assert.Single(transport.Requests);
    }
}

public class HttpClientTransportTests
{
    [Fact]
    public async Task FormEncodedBody_RidesWithFormContentTypeHeader()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var sendTask = new HttpClientTransport().SendAsync(
            new RawHttpRequest(
                "POST",
                $"http://127.0.0.1:{port}/v1/oauth2/token",
                new Dictionary<string, string>(),
                "grant_type=client_credentials&client_id=x&client_secret=y",
                "application/x-www-form-urlencoded"),
            CancellationToken.None);

        using var client = await listener.AcceptTcpClientAsync();
        using var stream = client.GetStream();
        var requestText = await ReadRequestText(stream);

        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"));

        var result = await sendTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("ok", result.Body);
        Assert.Contains("Content-Type: application/x-www-form-urlencoded", requestText);
        Assert.Contains("grant_type=client_credentials&client_id=x&client_secret=y", requestText);
        listener.Stop();
    }

    private static async Task<string> ReadRequestText(NetworkStream stream)
    {
        var buffer = new byte[65536];
        var received = new List<byte>();
        while (!Encoding.ASCII.GetString(received.ToArray()).Contains("\r\n\r\n"))
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));
            if (read == 0)
            {
                break;
            }
            received.AddRange(buffer.AsMemory(0, read).ToArray());
        }
        return Encoding.ASCII.GetString(received.ToArray());
    }
}
