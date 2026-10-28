using System.Text.Json;
using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

public class KeyCommandsTests
{
    private sealed class FakeCredentialStore : ICredentialStore
    {
        public Dictionary<string, string> Entries { get; } = [];

        public string? Read(string target) => Entries.TryGetValue(target, out var value) ? value : null;

        public void Write(string target, string secret) => Entries[target] = secret;

        public bool Delete(string target) => Entries.Remove(target);
    }

    private sealed class FakeConsole : IKeyConsole
    {
        public bool IsInteractive { get; init; } = true;
        public Queue<string> Inputs { get; init; } = new();
        public List<string> Written { get; } = [];

        public string ReadLine() =>
            Inputs.Count > 0 ? Inputs.Dequeue() : throw new InvalidOperationException("input script exhausted");

        public void WriteLine(string message) => Written.Add(message);
    }

    private static (KeyCommands Commands, FakeCredentialStore Store, FakeConsole Console) Build(
        FakeConsole console, FakeCredentialStore? store = null)
    {
        store ??= new FakeCredentialStore();
        return (new KeyCommands(store, new KeyConfig(store), console), store, console);
    }

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    [Fact]
    public void Set_DigiKey_PromptsClientPair()
    {
        var console = new FakeConsole { Inputs = new Queue<string>(new[] { "id-1", "secret-1" }) };
        var (commands, store, _) = Build(console);

        var json = commands.Set(new KeyCommandArgs("set", VendorId.Digikey));

        Assert.Equal(
            "{\"ok\":true,\"command\":\"key\",\"sub\":\"set\",\"vendor\":\"digikey\",\"stored\":[\"client-id\",\"client-secret\"]}",
            json);
        Assert.Equal("id-1", store.Entries["parts-search/digikey/client-id"]);
        Assert.Equal("secret-1", store.Entries["parts-search/digikey/client-secret"]);
        Assert.False(store.Entries.ContainsKey("parts-search/digikey/api-key"));
        Assert.Contains("digikey client-id: ", console.Written);
        Assert.Contains("digikey client-secret: ", console.Written);
        Assert.Equal(2, console.Written.Count);
        Assert.All(console.Written, line => Assert.DoesNotContain("id-1", line));
        Assert.All(console.Written, line => Assert.DoesNotContain("secret-1", line));
    }

    [Fact]
    public void Set_Mouser_SingleRequiredPrompt()
    {
        var console = new FakeConsole { Inputs = new Queue<string>(new[] { "mk-1" }) };
        var (commands, store, _) = Build(console);

        var json = commands.Set(new KeyCommandArgs("set", VendorId.Mouser));

        Assert.Equal(
            "{\"ok\":true,\"command\":\"key\",\"sub\":\"set\",\"vendor\":\"mouser\",\"stored\":[\"api-key\"]}",
            json);
        Assert.Equal("mk-1", store.Entries["parts-search/mouser/api-key"]);
        var prompt = Assert.Single(console.Written);
        Assert.Equal("mouser api-key: ", prompt);
    }

    [Fact]
    public void Set_EmptyRequiredField_Reprompts()
    {
        var console = new FakeConsole { Inputs = new Queue<string>(new[] { "", "real-id", "real-secret" }) };
        var (commands, store, _) = Build(console);

        commands.Set(new KeyCommandArgs("set", VendorId.Digikey));

        Assert.Equal("real-id", store.Entries["parts-search/digikey/client-id"]);
        Assert.Equal(2, console.Written.Count(line => line == "digikey client-id: "));
        Assert.All(console.Written, line => Assert.DoesNotContain("real-id", line));
    }

    [Fact]
    public void Set_WarnsWhenEnvVarShadowsStoredSecret()
    {
        var original = Environment.GetEnvironmentVariable("DIGIKEY_CLIENT_ID");
        try
        {
            Environment.SetEnvironmentVariable("DIGIKEY_CLIENT_ID", "env-value", EnvironmentVariableTarget.Process);
            var console = new FakeConsole { Inputs = new Queue<string>(new[] { "id-1", "secret-1" }) };
            var store = new FakeCredentialStore();
            var commands = new KeyCommands(store, new KeyConfig(store), console);

            commands.Set(new KeyCommandArgs("set", VendorId.Digikey));

            Assert.Contains(console.Written, line =>
                line.Contains("DIGIKEY_CLIENT_ID") && line.Contains("override the stored value"));
            Assert.DoesNotContain(console.Written, line => line.Contains("DIGIKEY_CLIENT_SECRET"));
            Assert.All(console.Written, line => Assert.DoesNotContain("env-value", line));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DIGIKEY_CLIENT_ID", original, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void Set_NonInteractiveStdin_ThrowsUsageError()
    {
        var console = new FakeConsole { IsInteractive = false };
        var (commands, store, _) = Build(console);

        var exception = Assert.Throws<KeyUsageException>(
            () => commands.Set(new KeyCommandArgs("set", VendorId.Digikey)));

        Assert.Contains("interactive", exception.Message);
        Assert.Empty(console.Written);
        Assert.Empty(store.Entries);
    }

    [Fact]
    public void List_ReportsAllVendors_PerSecretSources_NeverValues()
    {
        var original = Environment.GetEnvironmentVariable("ELEMENT14_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("ELEMENT14_API_KEY", "e14-env-value", EnvironmentVariableTarget.Process);
            var store = new FakeCredentialStore();
            store.Entries["parts-search/digikey/client-id"] = "id-value-9M4Q";
            store.Entries["parts-search/digikey/client-secret"] = "secret-value-2V8Z";
            store.Entries["parts-search/mouser/api-key"] = "mouser-value-6N1W";
            var console = new FakeConsole();
            var commands = new KeyCommands(store, new KeyConfig(store), console);

            var json = commands.List();

            using var document = Parse(json);
            var vendors = document.RootElement.GetProperty("vendors");
            Assert.Equal("credential manager", vendors.GetProperty("digikey").GetProperty("client-id").GetProperty("source").GetString());
            Assert.Equal("credential manager", vendors.GetProperty("digikey").GetProperty("client-secret").GetProperty("source").GetString());
            Assert.Equal(2, vendors.GetProperty("digikey").EnumerateObject().Count());
            Assert.Equal("credential manager", vendors.GetProperty("mouser").GetProperty("api-key").GetProperty("source").GetString());
            var newark = vendors.GetProperty("newark");
            Assert.Equal("env", newark.GetProperty("api-key").GetProperty("source").GetString());
            Assert.Equal(1, newark.EnumerateObject().Count());

            Assert.DoesNotContain("id-value-9M4Q", json);
            Assert.DoesNotContain("secret-value-2V8Z", json);
            Assert.DoesNotContain("mouser-value-6N1W", json);
            Assert.DoesNotContain("e14-env-value", json);
            Assert.Empty(console.Written);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ELEMENT14_API_KEY", original, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void Clear_DeletesOnlyThisToolsPrefixedEntries_ReportsCleared()
    {
        var console = new FakeConsole();
        var store = new FakeCredentialStore();
        store.Entries["parts-search/digikey/client-id"] = "id-1";
        store.Entries["parts-search/digikey/client-secret"] = "secret-1";
        store.Entries["parts-search/digikey/api-key"] = "key-1";
        store.Entries["other-app/credential"] = "foreign";
        var commands = new KeyCommands(store, new KeyConfig(store), console);

        var json = commands.Clear(new KeyCommandArgs("clear", VendorId.Digikey));

        Assert.Equal(
            "{\"ok\":true,\"command\":\"key\",\"sub\":\"clear\",\"vendor\":\"digikey\",\"cleared\":[\"client-id\",\"client-secret\",\"api-key\"]}",
            json);
        Assert.False(store.Entries.ContainsKey("parts-search/digikey/client-id"));
        Assert.False(store.Entries.ContainsKey("parts-search/digikey/client-secret"));
        Assert.False(store.Entries.ContainsKey("parts-search/digikey/api-key"));
        Assert.Equal("foreign", store.Entries["other-app/credential"]);
    }

    [Fact]
    public void Clear_NothingStored_ReportsEmptyCleared()
    {
        var (commands, store, _) = Build(new FakeConsole());

        var json = commands.Clear(new KeyCommandArgs("clear", VendorId.Newark));

        Assert.Equal(
            "{\"ok\":true,\"command\":\"key\",\"sub\":\"clear\",\"vendor\":\"newark\",\"cleared\":[]}",
            json);
        Assert.Empty(store.Entries);
    }

    // ---- key test probe ----

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeRawSender : IRawHttpSender
    {
        public Func<RawHttpRequest, RawHttpResponse>? Responder { get; set; }
        public List<RawHttpRequest> Requests { get; } = [];

        public Task<RawHttpResponse> SendAsync(RawHttpRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Responder?.Invoke(request) ?? new RawHttpResponse(200, "{}"));
        }
    }

    private sealed class ProbeClient : IVendorClient
    {
        public required VendorId VendorId { get; init; }
        public Func<string, IReadOnlyList<PartResult>>? ParseBehavior { get; init; }

        public bool IsConfigured(KeyConfig keyConfig) =>
            keyConfig.GetSecret(VendorId, CredentialTargets.ClientIdField) is not null;

        public RawHttpRequest BuildSearchRequest(string query, int limit) => new(
            "GET",
            $"https://{Host(VendorId)}/search?q={Uri.EscapeDataString(query)}&limit={limit}",
            new Dictionary<string, string>(),
            null);

        public Task<RawHttpRequest> ApplySecretsAsync(RawHttpRequest request, KeyConfig keyConfig, CancellationToken cancellationToken) =>
            Task.FromResult(request);

        public IReadOnlyList<PartResult> ParseResponse(string rawJson, string query) =>
            ParseBehavior?.Invoke(rawJson) ?? [];

        public RawHttpRequest BuildDetailsRequest(string vendorCode) => throw new NotSupportedException();

        public PartResult ParseDetailsResponse(string rawJson) => throw new NotSupportedException();

        private static string Host(VendorId vendor) => vendor switch
        {
            VendorId.Digikey => "api.digikey.com",
            VendorId.Mouser => "api.mouser.com",
            _ => "api.element14.com",
        };
    }

    private sealed class ProbeHarness : IDisposable
    {
        public FixedClock Clock { get; } = new();
        public FakeRawSender Sender { get; } = new();
        public FakeCredentialStore Store { get; } = new();
        public ResponseCache Cache { get; }
        public SearchService Service { get; }
        public KeyCommands Commands { get; }
        public string CacheRoot { get; } =
            Path.Combine(Path.GetTempPath(), "parts-search-tests", Guid.NewGuid().ToString("N"));

        public ProbeHarness(bool configureDigiKey, params IVendorClient[] clients)
        {
            if (configureDigiKey)
            {
                Store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientIdField)] = "dk-id";
                Store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientSecretField)] = "dk-secret";
            }
            Cache = new ResponseCache(Clock, enabled: true, root: CacheRoot);
            var keyConfig = new KeyConfig(Store);
            Service = new SearchService(
                clients, keyConfig, Cache,
                new RateLimiter(Clock, delay: _ => Task.CompletedTask),
                new HttpSend(Sender, delay: _ => Task.CompletedTask),
                Clock);
            Commands = new KeyCommands(Store, keyConfig, new FakeConsole());
        }

        public void Dispose()
        {
            if (Directory.Exists(CacheRoot))
                Directory.Delete(CacheRoot, recursive: true);
        }
    }

    private static PartResult ProbePart() => new()
    {
        Mpn = "A1",
        VendorPartNumber = "D1",
        Description = "d",
        Manufacturer = "m",
        Url = "https://example.com/a",
    };

    [Fact]
    public async Task Test_Results_YieldPass_UnconfiguredYieldNoKey()
    {
        using var harness = new ProbeHarness(
            configureDigiKey: true,
            new ProbeClient { VendorId = VendorId.Digikey, ParseBehavior = _ => [ProbePart()] });

        var json = await harness.Commands.TestAsync(harness.Service, CancellationToken.None);

        Assert.Equal(
            "{\"ok\":true,\"command\":\"key\",\"sub\":\"test\",\"vendors\":{\"digikey\":\"pass\",\"mouser\":\"no_key\",\"newark\":\"no_key\"}}",
            json);
        var request = Assert.Single(harness.Sender.Requests);
        Assert.Contains("q=resistor&limit=1", request.Url);
    }

    [Fact]
    public async Task Test_EmptyResults_ReportsSuspectKeyType()
    {
        using var harness = new ProbeHarness(
            configureDigiKey: true,
            new ProbeClient { VendorId = VendorId.Digikey, ParseBehavior = _ => [] });

        var json = await harness.Commands.TestAsync(harness.Service, CancellationToken.None);

        Assert.Contains("\"digikey\":\"empty (suspect key type)\"", json);
        Assert.Contains("\"mouser\":\"no_key\"", json);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Test_KeyRejection_ReportsRejected(int status)
    {
        using var harness = new ProbeHarness(
            configureDigiKey: true,
            new ProbeClient { VendorId = VendorId.Digikey });
        harness.Sender.Responder = _ => new RawHttpResponse(status, "denied");

        var json = await harness.Commands.TestAsync(harness.Service, CancellationToken.None);

        Assert.Contains("\"digikey\":\"rejected\"", json);
    }

    [Fact]
    public async Task Test_TransientFailure_ReportsError()
    {
        using var harness = new ProbeHarness(
            configureDigiKey: true,
            new ProbeClient { VendorId = VendorId.Digikey });
        harness.Sender.Responder = _ => new RawHttpResponse(500, "boom");

        var json = await harness.Commands.TestAsync(harness.Service, CancellationToken.None);

        Assert.Contains("\"digikey\":\"error\"", json);
    }

    [Fact]
    public async Task Test_Unconfigured_ReportsNoKey_NoHttp()
    {
        using var harness = new ProbeHarness(
            configureDigiKey: false,
            new ProbeClient { VendorId = VendorId.Digikey });

        var json = await harness.Commands.TestAsync(harness.Service, CancellationToken.None);

        Assert.Equal(
            "{\"ok\":true,\"command\":\"key\",\"sub\":\"test\",\"vendors\":{\"digikey\":\"no_key\",\"mouser\":\"no_key\",\"newark\":\"no_key\"}}",
            json);
        Assert.Empty(harness.Sender.Requests);
    }

    [Fact]
    public async Task Test_Probe_NeverReadsCache_WritesThrough()
    {
        using var harness = new ProbeHarness(
            configureDigiKey: true,
            new ProbeClient
            {
                VendorId = VendorId.Digikey,
                ParseBehavior = raw => raw.Contains("fresh") ? [ProbePart()] : [],
            });
        harness.Cache.Write(VendorId.Digikey, CacheKind.Search, "resistor", 1, "stale-body");
        harness.Sender.Responder = _ => new RawHttpResponse(200, "fresh-body");

        var json = await harness.Commands.TestAsync(harness.Service, CancellationToken.None);

        // A cache read would have served "stale-body" ⇒ empty; the fresh fetch proves none happened.
        Assert.Contains("\"digikey\":\"pass\"", json);
        var entry = harness.Cache.Read(VendorId.Digikey, CacheKind.Search, "resistor", 1);
        Assert.NotNull(entry);
        Assert.Equal("fresh-body", entry.Raw);
    }
}
