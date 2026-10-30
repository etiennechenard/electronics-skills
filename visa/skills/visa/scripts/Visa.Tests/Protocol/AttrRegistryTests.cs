using Visa.Core.Protocol;
using Xunit;

namespace Visa.Tests.Protocol;

public class AttrRegistryTests
{
    // Nine known names from TechDesignCliContract §6 — assert exact id and type
    [Theory]
    [InlineData("VI_ATTR_TMO_VALUE",    0x3FFF001Au, AttrType.U32)]
    [InlineData("VI_ATTR_TERMCHAR",     0x3FFF0018u, AttrType.U8)]
    [InlineData("VI_ATTR_TERMCHAR_EN",  0x3FFF0038u, AttrType.Boolean)]
    [InlineData("VI_ATTR_SEND_END_EN",  0x3FFF0016u, AttrType.Boolean)]
    [InlineData("VI_ATTR_ASRL_BAUD",       0x3FFF0021u, AttrType.U32)]
    [InlineData("VI_ATTR_ASRL_DATA_BITS",  0x3FFF0022u, AttrType.U16)]
    [InlineData("VI_ATTR_ASRL_PARITY",     0x3FFF0023u, AttrType.U16)]
    [InlineData("VI_ATTR_ASRL_STOP_BITS",  0x3FFF0024u, AttrType.U16)]
    [InlineData("VI_ATTR_ASRL_FLOW_CNTRL", 0x3FFF0025u, AttrType.U16)]
    public void KnownName_resolves_to_exact_id_and_type(string name, uint expectedId, AttrType expectedType)
    {
        var entry = AttrRegistry.Resolve(name);
        Assert.Equal(expectedId, entry.Id);
        Assert.Equal(expectedType, entry.Type);
    }

    // Known name with matching typeOverride is allowed
    [Fact]
    public void KnownName_with_matching_typeOverride_succeeds()
    {
        var entry = AttrRegistry.Resolve("VI_ATTR_TMO_VALUE", AttrType.U32);
        Assert.Equal(0x3FFF001Au, entry.Id);
        Assert.Equal(AttrType.U32, entry.Type);
    }

    // Known name with conflicting typeOverride is an error
    [Fact]
    public void KnownName_conflicting_typeOverride_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AttrRegistry.Resolve("VI_ATTR_TMO_VALUE", AttrType.U16));
    }

    // Raw hex without typeOverride defaults to U32
    [Fact]
    public void RawHex_no_override_defaults_to_U32()
    {
        var entry = AttrRegistry.Resolve("0x3FFF0001");
        Assert.Equal(0x3FFF0001u, entry.Id);
        Assert.Equal(AttrType.U32, entry.Type);
    }

    // Raw hex with typeOverride uses the override type
    [Fact]
    public void RawHex_with_override_uses_override()
    {
        var entry = AttrRegistry.Resolve("0x3FFF0001", AttrType.U8);
        Assert.Equal(0x3FFF0001u, entry.Id);
        Assert.Equal(AttrType.U8, entry.Type);
    }

    // Raw hex is case-insensitive (0X prefix also OK)
    [Fact]
    public void RawHex_uppercase_prefix_accepted()
    {
        var entry = AttrRegistry.Resolve("0X3FFF0001");
        Assert.Equal(0x3FFF0001u, entry.Id);
        Assert.Equal(AttrType.U32, entry.Type);
    }

    // Unknown token (not a known name, not a raw hex) is rejected
    [Theory]
    [InlineData("UNKNOWN_ATTR")]
    [InlineData("VI_ATTR_NONEXISTENT")]
    [InlineData("1234")]          // decimal, not 0x-prefixed hex
    [InlineData("")]
    public void Unknown_token_throws(string token)
    {
        Assert.Throws<ArgumentException>(() => AttrRegistry.Resolve(token));
    }
}
