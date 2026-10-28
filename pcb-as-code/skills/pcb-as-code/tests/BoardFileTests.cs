using PcbFile;
using Specctra;
using Xunit;

namespace PcbAsCode.Tests;

/// The board read as text (boardfile.cs) and the DSN written from it (dsn.cs).
public class BoardFileTests
{
    static string Dsn(Board b) =>
        DsnWriter.Write(b, [new("Default", 250_000, 200_000, 600_000, 300_000, [])], 250_000, "t.dsn");

    [Fact]
    public void Pads_areInAbsoluteCoordinates()
    {
        var b = TestBoards.Load();
        var j2 = b.Footprints.Single(f => f.Ref == "J2");
        Assert.True(j2.Back);
        Assert.Equal((115_000_000L, 105_000_000L), j2.Pads[0].Position);
        Assert.Equal((106_000_000L, 105_000_000L), b.Footprints[0].Pads[1].Position);
        Assert.Equal((800_000L, 800_000L), b.Footprints[0].Pads[1].Drill);
    }

    [Fact]
    public void BackPads_mapBackToTheLibraryFrame()
    {
        // J2 (back, orientation -90): pad 2 is stored at (1, -0.5) angle -180,
        // which is library (1, 0.5) at 90 degrees
        var pad = TestBoards.Load().Footprints.Single(f => f.Ref == "J2").Pads[1];
        Assert.Equal((1_000_000L, 500_000L), pad.LibLocal);
        Assert.Equal(90, pad.LibAngle);
        Assert.Equal((115_500_000L, 106_000_000L), pad.Position);
    }

    [Theory]
    [InlineData(1000, 0, 90, 0, -1000)]          // KiCad angles turn counter-clockwise on screen (y down)
    [InlineData(1000, 0, 180, -1000, 0)]
    [InlineData(0, 1000, 90, 1000, 0)]
    [InlineData(1000, 0, 45, 707, -707)]         // the sin/cos path, same sense
    public void Rotate_followsKicadsSense(long x, long y, double deg, long ex, long ey) =>
        Assert.Equal((ex, ey), Nm.Rotate(x, y, deg));

    [Fact]
    public void RoutedCopper_isTheUnlockedTracksAndVias()
    {
        var b = TestBoards.Load();
        var routed = b.RoutedCopper(drop: true);
        Assert.Equal(2, routed.Count);
        Assert.Equal(2, b.Items("segment").Count());
        Assert.Empty(b.Items("via"));
    }

    [Fact]
    public void EdgeContours_closeTheOutline()
    {
        var c = Assert.Single(TestBoards.Load().EdgeContours());
        Assert.Equal(4, c.Pts.Count);
        Assert.Equal(200e12, Board.Area(c.Pts), 3);
    }

    [Fact]
    public void EdgeContours_chainLinesAndArcsInAnyOrderAndDirection()
    {
        // a D: three lines (one drawn backwards) and a half circle of radius 5 mm
        var b = TestBoards.Load(TestBoards.Pcb.Replace(
            "(gr_rect (start 100 100) (end 120 110) (stroke (width 0.1) (type default)) (layer \"Edge.Cuts\") (uuid \"00000000-0000-0000-0000-000000000001\"))",
            """
            (gr_line (start 110 110) (end 100 110) (stroke (width 0.1) (type default)) (layer "Edge.Cuts") (uuid "00000000-0000-0000-0000-000000000061"))
              (gr_arc (start 110 100) (mid 115 105) (end 110 110) (stroke (width 0.1) (type default)) (layer "Edge.Cuts") (uuid "00000000-0000-0000-0000-000000000062"))
              (gr_line (start 100 100) (end 110 100) (stroke (width 0.1) (type default)) (layer "Edge.Cuts") (uuid "00000000-0000-0000-0000-000000000063"))
              (gr_line (start 100 100) (end 100 110) (stroke (width 0.1) (type default)) (layer "Edge.Cuts") (uuid "00000000-0000-0000-0000-000000000064"))
            """));
        var c = Assert.Single(b.EdgeContours());
        Assert.InRange(Board.Area(c.Pts), 139.2e12, 139.5e12);      // 100 + 12.5 pi mm^2, arc on r + error/2
        var (l, t, r, bot) = b.EdgeBox();
        Assert.Equal((99_950_000L, 99_950_000L, 110_050_000L), (l, t, bot));
        Assert.InRange(r, 115_050_000, 115_053_000);             // the arc's points, plus half the stroke
    }

    [Fact]
    public void EdgeContours_joinAnOpenChainListedFromItsMiddle()
    {
        // an open outline A-B-C listed as B-C then A-B: one contour, not two
        var b = TestBoards.Load(TestBoards.Pcb.Replace(
            "(gr_rect (start 100 100) (end 120 110) (stroke (width 0.1) (type default)) (layer \"Edge.Cuts\") (uuid \"00000000-0000-0000-0000-000000000001\"))",
            """
            (gr_line (start 120 100) (end 120 110) (stroke (width 0.1) (type default)) (layer "Edge.Cuts") (uuid "00000000-0000-0000-0000-000000000061"))
              (gr_line (start 100 100) (end 120 100) (stroke (width 0.1) (type default)) (layer "Edge.Cuts") (uuid "00000000-0000-0000-0000-000000000062"))
            """));
        var c = Assert.Single(b.EdgeContours());
        Assert.Equal([(100_000_000L, 100_000_000L), (120_000_000L, 100_000_000L), (120_000_000L, 110_000_000L)], c.Pts);
    }

    [Theory]
    [InlineData("")]
    [InlineData("(kicad_pcb (version 20260206) (footprint \"R\"")]
    [InlineData("(kicad_sch (version 20250114))")]
    public void Load_refusesAFileThatIsNotAWholeBoard(string text) =>
        Assert.ThrowsAny<Exception>(() => TestBoards.Load(text));

    [Fact]
    public void CirclePoints_straddleTheCircle()
    {
        var pts = Board.CirclePoints((0, 0), 1_000_000, 5000);
        Assert.True(pts.Count >= 8);
        Assert.All(pts, p => Assert.InRange(double.Hypot(p.X, p.Y), 1_002_499, 1_002_501));     // on r + error/2
    }

    [Fact]
    public void Courtyard_isInAbsoluteCoordinates()
    {
        var b = TestBoards.Load(TestBoards.Pcb.Replace("(pad \"1\" smd rect (at -1 0)",
            "(fp_rect (start -2 -1) (end 2 1) (stroke (width 0.05) (type default)) (layer \"F.CrtYd\") (uuid \"00000000-0000-0000-0000-000000000070\"))\n    (pad \"1\" smd rect (at -1 0)"));
        var c = Assert.Single(Board.Courtyard(b.Footprints[0], "F.CrtYd"));
        Assert.Equal([(103_000_000L, 104_000_000L), (107_000_000L, 104_000_000L), (107_000_000L, 106_000_000L), (103_000_000L, 106_000_000L)], c.Pts);
    }

    [Fact]
    public void Dsn_writesRuleAreasAndFarSidePads()
    {
        var pcb = TestBoards.Pcb
            .Replace("  (segment (start 104 105)", """
              (zone (net "") (layers "F&B.Cu") (uuid "00000000-0000-0000-0000-000000000080") (name "ko")
                (keepout (tracks not_allowed) (vias allowed) (pads allowed) (copperpour allowed) (footprints allowed))
                (polygon (pts (xy 101 101) (xy 103 101) (xy 103 103) (xy 101 103))))
              (zone (net "") (layers "F.Cu") (uuid "00000000-0000-0000-0000-000000000082") (name "all")
                (keepout (tracks not_allowed) (vias not_allowed) (pads allowed) (copperpour allowed) (footprints allowed))
                (polygon (pts (xy 104 101) (xy 105 101) (xy 105 102) (xy 104 102))))
              (zone (net "") (layers "B.Cu") (uuid "00000000-0000-0000-0000-000000000083") (name "novia")
                (keepout (tracks allowed) (vias not_allowed) (pads allowed) (copperpour allowed) (footprints allowed))
                (polygon (pts (xy 106 101) (xy 107 101) (xy 107 102) (xy 106 102))))
              (gr_circle (center 118 108) (end 118.5 108) (stroke (width 0.1) (type default)) (layer "Edge.Cuts") (uuid "00000000-0000-0000-0000-000000000084"))
              (segment (start 104 105)
            """.TrimEnd())
            // J1 (front) gets a pad on B.Cu only: the bottom of the image
            .Replace("(pad \"1\" smd rect (at -1 0)", "(pad \"3\" smd rect (at 0 1) (size 1 0.5) (layers \"B.Cu\" \"B.Mask\") (net \"A\") (uuid \"00000000-0000-0000-0000-000000000081\"))\n    (pad \"1\" smd rect (at -1 0)")
            // J2 (back) gets one on F.Cu only: its far side, the bottom too
            .Replace("(pad \"1\" smd rect (at 0 0 -90)", "(pad \"3\" smd rect (at 0 1 -90) (size 2 0.5) (layers \"F.Cu\" \"F.Mask\") (net \"A\") (uuid \"00000000-0000-0000-0000-000000000085\"))\n    (pad \"1\" smd rect (at 0 0 -90)");
        var dsn = Dsn(TestBoards.Load(pcb));
        Assert.Contains("(wire_keepout \"\" (polygon F.Cu 0   101000 -101000", dsn);
        Assert.Contains("(wire_keepout \"\" (polygon B.Cu 0   101000 -101000", dsn);
        Assert.Contains("(keepout \"\" (polygon F.Cu 0   104000 -101000", dsn);
        Assert.Contains("(via_keepout \"\" (polygon B.Cu 0   106000 -101000", dsn);
        Assert.Contains("(keepout \"\" (polygon signal 0", dsn);                  // the Edge.Cuts circle: a cutout
        Assert.Contains("(pin Rect[B]Pad_1000.000000x500.000000_um 3 0 -1000)", dsn);
        Assert.Contains("(padstack Rect[B]Pad_1000.000000x500.000000_um\n      (shape (rect B.Cu", dsn);
        Assert.Contains("(padstack Rect[B]Pad_2000.000000x500.000000_um\n      (shape (rect B.Cu", dsn);
    }

    [Fact]
    public void Outlines_refuseNoOutlineAndABezier()
    {
        var bare = TestBoards.Load(TestBoards.Pcb.Replace(
            "(gr_rect (start 100 100) (end 120 110) (stroke (width 0.1) (type default)) (layer \"Edge.Cuts\") (uuid \"00000000-0000-0000-0000-000000000001\"))", ""));
        Assert.Throws<InvalidOperationException>(() => bare.EdgeBox());
        Assert.Throws<InvalidOperationException>(() => Dsn(bare));
        var curve = TestBoards.Load(TestBoards.Pcb.Replace("  (gr_text \"TEST\"",
            "  (gr_curve (pts (xy 100 100) (xy 105 99) (xy 115 99) (xy 120 100)) (stroke (width 0.1) (type default)) (layer \"Edge.Cuts\") (uuid \"00000000-0000-0000-0000-000000000086\"))\n  (gr_text \"TEST\""));
        Assert.Throws<NotSupportedException>(() => curve.EdgeContours());
    }

    [Fact]
    public void Dsn_refusesLockedCopperOnANetWithNoPad()
    {
        var b = TestBoards.Load(TestBoards.Pcb.Replace("(locked yes) (layer \"F.Cu\") (net \"A\") (uuid \"00000000-0000-0000-0000-000000000009\")",
            "(locked yes) (layer \"F.Cu\") (net \"Z\") (uuid \"00000000-0000-0000-0000-000000000009\")"));
        Assert.Contains("\"Z\"", Assert.Throws<InvalidOperationException>(() => Dsn(b)).Message);
    }

    [Theory]
    [InlineData(0, 0, false)]           // top-left corner
    [InlineData(500, 0, false)]         // top edge (SHAPE_LINE_CHAIN::PointInside's rule)
    [InlineData(500, 1000, true)]       // bottom edge
    [InlineData(0, 500, true)]          // left edge
    [InlineData(1000, 500, false)]      // right edge
    [InlineData(500, 500, true)]
    public void Contains_followsKicadsEdgeRule(long x, long y, bool inside)
    {
        List<(long, long)> sq = [(0, 0), (1000, 0), (1000, 1000), (0, 1000)];
        Assert.Equal(inside, Board.Contains(sq, x, y));
    }

    [Fact]
    public void Dsn_writesTheBoardTheWayPcbnewDoes()
    {
        var b = TestBoards.Load();
        b.RoutedCopper(drop: true);
        var dsn = Dsn(b);
        // the outline's centre line, closed; y up
        Assert.Contains("(path pcb 0   100000 -100000  120000 -100000  120000 -110000  100000 -110000  100000 -100000)", dsn);
        Assert.Contains("(place J1 105000 -105000 front 0 (PN \"\"))", dsn);
        Assert.Contains("(place J2 115000 -105000 back 90 (PN \"\"))", dsn);      // back: orientation + 180
        Assert.Contains("(pin Rect[T]Pad_1000.000000x500.000000_um 1 -1000 0)", dsn);
        // a back part's pins in the library frame, rotated relative to it
        Assert.Contains("(pin Rect[T]Pad_1000.000000x500.000000_um (rotate 90) 2 1000 -500)", dsn);
        Assert.Contains("(padstack Round[A]Pad_1600.000000_um\n      (shape (circle F.Cu 1600))\n      (shape (circle B.Cu 1600))", dsn);
        Assert.Contains("(class kicad_default A B\n", dsn);
        // consecutive locked tracks of one net chain into one fixed wire
        Assert.Contains("(wire (path F.Cu 250  104000 -105000  110000 -105000  114000 -105000)(net A)(type fix))", dsn);
        Assert.Contains("(pins J1-1 J2-1)", dsn);
    }

    [Fact]
    public void Dsn_keepsTheLayoutTheShuffleNeeds()
    {
        var dsn = Dsn(TestBoards.Load());
        var shuffled = Specctra.Dsn.Shuffle(dsn, 7);      // throws on a layout it cannot read
        Assert.Equal(dsn.Split('\n').Order(), shuffled.Split('\n').Order());   // a reordering, nothing lost
    }

    [Fact]
    public void Dsn_writesLockedViasAsFixedWiring()
    {
        var b = TestBoards.Load(TestBoards.Pcb.Replace("(drill 0.3) (layers \"F.Cu\" \"B.Cu\")", "(drill 0.3) (layers \"F.Cu\" \"B.Cu\") (locked yes)"));
        b.RoutedCopper(drop: true);
        var dsn = Dsn(b);
        Assert.Contains("(via \"Via[0-1]_600:300_um\" 106900 -105000(net B)(type fix))", dsn);
        Assert.Contains("(padstack \"Via[0-1]_600:300_um\"", dsn);
    }

    [Fact]
    public void Dsn_refusesInnerLayers()
    {
        var b = TestBoards.Load(TestBoards.Pcb.Replace("(generator \"test\")",
            "(generator \"test\") (layers (0 \"F.Cu\" signal) (4 \"In1.Cu\" signal) (2 \"B.Cu\" signal))"));
        Assert.Throws<NotSupportedException>(() => Dsn(b));
    }

    [Fact]
    public void Builders_writeLockedCopperTheRouterKeeps()
    {
        var b = TestBoards.Load();
        b.Add(Board.Segment((101_000_000, 101_000_000), (102_000_000, 101_000_000), 200_000, "B.Cu", "A",
            "00000000-0000-0000-0000-000000000020", locked: true));
        b.Add(Board.Via((101_000_000, 101_000_000), 600_000, 300_000, "A", "00000000-0000-0000-0000-000000000021", locked: true));
        b.RoutedCopper(drop: true);
        Assert.Equal(3, b.Items("segment").Count());
        var via = Assert.Single(b.Items("via"));
        Assert.Equal((101_000_000L, 101_000_000L), Board.Xy(via, "at"));
    }

    [Fact]
    public void ZoneFills_areTheFilledPolygonsNotTheOutline()
    {
        var zone = (List<object>)Sx.Sexpr.Parse("""
            (zone (net "GND") (layers "F.Cu" "B.Cu") (uuid "00000000-0000-0000-0000-000000000030")
              (polygon (pts (xy 100 100) (xy 120 100) (xy 120 110) (xy 100 110)))
              (filled_polygon (layer "F.Cu") (pts (xy 101 101) (xy 119 101) (xy 119 109) (xy 101 109)))
              (filled_polygon (layer "B.Cu") (pts (xy 101 101) (xy 110 101) (xy 110 109) (xy 101 109))))
            """);
        var fills = Board.ZoneFills(zone);
        Assert.Equal(["F.Cu", "B.Cu"], fills.Select(f => f.Layer));
        Assert.False(Board.Contains(fills[1].Pts, 115e6, 105e6));
        Assert.True(Board.Contains(Board.ZoneOutline(zone), 115e6, 105e6));
    }

    [Fact]
    public void EdgeBox_skipsTextOnEdgeCuts()
    {
        var b = TestBoards.Load(TestBoards.Pcb.Replace("(gr_text \"TEST\" (at 110 102) (layer \"F.SilkS\")",
            "(gr_text \"E\" (at 0 0) (layer \"Edge.Cuts\") (uuid \"00000000-0000-0000-0000-000000000040\") (effects (font (size 1 1))))\n  (gr_text \"TEST\" (at 110 102) (layer \"F.SilkS\")"));
        Assert.Equal((99_950_000L, 99_950_000L, 120_050_000L, 110_050_000L), b.EdgeBox());
    }

    [Fact]
    public void DsnProject_readsTheNetClassesAndHoleClearance()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pcbascode-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var (classes, hole) = DsnWriter.Project(TestBoards.Temp(dir, "t.kicad_pro", TestBoards.Pro));
            Assert.Equal(200_000, hole);            // min_hole_clearance, not min_hole_to_hole
            Assert.Equal(["Default", "Power"], classes.Select(c => c.Name));
            Assert.Equal("Power", DsnWriter.ClassOf(classes, "+24V"));
            Assert.Equal("Default", DsnWriter.ClassOf(classes, "SDA"));
            // nets assigned by name (KiCad 8+ writes a list, KiCad 7 a string)
            var assigned = TestBoards.Pro.Replace("\"netclass_patterns\":",
                "\"netclass_assignments\": { \"VBUS\": [\"Power\"], \"VM.1\": \"Power\" }, \"netclass_patterns\":");
            (classes, _) = DsnWriter.Project(TestBoards.Temp(dir, "a.kicad_pro", assigned));
            Assert.Equal(["Power", "Power", "Default"], new[] { "VBUS", "VM.1", "VMX1" }.Select(n => DsnWriter.ClassOf(classes, n)));
        }
        finally { Directory.Delete(dir, true); }
    }
}
