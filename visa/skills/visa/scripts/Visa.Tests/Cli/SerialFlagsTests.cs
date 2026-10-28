using Visa.Cli;
using Xunit;

namespace Visa.Tests.Cli;

public class SerialFlagsTests
{
    private static ParsedArgs Parse(string[] args)
    {
        var parser = new ArgParser(0, SerialFlags.Specs);
        return parser.Parse(args);
    }

    [Fact]
    public void AllFlagsAbsent_YieldsEmptyConfig()
    {
        var config = SerialFlags.Extract(Parse([]));
        Assert.Empty(config.GetSetAttributes());
    }

    [Fact]
    public void Baud_Parsed()
    {
        var config = SerialFlags.Extract(Parse(["--baud", "9600"]));
        var attrs = config.GetSetAttributes().ToList();
        Assert.Single(attrs);
        Assert.Equal(0x3FFF0021u, attrs[0].AttrId);
        Assert.Equal(9600u, attrs[0].EncodedValue);
    }

    [Fact]
    public void DataBits_Parsed()
    {
        var config = SerialFlags.Extract(Parse(["--data-bits", "8"]));
        var attrs = config.GetSetAttributes().ToList();
        Assert.Single(attrs);
        Assert.Equal(0x3FFF0022u, attrs[0].AttrId);
        Assert.Equal(8u, attrs[0].EncodedValue);
    }

    [Fact]
    public void Parity_Parsed()
    {
        var config = SerialFlags.Extract(Parse(["--parity", "even"]));
        var attrs = config.GetSetAttributes().ToList();
        Assert.Single(attrs);
        Assert.Equal(0x3FFF0023u, attrs[0].AttrId);
        Assert.Equal(2u, attrs[0].EncodedValue);
    }

    [Fact]
    public void StopBits_Parsed()
    {
        var config = SerialFlags.Extract(Parse(["--stop-bits", "1.5"]));
        var attrs = config.GetSetAttributes().ToList();
        Assert.Single(attrs);
        Assert.Equal(0x3FFF0024u, attrs[0].AttrId);
        Assert.Equal(15u, attrs[0].EncodedValue);
    }

    [Fact]
    public void FlowControl_Parsed()
    {
        var config = SerialFlags.Extract(Parse(["--flow-control", "rts"]));
        var attrs = config.GetSetAttributes().ToList();
        Assert.Single(attrs);
        Assert.Equal(0x3FFF0025u, attrs[0].AttrId);
        Assert.Equal(2u, attrs[0].EncodedValue);
    }

    [Fact]
    public void InvalidBaud_ThrowsUsageException()
    {
        Assert.Throws<UsageException>(() => SerialFlags.Extract(Parse(["--baud", "notanumber"])));
    }

    [Fact]
    public void ZeroBaud_ThrowsUsageException()
    {
        Assert.Throws<UsageException>(() => SerialFlags.Extract(Parse(["--baud", "0"])));
    }

    [Fact]
    public void InvalidDataBits_ThrowsUsageException()
    {
        Assert.Throws<UsageException>(() => SerialFlags.Extract(Parse(["--data-bits", "9"])));
    }

    [Fact]
    public void InvalidParity_ThrowsUsageException()
    {
        Assert.Throws<UsageException>(() => SerialFlags.Extract(Parse(["--parity", "Even"])));
    }

    [Fact]
    public void InvalidStopBits_ThrowsUsageException()
    {
        Assert.Throws<UsageException>(() => SerialFlags.Extract(Parse(["--stop-bits", "3"])));
    }

    [Fact]
    public void InvalidFlowControl_ThrowsUsageException()
    {
        Assert.Throws<UsageException>(() => SerialFlags.Extract(Parse(["--flow-control", "RTS"])));
    }
}
