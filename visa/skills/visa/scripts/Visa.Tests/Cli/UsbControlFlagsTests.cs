using Visa.Cli;
using Xunit;

namespace Visa.Tests.Cli;

public class UsbControlFlagsTests
{
    // Signed range edges
    [Theory]
    [InlineData("-32768", -32768)]
    [InlineData("32767",   32767)]
    [InlineData("0",           0)]
    [InlineData("0x1F",       31)]
    [InlineData("0xFF",      255)]
    public void ParseSigned_ValidValues(string input, short expected)
    {
        Assert.Equal(expected, UsbControlFlags.ParseSigned("request-type", input));
    }

    [Theory]
    [InlineData("32768")]   // one above max
    [InlineData("-32769")]  // one below min
    [InlineData("notanumber")]
    [InlineData("0xGG")]
    public void ParseSigned_InvalidValues_Throw(string input)
    {
        Assert.Throws<UsageException>(() => UsbControlFlags.ParseSigned("request-type", input));
    }

    // Unsigned range edges
    [Theory]
    [InlineData("0",     0)]
    [InlineData("65535", 65535)]
    [InlineData("0xFF",  255)]
    [InlineData("0xFFFF", 65535)]
    public void ParseUnsigned_ValidValues(string input, ushort expected)
    {
        Assert.Equal(expected, UsbControlFlags.ParseUnsigned("length", input));
    }

    [Theory]
    [InlineData("-1")]     // negative
    [InlineData("65536")]  // one above max
    [InlineData("notanumber")]
    [InlineData("0xGG")]
    public void ParseUnsigned_InvalidValues_Throw(string input)
    {
        Assert.Throws<UsageException>(() => UsbControlFlags.ParseUnsigned("length", input));
    }

    // Case-insensitive 0x prefix
    [Fact]
    public void ParseSigned_UppercaseHexPrefix_Accepted()
    {
        Assert.Equal(255, UsbControlFlags.ParseSigned("request", "0XFF"));
    }

    [Fact]
    public void ParseUnsigned_UppercaseHexPrefix_Accepted()
    {
        Assert.Equal(255, UsbControlFlags.ParseUnsigned("value", "0XFF"));
    }
}
