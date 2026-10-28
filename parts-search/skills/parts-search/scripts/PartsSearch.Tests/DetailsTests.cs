using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

public class DetailsTests
{
    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", fileName);

    private static DigiKeyClient DigiKeyClient() =>
        new(DigiKeyAuthTestDouble.Auth());

    [Fact]
    public void DigiKey_DetailsRequest_BuildsCapturedProductDetailsForm()
    {
        var request = DigiKeyClient().BuildDetailsRequest("296-1275-1-ND");

        Assert.Equal("GET", request.Method);
        Assert.Equal("https://api.digikey.com/products/v4/search/296-1275-1-ND/productdetails", request.Url);
        Assert.Null(request.Body);
        Assert.Equal("CA", request.Headers["X-DIGIKEY-Locale-Site"]);
        Assert.Equal("en", request.Headers["X-DIGIKEY-Locale-Language"]);
        Assert.Equal("CAD", request.Headers["X-DIGIKEY-Locale-Currency"]);
        Assert.DoesNotContain(request.Headers.Keys, header => header.Contains("Authorization", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DigiKey_DetailsParse_MapsFullPart_WithParametersAndDatasheets()
    {
        var part = DigiKeyClient().ParseDetailsResponse(File.ReadAllText(FixturePath("digikey-details.json")));

        Assert.Equal("TPIC6B595DWR", part.Mpn);
        Assert.Equal("296-1351-1-ND", part.VendorPartNumber);
        Assert.Equal("Texas Instruments", part.Manufacturer);
        Assert.Equal(36075, part.Stock);
        Assert.Equal(2.33m, part.UnitPrice);
        Assert.Equal("CAD", part.Currency);
        Assert.Equal(LifecycleStage.Active, part.Lifecycle);
        Assert.Equal("Integrated Circuits (ICs)", part.Category);

        // Vendor order and spelling are preserved end to end.
        Assert.Equal("Logic Type", part.Parameters![0].Name);
        Assert.Equal("Shift Register", part.Parameters[0].Value);
        Assert.Equal("Output Type", part.Parameters[1].Name);
        Assert.Equal("Open Drain", part.Parameters[1].Value);
        Assert.True(part.Parameters.Count > 2);

        var datasheet = Assert.Single(part.Datasheets!);
        Assert.Equal(
            "https://www.ti.com/general/docs/suppproductinfo.tsp?distId=10&gotoUrl=https%3A%2F%2Fwww.ti.com%2Flit%2Fgpn%2Ftpic6b595",
            datasheet);
    }

    [Fact]
    public void DigiKey_DetailsParse_AbsentParametersAndDatasheetUrl_MapToEmptyLists()
    {
        const string raw = """
            {"Product":{"ManufacturerProductNumber":"TPIC6B595DWR","ProductUrl":"https://www.digikey.ca/p","ProductStatus":{"Status":"Active"}}}
            """;

        var part = DigiKeyClient().ParseDetailsResponse(raw);

        Assert.Equal("TPIC6B595DWR", part.Mpn);
        Assert.NotNull(part.Parameters);
        Assert.Empty(part.Parameters!);
        Assert.NotNull(part.Datasheets);
        Assert.Empty(part.Datasheets!);
    }
}
