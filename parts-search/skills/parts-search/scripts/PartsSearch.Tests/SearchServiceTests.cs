using System.Text.Json;
using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

internal static class SearchTestTime
{
    internal static readonly DateTimeOffset FixedNow = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
}

public class SearchServiceTests
{

    // Beyond the canonical three, exactly as a registry-only vendor arrives.
    private static readonly VendorId FourthVendor = (VendorId)4;

    private static void ConfigureVendor(FakeCredentialStore store, params VendorId[] vendors)
    {
        foreach (var vendor in vendors)
        {
            store.Entries[CredentialTargets.TargetFor(vendor, FakeVendorClientSecretField(vendor))] =
                $"{vendor.ToString().ToLowerInvariant()}-key";
        }
    }

    private static string FakeVendorClientSecretField(VendorId vendor) =>
        vendor == VendorId.Digikey ? CredentialTargets.ClientSecretField : CredentialTargets.ApiKeyField;

    private static SearchRequest Search(string query, IReadOnlyList<VendorId>? vendors = null, int qty = 1, bool noCache = false, int limit = 10) =>
        new(query, vendors, qty, limit, OutputFormat.Json, noCache);

    private static AlternativesRequest Alternatives(string query, IReadOnlyList<VendorId>? vendors = null, int qty = 1, int limit = 10, bool noCache = false) =>
        new(query, vendors, qty, limit, OutputFormat.Json, noCache);

    private sealed class QueryAwareClient : IVendorClient
    {
        public required VendorId VendorId { get; init; }
        public required Func<string, IReadOnlyList<PartResult>> ResultsFor { get; init; }
        public Func<string, string?>? NarrowQuery { get; init; }
        public List<string> BuiltQueries { get; } = [];
        public List<RawHttpRequest> BuiltRequests { get; } = [];

        public bool IsConfigured(KeyConfig keyConfig) => true;

        public string? NarrowSearchQuery(string query) => NarrowQuery?.Invoke(query);

        public RawHttpRequest BuildSearchRequest(string query, int limit)
        {
            BuiltQueries.Add(query);
            var request = new RawHttpRequest(
                "POST",
                $"https://api.digikey.com/search?q={Uri.EscapeDataString(query)}&limit={limit}",
                new Dictionary<string, string>(),
                null);
            BuiltRequests.Add(request);
            return request;
        }

        public Task<RawHttpRequest> ApplySecretsAsync(RawHttpRequest request, KeyConfig keyConfig, CancellationToken cancellationToken) =>
            Task.FromResult(request);

        public IReadOnlyList<PartResult> ParseResponse(string rawJson, string query) => ResultsFor(query);

        public RawHttpRequest BuildDetailsRequest(string vendorCode) => throw new NotSupportedException();

        public PartResult ParseDetailsResponse(string rawJson) => throw new NotSupportedException();
    }

    private static PartResult Part(string mpn, string vendorCode, string? currency = null, params PriceBreak[] breaks) => new()
    {
        Mpn = mpn,
        VendorPartNumber = vendorCode,
        Description = "Test description",
        Manufacturer = "Test Mfr",
        Url = $"https://example.com/{mpn}",
        Currency = currency,
        PriceBreaks = breaks,
    };

    [Fact]
    public async Task MixedStatuses_CompleteEnvelope_FixedVendorOrder()
    {
        var digikeyClient = new FakeVendorClient
        {
            VendorId = VendorId.Digikey,
            Results =
            [
                Part("TPIC6B595DWR", "296-1275-1-ND", "CAD", new PriceBreak(1, 1.62m), new PriceBreak(10, 1.38m)),
            ],
        };
        var newarkClient = new FakeVendorClient { VendorId = VendorId.Newark, ParseFailure = new JsonException("bad json") };
        using var harness = new Harness(
            store => ConfigureVendor(store, VendorId.Digikey, VendorId.Newark),
            digikeyClient,
            new FakeVendorClient { VendorId = VendorId.Mouser, ConfiguredOverride = _ => false },
            newarkClient);

        var envelope = await harness.Service.SearchAsync(Search("TPIC6B595DWR", qty: 10));

        Assert.True(envelope.Ok);
        Assert.Equal(CommandKind.Search, envelope.Command);
        Assert.Equal("TPIC6B595DWR", envelope.Query);
        Assert.Equal(10, envelope.Qty);
        Assert.Equal(SearchTestTime.FixedNow, envelope.AsOf);

        Assert.Equal(3, envelope.Vendors.Count);
        var digikey = envelope.Vendors[0];
        Assert.Equal(VendorId.Digikey, digikey.Vendor);
        Assert.Equal(VendorStatus.Ok, digikey.Status);
        Assert.False(digikey.Cached);
        var part = Assert.Single(digikey.Parts);
        Assert.Equal(MatchKind.Exact, part.Match);
        Assert.Equal(1.38m, part.UnitPrice);
        Assert.Equal("CAD", part.Currency);

        var mouser = envelope.Vendors[1];
        Assert.Equal(VendorStatus.NoKey, mouser.Status);
        Assert.Null(mouser.Cached);
        Assert.Empty(mouser.Parts);

        var newark = envelope.Vendors[2];
        Assert.Equal(VendorStatus.Error, newark.Status);
        Assert.Equal("bad json", newark.Error);
        Assert.Null(newark.Cached);
        Assert.Empty(newark.Parts);

        // The send path carries the client's secret stamp, applied exactly once per send;
        // newark's request also went out (its parse failure comes after the send).
        Assert.Equal(2, harness.Sender.Requests.Count);
        var sent = harness.Sender.Requests.Single(r => r.Url.Contains("digikey"));
        Assert.Equal("Bearer fake", sent.Headers["Authorization"]);
        Assert.Single(digikeyClient.StampedRequests);
        Assert.Single(newarkClient.StampedRequests);
    }

    [Fact]
    public async Task AllConfiguredVendorsErroring_StillCompleteEnvelope()
    {
        using var harness = new Harness(
            store => ConfigureVendor(store, VendorId.Digikey, VendorId.Mouser, VendorId.Newark),
            new FakeVendorClient { VendorId = VendorId.Digikey },
            new FakeVendorClient { VendorId = VendorId.Mouser },
            new FakeVendorClient { VendorId = VendorId.Newark });
        harness.Sender.Responder = _ => new RawHttpResponse(500, "boom");

        var envelope = await harness.Service.SearchAsync(Search("x"));

        Assert.True(envelope.Ok);
        Assert.Equal(3, envelope.Vendors.Count);
        Assert.All(envelope.Vendors, outcome =>
        {
            Assert.Equal(VendorStatus.Error, outcome.Status);
            Assert.Equal("http 500 (1 retry)", outcome.Error);
            Assert.Null(outcome.Cached);
            Assert.Empty(outcome.Parts);
        });
    }

    [Fact]
    public async Task ZeroConfiguredKeys_AllVendorsNoKey_NoHttp()
    {
        using var harness = new Harness(
            new FakeVendorClient { VendorId = VendorId.Digikey },
            new FakeVendorClient { VendorId = VendorId.Mouser },
            new FakeVendorClient { VendorId = VendorId.Newark });

        var envelope = await harness.Service.SearchAsync(Search("resistor"));

        Assert.True(envelope.Ok);
        Assert.All(envelope.Vendors, outcome =>
        {
            Assert.Equal(VendorStatus.NoKey, outcome.Status);
            Assert.Null(outcome.Cached);
            Assert.Empty(outcome.Parts);
        });
        Assert.Empty(harness.Sender.Requests);
    }

    [Fact]
    public async Task UnregisteredCanonicalVendor_EnumeratesAsNoKey()
    {
        using var harness = new Harness(
            store => ConfigureVendor(store, VendorId.Digikey),
            new FakeVendorClient { VendorId = VendorId.Digikey, Results = [Part("A1", "D1")] });

        var envelope = await harness.Service.SearchAsync(Search("a1"));

        Assert.Equal(3, envelope.Vendors.Count);
        Assert.Equal(VendorStatus.Ok, envelope.Vendors[0].Status);
        Assert.Equal(VendorStatus.NoKey, envelope.Vendors[1].Status);
        Assert.Equal(VendorStatus.NoKey, envelope.Vendors[2].Status);
        Assert.Single(harness.Sender.Requests);
    }

    [Fact]
    public async Task CacheHit_ReusesRaw_IssuesNoHttp()
    {
        using var harness = new Harness(
            store => ConfigureVendor(store, VendorId.Digikey),
            new FakeVendorClient { VendorId = VendorId.Digikey, Results = [Part("A1", "D1")] });

        var first = await harness.Service.SearchAsync(Search("hitq"));
        var second = await harness.Service.SearchAsync(Search("hitq"));

        Assert.False(first.Vendors[0].Cached);
        var outcome = second.Vendors[0];
        Assert.True(outcome.Cached);
        Assert.Equal(VendorStatus.Ok, outcome.Status);
        Assert.Single(outcome.Parts);
        Assert.Single(harness.Sender.Requests);
    }

    [Fact]
    public async Task NoCache_AlwaysFetches_WritesNothing()
    {
        using var harness = new Harness(
            store => ConfigureVendor(store, VendorId.Digikey),
            new FakeVendorClient { VendorId = VendorId.Digikey, Results = [Part("A1", "D1")] });

        var first = await harness.Service.SearchAsync(Search("ncq", noCache: true));
        var second = await harness.Service.SearchAsync(Search("ncq", noCache: true));

        Assert.False(first.Vendors[0].Cached);
        Assert.False(second.Vendors[0].Cached);
        Assert.Equal(2, harness.Sender.Requests.Count);
        Assert.True(
            !Directory.Exists(harness.CacheRoot)
            || !Directory.EnumerateFiles(harness.CacheRoot).Any());
    }

    // ---- keyword narrowing ----

    // The same rule DigiKeyClient applies: drop the last token while one remains.
    private static string? DropLastToken(string query)
    {
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length >= 2 ? string.Join(' ', tokens[..^1]) : null;
    }

    [Fact]
    public async Task KeywordNarrowing_EmptyFullQuery_RetriesShorterFormsUntilResults()
    {
        var client = new QueryAwareClient
        {
            VendorId = VendorId.Digikey,
            ResultsFor = query => query.ToLowerInvariant() == "gpib" ? [Part("CIB24S", "C1")] : [],
            NarrowQuery = DropLastToken,
        };
        using var harness = new Harness(store => ConfigureVendor(store, VendorId.Digikey), client);

        var envelope = await harness.Service.SearchAsync(Search("GPIB IEEE-488 connector", [VendorId.Digikey]));

        var outcome = envelope.Vendors[0];
        Assert.Equal(VendorStatus.Ok, outcome.Status);
        Assert.Equal("GPIB", outcome.NarrowedTo); // the serving query, not the requested one
        Assert.Equal(
            new[] { "GPIB IEEE-488 connector", "GPIB IEEE-488", "GPIB" },
            client.BuiltQueries.ToArray());
        Assert.Equal(3, harness.Sender.Requests.Count);
        // Match stays classified against the requested query, not the narrowed form.
        Assert.Equal(MatchKind.Keyword, Assert.Single(outcome.Parts).Match);
    }

    [Fact]
    public async Task KeywordNarrowing_AllFormsEmpty_NoResultsWithoutNarrowedTo()
    {
        var client = new QueryAwareClient
        {
            VendorId = VendorId.Digikey,
            ResultsFor = _ => [],
            NarrowQuery = DropLastToken,
        };
        using var harness = new Harness(store => ConfigureVendor(store, VendorId.Digikey), client);

        var envelope = await harness.Service.SearchAsync(Search("a b c", [VendorId.Digikey]));

        var outcome = envelope.Vendors[0];
        Assert.Equal(VendorStatus.NoResults, outcome.Status);
        Assert.Null(outcome.NarrowedTo);
        Assert.Equal(new[] { "a b c", "a b", "a" }, client.BuiltQueries.ToArray());
    }

    [Fact]
    public async Task KeywordNarrowing_FullQueryHits_NoNarrowingNoNarrowedTo()
    {
        var client = new QueryAwareClient
        {
            VendorId = VendorId.Digikey,
            ResultsFor = _ => [Part("A1", "D1")],
            NarrowQuery = DropLastToken,
        };
        using var harness = new Harness(store => ConfigureVendor(store, VendorId.Digikey), client);

        var envelope = await harness.Service.SearchAsync(Search("multi word query", [VendorId.Digikey]));

        var outcome = envelope.Vendors[0];
        Assert.Equal(VendorStatus.Ok, outcome.Status);
        Assert.Null(outcome.NarrowedTo);
        Assert.Single(client.BuiltQueries); // nothing to narrow: the full query matched
    }

    [Fact]
    public async Task KeywordNarrowing_CachedSecondRun_AllLevelsCacheServed_CachedTrueNoHttp()
    {
        var client = new QueryAwareClient
        {
            VendorId = VendorId.Digikey,
            ResultsFor = query => query.ToLowerInvariant() == "gpib" ? [Part("CIB24S", "C1")] : [],
            NarrowQuery = DropLastToken,
        };
        using var harness = new Harness(store => ConfigureVendor(store, VendorId.Digikey), client);

        var first = await harness.Service.SearchAsync(Search("GPIB IEEE-488 connector", [VendorId.Digikey]));
        var second = await harness.Service.SearchAsync(Search("GPIB IEEE-488 connector", [VendorId.Digikey]));

        Assert.False(first.Vendors[0].Cached);
        var outcome = second.Vendors[0];
        Assert.Equal(VendorStatus.Ok, outcome.Status);
        Assert.True(outcome.Cached); // every level, including the empty originals, was cache-served
        Assert.Equal("GPIB", outcome.NarrowedTo);
        Assert.Equal(3, client.BuiltQueries.Count); // 3 first run + 0 refetched second run
        Assert.Equal(3, harness.Sender.Requests.Count);
    }

    // Mouser's by-number endpoint takes no server-side records cap, so the service
    // itself must keep --limit honest; endpoints that cap server-side are unaffected.
    [Fact]
    public async Task Search_CapsParsedPartsToTheRequestedLimit()
    {
        var client = new QueryAwareClient
        {
            VendorId = VendorId.Digikey,
            ResultsFor = _ => [Part("A1", "D1"), Part("A2", "D2"), Part("A3", "D3")],
        };
        using var harness = new Harness(store => ConfigureVendor(store, VendorId.Digikey), client);

        var envelope = await harness.Service.SearchAsync(Search("uncapped endpoint", [VendorId.Digikey], limit: 2));

        var outcome = envelope.Vendors[0];
        Assert.Equal(VendorStatus.Ok, outcome.Status);
        Assert.Equal(2, outcome.Parts.Count);
    }

    // `cached` reports the provenance of the response that produced the parts: when the
    // full-query level refetches empty but a narrowed level is cache-served with parts,
    // the parts are cache data and the flag must say so.
    [Fact]
    public async Task KeywordNarrowing_MixedProvenance_CachedFollowsTheServingResponse()
    {
        var client = new QueryAwareClient
        {
            VendorId = VendorId.Digikey,
            ResultsFor = query => query.ToLowerInvariant() == "gpib" ? [Part("CIB24S", "C1")] : [],
            NarrowQuery = DropLastToken,
        };
        using var harness = new Harness(store => ConfigureVendor(store, VendorId.Digikey), client);
        await harness.Service.SearchAsync(Search("GPIB IEEE-488 connector", [VendorId.Digikey])); // warms every level

        var full = ResponseCache.CacheKey(VendorId.Digikey, CacheKind.Search, "GPIB IEEE-488 connector", 10, ResponseCache.Market);
        File.Delete(Path.Combine(harness.CacheRoot, full + ".json"));

        var envelope = await harness.Service.SearchAsync(Search("GPIB IEEE-488 connector", [VendorId.Digikey]));

        var outcome = envelope.Vendors[0];
        Assert.Equal(VendorStatus.Ok, outcome.Status);
        Assert.Equal("GPIB", outcome.NarrowedTo);
        Assert.True(outcome.Cached); // only the empty full-query level went to the network
        Assert.Equal(4, harness.Sender.Requests.Count); // 3 warming + 1 refetch
    }

    // Check's vocabulary is exact matches for the requested MPN: a multi-token MPN that
    // narrowed must not leak narrowed_to into the check envelope.
    [Fact]
    public async Task Check_MultiTokenMpn_StripsNarrowingMetadata()
    {
        var client = new QueryAwareClient
        {
            VendorId = VendorId.Digikey,
            ResultsFor = query => query.ToLowerInvariant() == "gpib" ? [Part("CIB24S", "C1")] : [],
            NarrowQuery = DropLastToken,
        };
        using var harness = new Harness(store => ConfigureVendor(store, VendorId.Digikey), client);

        var envelope = await harness.Service.CheckAsync(
            new CheckRequest(["GPIB connector"], [VendorId.Digikey], 1, OutputFormat.Json, false));

        var outcome = Assert.Single(Assert.Single(envelope.Lines).Vendors);
        Assert.Equal(VendorStatus.Ok, outcome.Status);
        Assert.Empty(outcome.Parts); // narrowed hits are never exact matches for the requested MPN
        Assert.Null(outcome.NarrowedTo);
    }

    [Fact]
    public async Task DigiKeyPacedBetweenConsecutiveSends_MouserNot()
    {
        using var harness = new Harness(
            store => ConfigureVendor(store, VendorId.Digikey, VendorId.Mouser),
            new FakeVendorClient { VendorId = VendorId.Digikey },
            new FakeVendorClient { VendorId = VendorId.Mouser });

        await harness.Service.SearchAsync(Search("q1", [VendorId.Digikey, VendorId.Mouser]));
        await harness.Service.SearchAsync(Search("q2", [VendorId.Digikey, VendorId.Mouser]));

        Assert.Equal(4, harness.Sender.Requests.Count);
        var delay = Assert.Single(harness.RateDelays);
        Assert.Equal(TimeSpan.FromMilliseconds(600), delay);
    }

    [Fact]
    public async Task FourthClient_RegistersAndParticipates()
    {
        using var harness = new Harness(
            store => ConfigureVendor(store, VendorId.Digikey),
            new FakeVendorClient { VendorId = VendorId.Digikey, Results = [Part("A1", "D1", "CAD")] },
            new FakeVendorClient
            {
                VendorId = FourthVendor,
                ConfiguredOverride = _ => true,
                Results = [Part("FOUR1", "F1", "CAD")],
            });

        var all = await harness.Service.SearchAsync(Search("q"));

        Assert.Equal(4, all.Vendors.Count);
        Assert.Equal(
            new[] { VendorId.Digikey, VendorId.Mouser, VendorId.Newark, FourthVendor },
            all.Vendors.Select(v => v.Vendor).ToArray());
        Assert.Equal(VendorStatus.Ok, all.Vendors[0].Status);
        Assert.Equal(VendorStatus.NoKey, all.Vendors[1].Status);
        Assert.Equal(VendorStatus.NoKey, all.Vendors[2].Status);
        Assert.Equal(VendorStatus.Ok, all.Vendors[3].Status);
        Assert.Single(all.Vendors[3].Parts);

        var narrowed = await harness.Service.SearchAsync(Search("q2", [FourthVendor]));
        var outcome = Assert.Single(narrowed.Vendors);
        Assert.Equal(FourthVendor, outcome.Vendor);
        Assert.Equal(VendorStatus.Ok, outcome.Status);
    }

    [Fact]
    public async Task RequestedVendors_OutcomesAndFetchesInCanonicalOrder()
    {
        using var harness = new Harness(
            store => ConfigureVendor(store, VendorId.Digikey, VendorId.Mouser),
            new FakeVendorClient { VendorId = VendorId.Digikey },
            new FakeVendorClient { VendorId = VendorId.Mouser });

        var envelope = await harness.Service.SearchAsync(Search("q", [VendorId.Mouser, VendorId.Digikey]));

        Assert.Equal(
            new[] { VendorId.Digikey, VendorId.Mouser },
            envelope.Vendors.Select(v => v.Vendor).ToArray());
        Assert.Contains("digikey", harness.Sender.Requests[0].Url);
        Assert.Contains("mouser", harness.Sender.Requests[1].Url);
    }

    [Fact]
    public async Task MatchAndPricingAttached_PerResult()
    {
        using var harness = new Harness(
            store => ConfigureVendor(store, VendorId.Digikey),
            new FakeVendorClient
            {
                VendorId = VendorId.Digikey,
                Results =
                [
                    Part("TPIC6B595DWR", "296-1275-1-ND", "CAD", new PriceBreak(1, 1.62m), new PriceBreak(10, 1.38m)),
                    Part("OTHER123", "R999"),
                    Part("CHEAP1", "R777", "CAD", new PriceBreak(1, 3.85m), new PriceBreak(10, 2.60m)),
                ],
            });

        var envelope = await harness.Service.SearchAsync(Search("tpic6b595dwr", qty: 8));
        var parts = envelope.Vendors[0].Parts;

        Assert.Equal(MatchKind.Exact, parts[0].Match);
        Assert.Equal(1.62m, parts[0].UnitPrice);
        Assert.Equal("CAD", parts[0].Currency);
        Assert.Null(parts[0].CheaperAt);

        Assert.Equal(MatchKind.Keyword, parts[1].Match);
        Assert.Null(parts[1].UnitPrice);
        Assert.Null(parts[1].Currency);
        Assert.Null(parts[1].CheaperAt);
        Assert.Empty(parts[1].PriceBreaks);

        Assert.Equal(3.85m, parts[2].UnitPrice);
        var hint = parts[2].CheaperAt;
        Assert.NotNull(hint);
        Assert.Equal(10, hint.Qty);
        Assert.Equal(2.60m, hint.UnitPrice);
        Assert.Equal(26.00m, hint.Total);
        Assert.Equal(4.80m, hint.Savings);
    }

    // The DigiKey token POST is a DigiKey-host HTTP call under the 600 ms minimum
    // interval: on the acquisition path the search send that follows it must be gated
    // against the token send on the shared limiter window.
    [Fact]
    public async Task DigiKeyTokenAcquisition_PacedBeforeTheSearchSend()
    {
        var store = new FakeCredentialStore();
        store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientIdField)] = "id-value-7Q2M";
        store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientSecretField)] = "secret-value-9X4K";
        var clock = new SearchFakeClock();
        var sender = new FakeRawSender();
        sender.Responder = request => request.Url.Contains("/v1/oauth2/token")
            ? new RawHttpResponse(200, """{"access_token":"paced-token","expires_in":600}""")
            : new RawHttpResponse(200, """{"Products":[{"ManufacturerProductNumber":"TPIC6B595DWR","ProductUrl":"https://example.com/x"}]}""");
        var rateDelays = new List<TimeSpan>();
        var limiter = new RateLimiter(clock, delay: span => { rateDelays.Add(span); return Task.CompletedTask; });
        var http = new HttpSend(sender, delay: _ => Task.CompletedTask);
        var cacheRoot = Path.Combine(Path.GetTempPath(), "parts-search-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new ResponseCache(clock, enabled: true, root: cacheRoot);
            var auth = new DigiKeyAuth(new KeyConfig(store), http, clock, limiter, Path.Combine(cacheRoot, "digikey-token.json"));
            var service = new SearchService([new DigiKeyClient(auth)], new KeyConfig(store), cache, limiter, http, clock);

            var envelope = await service.SearchAsync(Search("q", [VendorId.Digikey]));

            Assert.Equal(2, sender.Requests.Count);
            Assert.Contains("/v1/oauth2/token", sender.Requests[0].Url);
            Assert.Contains("search/keyword", sender.Requests[1].Url);
            var delay = Assert.Single(rateDelays);
            Assert.Equal(TimeSpan.FromMilliseconds(600), delay);
            Assert.Equal(VendorStatus.Ok, envelope.Vendors[0].Status);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
        }
    }

    // ---- alternatives flow ----

    private static PartResult AltPart(string mpn, string vendorCode, string category, int? stock = 100, MatchKind match = MatchKind.Keyword) => new()
    {
        Mpn = mpn,
        VendorPartNumber = vendorCode,
        Description = "IC SR OPEN DRAIN 8BIT 20-SOIC",
        Manufacturer = "Texas Instruments",
        Stock = stock,
        Category = category,
        Url = $"https://example.com/{vendorCode}",
        Match = match,
    };

    private static readonly Func<string, IReadOnlyList<PartResult>> Stage1Results = query => query == "TPIC6B595DWR"
        ? new[]
        {
            AltPart("TPIC6B595DWR", "296-1351-1-ND", "Shift Registers", 36075, MatchKind.Exact),
            AltPart("TPIC6B595DWRG4", "296-1351-2-ND", "Shift Registers", 400),
            AltPart("CD4021BE", "CD4021-ND", "Shift Registers", 900),
            AltPart("MC14021B", "MC14021-ND", "Shift Registers", 50),
        }
        : query.StartsWith("texas instruments")
        ? new[]
        {
            AltPart("STPIC6B595", "STPIC6-ND", "Shift Registers", 700),
            AltPart("TPIC6B595DWRG4", "296-1351-2-ND", "Shift Registers", 777), // dedup: stage 1 owns this code
        }
        : [];

    [Fact]
    public async Task Alternatives_TwoSearchesPerVendor_Stage2QueryDeterministicFromPivot()
    {
        var client = new QueryAwareClient
        {
            VendorId = VendorId.Digikey,
            ResultsFor = Stage1Results,
        };
        using var harness = new Harness(client);

        var envelope = await harness.Service.AlternativesAsync(Alternatives("TPIC6B595DWR", [VendorId.Digikey]));

        Assert.Equal(2, client.BuiltQueries.Count);
        Assert.Equal("TPIC6B595DWR", client.BuiltQueries[0]);
        Assert.Equal("texas instruments ic sr open drain 8bit 20-soic", client.BuiltQueries[1]);
        Assert.True(envelope.Ok);
        Assert.Equal(CommandKind.Alternatives, envelope.Command);
    }

    [Fact]
    public async Task Alternatives_EnvelopeCarriesRelationAndReasonOnEveryPart()
    {
        using var harness = new Harness(new QueryAwareClient { VendorId = VendorId.Digikey, ResultsFor = Stage1Results });

        var envelope = await harness.Service.AlternativesAsync(Alternatives("TPIC6B595DWR", [VendorId.Digikey]));

        var outcome = Assert.Single(envelope.Vendors);
        Assert.Equal(VendorStatus.Ok, outcome.Status);
        Assert.All(outcome.Parts, part =>
        {
            Assert.NotNull(part.Relation);
            Assert.NotNull(part.Reason);
        });
        Assert.Contains(outcome.Parts, part => part.Relation == RelationKind.Exact);
        Assert.Contains(outcome.Parts, part => part.Relation == RelationKind.Variant);
        Assert.Contains(outcome.Parts, part => part.Relation == RelationKind.Alternative);
    }

    [Fact]
    public async Task Alternatives_LimitCapsAlternativesOnly_ExactAndVariantsStay()
    {
        using var harness = new Harness(new QueryAwareClient { VendorId = VendorId.Digikey, ResultsFor = Stage1Results });

        var envelope = await harness.Service.AlternativesAsync(Alternatives("TPIC6B595DWR", [VendorId.Digikey], limit: 1));

        var outcome = Assert.Single(envelope.Vendors);
        Assert.Equal(1, outcome.Parts.Count(part => part.Relation == RelationKind.Alternative));
        Assert.Single(outcome.Parts, part => part.Relation == RelationKind.Exact);
        Assert.Single(outcome.Parts, part => part.Relation == RelationKind.Variant);
    }

    [Fact]
    public async Task Alternatives_NoResultsVendor_GetsNoStage2()
    {
        var client = new QueryAwareClient { VendorId = VendorId.Digikey, ResultsFor = _ => [] };
        using var harness = new Harness(client);

        var envelope = await harness.Service.AlternativesAsync(Alternatives("nothing", [VendorId.Digikey]));

        var outcome = Assert.Single(envelope.Vendors);
        Assert.Equal(VendorStatus.NoResults, outcome.Status);
        Assert.Single(client.BuiltQueries); // stage 1 only
    }

    [Fact]
    public async Task Alternatives_Stage2Error_Isolated_KeepsStage1Results_Uncached()
    {
        var client = new QueryAwareClient
        {
            VendorId = VendorId.Digikey,
            ResultsFor = Stage1Results,
        };
        using var harness = new Harness(client);
        harness.Sender.Responder = request => request.Url.Contains("q=texas%20instruments")
            ? new RawHttpResponse(500, "boom")
            : new RawHttpResponse(200, "{}");

        var envelope = await harness.Service.AlternativesAsync(Alternatives("TPIC6B595DWR", [VendorId.Digikey]));

        var outcome = Assert.Single(envelope.Vendors);
        Assert.Equal(VendorStatus.Ok, outcome.Status);
        Assert.True(outcome.Parts.Count >= 4); // stage-1 parts classified and kept
        Assert.False(outcome.Cached);
    }

    [Fact]
    public async Task Alternatives_Stage1Error_Isolated_NoStage2()
    {
        var client = new QueryAwareClient { VendorId = VendorId.Digikey, ResultsFor = Stage1Results };
        using var harness = new Harness(client);
        harness.Sender.Responder = _ => new RawHttpResponse(500, "boom");

        var envelope = await harness.Service.AlternativesAsync(Alternatives("TPIC6B595DWR", [VendorId.Digikey]));

        var outcome = Assert.Single(envelope.Vendors);
        Assert.Equal(VendorStatus.Error, outcome.Status);
        Assert.Contains("http 500 (1 retry)", outcome.Error);
        Assert.Single(client.BuiltQueries); // no stage-2 build without a pivot
    }

    [Fact]
    public async Task Alternatives_Cached_TrueOnlyWhenBothStagesCacheServed()
    {
        var store = new FakeCredentialStore();
        store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientIdField)] = "id-value-7Q2M";
        store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientSecretField)] = "secret-value-9X4K";
        var cacheRoot = Path.Combine(Path.GetTempPath(), "parts-search-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new SearchFakeClock();
            var sender = new FakeRawSender();
            sender.Responder = request => request.Url.Contains("/v1/oauth2/token")
                ? new RawHttpResponse(200, """{"access_token":"paced-token","expires_in":600}""")
                : new RawHttpResponse(200, """{"Products":[{"ManufacturerProductNumber":"TPIC6B595DWR","ProductUrl":"https://example.com/x"}]}""");
            var limiter = new RateLimiter(clock, delay: _ => Task.CompletedTask);
            var http = new HttpSend(sender, delay: _ => Task.CompletedTask);
            var cache = new ResponseCache(clock, enabled: true, root: cacheRoot);
            var auth = new DigiKeyAuth(new KeyConfig(store), http, clock, limiter, Path.Combine(cacheRoot, "digikey-token.json"));
            var service = new SearchService(
                [new DigiKeyClient(auth)],
                new KeyConfig(store), cache, limiter, http, clock);

            var firstEnvelope = await service.AlternativesAsync(Alternatives("TPIC6B595DWR", [VendorId.Digikey]));
            var secondEnvelope = await service.AlternativesAsync(Alternatives("TPIC6B595DWR", [VendorId.Digikey]));

            Assert.False(Assert.Single(firstEnvelope.Vendors).Cached);
            var outcome = Assert.Single(secondEnvelope.Vendors);
            Assert.True(outcome.Cached);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
        }
    }

    // A cache-served stage-2 no_results is still cache-served — no network,
    // no error — so `cached` must stay true on the second run.
    [Fact]
    public async Task Alternatives_CacheServedEmptyStage2_KeepsCachedTrue()
    {
        var client = new QueryAwareClient
        {
            VendorId = VendorId.Digikey,
            ResultsFor = query => query == "TPIC6B595DWR" ? Stage1Results(query) : [],
        };
        using var harness = new Harness(client);

        var first = await harness.Service.AlternativesAsync(Alternatives("TPIC6B595DWR", [VendorId.Digikey]));
        var second = await harness.Service.AlternativesAsync(Alternatives("TPIC6B595DWR", [VendorId.Digikey]));

        Assert.False(Assert.Single(first.Vendors).Cached);
        var outcome = Assert.Single(second.Vendors);
        Assert.Equal(VendorStatus.Ok, outcome.Status);
        Assert.True(outcome.Cached);
        Assert.Equal(2, client.BuiltQueries.Count); // no refetch on the second run
    }

    // The stages fetch at the default limit, so --limit 1 cannot hide an exact match
    // ranked below the top relevance hit; it caps only the alternatives list.
    [Fact]
    public async Task Alternatives_SmallLimit_CannotFetchPastTheExactMatch()
    {
        var client = new QueryAwareClient
        {
            VendorId = VendorId.Digikey,
            ResultsFor = Stage1Results,
        };
        using var harness = new Harness(client);

        var envelope = await harness.Service.AlternativesAsync(Alternatives("TPIC6B595DWR", [VendorId.Digikey], limit: 1));

        var outcome = Assert.Single(envelope.Vendors);
        Assert.Contains(outcome.Parts, part => part.Relation == RelationKind.Exact);
        Assert.Single(outcome.Parts, part => part.Relation == RelationKind.Variant); // deduped stage-2 twin
        Assert.Single(outcome.Parts, part => part.Relation == RelationKind.Alternative);
        Assert.All(outcome.Parts, part =>
        {
            Assert.NotNull(part.Relation);
            Assert.NotNull(part.Reason);
        });
    }

    // The stages fetch at the default limit, so --limit 1 cannot hide an exact match
    // ranked below the top relevance hit; it caps only the alternatives list.
    [Fact]
    public async Task Alternatives_SmallLimit_ExactMatchRankedBelowTopHit_IsStillFetched()
    {
        var client = new QueryAwareClient
        {
            VendorId = VendorId.Digikey,
            ResultsFor = query => query == "TPIC6B595DWR"
                ? new[]
                {
                    AltPart("CD4021BE", "CD4021-ND", "Shift Registers", 900, MatchKind.Keyword),
                    AltPart("TPIC6B595DWR", "296-1351-1-ND", "Shift Registers", 36075, MatchKind.Exact),
                }
                : [],
        };
        using var harness = new Harness(client);

        var envelope = await harness.Service.AlternativesAsync(Alternatives("TPIC6B595DWR", [VendorId.Digikey], limit: 1));

        var outcome = Assert.Single(envelope.Vendors);
        Assert.Contains(outcome.Parts, part => part.Mpn == "TPIC6B595DWR" && part.Relation == RelationKind.Exact);
        Assert.All(client.BuiltRequests, request => Assert.Contains("limit=10", request.Url));
    }

    // A vendor error status is never parsed as a search response and never cached: a 429
    // body must surface as that vendor's error, not a sticky 24 h no_results.
    [Fact]
    public async Task VendorErrorStatus_ErrorOutcome_NothingCached()
    {
        var client = new QueryAwareClient
        {
            VendorId = VendorId.Digikey,
            ResultsFor = _ => [Part("TPIC6B595DWR", "296-1351-1-ND", "CAD", new PriceBreak(1, 1.62m))],
        };
        using var harness = new Harness(store => ConfigureVendor(store, VendorId.Digikey), client);
        harness.Sender.Responder = _ => new RawHttpResponse(429, """{"error":"quota exceeded"}""");

        var envelope = await harness.Service.SearchAsync(Search("TPIC6B595DWR", [VendorId.Digikey]));

        var outcome = Assert.Single(envelope.Vendors);
        Assert.Equal(VendorStatus.Error, outcome.Status);
        Assert.Contains("http 429", outcome.Error);
        Assert.False(Directory.Exists(harness.CacheRoot) && Directory.EnumerateFiles(harness.CacheRoot).Any());
    }

    // The search fetch's pacing gate sits after token acquisition, so the token POST and
    // the search POST stay a full 600 ms window apart on the refresh path.
    [Fact]
    public async Task DigiKeyFetch_TokenRefreshPacesSearchSendAfterTokenPost()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "parts-search-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FakeCredentialStore();
            store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientIdField)] = "id-value-3K1N";
            store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientSecretField)] = "secret-value-7R9Q";
            var clock = new SearchFakeClock();
            var delays = new List<TimeSpan>();
            var sender = new FakeRawSender
            {
                Responder = request =>
                {
                    // Each fake HTTP round trip takes 300 ms of clock time.
                    clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromMilliseconds(300));
                    return request.Url.Contains("/v1/oauth2/token")
                        ? new RawHttpResponse(200, """{"access_token":"t","expires_in":600}""")
                        : new RawHttpResponse(200, "{}");
                },
            };
            var limiter = new RateLimiter(clock, delay: span =>
            {
                delays.Add(span);
                return Task.CompletedTask;
            });
            var http = new HttpSend(sender, delay: _ => Task.CompletedTask);
            var cache = new ResponseCache(clock, enabled: true, root: cacheRoot);
            var auth = new DigiKeyAuth(new KeyConfig(store), http, clock, limiter, Path.Combine(cacheRoot, "digikey-token.json"));
            var service = new SearchService(
                [new DigiKeyClient(auth)],
                new KeyConfig(store), cache, limiter, http, clock);

            _ = await service.SearchAsync(Search("TPIC6B595DWR", [VendorId.Digikey]));

            // The token gate fired at t0; the token POST advanced the clock to t0+300 ms;
            // the search send's gate must wait out the remaining 300 ms of the window.
            Assert.Equal(TimeSpan.FromMilliseconds(300), Assert.Single(delays));
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
        }
    }

    // Per-stage cached conjuncts and the service-level stage-1-wins dedup.
    [Fact]
    public async Task Alternatives_Cached_FalseWhenExactlyOneStageIsCacheServed()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "parts-search-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FakeCredentialStore();
            store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientIdField)] = "id-value-7Q2M";
            store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientSecretField)] = "secret-value-9X4K";
            var clock = new SearchFakeClock();
            var sender = new FakeRawSender();
            sender.Responder = request => request.Url.Contains("/v1/oauth2/token")
                ? new RawHttpResponse(200, """{"access_token":"t","expires_in":600}""")
                : new RawHttpResponse(200, """{"Products":[{"ManufacturerProductNumber":"TPIC6B595DWR","Manufacturer":{"Name":"Texas Instruments"},"Description":{"ProductDescription":"IC SR OPEN DRAIN 8BIT 20-SOIC"},"ProductUrl":"https://example.com/x"}]}""");
            var limiter = new RateLimiter(clock, delay: _ => Task.CompletedTask);
            var http = new HttpSend(sender, delay: _ => Task.CompletedTask);
            var cache = new ResponseCache(clock, enabled: true, root: cacheRoot);
            var auth = new DigiKeyAuth(new KeyConfig(store), http, clock, limiter, Path.Combine(cacheRoot, "digikey-token.json"));
            var service = new SearchService(
                [new DigiKeyClient(auth)],
                new KeyConfig(store), cache, limiter, http, clock);

            // Warm both stages.
            _ = await service.AlternativesAsync(Alternatives("TPIC6B595DWR", [VendorId.Digikey]));

            // Drop the stage-1 entry: stage 1 networked, stage 2 cache-served.
            var stage1Key = ResponseCache.CacheKey(VendorId.Digikey, CacheKind.Search, "TPIC6B595DWR", 10, ResponseCache.Market);
            File.Delete(Path.Combine(cacheRoot, stage1Key + ".json"));
            var stage1Networked = await service.AlternativesAsync(Alternatives("TPIC6B595DWR", [VendorId.Digikey]));
            Assert.False(Assert.Single(stage1Networked.Vendors).Cached);

            // Drop the stage-2 entry (restoring stage 1): stage 1 cache-served, stage 2 networked.
            var stage2Query = "texas instruments ic sr open drain 8bit 20-soic";
            var stage2Key = ResponseCache.CacheKey(VendorId.Digikey, CacheKind.Search, stage2Query, 10, ResponseCache.Market);
            File.Delete(Path.Combine(cacheRoot, stage2Key + ".json"));
            var stage2Networked = await service.AlternativesAsync(Alternatives("TPIC6B595DWR", [VendorId.Digikey]));
            Assert.False(Assert.Single(stage2Networked.Vendors).Cached);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Alternatives_CrossStageDedup_Stage1Wins_EndToEnd()
    {
        using var harness = new Harness(new QueryAwareClient { VendorId = VendorId.Digikey, ResultsFor = Stage1Results });

        var envelope = await harness.Service.AlternativesAsync(Alternatives("TPIC6B595DWR", [VendorId.Digikey]));

        var outcome = Assert.Single(envelope.Vendors);
        var g4 = outcome.Parts.Single(part => part.Mpn == "TPIC6B595DWRG4");
        Assert.Equal(400, g4.Stock); // the stage-1 occurrence's data, not stage 2's 777
        Assert.Single(outcome.Parts, part => part.Mpn == "TPIC6B595DWRG4");
    }

    [Fact]
    public async Task Alternatives_UnregisteredVendor_ReportsNoKey()
    {
        using var harness = new Harness();

        var envelope = await harness.Service.AlternativesAsync(Alternatives("TPIC6B595DWR", [VendorId.Digikey]));

        var outcome = Assert.Single(envelope.Vendors);
        Assert.Equal(VendorStatus.NoKey, outcome.Status);
        Assert.Empty(harness.Sender.Requests);
    }

    // ---- details flow ----

    private static PartResult ExpandedPart() => new()
    {
        Mpn = "TPIC6B595DWR",
        VendorPartNumber = "296-1351-1-ND",
        Description = "IC SR OPEN DRAIN 8BIT 20-SOIC",
        Manufacturer = "Texas Instruments",
        Stock = 36075,
        Category = "Shift Registers",
        Url = "https://example.com/expanded",
        Match = MatchKind.Keyword,
        PriceBreaks = [new PriceBreak(1, 2.33m), new PriceBreak(10, 1.697m)],
        Parameters =
        [
            new PartParameter("Logic Type", "Shift Register"),
            new PartParameter("Output Type", "Open Drain"),
        ],
        Datasheets = ["https://example.com/ds1.pdf", "https://example.com/ds2.pdf"],
    };

    private static DetailsRequest Details(string query, int qty = 1) =>
        new(query, VendorId.Digikey, qty, OutputFormat.Json, NoCache: false);

    [Fact]
    public async Task Details_ResolvesByExactMatch_ThenExpandsThatPart()
    {
        PartResult[] stage1 =
        [
            AltPart("OTHER1", "OTHER-ND", "Shift Registers", 10),
            AltPart("TPIC6B595DWR", "296-1351-1-ND", "Shift Registers", 36075),
        ];
        var client = new FakeVendorClient
        {
            VendorId = VendorId.Digikey,
            ConfiguredOverride = _ => true,
            Results = stage1,
            DetailsResult = ExpandedPart(),
        };
        using var harness = new Harness(client);

        var envelope = await harness.Service.DetailsAsync(Details("TPIC6B595DWR", qty: 8));

        Assert.Equal("296-1351-1-ND", Assert.Single(client.DetailsRequests)); // the exact match's code
        var outcome = Assert.Single(envelope.Vendors);
        Assert.Equal(VendorStatus.Ok, outcome.Status);
        var part = Assert.Single(outcome.Parts);
        Assert.Equal("TPIC6B595DWR", part.Mpn);
        Assert.Equal(2.33m, part.UnitPrice); // --qty 8 prices the expanded part from its qty-1 break
        Assert.True(envelope.Ok);
        Assert.Equal(CommandKind.Details, envelope.Command);
        Assert.Equal("TPIC6B595DWR", envelope.Query);
    }

    [Fact]
    public async Task Details_NoExactMatch_ResolvesTheTopResult()
    {
        PartResult[] stage1 =
        [
            AltPart("FIRST1", "FIRST-ND", "Shift Registers", 10),
            AltPart("SECOND1", "SECOND-ND", "Shift Registers", 5),
        ];
        var client = new FakeVendorClient
        {
            VendorId = VendorId.Digikey,
            ConfiguredOverride = _ => true,
            Results = stage1,
            DetailsResult = ExpandedPart() with { Mpn = "FIRST1" },
        };
        using var harness = new Harness(client);

        var envelope = await harness.Service.DetailsAsync(Details("power shift"));

        Assert.Equal("FIRST-ND", Assert.Single(client.DetailsRequests));
        var part = Assert.Single(Assert.Single(envelope.Vendors).Parts);
        Assert.Equal("FIRST1", part.Mpn);
        // A keyword-only resolution stays visible on the part's match field.
        Assert.Equal(MatchKind.Keyword, part.Match);
    }

    [Fact]
    public async Task Details_NoResults_EndsOutcomeWithNoExpansion()
    {
        var client = new FakeVendorClient { VendorId = VendorId.Digikey, ConfiguredOverride = _ => true, Results = [] };
        using var harness = new Harness(client);

        var envelope = await harness.Service.DetailsAsync(Details("nothing"));

        var outcome = Assert.Single(envelope.Vendors);
        Assert.Equal(VendorStatus.NoResults, outcome.Status);
        Assert.Empty(client.DetailsRequests); // no expansion without a pivot
    }

    [Fact]
    public async Task Details_EntriesCachedUnderKindDetails_AndSecondRunReadsThem()
    {
        var client = new FakeVendorClient
        {
            VendorId = VendorId.Digikey,
            ConfiguredOverride = _ => true,
            Results = [AltPart("TPIC6B595DWR", "296-1351-1-ND", "Shift Registers", 36075, MatchKind.Exact)],
            DetailsResult = ExpandedPart(),
        };
        var cacheRoot = Path.Combine(Path.GetTempPath(), "parts-search-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new SearchFakeClock();
            var sender = new FakeRawSender();
            var limiter = new RateLimiter(clock, delay: _ => Task.CompletedTask);
            var http = new HttpSend(sender, delay: _ => Task.CompletedTask);
            var store = new FakeCredentialStore();
            var cache = new ResponseCache(clock, enabled: true, root: cacheRoot);
            var service = new SearchService([client], new KeyConfig(store), cache, limiter, http, clock);

            var firstEnvelope = await service.DetailsAsync(Details("TPIC6B595DWR"));
            var secondEnvelope = await service.DetailsAsync(Details("TPIC6B595DWR"));

            // Write-through: the details entry exists under kind:"details" keyed by the code.
            var key = ResponseCache.CacheKey(VendorId.Digikey, CacheKind.Details, "296-1351-1-ND", 1, ResponseCache.Market);
            Assert.True(File.Exists(Path.Combine(cacheRoot, key + ".json")));

            // The second run serves both calls from cache: no HTTP beyond run 1's two calls.
            Assert.Equal(2, sender.Requests.Count);
            var firstOutcome = Assert.Single(firstEnvelope.Vendors);
            Assert.False(firstOutcome.Cached);
            var secondOutcome = Assert.Single(secondEnvelope.Vendors);
            Assert.True(secondOutcome.Cached);

            // The cache-served parse is re-priced and re-classified exactly like a fresh fetch.
            var firstPart = Assert.Single(firstOutcome.Parts);
            var secondPart = Assert.Single(secondOutcome.Parts);
            Assert.Equal(firstPart.UnitPrice, secondPart.UnitPrice);
            Assert.Equal(firstPart.Match, secondPart.Match);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Details_Cached_FalseWhenOnlyOneCallWasCacheServed()
    {
        var client = new FakeVendorClient
        {
            VendorId = VendorId.Digikey,
            ConfiguredOverride = _ => true,
            Results = [AltPart("TPIC6B595DWR", "296-1351-1-ND", "Shift Registers", 36075, MatchKind.Exact)],
            DetailsResult = ExpandedPart(),
        };
        var cacheRoot = Path.Combine(Path.GetTempPath(), "parts-search-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new SearchFakeClock();
            var sender = new FakeRawSender();
            var limiter = new RateLimiter(clock, delay: _ => Task.CompletedTask);
            var http = new HttpSend(sender, delay: _ => Task.CompletedTask);
            var store = new FakeCredentialStore();
            var cache = new ResponseCache(clock, enabled: true, root: cacheRoot);
            var service = new SearchService([client], new KeyConfig(store), cache, limiter, http, clock);

            _ = await service.DetailsAsync(Details("TPIC6B595DWR"));

            // Drop only the details entry: the resolve stays cached, the expansion refetches.
            var detailsKey = ResponseCache.CacheKey(VendorId.Digikey, CacheKind.Details, "296-1351-1-ND", 1, ResponseCache.Market);
            File.Delete(Path.Combine(cacheRoot, detailsKey + ".json"));

            var envelope = await service.DetailsAsync(Details("TPIC6B595DWR"));

            var outcome = Assert.Single(envelope.Vendors);
            Assert.False(outcome.Cached); // the expansion hit the network
            Assert.Equal(3, sender.Requests.Count); // resolve(1) + details(1) + refetched details(1)
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Details_ParametersAndDatasheets_PassThroughInVendorOrder()
    {
        var client = new FakeVendorClient
        {
            VendorId = VendorId.Digikey,
            ConfiguredOverride = _ => true,
            Results = [AltPart("TPIC6B595DWR", "296-1351-1-ND", "Shift Registers", 36075, MatchKind.Exact)],
            DetailsResult = ExpandedPart(),
        };
        using var harness = new Harness(client);

        var envelope = await harness.Service.DetailsAsync(Details("TPIC6B595DWR"));

        var part = Assert.Single(Assert.Single(envelope.Vendors).Parts);
        Assert.Equal(
            new[] { ("Logic Type", "Shift Register"), ("Output Type", "Open Drain") },
            part.Parameters!.Select(parameter => (parameter.Name, parameter.Value)).ToArray());
        Assert.Equal(
            new[] { "https://example.com/ds1.pdf", "https://example.com/ds2.pdf" },
            part.Datasheets!.ToArray());
    }

    [Fact]
    public async Task Details_ExpansionError_Isolated_ToVendorError()
    {
        var client = new FakeVendorClient
        {
            VendorId = VendorId.Digikey,
            ConfiguredOverride = _ => true,
            Results = [AltPart("TPIC6B595DWR", "296-1351-1-ND", "Shift Registers", 36075, MatchKind.Exact)],
        };
        using var harness = new Harness(client);
        harness.Sender.Responder = request => request.Url.Contains("/details")
            ? new RawHttpResponse(500, "boom")
            : new RawHttpResponse(200, "{}");

        var envelope = await harness.Service.DetailsAsync(Details("TPIC6B595DWR"));

        var outcome = Assert.Single(envelope.Vendors);
        Assert.Equal(VendorStatus.Error, outcome.Status);
        Assert.Contains("http 500 (1 retry)", outcome.Error);
        Assert.Empty(outcome.Parts);
    }

    [Fact]
    public async Task Details_UnconfiguredVendor_ReportsNoKey()
    {
        using var harness = new Harness();

        var envelope = await harness.Service.DetailsAsync(Details("TPIC6B595DWR"));

        var outcome = Assert.Single(envelope.Vendors);
        Assert.Equal(VendorStatus.NoKey, outcome.Status);
        Assert.Empty(harness.Sender.Requests);
    }

    [Fact]
    public async Task NoResults_StatusReported_CachedStillSet()
    {
        using var harness = new Harness(
            store => ConfigureVendor(store, VendorId.Digikey),
            new FakeVendorClient { VendorId = VendorId.Digikey });

        var envelope = await harness.Service.SearchAsync(Search("nothing"));

        var outcome = envelope.Vendors[0];
        Assert.Equal(VendorStatus.NoResults, outcome.Status);
        Assert.False(outcome.Cached);
        Assert.Empty(outcome.Parts);
    }
}

internal sealed class SearchFakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = SearchTestTime.FixedNow;
}

internal sealed class FakeCredentialStore : ICredentialStore
{
    public Dictionary<string, string> Entries { get; } = [];

    public string? Read(string target) => Entries.TryGetValue(target, out var value) ? value : null;

    public void Write(string target, string secret) => Entries[target] = secret;

    public bool Delete(string target) => Entries.Remove(target);
}

internal sealed class FakeRawSender : IRawHttpSender
{
    public List<RawHttpRequest> Requests { get; } = [];

    public Func<RawHttpRequest, RawHttpResponse>? Responder { get; set; }

    public Task<RawHttpResponse> SendAsync(RawHttpRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Responder?.Invoke(request) ?? new RawHttpResponse(200, "{}"));
    }
}

internal sealed class FakeVendorClient : IVendorClient
{
    public required VendorId VendorId { get; init; }
    public IReadOnlyList<PartResult> Results { get; init; } = [];
    public Exception? ParseFailure { get; init; }
    public Func<KeyConfig, bool>? ConfiguredOverride { get; init; }
    public List<RawHttpRequest> StampedRequests { get; } = [];
    public List<string> BuiltQueries { get; } = [];
    public List<string> DetailsRequests { get; } = [];
    public PartResult? DetailsResult { get; init; }

    public bool IsConfigured(KeyConfig keyConfig) =>
        ConfiguredOverride?.Invoke(keyConfig)
        ?? keyConfig.GetSecret(VendorId, SecretFieldFor(VendorId)) is not null;

    // HttpSend only relays official vendor hosts, so the fake builds on them.
    public RawHttpRequest BuildSearchRequest(string query, int limit)
    {
        BuiltQueries.Add(query);
        return new RawHttpRequest(
            "POST",
            $"https://{Host(VendorId)}/search",
            new Dictionary<string, string>(),
            $"{{\"query\":\"{query}\",\"limit\":{limit}}}");
    }

    public Task<RawHttpRequest> ApplySecretsAsync(RawHttpRequest request, KeyConfig keyConfig, CancellationToken cancellationToken)
    {
        var stamped = request with
        {
            Headers = new Dictionary<string, string>(request.Headers) { ["Authorization"] = "Bearer fake" },
        };
        StampedRequests.Add(stamped);
        return Task.FromResult(stamped);
    }

    public IReadOnlyList<PartResult> ParseResponse(string rawJson, string query)
    {
        if (ParseFailure is not null) throw ParseFailure;
        return Results;
    }

    public RawHttpRequest BuildDetailsRequest(string vendorCode)
    {
        DetailsRequests.Add(vendorCode);
        return new RawHttpRequest(
            "GET",
            $"https://{Host(VendorId)}/details?code={Uri.EscapeDataString(vendorCode)}",
            new Dictionary<string, string>(),
            null);
    }

    public PartResult ParseDetailsResponse(string rawJson) =>
        DetailsResult ?? throw new NotSupportedException("no scripted details result");

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

internal sealed class Harness : IDisposable
{
    public SearchFakeClock Clock { get; } = new SearchFakeClock();
    public FakeRawSender Sender { get; } = new();
    public FakeCredentialStore Store { get; } = new();
    public List<TimeSpan> RateDelays { get; } = [];
    public string CacheRoot { get; } =
        Path.Combine(Path.GetTempPath(), "parts-search-tests", Guid.NewGuid().ToString("N"));
    public SearchService Service { get; }

    public Harness(params IVendorClient[] clients)
        : this(_ => { }, clients)
    {
    }

    // KeyConfig resolves once at construction, so the store must be populated first.
    public Harness(Action<FakeCredentialStore> configureStore, params IVendorClient[] clients)
    {
        configureStore(Store);
        var limiter = new RateLimiter(Clock, delay: span =>
        {
            RateDelays.Add(span);
            return Task.CompletedTask;
        });
        var http = new HttpSend(Sender, delay: _ => Task.CompletedTask);
        var cache = new ResponseCache(Clock, enabled: true, root: CacheRoot);
        Service = new SearchService(clients, new KeyConfig(Store), cache, limiter, http, Clock);
    }

    public void Dispose()
    {
        if (Directory.Exists(CacheRoot))
            Directory.Delete(CacheRoot, recursive: true);
    }
}
