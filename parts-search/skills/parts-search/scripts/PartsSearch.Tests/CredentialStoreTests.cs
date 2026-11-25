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

    [Fact]
    public void MacKeychainStore_RefusesTargetsOutsideToolPrefix()
    {
        var store = new MacKeychainCredentialStore();
        Assert.Throws<ArgumentException>(() => store.Read("other-app/secret"));
        Assert.Throws<ArgumentException>(() => store.Write("other-app/secret", "x"));
        Assert.Throws<ArgumentException>(() => store.Delete("other-app/secret"));
        Assert.Throws<ArgumentException>(() => store.Read("parts-searcher/evil"));
    }

    [Fact]
    public void CreateDefault_PicksTheOsStore()
    {
        var store = CredentialStores.CreateDefault();
        if (OperatingSystem.IsWindows()) Assert.IsType<Win32CredentialStore>(store);
        else if (OperatingSystem.IsMacOS()) Assert.IsType<MacKeychainCredentialStore>(store);
        else Assert.IsType<NullCredentialStore>(store);
    }

    // Opt-in (PARTS_SEARCH_KEYCHAIN_TEST=1): writes, overwrites, reads and deletes one
    // throwaway item in the real login Keychain.
    [SkippableFact]
    public void MacKeychainStore_RoundTripsAgainstTheRealKeychain()
    {
        Skip.IfNot(OperatingSystem.IsMacOS(), "macOS only");
        Skip.IfNot(Environment.GetEnvironmentVariable("PARTS_SEARCH_KEYCHAIN_TEST") == "1",
            "set PARTS_SEARCH_KEYCHAIN_TEST=1 to touch the real login Keychain");

        var store = new MacKeychainCredentialStore();
        var target = $"{CredentialTargets.TargetPrefix}test/{Guid.NewGuid():N}";
        try
        {
            Assert.Null(store.Read(target));
            store.Write(target, "first-värde");
            Assert.Equal("first-värde", store.Read(target));
            store.Write(target, "second");
            Assert.Equal("second", store.Read(target));
            Assert.True(store.Delete(target));
            Assert.Null(store.Read(target));
            Assert.False(store.Delete(target));
        }
        finally
        {
            store.Delete(target);
        }
    }
}
