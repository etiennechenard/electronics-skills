using Visa.Cli;
using Xunit;

namespace Visa.Tests.Cli;

public class ArgParserTests
{
    private static ArgParser OnePositional(IEnumerable<FlagSpec>? specs = null) =>
        new(1, specs ?? []);

    private static ArgParser TwoPositionals(IEnumerable<FlagSpec>? specs = null) =>
        new(2, specs ?? []);

    private static ArgParser ZeroPositionals(IEnumerable<FlagSpec>? specs = null) =>
        new(0, specs ?? []);

    [Fact]
    public void RequiredPositional_Missing_Throws()
    {
        var parser = OnePositional();
        var ex = Assert.Throws<UsageException>(() => parser.Parse([]));
        Assert.Contains("Missing", ex.Message);
    }

    [Fact]
    public void UnknownFlag_Throws()
    {
        var parser = OnePositional();
        var ex = Assert.Throws<UsageException>(() => parser.Parse(["addr", "--unknown"]));
        Assert.Contains("Unknown flag '--unknown'", ex.Message);
    }

    [Fact]
    public void FlagValueMissing_Throws()
    {
        var parser = OnePositional([new FlagSpec { Name = "timeout" }]);
        var ex = Assert.Throws<UsageException>(() => parser.Parse(["addr", "--timeout"]));
        Assert.Contains("requires a value", ex.Message);
    }

    [Fact]
    public void RepeatableFlag_MultipleValues_AllCollected()
    {
        var parser = ZeroPositionals([new FlagSpec { Name = "cmd", Kind = FlagKind.Repeatable }]);
        var result = parser.Parse(["--cmd", "A", "--cmd", "B", "--cmd", "C"]);
        Assert.Equal(["A", "B", "C"], result.GetRepeatable("cmd"));
    }

    [Fact]
    public void SingleValueFlag_PassedTwice_Throws()
    {
        var parser = OnePositional([new FlagSpec { Name = "timeout" }]);
        var ex = Assert.Throws<UsageException>(() =>
            parser.Parse(["addr", "--timeout", "1000", "--timeout", "2000"]));
        Assert.Contains("more than once", ex.Message);
    }

    [Fact]
    public void ExtraPositional_Throws()
    {
        var parser = OnePositional();
        var ex = Assert.Throws<UsageException>(() => parser.Parse(["addr", "extra"]));
        Assert.Contains("Unexpected argument 'extra'", ex.Message);
    }

    [Fact]
    public void FlagEqualsValueForm_Throws()
    {
        var parser = OnePositional([new FlagSpec { Name = "timeout" }]);
        var ex = Assert.Throws<UsageException>(() => parser.Parse(["addr", "--timeout=5000"]));
        Assert.Contains("'--flag=value' syntax", ex.Message);
    }

    [Fact]
    public void HelpFlag_ShortCircuits()
    {
        var parser = OnePositional();
        var result = parser.Parse(["--help"]);
        Assert.True(result.HelpRequested);
    }

    [Fact]
    public void ValidPositionalAndFlag_ParsesCorrectly()
    {
        var parser = OnePositional([new FlagSpec { Name = "timeout" }]);
        var result = parser.Parse(["GPIB0::5::INSTR", "--timeout", "3000"]);
        Assert.Equal("GPIB0::5::INSTR", result.Positionals[0]);
        Assert.Equal("3000", result.GetFlag("timeout"));
    }

    [Fact]
    public void SwitchFlag_SetWhenPresent_AbsentOtherwise()
    {
        var parser = OnePositional([new FlagSpec { Name = "no-eoi", Kind = FlagKind.Switch }]);
        var withSwitch = parser.Parse(["addr", "--no-eoi"]);
        var withoutSwitch = parser.Parse(["addr"]);
        Assert.True(withSwitch.GetSwitch("no-eoi"));
        Assert.False(withoutSwitch.GetSwitch("no-eoi"));
    }

    [Fact]
    public void TwoPositionals_Parsed()
    {
        var parser = TwoPositionals();
        var result = parser.Parse(["GPIB0", "on"]);
        Assert.Equal("GPIB0", result.Positionals[0]);
        Assert.Equal("on", result.Positionals[1]);
    }

    [Fact]
    public void RequiredFlag_Missing_Throws()
    {
        var parser = OnePositional([new FlagSpec { Name = "file", Required = true }]);
        var ex = Assert.Throws<UsageException>(() => parser.Parse(["addr"]));
        Assert.Contains("Missing required flag '--file'", ex.Message);
    }

    [Fact]
    public void MutuallyExclusiveFlags_BothPresent_Throws()
    {
        var specs = new FlagSpec[]
        {
            new() { Name = "cmd", Kind = FlagKind.Repeatable },
            new() { Name = "file" },
        };
        var parser = new ArgParser(1, specs, mutuallyExclusive: [["cmd", "file"]]);
        var ex = Assert.Throws<UsageException>(() =>
            parser.Parse(["addr", "--cmd", "X", "--file", "f.txt"]));
        Assert.Equal("argparse", ex.Operation);
        Assert.Contains("mutually exclusive", ex.Message);
        Assert.Contains("--cmd", ex.Message);
        Assert.Contains("--file", ex.Message);
    }

    [Fact]
    public void MutuallyExclusiveFlags_OnlyOne_DoesNotThrow()
    {
        var specs = new FlagSpec[]
        {
            new() { Name = "cmd", Kind = FlagKind.Repeatable },
            new() { Name = "file" },
        };
        var parser = new ArgParser(1, specs, mutuallyExclusive: [["cmd", "file"]]);
        var result = parser.Parse(["addr", "--cmd", "X"]);
        Assert.Equal(["X"], result.GetRepeatable("cmd"));
    }
}
