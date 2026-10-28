using System.Runtime.InteropServices;
using System.Text.Json;

namespace PartsSearch;

internal interface IKeyConsole
{
    bool IsInteractive { get; }

    string ReadLine();

    void WriteLine(string message);
}

// Human-facing key-command I/O: prompts and notes go to stderr so stdout carries
// exactly the one JSON result object. Prompts read line input with the console's
// echo disabled — hidden like a ReadKey loop, but pasted text lands in the line
// buffer instead of being dropped; the program itself never writes a secret.
internal sealed class KeyConsole : IKeyConsole
{
    private const int StdInputHandle = -10;
    private const int EnableEchoInput = 0x0004;

    public bool IsInteractive => !Console.IsInputRedirected;

    public void WriteLine(string message) => Console.Error.WriteLine(message);

    public string ReadLine()
    {
        var handle = GetStdHandle(StdInputHandle);
        _ = GetConsoleMode(handle, out var previousMode);
        _ = SetConsoleMode(handle, previousMode & ~EnableEchoInput);
        try
        {
            return Console.ReadLine() ?? "";
        }
        finally
        {
            _ = SetConsoleMode(handle, previousMode);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out int lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, int dwMode);
}

// Thrown for key-subcommand usage errors.
internal sealed class KeyUsageException(string message) : Exception(message);

internal sealed class KeyCommands
{
    private readonly ICredentialStore _store;
    private readonly KeyConfig _keyConfig;
    private readonly IKeyConsole _console;

    public KeyCommands(ICredentialStore store, KeyConfig keyConfig, IKeyConsole console)
    {
        _store = store;
        _keyConfig = keyConfig;
        _console = console;
    }

    public string Set(KeyCommandArgs args)
    {
        var vendor = args.Vendor ?? throw new InvalidOperationException("key set requires a vendor token");
        if (!_console.IsInteractive)
            throw new KeyUsageException("key set requires an interactive terminal to read prompts");

        var vendorToken = Name(vendor);
        var stored = new List<string>();
        foreach (var field in CredentialTargets.FieldsFor(vendor))
        {
            var value = Prompt(vendorToken, field);
            _store.Write(CredentialTargets.TargetFor(vendor, field), value);
            stored.Add(field);
            // KeyConfig resolved at startup: an Env source means this environment
            // variable already wins over the secret just stored.
            if (_keyConfig.GetSource(vendor, field) == SecretSource.Env)
                _console.WriteLine($"{KeyConfig.EnvNameFor(vendor, field)} is set in the environment and will override the stored value");
        }

        return Serialize(new { ok = true, command = "key", sub = "set", vendor = vendorToken, stored });
    }

    public string List()
    {
        var vendors = new Dictionary<string, object>();
        foreach (var vendor in new[] { VendorId.Digikey, VendorId.Mouser, VendorId.Newark })
        {
            var fields = new Dictionary<string, object>();
            foreach (var field in CredentialTargets.FieldsFor(vendor))
                fields[field] = new { source = SourceText(_keyConfig.GetSource(vendor, field)) };
            vendors[Name(vendor)] = fields;
        }

        return Serialize(new { ok = true, command = "key", sub = "list", vendors });
    }

    public string Clear(KeyCommandArgs args)
    {
        var vendor = args.Vendor ?? throw new InvalidOperationException("key clear requires a vendor token");
        var cleared = new List<string>();
        foreach (var field in CredentialTargets.FieldsFor(vendor))
        {
            if (_store.Delete(CredentialTargets.TargetFor(vendor, field)))
                cleared.Add(field);
        }
        // Legacy installs may still hold the removed DigiKey api-key target; clear sweeps
        // it so no dead credential stays behind.
        if (vendor == VendorId.Digikey &&
            _store.Delete(CredentialTargets.TargetFor(vendor, CredentialTargets.ApiKeyField)))
            cleared.Add(CredentialTargets.ApiKeyField);

        return Serialize(new { ok = true, command = "key", sub = "clear", vendor = Name(vendor), cleared });
    }

    public async Task<string> TestAsync(SearchService service, CancellationToken cancellationToken)
    {
        var statuses = await service.ProbeKeysAsync(cancellationToken);
        var vendors = statuses.ToDictionary(kv => Name(kv.Key), kv => kv.Value);
        return Serialize(new { ok = true, command = "key", sub = "test", vendors });
    }

    private string Prompt(string vendorToken, string field)
    {
        while (true)
        {
            _console.WriteLine($"{vendorToken} {field}: ");
            var value = _console.ReadLine();
            if (value.Length > 0) return value;
        }
    }

    private static string SourceText(SecretSource source) => source switch
    {
        SecretSource.Env => "env",
        SecretSource.CredentialManager => "credential manager",
        _ => "none",
    };

    private static string Name(VendorId vendor) => vendor.ToString().ToLowerInvariant();

    private static string Serialize(object result) => JsonSerializer.Serialize(result, WireJson.Options);
}
