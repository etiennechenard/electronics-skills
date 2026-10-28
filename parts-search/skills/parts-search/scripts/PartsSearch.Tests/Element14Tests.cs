using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

public class Element14Tests
{
    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", fileName);

    private static Element14Client NewClient() => new();

    [Fact]
    public void SearchFixture_ParsesProductFieldMapping()
    {
        var parts = NewClient().ParseResponse(File.ReadAllText(FixturePath("element14-search.json")), "83F8917");

        var part = Assert.Single(parts);
        Assert.Equal("83F8917", part.VendorPartNumber);
        Assert.Equal("1402H", part.Mpn);
        Assert.Equal("HAMMOND", part.Manufacturer);
        Assert.StartsWith("HAMMOND - 1402H - Metal Enclosure", part.Description);
        Assert.Equal(105, part.Stock);
        Assert.Equal(LifecycleStage.Unknown, part.Lifecycle);
        Assert.Null(part.Category);
        Assert.Null(part.Url);
    }

    [Fact]
    public void SearchFixture_Prices_MapToBreaksWithCad()
    {
        var parts = NewClient().ParseResponse(File.ReadAllText(FixturePath("element14-search.json")), "83F8917");

        var breaks = parts[0].PriceBreaks;
        Assert.Equal(1, breaks[0].Qty);
        Assert.Equal(110.25m, breaks[0].Price);
        Assert.Equal(5, breaks[1].Qty);
        Assert.Equal(97.77m, breaks[1].Price);
        Assert.Equal(10, breaks[2].Qty);
        Assert.Equal(90.94m, breaks[2].Price);
        Assert.Equal("CAD", parts[0].Currency);
        Assert.Equal(110.25m, parts[0].UnitPrice);
    }

    [Theory]
    [InlineData("reverse mount green 1206 LED")]
    [InlineData("TPIC6B595DWR")]
    [InlineData("83F8917")]
    public void SearchRequest_EveryQueryKind_RidesTheAnyField(string query)
    {
        var request = NewClient().BuildSearchRequest(query, 10);

        Assert.Equal("GET", request.Method);
        Assert.StartsWith("https://api.element14.com/catalog/products?term=any:", request.Url);
        Assert.Contains(Uri.EscapeDataString(query), request.Url);
        Assert.Contains("storeInfo.id=canada.newark.com", request.Url);
        Assert.Contains("resultsSettings.responseGroup=large", request.Url);
        Assert.Contains("resultsSettings.offset=0", request.Url);
        Assert.Contains("resultsSettings.numberOfResults=10", request.Url);
        Assert.Contains("callInfo.responseDataFormat=json", request.Url);
        Assert.DoesNotContain("term=id:", request.Url);
        Assert.DoesNotContain("manuPartNum", request.Url);
        Assert.DoesNotContain("apiKey", request.Url);
    }

    [Fact]
    public void SearchRequest_UrlEncodesTheQuery()
    {
        var request = NewClient().BuildSearchRequest("reverse mount green 1206 LED", 10);

        Assert.Contains("term=any:reverse%20mount%20green%201206%20LED", request.Url);
    }

    [Fact]
    public void DetailsRequest_BuildsTheIdScopedForm()
    {
        var request = NewClient().BuildDetailsRequest("83F8917");

        Assert.Equal("GET", request.Method);
        Assert.Equal(
            "https://api.element14.com/catalog/products?term=id:83F8917" +
            "&storeInfo.id=canada.newark.com&resultsSettings.responseGroup=large&callInfo.responseDataFormat=json",
            request.Url);
        Assert.Null(request.Body);
    }

    [Fact]
    public void DetailsParse_AttributesMapToParameters_DatasheetsToUrls()
    {
        var part = NewClient().ParseDetailsResponse(File.ReadAllText(FixturePath("element14-details.json")));

        Assert.Equal("83F8917", part.VendorPartNumber);
        Assert.Equal("1402H", part.Mpn);
        Assert.Equal(105, part.Stock);

        // Vendor order and spelling are preserved end to end.
        Assert.Equal("rohsCompliant", part.Parameters![0].Name);
        Assert.Equal("YES", part.Parameters[0].Value);
        Assert.Equal("IP Rating", part.Parameters[1].Name);
        Assert.Equal("IP32", part.Parameters[1].Value);
        Assert.True(part.Parameters.Count > 2);

        var datasheet = Assert.Single(part.Datasheets!);
        Assert.Equal("http://www.farnell.com/datasheets/1160334.pdf", datasheet);
    }

    [Fact]
    public void DetailsParse_AbsentAttributesAndDatasheets_MapToEmptyLists()
    {
        const string raw = """
            {"keywordSearchReturn":{"numberOfResults":1,"products":[{"sku":"83F8917","displayName":"HAMMOND - 1402H","productStatus":"DIRECT_SHIP","brandName":"HAMMOND","translatedManufacturerPartNumber":"1402H","stock":{"level":105}}]}}
            """;

        var part = NewClient().ParseDetailsResponse(raw);

        Assert.Equal("83F8917", part.VendorPartNumber);
        Assert.NotNull(part.Parameters);
        Assert.Empty(part.Parameters!);
        Assert.NotNull(part.Datasheets);
        Assert.Empty(part.Datasheets!);
    }

    [Fact]
    public void SearchParse_ReadsEitherResponseWrapper()
    {
        const string partNumberReturn = """
            {"premierFarnellPartNumberReturn":{"numberOfResults":1,"products":[{"sku":"83F8917","displayName":"HAMMOND - 1402H","brandName":"HAMMOND","translatedManufacturerPartNumber":"1402H","productStatus":"DIRECT_SHIP","stock":{"level":105}}]}}
            """;

        var parts = NewClient().ParseResponse(partNumberReturn, "83F8917");

        var part = Assert.Single(parts);
        Assert.Equal("1402H", part.Mpn);
    }

    [Fact]
    public async Task ApplySecrets_AppendsCallInfoApiKey_LeavingTheBuiltRequestUntouched()
    {
        var request = NewClient().BuildSearchRequest("83F8917", 10);
        var store = new Element14FakeStore();
        store.Entries[CredentialTargets.TargetFor(VendorId.Newark, CredentialTargets.ApiKeyField)] = "e14-key-3F8N";
        var keyConfig = new KeyConfig(store);

        var stamped = await NewClient().ApplySecretsAsync(request, keyConfig, CancellationToken.None);

        Assert.EndsWith("&callInfo.apiKey=e14-key-3F8N", stamped.Url);
        Assert.DoesNotContain("apiKey", request.Url);
    }

    [Fact]
    public void IsConfigured_RequiresApiKey()
    {
        var store = new Element14FakeStore();
        Assert.False(NewClient().IsConfigured(new KeyConfig(store)));
        store.Entries[CredentialTargets.TargetFor(VendorId.Newark, CredentialTargets.ApiKeyField)] = "e14-key";
        Assert.True(NewClient().IsConfigured(new KeyConfig(store)));
    }

    private sealed class Element14FakeStore : ICredentialStore
    {
        public Dictionary<string, string> Entries { get; } = [];

        public string? Read(string target) => Entries.TryGetValue(target, out var value) ? value : null;

        public void Write(string target, string secret) => Entries[target] = secret;

        public bool Delete(string target) => Entries.Remove(target);
    }
}
