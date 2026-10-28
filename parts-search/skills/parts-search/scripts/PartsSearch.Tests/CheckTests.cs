using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

public class CheckTests
{
    private static FakeVendorClient DigiKeyVendor() => new()
    {
        VendorId = VendorId.Digikey,
        Results =
        [
            new PartResult
            {
                Mpn = "AAA111",
                VendorPartNumber = "D-A1",
                Description = "d",
                Manufacturer = "Texas Instruments",
                Url = "https://example.com/D-A1",
            },
        ],
    };

    private static FakeVendorClient MouserVendor() => new()
    {
        VendorId = VendorId.Mouser,
        Results =
        [
            new PartResult
            {
                Mpn = "BBB222",
                VendorPartNumber = "M-B1",
                Description = "d",
                Manufacturer = "Texas Instruments",
                Url = "https://example.com/M-B1",
            },
            new PartResult
            {
                Mpn = "BBB333",
                VendorPartNumber = "M-B2",
                Description = "d",
                Manufacturer = "Texas Instruments",
                Url = "https://example.com/M-B2",
            },
        ],
    };

    private static CheckRequest Check(params string[] mpns) =>
        new(mpns, null, Qty: 10, OutputFormat.Json, NoCache: false);

    private static void ConfigureVendors(FakeCredentialStore store)
    {
        store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientIdField)] = "id";
        store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientSecretField)] = "secret";
        store.Entries[CredentialTargets.TargetFor(VendorId.Mouser, CredentialTargets.ApiKeyField)] = "mk";
    }

    [Fact]
    public async Task Check_LinesInFlagOrder_ExactOnlyFiltering_PerVendor()
    {
        using var harness = new Harness(store => ConfigureVendors(store), DigiKeyVendor(), MouserVendor());

        var envelope = await harness.Service.CheckAsync(Check("AAA111", "BBB222", "CCC333"));

        Assert.True(envelope.Ok);
        Assert.Equal(CommandKind.Check, envelope.Command);
        Assert.Equal(10, envelope.Qty);
        Assert.Equal(3, envelope.Lines.Count);

        var line0 = envelope.Lines[0];
        Assert.Equal("AAA111", line0.Mpn);
        var digikey0 = line0.Vendors.Single(v => v.Vendor == VendorId.Digikey);
        Assert.Equal(VendorStatus.Ok, digikey0.Status);
        var exactPart = Assert.Single(digikey0.Parts);
        Assert.Equal(MatchKind.Exact, exactPart.Match);
        Assert.Equal("AAA111", exactPart.Mpn);
        var mouser0 = line0.Vendors.Single(v => v.Vendor == VendorId.Mouser);
        Assert.Equal(VendorStatus.Ok, mouser0.Status); // searched, no exact match
        Assert.Empty(mouser0.Parts);

        var line1 = envelope.Lines[1];
        Assert.Equal("BBB222", line1.Mpn);
        var digikey1 = line1.Vendors.Single(v => v.Vendor == VendorId.Digikey);
        Assert.Equal(VendorStatus.Ok, digikey1.Status); // searched, no exact match
        Assert.Empty(digikey1.Parts);
        var mouser1 = line1.Vendors.Single(v => v.Vendor == VendorId.Mouser);
        Assert.Equal(VendorStatus.Ok, mouser1.Status);
        Assert.Single(mouser1.Parts);

        var line2 = envelope.Lines[2];
        Assert.Equal(VendorStatus.Ok, line2.Vendors.Single(v => v.Vendor == VendorId.Digikey).Status);
        Assert.Empty(line2.Vendors.Single(v => v.Vendor == VendorId.Digikey).Parts);
        Assert.Equal(VendorStatus.Ok, line2.Vendors.Single(v => v.Vendor == VendorId.Mouser).Status);
        Assert.Empty(line2.Vendors.Single(v => v.Vendor == VendorId.Mouser).Parts);
        Assert.Equal(VendorStatus.NoKey, line2.Vendors.Single(v => v.Vendor == VendorId.Newark).Status);
    }

    [Fact]
    public async Task Check_NoResults_CollapsedToOkWithEmptyParts()
    {
        var emptyVendor = new FakeVendorClient { VendorId = VendorId.Digikey, Results = [], ConfiguredOverride = _ => true };
        using var harness = new Harness(emptyVendor);

        var envelope = await harness.Service.CheckAsync(Check("NOTHING"));

        var outcome = Assert.Single(envelope.Lines).Vendors.Single(v => v.Vendor == VendorId.Digikey);
        Assert.Equal(VendorStatus.Ok, outcome.Status); // no_results collapsed
        Assert.Empty(outcome.Parts);
        Assert.Equal(VendorStatus.NoKey, envelope.Lines[0].Vendors.Single(v => v.Vendor == VendorId.Newark).Status);
    }

    [Fact]
    public async Task Check_PerLineDegradation_OneVendorErroring_LeavesOthersIntact()
    {
        using var harness = new Harness(store => ConfigureVendors(store), DigiKeyVendor(), MouserVendor());
        harness.Sender.Responder = request => request.Url.Contains("digikey")
            ? new RawHttpResponse(500, "boom")
            : new RawHttpResponse(200, "{}");

        var envelope = await harness.Service.CheckAsync(Check("AAA111", "BBB222"));

        foreach (var line in envelope.Lines)
        {
            Assert.Equal(VendorStatus.Error, line.Vendors.Single(v => v.Vendor == VendorId.Digikey).Status);
            Assert.Equal(VendorStatus.Ok, line.Vendors.Single(v => v.Vendor == VendorId.Mouser).Status);
        }
        var bbbLine = envelope.Lines.Single(line => line.Mpn == "BBB222");
        Assert.Single(bbbLine.Vendors.Single(v => v.Vendor == VendorId.Mouser).Parts);
        Assert.True(envelope.Ok); // exit 0 whatever the per-line outcomes
    }

    [Fact]
    public async Task Check_DigiKeyPacing_AppliesAcrossLines()
    {
        using var harness = new Harness(store => ConfigureVendors(store), DigiKeyVendor());

        await harness.Service.CheckAsync(Check("AAA111", "AAA112"));

        // Two DigiKey sends across the two lines: one 600 ms wait between them.
        var delay = Assert.Single(harness.RateDelays);
        Assert.Equal(TimeSpan.FromMilliseconds(600), delay);
    }

    [Fact]
    public async Task Check_Cached_SecondRunServesEveryLineFromCache()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "parts-search-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new SearchFakeClock();
            var sender = new FakeRawSender();
            var store = new FakeCredentialStore();
            ConfigureVendors(store);
            var limiter = new RateLimiter(clock, delay: _ => Task.CompletedTask);
            var http = new HttpSend(sender, delay: _ => Task.CompletedTask);
            var cache = new ResponseCache(clock, enabled: true, root: cacheRoot);
            var service = new SearchService([DigiKeyVendor()], new KeyConfig(store), cache, limiter, http, clock);

            var first = await service.CheckAsync(Check("AAA111"));
            var second = await service.CheckAsync(Check("AAA111"));

            Assert.False(Assert.Single(first.Lines).Vendors[0].Cached);
            Assert.True(Assert.Single(second.Lines).Vendors[0].Cached);
            Assert.Equal(1, sender.Requests.Count); // no HTTP on the second run
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Check_UnconfiguredVendor_ReportsNoKey()
    {
        using var harness = new Harness();

        var envelope = await harness.Service.CheckAsync(Check("AAA111"));

        Assert.All(Assert.Single(envelope.Lines).Vendors, outcome => Assert.Equal(VendorStatus.NoKey, outcome.Status));
        Assert.Empty(harness.Sender.Requests);
    }
}
