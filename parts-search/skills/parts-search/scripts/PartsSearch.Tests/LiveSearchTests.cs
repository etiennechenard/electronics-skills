using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

// Opt-in live smoke against the real vendor APIs: runs only with
// PARTS_SEARCH_LIVE=1 and the vendor's key configured; every leg otherwise skips with
// a visible reason so the default suite never makes live vendor calls.
[Trait("Category", "Live")]
public class LiveSearchTests
{
    private static bool LiveEnabled => Environment.GetEnvironmentVariable("PARTS_SEARCH_LIVE") == "1";

    private static bool VendorConfigured(VendorId vendor)
    {
        var keyConfig = new KeyConfig(new Win32CredentialStore());
        return vendor switch
        {
            VendorId.Digikey => keyConfig.GetSecret(VendorId.Digikey, CredentialTargets.ClientIdField) is not null
                && keyConfig.GetSecret(VendorId.Digikey, CredentialTargets.ClientSecretField) is not null,
            _ => keyConfig.GetSecret(vendor, CredentialTargets.ApiKeyField) is not null,
        };
    }

    // The same composition the CLI uses: all three registered clients, real Credential
    // Manager reads, real HTTP with the retry/pacing layer, and the production cache
    // (the DigiKey token persists there across legs).
    private static SearchService LiveService()
    {
        var store = new Win32CredentialStore();
        var keyConfig = new KeyConfig(store);
        var clock = new SystemClock();
        var cache = new ResponseCache(clock);
        var rateLimiter = new RateLimiter(clock);
        var http = new HttpSend(new HttpClientTransport());
        var auth = new DigiKeyAuth(keyConfig, http, clock, rateLimiter);
        return new SearchService(
            [
                new DigiKeyClient(auth),
                new MouserClient(),
                new Element14Client(),
            ],
            keyConfig, cache, rateLimiter, http, clock);
    }

    private static void AssertNormalizedPart(PartResult part)
    {
        Assert.False(string.IsNullOrWhiteSpace(part.Mpn));
        Assert.False(string.IsNullOrWhiteSpace(part.Description));
        Assert.False(string.IsNullOrWhiteSpace(part.Manufacturer));
        Assert.False(string.IsNullOrWhiteSpace(part.Url));
        Assert.True(part.Lifecycle is LifecycleStage.Active or LifecycleStage.Nrnd
            or LifecycleStage.Eol or LifecycleStage.Obsolete or LifecycleStage.Unknown);
    }

    [SkippableFact]
    public async Task Live_DigiKeySearch_TPIC6B595DWR()
    {
        Skip.If(!LiveEnabled, "PARTS_SEARCH_LIVE is not 1 — live smoke is opt-in");
        Skip.If(!VendorConfigured(VendorId.Digikey), "no DigiKey credentials configured");
        var service = LiveService();

        var envelope = await service.SearchAsync(
            new SearchRequest("TPIC6B595DWR", [VendorId.Digikey], Qty: 1, Limit: 10, Format: OutputFormat.Json, NoCache: true));

        var outcome = Assert.Single(envelope.Vendors);
        Assert.Equal(VendorStatus.Ok, outcome.Status);
        Assert.NotEmpty(outcome.Parts);
        Assert.All(outcome.Parts, AssertNormalizedPart);
        Assert.All(outcome.Parts, part => Assert.Equal("CAD", part.Currency));
        var exact = Assert.Single(outcome.Parts, part => part.Match == MatchKind.Exact);
        Assert.NotEqual(LifecycleStage.Unknown, exact.Lifecycle); // the status string must map to a real stage
    }

    [SkippableFact]
    public async Task Live_MouserSearch_TPIC6B595DWR()
    {
        Skip.If(!LiveEnabled, "PARTS_SEARCH_LIVE is not 1 — live smoke is opt-in");
        Skip.If(!VendorConfigured(VendorId.Mouser), "no Mouser API key configured");
        var service = LiveService();

        var envelope = await service.SearchAsync(
            new SearchRequest("TPIC6B595DWR", [VendorId.Mouser], Qty: 1, Limit: 10, Format: OutputFormat.Json, NoCache: true));

        var outcome = Assert.Single(envelope.Vendors);
        Assert.Equal(VendorStatus.Ok, outcome.Status);
        Assert.NotEmpty(outcome.Parts);
        Assert.All(outcome.Parts, AssertNormalizedPart);
        Assert.All(outcome.Parts, part => Assert.Equal("CAD", part.Currency));
    }

    [SkippableFact]
    public async Task Live_Element14Search_OrderCode_WithMpNFallback()
    {
        Skip.If(!LiveEnabled, "PARTS_SEARCH_LIVE is not 1 — live smoke is opt-in");
        Skip.If(!VendorConfigured(VendorId.Newark), "no element14 API key configured");
        var service = LiveService();

        var envelope = await service.SearchAsync(
            new SearchRequest("83F8917", [VendorId.Newark], Qty: 1, Limit: 10, Format: OutputFormat.Json, NoCache: true));

        var outcome = Assert.Single(envelope.Vendors);
        Assert.Equal(VendorStatus.Ok, outcome.Status);
        if (outcome.Parts.Count > 0)
        {
            Assert.All(outcome.Parts, part =>
            {
                Assert.False(string.IsNullOrWhiteSpace(part.Mpn));
                Assert.False(string.IsNullOrWhiteSpace(part.Description));
                Assert.False(string.IsNullOrWhiteSpace(part.Manufacturer));
                Assert.Null(part.Url);
                Assert.Equal("CAD", part.Currency);
            });
            return;
        }

        var fallback = await service.SearchAsync(
            new SearchRequest("1402H", [VendorId.Newark], Qty: 1, Limit: 10, Format: OutputFormat.Json, NoCache: true));
        var fallbackOutcome = Assert.Single(fallback.Vendors);
        Assert.Equal(VendorStatus.Ok, fallbackOutcome.Status);
        Assert.NotEmpty(fallbackOutcome.Parts);
        Assert.All(fallbackOutcome.Parts, part =>
        {
            Assert.False(string.IsNullOrWhiteSpace(part.Mpn));
            Assert.Null(part.Url);
            Assert.Equal("CAD", part.Currency);
        });
    }

    [SkippableFact]
    public async Task Live_DigiKeySearch_DistributorCode_HM1705ND()
    {
        Skip.If(!LiveEnabled, "PARTS_SEARCH_LIVE is not 1 — live smoke is opt-in");
        Skip.If(!VendorConfigured(VendorId.Digikey), "no DigiKey credentials configured");
        var service = LiveService();

        var envelope = await service.SearchAsync(
            new SearchRequest("HM1705-ND", [VendorId.Digikey], Qty: 1, Limit: 10, Format: OutputFormat.Json, NoCache: true));

        var outcome = Assert.Single(envelope.Vendors);
        Assert.Equal(VendorStatus.Ok, outcome.Status);
        Assert.NotEmpty(outcome.Parts);
        Assert.All(outcome.Parts, AssertNormalizedPart);
    }

    [SkippableFact]
    public async Task Live_DigiKeyAlternatives_RelationInvariants()
    {
        Skip.If(!LiveEnabled, "PARTS_SEARCH_LIVE is not 1 — live smoke is opt-in");
        Skip.If(!VendorConfigured(VendorId.Digikey), "no DigiKey credentials configured");
        var service = LiveService();

        var envelope = await service.AlternativesAsync(
            new AlternativesRequest("TPIC6B595DWR", [VendorId.Digikey], Qty: 1, Limit: 5, Format: OutputFormat.Json, NoCache: true));

        var outcome = Assert.Single(envelope.Vendors);
        Assert.Equal(VendorStatus.Ok, outcome.Status);
        Assert.Contains(outcome.Parts, part => part.Relation == RelationKind.Exact);
        Assert.All(outcome.Parts, part =>
        {
            Assert.NotNull(part.Relation);
            Assert.NotNull(part.Reason);
            Assert.Equal("CAD", part.Currency);
        });
    }

    [SkippableFact]
    public async Task Live_DigiKeyDetails_ParametersAndDatasheets()
    {
        Skip.If(!LiveEnabled, "PARTS_SEARCH_LIVE is not 1 — live smoke is opt-in");
        Skip.If(!VendorConfigured(VendorId.Digikey), "no DigiKey credentials configured");
        var service = LiveService();

        var envelope = await service.DetailsAsync(
            new DetailsRequest("TPIC6B595DWR", VendorId.Digikey, Qty: 1, Format: OutputFormat.Json, NoCache: true));

        var outcome = Assert.Single(envelope.Vendors);
        Assert.Equal(VendorStatus.Ok, outcome.Status);
        var part = Assert.Single(outcome.Parts);
        Assert.NotEmpty(part.Parameters!);
        Assert.NotEmpty(part.Datasheets!);
    }

    [SkippableFact]
    public async Task Live_Check_TwoMpns_AcrossConfiguredVendors()
    {
        Skip.If(!LiveEnabled, "PARTS_SEARCH_LIVE is not 1 — live smoke is opt-in");
        Skip.If(!VendorConfigured(VendorId.Digikey), "no DigiKey credentials configured");
        var service = LiveService();

        var envelope = await service.CheckAsync(
            new CheckRequest(["TPIC6B595DWR", "HM1705-ND"], null, Qty: 1, Format: OutputFormat.Json, NoCache: true));

        Assert.Equal(2, envelope.Lines.Count);
        Assert.Equal("TPIC6B595DWR", envelope.Lines[0].Mpn);
        Assert.Equal("HM1705-ND", envelope.Lines[1].Mpn);
        foreach (var line in envelope.Lines)
        {
            Assert.All(line.Vendors, outcome =>
                Assert.True(outcome.Status is VendorStatus.Ok or VendorStatus.NoKey or VendorStatus.Error));
            var digikey = line.Vendors.Single(v => v.Vendor == VendorId.Digikey);
            if (digikey.Status == VendorStatus.Ok)
                Assert.All(digikey.Parts, part => Assert.Equal(MatchKind.Exact, part.Match)); // exact-only lines
        }
        var tpicLine = envelope.Lines[0];
        Assert.Equal(VendorStatus.Ok, tpicLine.Vendors.Single(v => v.Vendor == VendorId.Digikey).Status);
    }

    [SkippableFact]
    public async Task Live_KeyTest_ConfiguredVendorsPass()
    {
        Skip.If(!LiveEnabled, "PARTS_SEARCH_LIVE is not 1 — live smoke is opt-in");
        var service = LiveService();

        var statuses = await service.ProbeKeysAsync(CancellationToken.None);

        foreach (var (vendor, status) in statuses)
        {
            if (!VendorConfigured(vendor))
                Assert.Equal("no_key", status);
            else
                Assert.True(status is "pass" or "empty (suspect key type)", $"{vendor} key test: {status}");
        }
        Assert.Contains(VendorId.Digikey, statuses.Keys);
        Assert.Contains(VendorId.Mouser, statuses.Keys);
        Assert.Contains(VendorId.Newark, statuses.Keys);
    }
}
