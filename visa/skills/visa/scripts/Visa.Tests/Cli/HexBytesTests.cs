using Visa.Cli;
using Xunit;

namespace Visa.Tests.Cli;

public class HexBytesTests
{
    [Fact]
    public void SpaceSeparated_ParsesCorrectly()
    {
        var result = HexBytes.Parse("40 41 3F");
        Assert.Equal([0x40, 0x41, 0x3F], result);
    }

    [Fact]
    public void NoSpaceLowercase_ParsesCorrectly()
    {
        var result = HexBytes.Parse("40413f");
        Assert.Equal([0x40, 0x41, 0x3F], result);
    }

    [Fact]
    public void NoSpaceMixedCase_ParsesCorrectly()
    {
        var result = HexBytes.Parse("40413F");
        Assert.Equal([0x40, 0x41, 0x3F], result);
    }

    [Fact]
    public void OddDigitCount_ThrowsUsageException()
    {
        Assert.Throws<UsageException>(() => HexBytes.Parse("404"));
    }

    [Fact]
    public void NonHexChar_ThrowsUsageException()
    {
        Assert.Throws<UsageException>(() => HexBytes.Parse("40GH"));
    }

    [Fact]
    public void EmptyString_ThrowsUsageException()
    {
        Assert.Throws<UsageException>(() => HexBytes.Parse(""));
    }

    [Fact]
    public void RoundTrip_FormatParseNormalized()
    {
        var bytes = HexBytes.Parse("40 41 3F");
        var formatted = HexBytes.Format(bytes);
        Assert.Equal("40413f", formatted);
    }

    [Fact]
    public void Format_ProducesLowercaseNoSpaces()
    {
        var bytes = new byte[] { 0xAB, 0xCD, 0xEF };
        Assert.Equal("abcdef", HexBytes.Format(bytes));
    }
}
