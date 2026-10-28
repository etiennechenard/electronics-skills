using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

public class KeyConfigTests
{
    // Pins the four environment-variable names independently of KeyConfig's own table.
    private static readonly (VendorId Vendor, string Field, string EnvName)[] Secrets =
    [
        (VendorId.Digikey, CredentialTargets.ClientIdField, "DIGIKEY_CLIENT_ID"),
        (VendorId.Digikey, CredentialTargets.ClientSecretField, "DIGIKEY_CLIENT_SECRET"),
        (VendorId.Mouser, CredentialTargets.ApiKeyField, "MOUSER_API_KEY"),
        (VendorId.Newark, CredentialTargets.ApiKeyField, "ELEMENT14_API_KEY"),
    ];

    private sealed class FakeCredentialStore : ICredentialStore
    {
        public Dictionary<string, string> Entries { get; } = [];

        public string? Read(string target) => Entries.TryGetValue(target, out var value) ? value : null;

        public void Write(string target, string secret) => Entries[target] = secret;

        public bool Delete(string target) => Entries.Remove(target);
    }

    private static void ClearEnvVars()
    {
        foreach (var (_, _, envName) in Secrets)
            Environment.SetEnvironmentVariable(envName, null, EnvironmentVariableTarget.Process);
    }

    private static void SetAllEnvVars(string valuePrefix)
    {
        foreach (var (_, _, envName) in Secrets)
            Environment.SetEnvironmentVariable(envName, valuePrefix + envName, EnvironmentVariableTarget.Process);
    }

    private static void StoreAllSecrets(FakeCredentialStore store, string valuePrefix)
    {
        foreach (var (vendor, field, _) in Secrets)
            store.Entries[CredentialTargets.TargetFor(vendor, field)] = $"{valuePrefix}{vendor}-{field}";
    }

    [Fact]
    public void EnvVarBeatsCredentialManager_PerSecret()
    {
        try
        {
            ClearEnvVars();
            var store = new FakeCredentialStore();
            StoreAllSecrets(store, "stored-");
            SetAllEnvVars("env-");

            var config = new KeyConfig(store);
            foreach (var (vendor, field, envName) in Secrets)
            {
                Assert.Equal("env-" + envName, config.GetSecret(vendor, field));
                Assert.Equal(SecretSource.Env, config.GetSource(vendor, field));
            }
        }
        finally
        {
            ClearEnvVars();
        }
    }

    [Fact]
    public void CredentialManagerServes_WhenEnvAbsent()
    {
        try
        {
            ClearEnvVars();
            var store = new FakeCredentialStore();
            StoreAllSecrets(store, "stored-");

            var config = new KeyConfig(store);
            foreach (var (vendor, field, _) in Secrets)
            {
                Assert.Equal($"stored-{vendor}-{field}", config.GetSecret(vendor, field));
                Assert.Equal(SecretSource.CredentialManager, config.GetSource(vendor, field));
            }
            Assert.True(config.IsConfigured(VendorId.Digikey));
            Assert.True(config.IsConfigured(VendorId.Mouser));
            Assert.True(config.IsConfigured(VendorId.Newark));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    [Fact]
    public void NeitherSourceConfigured_NoneAndVendorUnconfigured()
    {
        try
        {
            ClearEnvVars();
            var config = new KeyConfig(new FakeCredentialStore());

            foreach (var (vendor, field, _) in Secrets)
            {
                Assert.Null(config.GetSecret(vendor, field));
                Assert.Equal(SecretSource.None, config.GetSource(vendor, field));
            }
            Assert.False(config.IsConfigured(VendorId.Digikey));
            Assert.False(config.IsConfigured(VendorId.Mouser));
            Assert.False(config.IsConfigured(VendorId.Newark));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    [Fact]
    public void EnvOnlyConfiguration_ConfiguresAllVendors()
    {
        try
        {
            ClearEnvVars();
            SetAllEnvVars("env-");

            var config = new KeyConfig(new FakeCredentialStore());
            foreach (var (vendor, field, _) in Secrets)
                Assert.Equal(SecretSource.Env, config.GetSource(vendor, field));
            Assert.True(config.IsConfigured(VendorId.Digikey));
            Assert.True(config.IsConfigured(VendorId.Mouser));
            Assert.True(config.IsConfigured(VendorId.Newark));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    [Fact]
    public void MixedSources_EnvWinsOnlyWhereSet()
    {
        try
        {
            ClearEnvVars();
            Environment.SetEnvironmentVariable("DIGIKEY_CLIENT_ID", "env-client-id", EnvironmentVariableTarget.Process);
            var store = new FakeCredentialStore();
            StoreAllSecrets(store, "stored-");

            var config = new KeyConfig(store);
            Assert.Equal("env-client-id", config.GetSecret(VendorId.Digikey, CredentialTargets.ClientIdField));
            Assert.Equal(SecretSource.Env, config.GetSource(VendorId.Digikey, CredentialTargets.ClientIdField));
            Assert.Equal("stored-Digikey-client-secret", config.GetSecret(VendorId.Digikey, CredentialTargets.ClientSecretField));
            Assert.Equal(SecretSource.CredentialManager, config.GetSource(VendorId.Digikey, CredentialTargets.ClientSecretField));
            Assert.Equal(SecretSource.CredentialManager, config.GetSource(VendorId.Mouser, CredentialTargets.ApiKeyField));
            Assert.Equal(SecretSource.CredentialManager, config.GetSource(VendorId.Newark, CredentialTargets.ApiKeyField));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    [Fact]
    public void DigiKey_ConfiguredWithClientPair()
    {
        try
        {
            ClearEnvVars();
            var store = new FakeCredentialStore();
            store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientIdField)] = "id";
            store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientSecretField)] = "secret";

            Assert.True(new KeyConfig(store).IsConfigured(VendorId.Digikey));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    [Fact]
    public void DigiKey_UnconfiguredWithoutClientPair()
    {
        try
        {
            ClearEnvVars();
            var store = new FakeCredentialStore();
            store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientIdField)] = "id";
            Assert.False(new KeyConfig(store).IsConfigured(VendorId.Digikey));

            // A stored value at the removed api-key target does not configure the vendor.
            var apiKeyOnly = new FakeCredentialStore();
            apiKeyOnly.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ApiKeyField)] = "key";
            Assert.False(new KeyConfig(apiKeyOnly).IsConfigured(VendorId.Digikey));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    [Fact]
    public void MouserAndNewark_ConfiguredExactlyWithApiKey()
    {
        try
        {
            ClearEnvVars();
            var store = new FakeCredentialStore();
            var config = new KeyConfig(store);
            Assert.False(config.IsConfigured(VendorId.Mouser));
            Assert.False(config.IsConfigured(VendorId.Newark));

            store.Entries[CredentialTargets.TargetFor(VendorId.Mouser, CredentialTargets.ApiKeyField)] = "mouser-key";
            var withMouser = new KeyConfig(store);
            Assert.True(withMouser.IsConfigured(VendorId.Mouser));
            Assert.False(withMouser.IsConfigured(VendorId.Newark));

            store.Entries[CredentialTargets.TargetFor(VendorId.Newark, CredentialTargets.ApiKeyField)] = "newark-key";
            var withBoth = new KeyConfig(store);
            Assert.True(withBoth.IsConfigured(VendorId.Mouser));
            Assert.True(withBoth.IsConfigured(VendorId.Newark));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    [Fact]
    public void EmptyEnvValue_CountsAsAbsent()
    {
        try
        {
            ClearEnvVars();
            Environment.SetEnvironmentVariable("DIGIKEY_CLIENT_SECRET", "", EnvironmentVariableTarget.Process);
            var store = new FakeCredentialStore();
            store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientIdField)] = "id";
            store.Entries[CredentialTargets.TargetFor(VendorId.Digikey, CredentialTargets.ClientSecretField)] = "stored-secret";

            var config = new KeyConfig(store);
            Assert.Equal(SecretSource.CredentialManager, config.GetSource(VendorId.Digikey, CredentialTargets.ClientSecretField));
            Assert.Equal("stored-secret", config.GetSecret(VendorId.Digikey, CredentialTargets.ClientSecretField));
            Assert.True(config.IsConfigured(VendorId.Digikey));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    [Fact]
    public void SecretsResolveOnce_AtConstruction()
    {
        try
        {
            ClearEnvVars();
            var store = new FakeCredentialStore();
            store.Entries[CredentialTargets.TargetFor(VendorId.Mouser, CredentialTargets.ApiKeyField)] = "stored-original";
            var config = new KeyConfig(store);

            store.Entries[CredentialTargets.TargetFor(VendorId.Mouser, CredentialTargets.ApiKeyField)] = "stored-later";
            Environment.SetEnvironmentVariable("MOUSER_API_KEY", "env-later", EnvironmentVariableTarget.Process);

            Assert.Equal("stored-original", config.GetSecret(VendorId.Mouser, CredentialTargets.ApiKeyField));
            Assert.Equal(SecretSource.CredentialManager, config.GetSource(VendorId.Mouser, CredentialTargets.ApiKeyField));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    [Fact]
    public void UnknownSecretPair_LookupThrows()
    {
        var config = new KeyConfig(new FakeCredentialStore());
        Assert.Throws<ArgumentException>(
            () => config.GetSecret(VendorId.Mouser, CredentialTargets.ClientIdField));
    }
}
