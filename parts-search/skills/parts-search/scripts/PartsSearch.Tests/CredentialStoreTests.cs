using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

public class CredentialStoreTests
{
    [Theory]
    [InlineData(VendorId.Digikey, CredentialTargets.ClientIdField, "parts-search/digikey/client-id")]
    [InlineData(VendorId.Digikey, CredentialTargets.ClientSecretField, "parts-search/digikey/client-secret")]
    [InlineData(VendorId.Mouser, CredentialTargets.ApiKeyField, "parts-search/mouser/api-key")]
    [InlineData(VendorId.Newark, CredentialTargets.ApiKeyField, "parts-search/newark/api-key")]
    public void TargetFor_BuildsLowercasePrefixedTargets(VendorId vendor, string field, string expected) =>
        Assert.Equal(expected, CredentialTargets.TargetFor(vendor, field));

    [Fact]
    public void FieldsFor_DigiKeyHasClientPair_MouserAndNewarkOne()
    {
        Assert.Equal(
            new[] { CredentialTargets.ClientIdField, CredentialTargets.ClientSecretField },
            CredentialTargets.FieldsFor(VendorId.Digikey));
        Assert.Equal(new[] { CredentialTargets.ApiKeyField }, CredentialTargets.FieldsFor(VendorId.Mouser));
        Assert.Equal(new[] { CredentialTargets.ApiKeyField }, CredentialTargets.FieldsFor(VendorId.Newark));
    }

    // The refusals fire before any P/Invoke, so these exercise the real store class
    // without touching the actual Credential Manager.
    [Fact]
    public void Win32Store_RefusesTargetsOutsideToolPrefix()
    {
        var store = new Win32CredentialStore();
        Assert.Throws<ArgumentException>(() => store.Read("other-app/secret"));
        Assert.Throws<ArgumentException>(() => store.Write("other-app/secret", "x"));
        Assert.Throws<ArgumentException>(() => store.Delete("other-app/secret"));
        Assert.Throws<ArgumentException>(() => store.Read("parts-searcher/evil"));
    }
}
