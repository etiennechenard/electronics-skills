using System.Text.Json;
using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

// Captures real Console output for the trace-line assertions, so it must never run
// alongside other tests that trace.
[Collection("output-capture")]
public class DigiKeyAuthTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private const string TokenValue = "test-token-value-9f3a";

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = FixedNow;
    }

    private sealed class FakeCredentialStore : ICredentialStore
    {
        public Dictionary<string, string> Entries { get; } = [];

        public string? Read(string target) => Entries.TryGetValue(target, out var value) ? value : null;

        public void Write(string target, string secret) => Entries[target] = secret;

        public bool Delete(string target) => Entries.Remove(target);
    }

    private sealed class FakeRawSender : IRawHttpSender
    {
        public List<RawHttpRequest> Requests { get; } = [];

        public Task<RawHttpResponse> SendAsync(RawHttpRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new RawHttpResponse(200, $$"""{"access_token":"{{TokenValue}}","expires_in":600}"""));
        }
    }

    private sealed class OutputCapture : IDisposable
    {
        public StringWriter Err { get; } = new();

        public OutputCapture()
        {
            _originalErr = Console.Error;
            Console.SetError(Err);
        }

        private readonly TextWriter _originalErr;

        public void Dispose()
        {
            Console.SetError(_originalErr);
            TraceLog.Configure(TraceLevel.Info);
        }
    }

    private sealed class Harness : IDisposable
    {
        public FakeClock Clock { get; } = new();
        public FakeRawSender Sender { get; } = new();
        public string TokenPath { get; }
        public DigiKeyAuth Auth { get; }
        public RateLimiter Limiter { get; }

        public Harness(string? tokenPath = null)
        {
            TokenPath = tokenPath ??
                Path.Combine(Path.GetTempPath(), "parts-search-tests", Guid.NewGuid().ToString("N"), "digikey-token.json");
            var store = new FakeCredentialStore();
            store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientIdField)] = "id-value-7Q2M";
            store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientSecretField)] = "secret-value-9X4K";
            var http = new HttpSend(Sender, delay: _ => Task.CompletedTask);
            Limiter = new RateLimiter(Clock, delay: _ => Task.CompletedTask);
            Auth = new DigiKeyAuth(new KeyConfig(store), http, Clock, Limiter, TokenPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path.GetDirectoryName(TokenPath)))
                Directory.Delete(Path.GetDirectoryName(TokenPath)!, recursive: true);
        }

        public void SeedTokenFile(TimeSpan remaining)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
            var json = JsonSerializer.Serialize(
                new CachedToken("cached-token-value-77b1", Clock.UtcNow + remaining),
                WireJson.Options);
            File.WriteAllText(TokenPath, json);
        }
    }

    private static string Captured(Action action)
    {
        using var capture = new OutputCapture();
        action();
        return capture.Err.ToString();
    }

    [Fact]
    public async Task FreshAcquisition_Fetches_StoresFile_AndReturnsToken()
    {
        using var harness = new Harness();

        var token = await harness.Auth.GetTokenAsync();

        Assert.Equal(TokenValue, token);
        var request = Assert.Single(harness.Sender.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://api.digikey.com/v1/oauth2/token", request.Url);
        Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
        Assert.DoesNotContain(request.Headers.Keys, header => header.Contains("Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("grant_type=client_credentials", request.Body);
        Assert.Contains("client_id=id-value-7Q2M", request.Body);
        Assert.Contains("client_secret=secret-value-9X4K", request.Body);
        Assert.Contains("scope=OAuth2%3Abasic", request.Body);

        Assert.True(File.Exists(harness.TokenPath));
        using var stored = JsonDocument.Parse(File.ReadAllText(harness.TokenPath));
        Assert.Equal(TokenValue, stored.RootElement.GetProperty("access_token").GetString());
        var expiresAt = DateTimeOffset.Parse(stored.RootElement.GetProperty("expires_at").GetString()!);
        Assert.Equal(600, (expiresAt - FixedNow).TotalSeconds);

        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(harness.TokenPath)!, "*.tmp"));
    }

    [Fact]
    public async Task UnexpiredCachedToken_Reused_WithNoHttp()
    {
        using var harness = new Harness();
        harness.SeedTokenFile(TimeSpan.FromHours(1));

        var stderr = Captured(() => harness.Auth.GetTokenAsync().Wait());

        Assert.Equal("cached-token-value-77b1", await harness.Auth.GetTokenAsync());
        Assert.Empty(harness.Sender.Requests);
        Assert.Contains("[parts-search] digikey token reused (expires in 60 min)", stderr);
    }

    [Theory]
    [InlineData(61, "cached-token-value-77b1", 0)]
    [InlineData(59, TokenValue, 1)]
    [InlineData(-30, TokenValue, 1)]
    public async Task RefreshMargin_Boundary_TriggersRefreshOnlyWithinSixtySeconds(
        int remainingSeconds, string expectedToken, int expectedRequests)
    {
        using var harness = new Harness();
        harness.SeedTokenFile(TimeSpan.FromSeconds(remainingSeconds));

        var token = await harness.Auth.GetTokenAsync();

        Assert.Equal(expectedToken, token);
        Assert.Equal(expectedRequests, harness.Sender.Requests.Count);
    }

    [Fact]
    public async Task ForcedRefresh_OverwritesFile_EmitsAcquisitionLine_NotReuseLine()
    {
        using var harness = new Harness();
        harness.SeedTokenFile(TimeSpan.FromSeconds(30));

        var stderr = Captured(() => harness.Auth.GetTokenAsync().Wait());

        Assert.Equal(TokenValue, await harness.Auth.GetTokenAsync());
        var request = Assert.Single(harness.Sender.Requests);
        Assert.Contains("grant_type=client_credentials", request.Body);
        Assert.Contains("[parts-search] digikey token acquired (expires in 10 min)", stderr);
        Assert.DoesNotContain("token reused", stderr);
        using var stored = JsonDocument.Parse(File.ReadAllText(harness.TokenPath));
        Assert.Equal(TokenValue, stored.RootElement.GetProperty("access_token").GetString());
    }

    [Fact]
    public async Task PersistedFile_RoundTrips_AcrossInstances()
    {
        using var first = new Harness();
        await first.Auth.GetTokenAsync();

        using var second = new Harness(first.TokenPath);
        var stderr = Captured(() => second.Auth.GetTokenAsync().Wait());

        Assert.Empty(second.Sender.Requests);
        Assert.Contains("token reused", stderr);
    }

    [Fact]
    public async Task TokenValue_NeverAppearsInAnyOutput()
    {
        using var harness = new Harness();
        harness.SeedTokenFile(TimeSpan.FromSeconds(10));
        using var capture = new OutputCapture();
        TraceLog.Configure(TraceLevel.Debug);

        _ = await harness.Auth.GetTokenAsync();

        var stderr = capture.Err.ToString();
        Assert.DoesNotContain(TokenValue, stderr);
        Assert.DoesNotContain("cached-token-value-77b1", stderr);
    }

    public void Dispose() => TraceLog.Configure(TraceLevel.Info);
}
