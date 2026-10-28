using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

[CollectionDefinition("output-capture", DisableParallelization = true)]
public sealed class OutputCaptureCollection
{
}

// Captures real Console output, so it must never run alongside other tests that trace.
[Collection("output-capture")]
public class KeyRedactionTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly string DigiKeyClientId = "id-value-7Q2M";
    private static readonly string DigiKeyClientSecret = "secret-value-9X4K";
    private static readonly string MouserApiKey = "mouser-value-5R1T";
    private static readonly string NewarkApiKey = "newark-value-8W6Z";

    private static readonly string[] AllSecretValues =
    [
        DigiKeyClientId,
        DigiKeyClientSecret,
        MouserApiKey,
        NewarkApiKey,
    ];

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
        public Func<RawHttpRequest, RawHttpResponse>? Responder { get; set; }

        public Task<RawHttpResponse> SendAsync(RawHttpRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Responder?.Invoke(request) ?? new RawHttpResponse(200, "{}"));
    }

    private sealed class FakeVendorClient : IVendorClient
    {
        public required VendorId VendorId { get; init; }
        public IReadOnlyList<PartResult> Results { get; init; } = [];
        public Func<KeyConfig, bool>? ConfiguredOverride { get; init; }
        public string? BuiltUrl { get; init; }

        public bool IsConfigured(KeyConfig keyConfig) =>
            ConfiguredOverride?.Invoke(keyConfig)
            ?? keyConfig.GetSecret(VendorId, SecretFieldFor(VendorId)) is not null;

        public RawHttpRequest BuildSearchRequest(string query, int limit) => new(
            "GET",
            BuiltUrl ?? $"https://{Host(VendorId)}/catalog/products?term=any:{Uri.EscapeDataString(query)}",
            new Dictionary<string, string>(),
            null);

        // The realistic leak path: the secret rides the URL until redaction scrubs it.
        public Task<RawHttpRequest> ApplySecretsAsync(RawHttpRequest request, KeyConfig keyConfig, CancellationToken cancellationToken)
        {
            var secret = keyConfig.GetSecret(VendorId, SecretFieldFor(VendorId)) ?? "missing";
            return Task.FromResult(request with { Url = $"{request.Url}&apiKey={secret}&callInfo.apiKey={secret}" });
        }

        public IReadOnlyList<PartResult> ParseResponse(string rawJson, string query) => Results;

        public RawHttpRequest BuildDetailsRequest(string vendorCode) => throw new NotSupportedException();

        public PartResult ParseDetailsResponse(string rawJson) => throw new NotSupportedException();

        private static string Host(VendorId vendor) => vendor switch
        {
            VendorId.Digikey => "api.digikey.com",
            VendorId.Mouser => "api.mouser.com",
            _ => "api.element14.com",
        };

        // The secret each vendor actually authenticates with: the DigiKey client pair
        // (the client secret stands in here), a single api key for Mouser and Newark.
        private static string SecretFieldFor(VendorId vendor) =>
            vendor == VendorId.Digikey ? CredentialTargets.ClientSecretField : CredentialTargets.ApiKeyField;
    }

    private sealed class OutputCapture : IDisposable
    {
        public StringWriter Out { get; } = new();
        public StringWriter Err { get; } = new();

        public OutputCapture()
        {
            _originalOut = Console.Out;
            _originalErr = Console.Error;
            Console.SetOut(Out);
            Console.SetError(Err);
        }

        private readonly TextWriter _originalOut;
        private readonly TextWriter _originalErr;

        public void Dispose()
        {
            Console.SetOut(_originalOut);
            Console.SetError(_originalErr);
            TraceLog.Configure(TraceLevel.Info);
        }
    }

    private sealed class Harness : IDisposable
    {
        public FakeClock Clock { get; } = new();
        public FakeRawSender Sender { get; } = new();
        public SearchService Service { get; }
        public string CacheRoot { get; } =
            Path.Combine(Path.GetTempPath(), "parts-search-tests", Guid.NewGuid().ToString("N"));

        public Harness(params FakeVendorClient[] clients)
        {
            var store = new FakeCredentialStore();
            store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientIdField)] = DigiKeyClientId;
            store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientSecretField)] = DigiKeyClientSecret;
            store.Entries[CredentialTargets.TargetFor(VendorId.Mouser, CredentialTargets.ApiKeyField)] = MouserApiKey;
            store.Entries[CredentialTargets.TargetFor(VendorId.Newark, CredentialTargets.ApiKeyField)] = NewarkApiKey;

            var limiter = new RateLimiter(Clock, delay: _ => Task.CompletedTask);
            var http = new HttpSend(Sender, delay: _ => Task.CompletedTask);
            var cache = new ResponseCache(Clock, enabled: true, root: CacheRoot);
            Service = new SearchService(clients, new KeyConfig(store), cache, limiter, http, Clock);
        }

        public void Dispose()
        {
            if (Directory.Exists(CacheRoot))
                Directory.Delete(CacheRoot, recursive: true);
        }
    }

    private static SearchRequest Search(string query, bool noCache = false) =>
        new(query, null, 1, 10, OutputFormat.Json, noCache);

    public void Dispose() => TraceLog.Configure(TraceLevel.Info);

    [Fact]
    public void DebugTracedUrl_MasksKeyParams_ValueAbsentEverywhere()
    {
        using var harness = new Harness(
            new FakeVendorClient { VendorId = VendorId.Digikey, Results = [Part("A1", "D1")] },
            new FakeVendorClient { VendorId = VendorId.Mouser, Results = [Part("B2", "M2")] },
            new FakeVendorClient { VendorId = VendorId.Newark, Results = [Part("C3", "N3")] });
        using var capture = new OutputCapture();
        TraceLog.Configure(TraceLevel.Debug);

        harness.Service.SearchAsync(Search("leakprobe")).Wait();

        var stderr = capture.Err.ToString();
        Assert.Contains("apiKey=***", stderr);
        Assert.Contains("callInfo.apiKey=***", stderr);
        Assert.All(AllSecretValues, value => Assert.DoesNotContain(value, stderr));
        Assert.DoesNotContain("url=https", capture.Out.ToString());
    }

    [Fact]
    public void ConfiguredSecretValues_NeverReachAnyOutput_AcrossFakedSearch()
    {
        using var harness = new Harness(
            new FakeVendorClient { VendorId = VendorId.Digikey, Results = [Part("A1", "D1")] },
            new FakeVendorClient { VendorId = VendorId.Mouser },
            new FakeVendorClient { VendorId = VendorId.Newark });
        using var capture = new OutputCapture();
        TraceLog.Configure(TraceLevel.Debug);

        var envelope = harness.Service.SearchAsync(Search("secretprobe")).Result;

        Assert.True(envelope.Ok);
        Assert.All(AllSecretValues, value =>
        {
            Assert.DoesNotContain(value, capture.Err.ToString());
            Assert.DoesNotContain(value, capture.Out.ToString());
        });
    }

    [Fact]
    public void Quiet_EmitsNothing()
    {
        using var capture = new OutputCapture();
        TraceLog.Configure(TraceLevel.Quiet);
        TraceLog.Info("info line");
        TraceLog.Debug("debug line");
        Assert.Equal(string.Empty, capture.Err.ToString());
        Assert.Equal(string.Empty, capture.Out.ToString());
    }

    [Fact]
    public void Quiet_SearchFlowEmitsNothing()
    {
        using var harness = new Harness(
            new FakeVendorClient { VendorId = VendorId.Digikey, Results = [Part("A1", "D1")] });
        using var capture = new OutputCapture();
        TraceLog.Configure(TraceLevel.Quiet);

        harness.Service.SearchAsync(Search("quietprobe")).Wait();

        Assert.DoesNotContain("quietprobe", capture.Err.ToString());
        Assert.DoesNotContain("quietprobe", capture.Out.ToString());
        Assert.DoesNotContain("[parts-search]", capture.Err.ToString());
    }

    [Fact]
    public void Info_EmitsStandardLines_NotDebugDetail()
    {
        using var harness = new Harness(
            new FakeVendorClient { VendorId = VendorId.Digikey, Results = [Part("A1", "D1")] },
            new FakeVendorClient { VendorId = VendorId.Mouser, Results = [Part("B2", "M2")] },
            new FakeVendorClient { VendorId = VendorId.Newark, Results = [Part("C3", "N3")] });
        using var capture = new OutputCapture();
        TraceLog.Configure(TraceLevel.Info);

        harness.Service.SearchAsync(Search("infoprobe")).Wait();

        var stderr = capture.Err.ToString();
        Assert.Contains("[parts-search] digikey search cache=MISS http=200 ms=", stderr);
        Assert.Matches(@"digikey search cache=MISS http=200 ms=\d+ results=1", stderr);
        Assert.DoesNotContain("url=", stderr);
        Assert.DoesNotContain("cache-key=", stderr);
        Assert.DoesNotContain("response-bytes=", stderr);
        Assert.DoesNotContain("configured=", stderr);

        var digiKeyAt = stderr.IndexOf("digikey search cache=MISS", StringComparison.Ordinal);
        var mouserAt = stderr.IndexOf("mouser search cache=MISS", StringComparison.Ordinal);
        var newarkAt = stderr.IndexOf("newark search cache=MISS", StringComparison.Ordinal);
        Assert.True(digiKeyAt < mouserAt && mouserAt < newarkAt);
    }

    [Fact]
    public void Debug_EmitsUrls_ByteCounts_AndCacheKeyDigests()
    {
        using var harness = new Harness(
            new FakeVendorClient { VendorId = VendorId.Digikey, Results = [Part("A1", "D1")] });
        using var capture = new OutputCapture();
        TraceLog.Configure(TraceLevel.Debug);

        harness.Service.SearchAsync(Search("debugprobe")).Wait();

        var stderr = capture.Err.ToString();
        Assert.Contains("search url=https://api.digikey.com/catalog/products?term=any:debugprobe&apiKey=***&callInfo.apiKey=***", stderr);
        Assert.Matches(@"cache-key=[0-9a-f]{64}", stderr);
        Assert.Matches(@"response-bytes=\d+", stderr);
        Assert.Contains("digikey configured=yes", stderr);
    }

    [Fact]
    public void CacheHit_AgeHumanized()
    {
        using var harness = new Harness(
            new FakeVendorClient { VendorId = VendorId.Digikey, Results = [Part("A1", "D1")] });
        using var capture = new OutputCapture();
        TraceLog.Configure(TraceLevel.Info);

        harness.Service.SearchAsync(Search("ageprobe")).Wait();
        harness.Clock.UtcNow = FixedNow.AddHours(2);
        harness.Service.SearchAsync(Search("ageprobe")).Wait();

        var stderr = capture.Err.ToString();
        Assert.Contains("[parts-search] digikey search cache=HIT age=2h results=1", stderr);
    }

    [Fact]
    public void LogLevel_GatedByEnvironmentVariable()
    {
        var original = Environment.GetEnvironmentVariable("PARTS_SEARCH_LOG_LEVEL");
        try
        {
            using var capture = new OutputCapture();

            Environment.SetEnvironmentVariable("PARTS_SEARCH_LOG_LEVEL", "debug", EnvironmentVariableTarget.Process);
            TraceLog.ConfigureFromEnvironment();
            var before = capture.Err.ToString().Length;
            TraceLog.Debug("debug gate line");
            Assert.Contains("debug gate line", capture.Err.ToString()[before..]);

            Environment.SetEnvironmentVariable("PARTS_SEARCH_LOG_LEVEL", "quiet", EnvironmentVariableTarget.Process);
            TraceLog.ConfigureFromEnvironment();
            before = capture.Err.ToString().Length;
            TraceLog.Info("quiet gate line");
            Assert.Equal(string.Empty, capture.Err.ToString()[before..]);

            Environment.SetEnvironmentVariable("PARTS_SEARCH_LOG_LEVEL", "bogus", EnvironmentVariableTarget.Process);
            TraceLog.ConfigureFromEnvironment();
            before = capture.Err.ToString().Length;
            TraceLog.Info("fallback info line");
            TraceLog.Debug("suppressed at fallback");
            var added = capture.Err.ToString()[before..];
            Assert.Contains("fallback info line", added);
            Assert.DoesNotContain("suppressed at fallback", added);

            Environment.SetEnvironmentVariable("PARTS_SEARCH_LOG_LEVEL", null, EnvironmentVariableTarget.Process);
            TraceLog.ConfigureFromEnvironment();
            before = capture.Err.ToString().Length;
            TraceLog.Debug("default info suppresses debug");
            TraceLog.Info("default info emits this");
            added = capture.Err.ToString()[before..];
            Assert.Contains("default info emits this", added);
            Assert.DoesNotContain("default info suppresses debug", added);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PARTS_SEARCH_LOG_LEVEL", original, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void HttpSendRetry_EmitsTraceLine()
    {
        using var harness = new Harness(
            new FakeVendorClient { VendorId = VendorId.Digikey, Results = [Part("A1", "D1")] });
        using var capture = new OutputCapture();
        TraceLog.Configure(TraceLevel.Info);
        var calls = 0;
        harness.Sender.Responder = _ => ++calls == 1
            ? new RawHttpResponse(500, "boom")
            : new RawHttpResponse(200, "{}");

        var envelope = harness.Service.SearchAsync(Search("retryprobe")).Result;

        Assert.True(envelope.Ok);
        Assert.Contains("[parts-search] digikey http 500, retrying once", capture.Err.ToString());
        Assert.Equal(VendorStatus.Ok, envelope.Vendors[0].Status);
    }

    [Fact]
    public void NonOfficialEndpointRefusal_SecretNeverReachesOutput()
    {
        var badClient = new FakeVendorClient
        {
            VendorId = VendorId.Digikey,
            BuiltUrl = "http://evil.example.com/search?term=any:x&apiKey=SK-LEAK-123",
        };
        using var harness = new Harness(badClient);
        using var capture = new OutputCapture();
        TraceLog.Configure(TraceLevel.Info);

        var envelope = harness.Service.SearchAsync(Search("leakprobe")).Result;

        var outcome = envelope.Vendors[0];
        Assert.Equal(VendorStatus.Error, outcome.Status);
        Assert.Contains("refusing non-official endpoint: http://evil.example.com/search?term=any:x&apiKey=***", outcome.Error!);
        Assert.DoesNotContain("SK-LEAK-123", outcome.Error!);
        Assert.DoesNotContain(DigiKeyClientSecret, outcome.Error!);
        var stderr = capture.Err.ToString();
        Assert.Contains("refusing non-official endpoint: http://evil.example.com/search?term=any:x&apiKey=***", stderr);
        Assert.DoesNotContain("SK-LEAK-123", stderr);
        Assert.DoesNotContain(DigiKeyClientSecret, stderr);
        Assert.DoesNotContain("SK-LEAK-123", capture.Out.ToString());
    }

    [Fact]
    public void RedactUrl_MasksEveryKeyParam_KeepsOthers()
    {
        Assert.Equal(
            "https://api.element14.com/catalog/products?term=any:led&callInfo.apiKey=***",
            TraceLog.RedactUrl("https://api.element14.com/catalog/products?term=any:led&callInfo.apiKey=ABC123"));
        Assert.Equal(
            "https://api.mouser.com/api/v2/search/keyword?currencyCode=CAD&apiKey=***",
            TraceLog.RedactUrl("https://api.mouser.com/api/v2/search/keyword?currencyCode=CAD&apiKey=XYZ789"));
        Assert.Equal(
            "https://host.test/p?APIKEY=***&token=keep&Key2=***",
            TraceLog.RedactUrl("https://host.test/p?APIKEY=plain&token=keep&Key2=also"));
        Assert.Equal("https://host.test/p", TraceLog.RedactUrl("https://host.test/p"));
    }

    private static PartResult Part(string mpn, string vendorCode) => new()
    {
        Mpn = mpn,
        VendorPartNumber = vendorCode,
        Description = "Test description",
        Manufacturer = "Test Mfr",
        Url = $"https://example.com/{mpn}",
    };
}
