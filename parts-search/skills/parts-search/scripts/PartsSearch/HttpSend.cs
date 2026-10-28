using System.Net;
using System.Text;

namespace PartsSearch;

public sealed record RawHttpRequest(
    string Method,
    string Url,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    string? ContentType = null);

public sealed record RawHttpResponse(int StatusCode, string Body);

public interface IRawHttpSender
{
    Task<RawHttpResponse> SendAsync(RawHttpRequest request, CancellationToken cancellationToken);
}

public sealed record HttpSendResult(bool Succeeded, int StatusCode, string Body, string? Error)
{
    public static HttpSendResult Ok(int statusCode, string body) => new(true, statusCode, body, null);

    public static HttpSendResult Fail(string error) => new(false, 0, "", error);
}

public sealed class HttpSend
{
    private static readonly HashSet<string> OfficialHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "api.digikey.com",
        "api.mouser.com",
        "api.element14.com",
    };

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly IRawHttpSender _transport;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly TimeSpan _timeout;

    public HttpSend(IRawHttpSender transport, Func<TimeSpan, Task>? delay = null, TimeSpan? timeout = null)
    {
        _transport = transport;
        _delay = delay ?? Task.Delay;
        _timeout = timeout ?? DefaultTimeout;
    }

    public async Task<HttpSendResult> SendAsync(VendorId vendor, RawHttpRequest request, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !OfficialHosts.Contains(uri.Host))
        {
            return HttpSendResult.Fail($"refusing non-official endpoint: {TraceLog.RedactUrl(request.Url)}");
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_timeout);
                var response = await _transport.SendAsync(request, timeoutCts.Token);
                if (response.StatusCode >= 500 && response.StatusCode <= 599)
                {
                    if (attempt == 1)
                    {
                        TraceLog.Info($"{Name(vendor)} http {response.StatusCode}, retrying once");
                        await _delay(RetryDelay);
                        continue;
                    }
                    return HttpSendResult.Fail($"http {response.StatusCode} (1 retry)");
                }
                if (response.StatusCode is 401 or 403)
                {
                    // The status rides the result so the key-test probe can classify rejections.
                    return new HttpSendResult(false, response.StatusCode, "", $"http {response.StatusCode}: {KeyHint(vendor)}");
                }
                if (response.StatusCode is >= 200 and <= 299)
                {
                    return HttpSendResult.Ok(response.StatusCode, response.Body);
                }
                // Any other status (429 quota, 400, 404, an unfollowed redirect) is a vendor
                // error: its body is not a search response, so parsing it would misreport the
                // vendor as no_results — and caching it would make that sticky for the TTL.
                return new HttpSendResult(false, response.StatusCode, "", $"http {response.StatusCode}");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The per-request timeout fired; caller cancellation (Ctrl+C) propagates.
                if (attempt == 1)
                {
                    TraceLog.Info($"{Name(vendor)} request timed out, retrying once");
                    await _delay(RetryDelay);
                    continue;
                }
                return HttpSendResult.Fail("request timed out (1 retry)");
            }
            catch (HttpRequestException)
            {
                if (attempt == 1)
                {
                    TraceLog.Info($"{Name(vendor)} network error, retrying once");
                    await _delay(RetryDelay);
                    continue;
                }
                return HttpSendResult.Fail("network error (1 retry)");
            }
        }
    }

    private static string Name(VendorId vendor) => vendor.ToString().ToLowerInvariant();

    private static string KeyHint(VendorId vendor) => vendor switch
    {
        VendorId.Digikey => "check DIGIKEY_CLIENT_ID and DIGIKEY_CLIENT_SECRET or re-run parts-search key set digikey",
        VendorId.Mouser => "check MOUSER_API_KEY or re-run parts-search key set mouser",
        _ => "check ELEMENT14_API_KEY or re-run parts-search key set newark",
    };
}

public sealed class HttpClientTransport : IRawHttpSender
{
    private readonly HttpClient _client;

    public HttpClientTransport(TimeSpan? timeout = null)
    {
        _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }, disposeHandler: true)
        {
            Timeout = timeout ?? HttpSend.DefaultTimeout,
        };
    }

    public async Task<RawHttpResponse> SendAsync(RawHttpRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
        foreach (var (name, value) in request.Headers)
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }
        if (request.Body is not null)
        {
            message.Content = new StringContent(request.Body, Encoding.UTF8, request.ContentType ?? "application/json");
        }
        using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new RawHttpResponse((int)response.StatusCode, body);
    }
}
