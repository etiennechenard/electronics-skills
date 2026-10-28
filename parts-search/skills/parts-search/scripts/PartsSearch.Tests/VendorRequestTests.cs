using System.Text.Json;
using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

public class VendorRequestTests
{
    private static DigiKeyClient DigiKeyClient() =>
        new(DigiKeyAuthTestDouble.Auth());

    [Fact]
    public void DigiKey_SearchRequest_UrlBodyAndLocaleHeaders()
    {
        var request = DigiKeyClient().BuildSearchRequest("TPIC6B595DWR", 25);

        Assert.Equal("POST", request.Method);
        Assert.Equal("https://api.digikey.com/products/v4/search/keyword?limit=25&offset=0", request.Url);
        Assert.Equal("application/json", request.ContentType);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("TPIC6B595DWR", body.RootElement.GetProperty("Keywords").GetString());
        Assert.Equal(25, body.RootElement.GetProperty("Limit").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("Offset").GetInt32());
        Assert.Equal("CA", request.Headers["X-DIGIKEY-Locale-Site"]);
        Assert.Equal("en", request.Headers["X-DIGIKEY-Locale-Language"]);
        Assert.Equal("CAD", request.Headers["X-DIGIKEY-Locale-Currency"]);
        Assert.DoesNotContain(request.Headers.Keys, header => header.Contains("Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(request.Headers.Keys, header => header.Contains("Client-Id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DigiKey_SearchRequest_DistributorCodeRidesTheKeywordSearch()
    {
        var request = DigiKeyClient().BuildSearchRequest("HM1705-ND", 10);

        Assert.Equal("https://api.digikey.com/products/v4/search/keyword?limit=10&offset=0", request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("HM1705-ND", body.RootElement.GetProperty("Keywords").GetString());
    }

    [Theory]
    [InlineData("panel mount usb", "panel mount")]
    [InlineData("GPIB IEEE-488 connector 24 position", "GPIB IEEE-488 connector 24")]
    [InlineData("two words", "two")]
    [InlineData("  spaced   out  ", "spaced")]
    public void DigiKey_NarrowSearchQuery_DropsTheLastToken(string query, string expected) =>
        Assert.Equal(expected, DigiKeyClient().NarrowSearchQuery(query));

    [Theory]
    [InlineData("resistor")]
    [InlineData("1840418")]
    [InlineData("  ")]
    public void DigiKey_NarrowSearchQuery_SingleTokenCannotNarrow(string query) =>
        Assert.Null(DigiKeyClient().NarrowSearchQuery(query));

    [Fact]
    public async Task DigiKey_ApplySecrets_StampsBearerAndClientIdAtSendTimeOnly()
    {
        var request = DigiKeyClient().BuildSearchRequest("TPIC6B595DWR", 10);
        var keyConfig = new KeyConfig(BuildPopulatedStore());

        var stamped = await DigiKeyClient().ApplySecretsAsync(request, keyConfig, CancellationToken.None);

        Assert.Equal("Bearer test-token-value-9f3a", stamped.Headers["Authorization"]);
        Assert.Equal("id-value-7Q2M", stamped.Headers["X-DIGIKEY-Client-Id"]);
        Assert.Null(request.Headers.GetValueOrDefault("Authorization"));
        Assert.Null(request.Headers.GetValueOrDefault("X-DIGIKEY-Client-Id"));
    }

    [Fact]
    public async Task DigiKey_ApplySecrets_OnDetailsRequest_StampsSameHeaders()
    {
        var request = DigiKeyClient().BuildDetailsRequest("296-1275-1-ND");
        var keyConfig = new KeyConfig(BuildPopulatedStore());

        var stamped = await DigiKeyClient().ApplySecretsAsync(request, keyConfig, CancellationToken.None);

        Assert.Equal("Bearer test-token-value-9f3a", stamped.Headers["Authorization"]);
        Assert.Equal("id-value-7Q2M", stamped.Headers["X-DIGIKEY-Client-Id"]);
        Assert.Equal("https://api.digikey.com/products/v4/search/296-1275-1-ND/productdetails", stamped.Url);
    }

    [Fact]
    public void DigiKey_IsConfigured_RequiresClientPair()
    {
        var store = BuildPopulatedStore();
        store.Entries.Remove(CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientSecretField));
        var client = DigiKeyClient();

        Assert.False(client.IsConfigured(new KeyConfig(store)));

        store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientSecretField)] = "secret";
        Assert.True(client.IsConfigured(new KeyConfig(store)));
    }

    private static VendorRequestFakeStore BuildPopulatedStore()
    {
        var store = new VendorRequestFakeStore();
        store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientIdField)] = "id-value-7Q2M";
        store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientSecretField)] = "secret-value-9X4K";
        return store;
    }
}

internal sealed class VendorRequestFakeStore : ICredentialStore
{
    public Dictionary<string, string> Entries { get; } = [];

    public string? Read(string target) => Entries.TryGetValue(target, out var value) ? value : null;

    public void Write(string target, string secret) => Entries[target] = secret;

    public bool Delete(string target) => Entries.Remove(target);
}
