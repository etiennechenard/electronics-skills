using System.Diagnostics;
using System.Text.Json;
using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

public class MarketplaceRegistrationTests
{
    // The marketplace root holds .claude-plugin/marketplace.json; the test assembly sits
    // several levels beneath it.
    private static readonly Lazy<string> MarketplaceRoot = new(FindMarketplaceRoot);

    private static string FindMarketplaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, ".claude-plugin", "marketplace.json")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("marketplace root not found above the test assembly");
    }

    // The any-cwd legs spawn `dotnet run`, whose build activity would otherwise pollute the
    // captured stdout; building once up front keeps the pinned spawn's stdout exactly the
    // CLI's own output.
    private static readonly Lazy<bool> CliBuilt = new(() =>
    {
        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = MarketplaceRoot.Value,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("build");
        start.ArgumentList.Add(CliProjectPath);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("CLI build failed: " + process.StandardError.ReadToEnd());
        return true;
    });

    private static string CliProjectPath =>
        Path.Combine(MarketplaceRoot.Value, "parts-search", "skills", "parts-search", "scripts", "PartsSearch", "PartsSearch.csproj");

    private static string PluginJsonPath =>
        Path.Combine(MarketplaceRoot.Value, "parts-search", ".claude-plugin", "plugin.json");

    private static string SkillMdPath =>
        Path.Combine(MarketplaceRoot.Value, "parts-search", "skills", "parts-search", "SKILL.md");

    private static string PluginDescription()
    {
        using var plugin = JsonDocument.Parse(File.ReadAllText(PluginJsonPath));
        return plugin.RootElement.GetProperty("description").GetString()!;
    }

    [Fact]
    public void PluginJson_Parses_CarriesNameVersionDescription()
    {
        using var plugin = JsonDocument.Parse(File.ReadAllText(PluginJsonPath));

        Assert.Equal("parts-search", plugin.RootElement.GetProperty("name").GetString());
        Assert.Equal("0.2.0", plugin.RootElement.GetProperty("version").GetString());
        Assert.False(string.IsNullOrWhiteSpace(plugin.RootElement.GetProperty("description").GetString()));
    }

    [Fact]
    public void MarketplaceJson_ContainsPartsSearchEntry_MatchingPluginDescription()
    {
        var marketplacePath = Path.Combine(MarketplaceRoot.Value, ".claude-plugin", "marketplace.json");
        using var marketplace = JsonDocument.Parse(File.ReadAllText(marketplacePath));

        var entry = marketplace.RootElement.GetProperty("plugins").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "parts-search");
        Assert.Equal("./parts-search", entry.GetProperty("source").GetString());
        Assert.Equal(PluginDescription(), entry.GetProperty("description").GetString());
    }

    [Fact]
    public void SkillMd_FrontmatterExists_InvocationFormMatchesCliSurface()
    {
        var skill = File.ReadAllText(SkillMdPath);

        Assert.StartsWith("---", skill);
        Assert.Contains("name: parts-search", skill);
        Assert.Contains("description:", skill);
        Assert.Contains(
            "dotnet run --project \"${CLAUDE_PLUGIN_ROOT}/skills/parts-search/scripts/PartsSearch\"",
            skill);
    }

    [Fact]
    public void CliCsproj_CarriesZeroPackageReferences()
    {
        var csproj = File.ReadAllText(CliProjectPath);

        Assert.DoesNotContain("<PackageReference", csproj);
    }

    private static (int ExitCode, string StdOut) SpawnCli(params string[] arguments)
    {
        _ = CliBuilt.Value;
        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetTempPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("--project");
        start.ArgumentList.Add(Path.Combine(
            MarketplaceRoot.Value, "parts-search", "skills", "parts-search", "scripts", "PartsSearch"));
        // The docs pin `--` as the required separator; the mechanical pin demonstrates it.
        start.ArgumentList.Add("--");
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        var stderrTask = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        _ = stderrTask.Result;
        return (process.ExitCode, stdout);
    }

    private static bool AnyVendorConfigured() =>
        new KeyConfig(new Win32CredentialStore()).IsConfigured(VendorId.Digikey)
        || new KeyConfig(new Win32CredentialStore()).IsConfigured(VendorId.Mouser)
        || new KeyConfig(new Win32CredentialStore()).IsConfigured(VendorId.Newark);

    [Fact]
    public void AnyCwdInvocation_UnknownFlag_Exits2WithOneJsonErrorObject()
    {
        var (exitCode, stdout) = SpawnCli("search", "--q", "resistor", "--bogus");

        Assert.Equal(2, exitCode);
        using var error = JsonDocument.Parse(stdout.Trim());
        Assert.False(error.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", error.RootElement.GetProperty("error").GetString());
        Assert.Equal("search", error.RootElement.GetProperty("command").GetString());
    }

    [SkippableFact]
    public void AnyCwdInvocation_ZeroKeys_Exits0WithAllVendorsNoKey()
    {
        Skip.If(AnyVendorConfigured(), "at least one vendor key is configured (keys provisioned at the fixture-capture gate) — the zero-key leg is not applicable and would make live vendor calls once real clients are registered");

        var (exitCode, stdout) = SpawnCli("search", "--q", "resistor", "--format", "json");

        Assert.Equal(0, exitCode);
        using var envelope = JsonDocument.Parse(stdout.Trim());
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("search", envelope.RootElement.GetProperty("command").GetString());
        var vendors = envelope.RootElement.GetProperty("vendors");
        Assert.Equal(3, vendors.GetArrayLength());
        Assert.All(vendors.EnumerateArray(), vendor =>
        {
            Assert.Equal("no_key", vendor.GetProperty("status").GetString());
            Assert.Equal(0, vendor.GetProperty("parts").GetArrayLength());
        });
    }
}
