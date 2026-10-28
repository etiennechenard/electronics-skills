using Design;
using Xunit;

namespace PcbAsCode.Tests;

/// The model's rules on inline parts, then invariants every board in
/// DesignModel.Boards must keep. The invariants hold for the example and
/// for the project's real design.cs alike; extend them with the project's
/// own (cables whose two ends agree, net maps of off-board parts, ...).
public class DesignTests
{
    [Fact]
    public void Flag_isAVirtualPwrFlagPart()
    {
        var f = DesignModel.Flag(2, "+24V");
        Assert.Equal("#FLG02", f.Ref);
        Assert.Equal("power:PWR_FLAG", f.Lib);
        Assert.Equal("+24V", f.Nets["1"]);
        Assert.False(f.InBom);
        Assert.False(f.OnBoard);
    }

    [Fact]
    public void ExpectedPins_skipsPowerFlags()
    {
        var pins = DesignModel.ExpectedPins([DesignModel.Flag(1, "GND"), DesignModel.Flag(2, "+5V")]);
        Assert.Empty(pins);
    }

    [Fact]
    public void ExpectedPins_skipsOffBoardParts()
    {
        var off = DesignModel.Make("SW1", "proj:Switch", "SP6T", "",
            new Dictionary<string, string> { ["C"] = "COIL1", ["1"] = "C0" }, onBoard: false);
        var on = DesignModel.Make("R1", "Device:R", "1k", null,
            new Dictionary<string, string> { ["1"] = "A", ["2"] = "B" });
        var pins = DesignModel.ExpectedPins([off, on]);
        Assert.Equal([("R1", "1"), ("R1", "2")], pins.Keys);
        Assert.Equal("A", pins[("R1", "1")]);
    }

    [Fact]
    public void Board_ExtraBom_rowsRoundTrip()
    {
        var rows = new[] { new[] { "H1", "1", "1x20 female header 2.54 mm", "", "socket strip", "" } };
        var board = new Board("test board", () => [], rows);
        Assert.Equal(rows, board.ExtraBom);
    }

    // ---- invariants of the design itself ---------------------------------

    public static TheoryData<string> Boards => new(DesignModel.Boards.Keys);

    [Theory]
    [MemberData(nameof(Boards))]
    public void EveryBoard_hasPinsAndUniqueRefs(string board)
    {
        var parts = DesignModel.Boards[board].Parts();
        Assert.NotEmpty(DesignModel.ExpectedPins(board));
        Assert.Empty(parts.GroupBy(c => c.Ref).Where(g => g.Count() > 1).Select(g => g.Key));
    }

    [Theory]
    [MemberData(nameof(Boards))]
    public void EveryOnBoardPart_hasAFootprintAndNamedNets(string board)
    {
        foreach (var c in DesignModel.Boards[board].Parts().Where(c => c.OnBoard && !c.Ref.StartsWith('#')))
        {
            Assert.True(c.Fp != "", $"{board} {c.Ref}: on-board part without a footprint");     // null: the symbol's own
            Assert.All(c.Nets, n => Assert.False(string.IsNullOrWhiteSpace(n.Value), $"{board} {c.Ref} pin {n.Key}: empty net"));
        }
    }

    [Theory]
    [MemberData(nameof(Boards))]
    public void EveryPowerFlag_landsOnARealPin(string board)
    {
        var flagNets = DesignModel.Boards[board].Parts()
            .Where(c => c.Ref.StartsWith("#FLG")).SelectMany(c => c.Nets.Values).ToHashSet();
        Assert.Subset(DesignModel.ExpectedPins(board).Values.ToHashSet(), flagNets);
    }

    /// Nets meant to have one pin (a lone test pad): list them here.
    static readonly HashSet<string> SinglePinNets = [];

    [Fact]
    public void EveryNet_reachesTwoPins()
    {
        // across the whole unit (a ribbon net may have one pin per board),
        // off-board parts included (a connector pin wired to a switch): a net
        // on one pin is a typo or a forgotten connection
        var lone = DesignModel.Boards
            .SelectMany(b => b.Value.Parts().Where(c => !c.Ref.StartsWith('#'))
                .SelectMany(c => c.Nets.Select(n => (Board: b.Key, c.Ref, Pin: n.Key, Net: n.Value))))
            .GroupBy(p => p.Net)
            .Where(g => g.Count() < 2 && !SinglePinNets.Contains(g.Key))
            .Select(g => $"{g.Key} ({g.First().Board} {g.First().Ref}.{g.First().Pin})");
        Assert.Empty(lone);
    }
}
