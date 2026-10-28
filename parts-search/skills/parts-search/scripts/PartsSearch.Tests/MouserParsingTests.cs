using System.Text.Json;
using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

public class MouserParsingTests
{
    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", fileName);

    private static MouserClient NewClient() => new();

    private static IReadOnlyList<PartResult> ParseSearchFixture() =>
        NewClient().ParseResponse(File.ReadAllText(FixturePath("mouser-search.json")), "TPIC6B595DWR");

    [Fact]
    public void SearchFixture_ParsesProductFieldMapping()
    {
        var parts = ParseSearchFixture();

        Assert.Equal(2, parts.Count);
        var part = parts[0];
        Assert.Equal("TPIC6B595DWR", part.Mpn);
        Assert.Equal("595-TPIC6B595DWR", part.VendorPartNumber);
        Assert.StartsWith("Counter Shift Registers Power 8-Bit Shift", part.Description);
        Assert.Equal("Texas Instruments", part.Manufacturer);
        Assert.Equal(17042, part.Stock);
        Assert.Equal(LifecycleStage.Unknown, part.Lifecycle);
        // First ProductAttributes "Packaging" entry — the default ordering form.
        Assert.Equal("Reel", part.Packaging);
        Assert.Equal("Counter Shift Registers", part.Category);
        Assert.Equal(
            "https://www.mouser.ca/en/ProductDetail/Texas-Instruments/TPIC6B595DWR?qs=w32V8uFkMxk11fsXFu3cTA%3D%3D",
            part.Url);
    }

    [Fact]
    public void SearchFixture_PriceBreaks_IncludingCurrency()
    {
        var parts = ParseSearchFixture();

        var breaks = parts[0].PriceBreaks;
        Assert.Equal(1, breaks[0].Qty);
        Assert.Equal(2.12m, breaks[0].Price);
        Assert.Equal(10, breaks[1].Qty);
        Assert.Equal(1.56m, breaks[1].Price);
        Assert.Equal("CAD", parts[0].Currency);
        // Unit price derived comes from the qty-1 break.
        Assert.Equal(2.12m, SearchService.UnitPriceAt(breaks, 8));
    }

    [Fact]
    public void SearchParse_AbsentCategory_YieldsNull()
    {
        const string raw = """
            {"Errors":[],"SearchResults":{"NumberOfResult":1,"Parts":[{"Availability":"100 In Stock","ManufacturerPartNumber":"TPIC6B595DWR","MouserPartNumber":"595-TPIC6B595DWR","Description":"Counter Shift Registers"}]}}
            """;

        var part = Assert.Single(NewClient().ParseResponse(raw, "TPIC6B595DWR"));

        Assert.Null(part.Category);
    }

    [Theory]
    [InlineData("17042 In Stock", 17042)]
    [InlineData("5000", 5000)]
    [InlineData("3 In Stock", 3)]
    [InlineData("Call", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("In Stock Soon", null)]
    public void Stock_IsFirstIntegerSequence_OrNull(string? availability, int? expected) =>
        Assert.Equal(expected, MouserClient.ParseStock(availability));

    [Fact]
    public void ParseResponse_MalformedOrEmptyBody_YieldsNoParts()
    {
        var client = NewClient();

        Assert.Empty(client.ParseResponse("{}", "TPIC6B595DWR"));
        Assert.Empty(client.ParseResponse("""{"Errors":[],"SearchResults":null}""", "TPIC6B595DWR"));
    }

    [Fact]
    public void SearchFixture_ParsesBothParts()
    {
        var parts = ParseSearchFixture();

        Assert.Equal("TPIC6B595DWR", parts[0].Mpn);
        Assert.Equal("TPIC6B595DWG4", parts[1].Mpn);
        // parts[0] carries no LifecycleStatus (=> Unknown, pinned in the field-mapping
        // leg); parts[1] carries "End of Life" and pins the mapped non-Unknown stage.
        Assert.Equal(LifecycleStage.Unknown, parts[0].Lifecycle);
        Assert.Equal(LifecycleStage.Eol, parts[1].Lifecycle);
    }

    [Fact]
    public void SearchRequest_V2Endpoint_WithCurrencyAndClampedRecords_NoSecrets()
    {
        var request = NewClient().BuildSearchRequest("shift register", 200);

        Assert.Equal("POST", request.Method);
        Assert.Equal("https://api.mouser.com/api/v2/search/keyword?currencyCode=CAD", request.Url);
        Assert.DoesNotContain("apiKey", request.Url);
        Assert.Empty(request.Headers);
        using var body = JsonDocument.Parse(request.Body!);
        var inner = body.RootElement.GetProperty("SearchByKeywordRequest");
        Assert.Equal("shift register", inner.GetProperty("keyword").GetString());
        Assert.Equal(50, inner.GetProperty("records").GetInt32());
        Assert.Equal(0, inner.GetProperty("startingRecord").GetInt32());
    }

    // A part-number-shaped query must not ride the fuzzy keyword endpoint — an unknown
    // MPN there comes back as a page of unrelated hits.
    [Fact]
    public void SearchRequest_PartNumberShapedQuery_RoutesToByNumberEndpoint()
    {
        var request = NewClient().BuildSearchRequest("T821M140A1S100CEU", 10);

        Assert.Equal("POST", request.Method);
        Assert.Equal("https://api.mouser.com/api/v2/search/partnumber?currencyCode=CAD", request.Url);
        Assert.DoesNotContain("apiKey", request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        var inner = body.RootElement.GetProperty("SearchByPartRequest");
        Assert.Equal("T821M140A1S100CEU", inner.GetProperty("mouserPartNumber").GetString());
        Assert.Equal("", inner.GetProperty("partSearchOptions").GetString());
        Assert.False(inner.TryGetProperty("records", out _));
    }

    // The routing predicate trims, so a padded query must carry its trimmed form onto
    // the wire — the by-number endpoint exact-matches, and padding would never hit.
    [Fact]
    public void SearchRequest_PaddedPartNumber_SendsTheTrimmedForm()
    {
        var request = NewClient().BuildSearchRequest("  T821M140A1S100CEU ", 10);

        Assert.Equal("https://api.mouser.com/api/v2/search/partnumber?currencyCode=CAD", request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        var inner = body.RootElement.GetProperty("SearchByPartRequest");
        Assert.Equal("T821M140A1S100CEU", inner.GetProperty("mouserPartNumber").GetString());
    }

    [Theory]
    [InlineData("TPIC6B595DWR", true)]
    [InlineData("1840418", true)]
    [InlineData("103309-3", true)]
    [InlineData("595-TPIC6B595DWR", true)]
    [InlineData("HM1705-ND", true)]
    [InlineData("B2B-PH-K-S", true)]
    [InlineData("resistor", false)]
    [InlineData("gpib", false)]
    [InlineData("usb b panel mount", false)]
    [InlineData("a1", false)]
    [InlineData("  1840418  ", true)]
    public void LooksLikePartNumber_SingleTokenContainingADigit(string query, bool expected) =>
        Assert.Equal(expected, MouserClient.LooksLikePartNumber(query));

    [Fact]
    public void DetailsRequest_PartNumberEndpoint_WithSearchByPartRequest()
    {
        var request = NewClient().BuildDetailsRequest("595-TPIC6B595DWR");

        Assert.Equal("POST", request.Method);
        Assert.Equal("https://api.mouser.com/api/v2/search/partnumber?currencyCode=CAD", request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        var inner = body.RootElement.GetProperty("SearchByPartRequest");
        Assert.Equal("595-TPIC6B595DWR", inner.GetProperty("mouserPartNumber").GetString());
    }

    [Fact]
    public async Task ApplySecrets_AppendsApiKeyToTheQuery_LeavingTheBuiltRequestUntouched()
    {
        var request = NewClient().BuildSearchRequest("shift register", 10);
        var keyConfig = new KeyConfig(PopulateMouserKey(new VendorRequestFakeStore()));

        var stamped = await NewClient().ApplySecretsAsync(request, keyConfig, CancellationToken.None);

        Assert.Equal("https://api.mouser.com/api/v2/search/keyword?currencyCode=CAD&apiKey=mk-value-6N1W", stamped.Url);
        Assert.Equal("https://api.mouser.com/api/v2/search/keyword?currencyCode=CAD", request.Url);
    }

    [Fact]
    public void IsConfigured_RequiresApiKey()
    {
        var store = new VendorRequestFakeStore();
        Assert.False(NewClient().IsConfigured(new KeyConfig(store)));
        store.Entries[CredentialTargets.TargetFor(VendorId.Mouser, CredentialTargets.ApiKeyField)] = "mk";
        Assert.True(NewClient().IsConfigured(new KeyConfig(store)));
    }

    private static VendorRequestFakeStore PopulateMouserKey(VendorRequestFakeStore store)
    {
        store.Entries[CredentialTargets.TargetFor(VendorId.Mouser, CredentialTargets.ApiKeyField)] = "mk-value-6N1W";
        return store;
    }

    [Fact]
    public void DetailsParse_SamePartShape_ParametersMapProductAttributes()
    {
        var part = NewClient().ParseDetailsResponse(File.ReadAllText(FixturePath("mouser-details.json")));

        Assert.Equal("TPIC6B595DWR", part.Mpn);
        Assert.Equal("595-TPIC6B595DWR", part.VendorPartNumber);
        Assert.Equal(17042, part.Stock);
        Assert.Equal("Reel", part.Packaging);
        // The by-number response carries no spec table; ProductAttributes is the
        // parameter-like data it does carry, vendor order preserved.
        Assert.Equal(
            new[] { ("Packaging", "Reel"), ("Packaging", "Cut Tape"), ("Packaging", "MouseReel"), ("Standard Pack Qty", "2000") },
            part.Parameters!.Select(p => (p.Name, p.Value)).ToArray());
        Assert.NotNull(part.Datasheets);
        Assert.Empty(part.Datasheets!);
    }

    [Fact]
    public void DetailsParse_EmptyParts_ThrowsThePinnedMessage()
    {
        const string raw = """{"Errors":[],"SearchResults":{"NumberOfResult":0,"Parts":[]}}""";

        var ex = Assert.Throws<InvalidOperationException>(() => NewClient().ParseDetailsResponse(raw));

        Assert.Equal("mouser details response carries no part", ex.Message);
    }

    [Fact]
    public void DetailsParse_PresentDataSheetUrl_MapsToDatasheets()
    {
        const string raw = """
            {"Errors":[],"SearchResults":{"NumberOfResult":1,"Parts":[{"Availability":"12 In Stock","ManufacturerPartNumber":"TPIC6B595DWR","MouserPartNumber":"595-TPIC6B595DWR","DataSheetUrl":"https://www.ti.com/lit/ds/symlink/tpic6b595.pdf","PriceBreaks":[{"Quantity":1,"Price":"$2.12","Currency":"CAD"}]}]}}
            """;

        var part = NewClient().ParseDetailsResponse(raw);

        var datasheet = Assert.Single(part.Datasheets!);
        Assert.Equal("https://www.ti.com/lit/ds/symlink/tpic6b595.pdf", datasheet);
    }
}
