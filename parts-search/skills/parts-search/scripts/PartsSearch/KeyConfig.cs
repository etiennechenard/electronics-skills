namespace PartsSearch;

public enum SecretSource
{
    Env,
    CredentialManager,
    None,
}

public sealed class KeyConfig
{
    private sealed record ResolvedSecret(string? Value, SecretSource Source);

    // The four vendor secrets and the environment variable that overrides each; a new
    // vendor's key wiring extends this table.
    private static readonly (VendorId Vendor, string Field, string EnvName)[] Secrets =
    [
        (VendorId.Digikey, CredentialTargets.ClientIdField, "DIGIKEY_CLIENT_ID"),
        (VendorId.Digikey, CredentialTargets.ClientSecretField, "DIGIKEY_CLIENT_SECRET"),
        (VendorId.Mouser, CredentialTargets.ApiKeyField, "MOUSER_API_KEY"),
        (VendorId.Newark, CredentialTargets.ApiKeyField, "ELEMENT14_API_KEY"),
    ];

    private readonly Dictionary<(VendorId Vendor, string Field), ResolvedSecret> _secrets = [];

    // Secrets are resolved once, here; environment and store changes afterwards are not picked up.
    public KeyConfig(ICredentialStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        foreach (var (vendor, field, envName) in Secrets)
            _secrets[(vendor, field)] = Resolve(store, vendor, field, envName);
    }

    public bool IsConfigured(VendorId vendor) => vendor switch
    {
        VendorId.Digikey => Has(vendor, CredentialTargets.ClientIdField) && Has(vendor, CredentialTargets.ClientSecretField),
        VendorId.Mouser => Has(vendor, CredentialTargets.ApiKeyField),
        VendorId.Newark => Has(vendor, CredentialTargets.ApiKeyField),
        _ => false,
    };

    public string? GetSecret(VendorId vendor, string field) => Lookup(vendor, field).Value;

    public SecretSource GetSource(VendorId vendor, string field) => Lookup(vendor, field).Source;

    public static string EnvNameFor(VendorId vendor, string field)
    {
        foreach (var (v, f, envName) in Secrets)
        {
            if (v == vendor && f == field) return envName;
        }
        throw new ArgumentException($"Unknown vendor secret '{vendor}/{field}'.", nameof(field));
    }

    private bool Has(VendorId vendor, string field) => Lookup(vendor, field).Value is not null;

    private ResolvedSecret Lookup(VendorId vendor, string field)
    {
        if (_secrets.TryGetValue((vendor, field), out var secret))
            return secret;
        throw new ArgumentException($"Unknown vendor secret '{vendor}/{field}'.", nameof(field));
    }

    // Empty values count as absent — an empty secret cannot authenticate.
    private static ResolvedSecret Resolve(ICredentialStore store, VendorId vendor, string field, string envName)
    {
        var envValue = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrEmpty(envValue))
            return new ResolvedSecret(envValue, SecretSource.Env);

        var stored = store.Read(CredentialTargets.TargetFor(vendor, field));
        if (!string.IsNullOrEmpty(stored))
            return new ResolvedSecret(stored, SecretSource.CredentialManager);

        return new ResolvedSecret(null, SecretSource.None);
    }
}
