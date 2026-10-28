using Visa.Cli;
using Xunit;

namespace Visa.Tests.Cli;

public class TermCharTests
{
    [Theory]
    [InlineData("NL",  10)]
    [InlineData("CR",  13)]
    [InlineData("10",  10)]
    [InlineData("255", 255)]
    [InlineData("0",   0)]
    public void ValidInputs_ParseCorrectly(string input, byte expected)
    {
        Assert.Equal(expected, TermChar.Parse(input));
    }

    [Theory]
    [InlineData("256")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("nl")]
    [InlineData("Nl")]
    [InlineData("cr")]
    [InlineData("NL ")]
    [InlineData("")]
    public void InvalidInputs_ThrowUsageException(string input)
    {
        Assert.Throws<UsageException>(() => TermChar.Parse(input));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void ParseEnable_ValidInputs_ParseCorrectly(string input, bool expected)
    {
        Assert.Equal(expected, TermChar.ParseEnable(input));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("true")]
    [InlineData("on")]
    [InlineData("")]
    public void ParseEnable_InvalidInputs_ThrowUsageException(string input)
    {
        Assert.Throws<UsageException>(() => TermChar.ParseEnable(input));
    }
}
