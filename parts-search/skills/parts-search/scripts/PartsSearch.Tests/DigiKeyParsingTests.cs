using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

public class DigiKeyParsingTests
{
    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", fileName);

    private static IReadOnlyList<PartResult> ParseSearchFixture() =>
        new DigiKeyClient(DigiKeyAuthTestDouble.Auth())
            .ParseResponse(File.ReadAllText(FixturePath("digikey-search.json")), "TPIC6B595DWR");

    [Fact]
    public void SearchFixture_ParsesProductFieldMapping()
    {
        var parts = ParseSearchFixture();

        Assert.Equal(2, parts.Count);
        var part = parts[0];
        Assert.Equal("TPIC6B595DWR", part.Mpn);
        Assert.Equal("296-1351-1-ND", part.VendorPartNumber);
        Assert.Equal("IC SR OPEN DRAIN 8BIT 20-SOIC", part.Description);
        Assert.Equal("Texas Instruments", part.Manufacturer);
        Assert.Equal(36075, part.Stock);
        Assert.Equal(2.33m, part.UnitPrice);
        Assert.Equal("CAD", part.Currency);
        Assert.Equal(LifecycleStage.Active, part.Lifecycle);
        Assert.Equal("Cut Tape (CT)", part.Packaging);
        Assert.Equal("Integrated Circuits (ICs)", part.Category);
        Assert.Equal("https://www.digikey.ca/en/products/detail/texas-instruments/TPIC6B595DWR/276994", part.Url);
    }

    [Fact]
    public void SearchFixture_PriceBreaks_ComeFromTheSmallQuantityOrderingVariation()
    {
        var parts = ParseSearchFixture();

        var breaks = parts[0].PriceBreaks;
        Assert.NotEmpty(breaks);
        Assert.Equal(1, breaks[0].Qty);
        Assert.Equal(2.33m, breaks[0].Price);
        Assert.Equal(10, breaks[1].Qty);
        Assert.Equal(1.697m, breaks[1].Price);
        Assert.Equal(2.33m, SearchService.UnitPriceAt(breaks, 8));
    }

    [Fact]
    public void SearchFixture_ParsesBothProducts()
    {
        var parts = ParseSearchFixture();

        Assert.Equal("TPIC6B595DWR", parts[0].Mpn);
        Assert.Equal("TPIC6B595DWRG4", parts[1].Mpn);
        Assert.All(parts, part => Assert.Null(part.Parameters));
        Assert.All(parts, part => Assert.Null(part.Datasheets));
    }

    [Fact]
    public void ParseResponse_MalformedOrEmptyBody_YieldsNoParts()
    {
        var client = new DigiKeyClient(DigiKeyAuthTestDouble.Auth());

        Assert.Empty(client.ParseResponse("{}", "TPIC6B595DWR"));
        Assert.Empty(client.ParseResponse("""{"Products":[]}""", "TPIC6B595DWR"));
    }

    [Theory]
    [InlineData("Not Recommended For New Designs", LifecycleStage.Nrnd)]
    [InlineData("NRND", LifecycleStage.Nrnd)]
    [InlineData("End Of Life", LifecycleStage.Eol)]
    [InlineData("EOL", LifecycleStage.Eol)]
    [InlineData("Last Time Buy", LifecycleStage.Eol)]
    [InlineData("Discontinued at Digi-Key", LifecycleStage.Eol)]
    [InlineData("Obsolete", LifecycleStage.Obsolete)]
    [InlineData("Active", LifecycleStage.Active)]
    [InlineData("ACT", LifecycleStage.Unknown)]
    [InlineData(null, LifecycleStage.Unknown)]
    [InlineData("Some Unmapped Text", LifecycleStage.Unknown)]
    public void LifecycleMapper_CaseInsensitiveSubstringTable_FirstHitWins(string? status, LifecycleStage expected) =>
        Assert.Equal(expected, LifecycleMapper.Map(status));

    [Fact]
    public void LifecycleMapper_NrndTextContainingActive_SubstringOrderDecides() =>
        Assert.Equal(LifecycleStage.Nrnd, LifecycleMapper.Map("Active, Not Recommended"));
}

// The client's request-build/parse surface is pure; only ApplySecretsAsync needs a token,
// which this double sources from a DigiKeyAuth wired to a scripted sender.
internal static class DigiKeyAuthTestDouble
{
    public static DigiKeyAuth Auth()
    {
        var store = new FakeDigiKeyCredentialStore();
        var sender = new FakeDigiKeyTokenSender();
        return new DigiKeyAuth(
            new KeyConfig(store),
            new HttpSend(sender, delay: _ => Task.CompletedTask),
            new FixedDigiKeyClock(),
            new RateLimiter(new FixedDigiKeyClock(), delay: _ => Task.CompletedTask),
            Path.Combine(Path.GetTempPath(), "parts-search-tests", "digikey-client-double", "digikey-token.json"));
    }

    private sealed class FakeDigiKeyCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> _entries = new()
        {
            [CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientIdField)] = "id-value-7Q2M",
            [CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientSecretField)] = "secret-value-9X4K",
        };

        public string? Read(string target) => _entries.TryGetValue(target, out var value) ? value : null;

        public void Write(string target, string secret) => _entries[target] = secret;

        public bool Delete(string target) => _entries.Remove(target);
    }

    private sealed class FakeDigiKeyTokenSender : IRawHttpSender
    {
        public Task<RawHttpResponse> SendAsync(RawHttpRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new RawHttpResponse(200, """{"access_token":"test-token-value-9f3a","expires_in":600}"""));
    }

    private sealed class FixedDigiKeyClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    }
}
