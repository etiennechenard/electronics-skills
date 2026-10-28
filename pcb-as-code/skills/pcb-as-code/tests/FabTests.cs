using Fab;
using Xunit;

namespace PcbAsCode.Tests;

public class FabTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pcbascode-fab-" + Guid.NewGuid().ToString("N")[..8]);
    private static readonly DateOnly Today = new(2026, 10, 26);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private FabRules Fab(DateOnly? checkedOn = null, string? drop = null, string trackWidth = "0.2",
        Dictionary<string, string>? set = null) =>
        FabRules.Load(TestBoards.Temp(_dir, "fab.json", TestBoards.FabJson(checkedOn ?? Today, drop, trackWidth, set)));

    [Fact]
    public void Load_readsValuesQuotesAndUnpublishedLimits()
    {
        var f = Fab();
        Assert.Equal("TestFab", f.Fab);
        Assert.Equal(Today, f.Checked);
        Assert.Equal(0.45, f["via_diameter"]);
        Assert.Null(f["npth_hole"]);
        Assert.Equal("not published", f.Rules["npth_hole"].Quote);
        Assert.Empty(f.Problems(Today));
    }

    [Fact]
    public void Problems_catchAMissingKey() =>
        Assert.Contains(Fab(drop: "hole_to_hole").Problems(Today), p => p.Contains("missing \"hole_to_hole\""));

    [Fact]
    public void Problems_catchAStaleCheck()
    {
        var p = Assert.Single(Fab(Today.AddDays(-91)).Problems(Today));
        Assert.Contains("re-fetch https://example.com/capabilities", p);
        Assert.Empty(Fab(Today.AddDays(-90)).Problems(Today));
    }

    [Fact]
    public void Problems_catchANonPositiveValue() =>
        Assert.Contains(Fab(trackWidth: "0").Problems(Today), p => p.Contains("\"track_width\" is 0 mm"));

    [Fact]
    public void Problems_catchAValueThatIsNotANumber()
    {
        // a string is not "unpublished": the limit would go unchecked
        var f = Fab(trackWidth: "\"0.13\"");
        Assert.Null(f["track_width"]);
        Assert.Contains(f.Problems(Today), p => p.Contains("\"track_width\": \"mm\" is \"0.13\", not a number or null"));
    }

    [Fact]
    public void Problems_catchANumberOutOfRange() =>
        Assert.Contains(Fab(trackWidth: "1e400").Problems(Today), p => p.Contains("\"track_width\": \"mm\" is 1e400"));

    private FabRules FabText(Func<string, string> edit) =>
        FabRules.Load(TestBoards.Temp(_dir, "fab.json", edit(TestBoards.FabJson(Today))));

    [Theory]
    [InlineData("\"TestFab page: via_hole 0.2\"", "\"\"", "\"via_hole\" has no quote from the page")]
    [InlineData("\"TestFab page: via_hole 0.2\"", "\"<the page's line, verbatim>\"", "\"via_hole\" quote is a placeholder")]
    [InlineData("https://example.com/capabilities", "example.com", "\"source\" is not the capability page's URL")]
    [InlineData("\"fab\": \"TestFab\"", "\"fab\": 7", "no \"fab\"")]
    [InlineData("\"checked\": \"2026-10-26\"", "\"checked\": \"26/10/2026\"", "\"checked\" is not a yyyy-mm-dd date")]
    [InlineData("{ \"mm\": 0.1, \"quote\": \"TestFab page: mask_bridge 0.1\" }", "0.1", "\"mask_bridge\": 0.1 is not an object")]
    public void Problems_catchABadlyWrittenFile(string from, string to, string problem)
    {
        var f = FabText(t => { Assert.Contains(from, t); return t.Replace(from, to); });
        Assert.Contains(f.Problems(Today), p => p.Contains(problem));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    public void Load_refusesATopLevelThatIsNotAnObject(string text) =>
        Assert.Throws<System.Text.Json.JsonException>(() => FabRules.Load(TestBoards.Temp(_dir, "fab.json", text)));

    [Theory]
    [InlineData("(gr_line (start 101 101) (end 103 101) (stroke (width 0) (type default)) (layer \"F.SilkS\") (uuid \"00000000-0000-0000-0000-000000000050\"))", "gr_line width")]
    [InlineData("(gr_text_box \"T\" (start 101 101) (end 110 104) (layer \"F.SilkS\") (uuid \"00000000-0000-0000-0000-000000000050\") (effects (font (size 0.5 0.5) (thickness 0.15))) (border no) (stroke (width 0.2) (type default)))", "text height 0.5")]
    public void Board_warnsOnAZeroWidthSilkLineAndASmallTextBox(string item, string text)
    {
        var pcb = TestBoards.Pcb.Replace("  (segment (start 104 105)", "  " + item + "\n  (segment (start 104 105)");
        var w = Assert.Single(FabCheck.Board(TestBoards.Load(pcb), Fab()), i => i.Where == "board" && i.Text.StartsWith(text));
        Assert.False(w.Error);
    }

    [Fact]
    public void Problems_refuseANullForALimitEveryFabPublishes() =>
        Assert.Contains(Fab(trackWidth: "null").Problems(Today), p => p.Contains("\"track_width\" is null, but every fab publishes it"));

    [Fact]
    public void Project_checksEverySetupRuleAndClassValue()
    {
        var pro = TestBoards.Temp(_dir, "t.kicad_pro", TestBoards.Pro);
        var strict = Fab(trackWidth: "0.6", set: new()
        {
            ["track_spacing"] = "0.3", ["via_hole"] = "0.5", ["via_diameter"] = "1.0", ["via_annular_ring"] = "0.3",
            ["pth_hole"] = "0.5", ["copper_to_edge"] = "0.4", ["silk_text_height"] = "1.2", ["silk_line_width"] = "0.2",
            ["mask_bridge"] = "0.2",
        });
        var issues = FabCheck.Project(pro, strict);
        Assert.Equal(["copper_to_edge", "hole_to_hole", "mask_bridge", "silk_line_width", "silk_text_height", "track_spacing",
                "track_width", "via_annular_ring", "via_diameter", "via_hole"],
            issues.Where(i => i.Where.EndsWith("board setup")).Select(i => i.Rule).Order());
        foreach (var cls in new[] { "Default", "Power" })           // Power's ring is (0.8 - 0.4) / 2 = 0.2
            Assert.Equal(["track_spacing", "track_width", "via_annular_ring", "via_diameter", "via_hole"],
                issues.Where(i => i.Where.EndsWith("net class " + cls)).Select(i => i.Rule).Order());
    }

    [Fact]
    public void Board_warnsOnAThinSilkscreenLine()
    {
        var pcb = TestBoards.Pcb.Replace("  (segment (start 104 105)",
            "  (gr_line (start 101 101) (end 103 101) (stroke (width 0.1) (type default)) (layer \"F.SilkS\") (uuid \"00000000-0000-0000-0000-000000000050\"))\n  (segment (start 104 105)");
        var w = Assert.Single(FabCheck.Board(TestBoards.Load(pcb), Fab()), i => i.Text.StartsWith("gr_line width"));
        Assert.False(w.Error);
        Assert.Equal("silk_line_width", w.Rule);
    }

    [Fact]
    public void Problems_catchACheckDateInTheFuture() =>
        Assert.Contains(Fab(Today.AddDays(1)).Problems(Today), p => p.Contains("in the future"));

    [Fact]
    public void Project_holdsTheBoardSetupAndNetClassesToTheFab()
    {
        var pro = TestBoards.Temp(_dir, "t.kicad_pro", TestBoards.Pro);
        var issue = Assert.Single(FabCheck.Project(pro, Fab()));
        Assert.Equal("hole_to_hole", issue.Rule);           // min_hole_to_hole 0.25 < 0.5
        Assert.True(issue.Error);

        // a fab with 0.3 mm tracks: the board setup (0.2) and Default (0.25) fall below, Power (0.5) does not
        var thin = FabCheck.Project(pro, Fab(trackWidth: "0.3")).Where(i => i.Rule == "track_width").ToList();
        Assert.Equal(2, thin.Count);
        Assert.Contains(thin, i => i.Where.EndsWith("net class Default"));
    }

    [Fact]
    public void Board_checksTheGeometryAsBuilt()
    {
        var issues = FabCheck.Board(TestBoards.Load(), Fab());
        var error = Assert.Single(issues, i => i.Error);
        Assert.Equal("hole_to_hole", error.Rule);           // the via 0.35 mm from J1's hole
        Assert.Contains("via B at (106.9, 105) / J1 pad 2", error.Where);
        // the legend is 0.8 mm / 0.1 mm stroke; J1's hidden reference is not checked
        Assert.Equal(["silk_line_width", "silk_text_height"], issues.Where(i => !i.Error).Select(i => i.Rule).Order());
    }

    [Fact]
    public void Board_catchesTracksBelowTheFabsWidth()
    {
        var issues = FabCheck.Board(TestBoards.Load(), Fab(trackWidth: "0.3"));
        Assert.Equal(3, issues.Count(i => i.Rule == "track_width"));
    }

    [Fact]
    public void Board_catchesViaAndPadDrillsAndRings()
    {
        // the via is 0.6/0.3 (ring 0.15), J1's hole 1.6/0.8 (ring 0.4)
        var fab = Fab(set: new()
        {
            ["via_hole"] = "0.35", ["via_diameter"] = "0.7", ["via_annular_ring"] = "0.2",
            ["pth_hole"] = "1.0", ["pth_annular_ring"] = "0.5",
        });
        var rules = FabCheck.Board(TestBoards.Load(), fab).Where(i => i.Error).Select(i => i.Rule).Order();
        Assert.Equal(["hole_to_hole", "pth_annular_ring", "pth_hole", "via_annular_ring", "via_diameter", "via_hole"], rules);
    }

    [Fact]
    public void Board_usesThePadToPadLimitBetweenPlatedPadsAndChecksNpth()
    {
        // J3: a plated hole 3 mm from J1's (gap 2.2 mm); H1: a 0.4 mm NPTH far from both
        var pcb = TestBoards.Pcb.Replace("  (segment (start 104 105)", """
              (footprint "T:Hole" (layer "F.Cu") (uuid "00000000-0000-0000-0000-000000000010") (at 103 105)
                (property "Reference" "J3" (at 0 0 0) (layer "F.SilkS") (hide yes) (uuid "00000000-0000-0000-0000-000000000011") (effects (font (size 1 1) (thickness 0.15))))
                (pad "1" thru_hole circle (at 0 0) (size 1.6 1.6) (drill 0.8) (layers "*.Cu" "*.Mask") (net "C") (uuid "00000000-0000-0000-0000-000000000012")))
              (footprint "T:Npth" (layer "F.Cu") (uuid "00000000-0000-0000-0000-000000000013") (at 118 108)
                (property "Reference" "H1" (at 0 0 0) (layer "F.SilkS") (hide yes) (uuid "00000000-0000-0000-0000-000000000014") (effects (font (size 1 1) (thickness 0.15))))
                (pad "" np_thru_hole circle (at 0 0) (size 0.4 0.4) (drill 0.4) (layers "*.Mask") (uuid "00000000-0000-0000-0000-000000000015")))
              (segment (start 104 105)
            """.TrimEnd());
        var issues = FabCheck.Board(TestBoards.Load(pcb), Fab(set: new() { ["pad_hole_to_pad_hole"] = "2.5", ["npth_hole"] = "0.5" }));
        var pads = Assert.Single(issues, i => i.Rule == "pad_hole_to_pad_hole");
        Assert.Contains("J1 pad 2 / J3 pad 1", pads.Where);
        Assert.Contains(issues, i => i.Rule == "npth_hole" && i.Where == "H1 pad ");
    }
}
