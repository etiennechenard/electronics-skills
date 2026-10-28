using Visa.Cli;
using Xunit;

namespace Visa.Tests.Cli;

public class BoardIdTests
{
    [Theory]
    [InlineData("GPIB0",         "GPIB0",         "GPIB0::INTFC")]
    [InlineData("GPIB12",        "GPIB12",        "GPIB12::INTFC")]
    [InlineData("GPIB0::INTFC",  "GPIB0::INTFC",  "GPIB0::INTFC")]
    [InlineData("GPIB99::INTFC", "GPIB99::INTFC", "GPIB99::INTFC")]
    public void ValidInputs_Normalize(string input, string expectedBoard, string expectedResource)
    {
        var (board, resource) = BoardId.Normalize(input);
        Assert.Equal(expectedBoard, board);
        Assert.Equal(expectedResource, resource);
    }

    [Theory]
    [InlineData("GPIB0::5::INSTR")]
    [InlineData("USB0::0x0957::INSTR")]
    [InlineData("gpib0")]
    [InlineData("Gpib0")]
    [InlineData("GPIB")]
    [InlineData("0")]
    [InlineData("GPIB0::intfc")]
    public void InvalidInputs_ThrowUsageException(string input)
    {
        var ex = Assert.Throws<UsageException>(() => BoardId.Normalize(input));
        Assert.Contains($"got '{input}'", ex.Message);
    }

    [Fact]
    public void EmptyString_ThrowsUsageException()
    {
        var ex = Assert.Throws<UsageException>(() => BoardId.Normalize(""));
        Assert.Contains("got ''", ex.Message);
    }
}
