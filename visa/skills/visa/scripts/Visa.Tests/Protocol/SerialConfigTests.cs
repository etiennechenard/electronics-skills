using Visa.Core.Protocol;
using Xunit;

namespace Visa.Tests.Protocol;

// Attribute IDs from TechDesignCliContract.md §6
file static class AsrlAttr
{
    public const uint Baud       = 0x3FFF0021;
    public const uint DataBits   = 0x3FFF0022;
    public const uint Parity     = 0x3FFF0023;
    public const uint StopBits   = 0x3FFF0024;
    public const uint FlowCntrl  = 0x3FFF0025;
}

public class SerialConfigTests
{
    // Baud — set and enumerate
    [Fact]
    public void SetBaud_stores_correct_attrId_and_value()
    {
        var cfg = new SerialConfig();
        cfg.SetBaud(9600);
        var attrs = cfg.GetSetAttributes().ToList();
        Assert.Single(attrs);
        Assert.Equal(AsrlAttr.Baud, attrs[0].AttrId);
        Assert.Equal(9600u, attrs[0].EncodedValue);
    }

    // DataBits — each valid value
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void SetDataBits_valid(byte bits)
    {
        var cfg = new SerialConfig();
        cfg.SetDataBits(bits);
        var attrs = cfg.GetSetAttributes().ToList();
        Assert.Single(attrs);
        Assert.Equal(AsrlAttr.DataBits, attrs[0].AttrId);
        Assert.Equal((uint)bits, attrs[0].EncodedValue);
    }

    // Parity — all five encodings
    [Theory]
    [InlineData("none",  0u)]
    [InlineData("odd",   1u)]
    [InlineData("even",  2u)]
    [InlineData("mark",  3u)]
    [InlineData("space", 4u)]
    public void SetParity_encodings(string spelling, uint expected)
    {
        var cfg = new SerialConfig();
        cfg.SetParity(spelling);
        var attrs = cfg.GetSetAttributes().ToList();
        Assert.Single(attrs);
        Assert.Equal(AsrlAttr.Parity, attrs[0].AttrId);
        Assert.Equal(expected, attrs[0].EncodedValue);
    }

    // StopBits — all three encodings
    [Theory]
    [InlineData("1",   10u)]
    [InlineData("1.5", 15u)]
    [InlineData("2",   20u)]
    public void SetStopBits_encodings(string spelling, uint expected)
    {
        var cfg = new SerialConfig();
        cfg.SetStopBits(spelling);
        var attrs = cfg.GetSetAttributes().ToList();
        Assert.Single(attrs);
        Assert.Equal(AsrlAttr.StopBits, attrs[0].AttrId);
        Assert.Equal(expected, attrs[0].EncodedValue);
    }

    // FlowControl — all four encodings
    [Theory]
    [InlineData("none", 0u)]
    [InlineData("xon",  1u)]
    [InlineData("rts",  2u)]
    [InlineData("dtr",  4u)]
    public void SetFlowControl_encodings(string spelling, uint expected)
    {
        var cfg = new SerialConfig();
        cfg.SetFlowControl(spelling);
        var attrs = cfg.GetSetAttributes().ToList();
        Assert.Single(attrs);
        Assert.Equal(AsrlAttr.FlowCntrl, attrs[0].AttrId);
        Assert.Equal(expected, attrs[0].EncodedValue);
    }

    // Unset params are not written — empty config yields no attributes
    [Fact]
    public void Empty_config_yields_no_attributes()
    {
        var cfg = new SerialConfig();
        Assert.Empty(cfg.GetSetAttributes());
    }

    // Partial config yields only set params
    [Fact]
    public void Partial_config_yields_only_set_params()
    {
        var cfg = new SerialConfig();
        cfg.SetBaud(115200);
        cfg.SetParity("odd");
        var attrs = cfg.GetSetAttributes().ToList();
        Assert.Equal(2, attrs.Count);
        Assert.Contains(attrs, a => a.AttrId == AsrlAttr.Baud);
        Assert.Contains(attrs, a => a.AttrId == AsrlAttr.Parity);
    }

    // Validation — zero baud rejected
    [Fact]
    public void SetBaud_zero_throws()
    {
        var cfg = new SerialConfig();
        Assert.Throws<ArgumentException>(() => cfg.SetBaud(0));
    }

    // Validation — data-bits out of range
    [Theory]
    [InlineData(4)]
    [InlineData(9)]
    public void SetDataBits_out_of_range_throws(byte bits)
    {
        var cfg = new SerialConfig();
        Assert.Throws<ArgumentException>(() => cfg.SetDataBits(bits));
    }

    // Validation — unknown parity spelling rejected
    [Theory]
    [InlineData("Even")]    // wrong case
    [InlineData("NONE")]    // wrong case
    [InlineData("unknown")] // unknown
    public void SetParity_invalid_throws(string spelling)
    {
        var cfg = new SerialConfig();
        Assert.Throws<ArgumentException>(() => cfg.SetParity(spelling));
    }

    // Validation — unknown stop-bits spelling rejected
    [Theory]
    [InlineData("1.0")]   // not a valid spelling
    [InlineData("2.0")]   // not a valid spelling
    [InlineData("Three")] // unknown
    public void SetStopBits_invalid_throws(string spelling)
    {
        var cfg = new SerialConfig();
        Assert.Throws<ArgumentException>(() => cfg.SetStopBits(spelling));
    }

    // Validation — unknown flow-control spelling rejected (wrong case or unknown)
    [Theory]
    [InlineData("XON")]     // wrong case
    [InlineData("DTR")]     // wrong case
    [InlineData("handshake")] // unknown
    public void SetFlowControl_invalid_throws(string spelling)
    {
        var cfg = new SerialConfig();
        Assert.Throws<ArgumentException>(() => cfg.SetFlowControl(spelling));
    }
}
