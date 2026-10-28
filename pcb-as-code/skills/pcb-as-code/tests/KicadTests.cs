using Kicad;
using Xunit;

namespace PcbAsCode.Tests;

public class KicadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pcb-as-code-tests-" + Guid.NewGuid().ToString("N")[..8]);

    private string TempFile(string name, string content)
    {
        Directory.CreateDirectory(_dir);
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, content);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // ------------------------------------------------------------------ //
    // report parsing (no kicad-cli involved)
    // ------------------------------------------------------------------ //

    [Fact]
    public void DrcReport_countsViolationsAndUnconnected()
    {
        var p = TempFile("drc.json", """
            {"violations": [
               {"severity": "error", "type": "clearance", "description": "x"},
               {"severity": "warning", "type": "starved_thermal", "description": "y"}],
             "unconnected_items": [{}, {}, {}]}
            """);
        var r = DrcReport.Load(p);
        Assert.Equal(2, r.Violations.Count);
        Assert.Equal(3, r.Unconnected);
        Assert.False(r.Clean());
        Assert.False(r.Clean(allowStarvedThermal: true));      // unconnected still fails
        Assert.Contains("error clearance x1", r.ToString());
    }

    [Fact]
    public void DrcReport_starvedThermalOnly_cleanWhenTolerated()
    {
        var p = TempFile("drc-starved.json",
            """{"violations": [{"severity": "warning", "type": "starved_thermal"}], "unconnected_items": []}""");
        var r = DrcReport.Load(p);
        Assert.True(r.Clean(allowStarvedThermal: true));
        Assert.False(r.Clean());                               // warnings count
    }

    [Fact]
    public void DrcReport_toleratesMissingKeys()
    {
        var p = TempFile("drc-empty.json", """{"violations": []}""");
        var r = DrcReport.Load(p);
        Assert.Equal(0, r.Unconnected);
        Assert.True(r.Clean());
    }

    [Fact]
    public void ErcReport_countsWarningsAsProblems()
    {
        var p = TempFile("erc.json", """
            {"sheets": [{"path": "/",
               "violations": [{"severity": "warning", "type": "pin_to_pin", "description": "d"}]}]}
            """);
        var r = ErcReport.Load(p);
        Assert.Single(r.Violations);
        Assert.False(r.Clean());

        var ok = TempFile("erc-ok.json", """{"sheets": [{"path": "/", "violations": []}]}""");
        Assert.True(ErcReport.Load(ok).Clean());
    }

    [Fact]
    public void Netlist_readsRefsPinsAndHierarchicalNames()
    {
        var p = TempFile("board.net", """
            (export (version "E")
              (design (source "x.kicad_sch"))
              (nets
                (net (code "1") (name "VIN")
                  (node (ref "R1") (pin "1") (pinfunction "A") (pintype "passive")))
                (net (code "2") (name "/sub/LED_A")
                  (node (ref "R1") (pin "2"))
                  (node (ref "D1") (pin "2")))))
            """);
        var n = Netlist.Read(p);
        Assert.Equal(3, n.Count);
        Assert.Equal("VIN", n[("R1", "1")]);
        Assert.Equal("sub/LED_A", n[("D1", "2")]);       // leading '/' stripped
    }

    // ------------------------------------------------------------------ //
    // live kicad-cli (they early-return — reporting as passed — when KiCad
    // is not installed)
    // ------------------------------------------------------------------ //

    public static bool KiCadInstalled => File.Exists(Cli.Exe);

    private const string CleanBoard = """
        (kicad_pcb
          (version 20250114)
          (generator "pcbnew")
          (generator_version "10.0")
          (general (legacy_teardrops no))
          (paper "A4")
          (layers
            (0 "F.Cu" signal) (31 "B.Cu" signal)
            (32 "B.Adhes" user "B.Adhesive") (33 "F.Adhes" user "F.Adhesive")
            (34 "B.Paste" user) (35 "F.Paste" user)
            (36 "B.SilkS" user "B.Silkscreen") (37 "F.SilkS" user "F.Silkscreen")
            (38 "B.Mask" user) (39 "F.Mask" user)
            (44 "Edge.Cuts" user)
            (45 "B.CrtYd" user "B.Courtyard") (46 "F.CrtYd" user "F.Courtyard"))
          (net 0 "")
          (net 1 "N1")
          (gr_rect (start 0 0) (end 100000000 100000000)
            (stroke (width 100000) (type solid)) (fill solid)
            (layer "Edge.Cuts") (uuid 00000000-0000-0000-0000-000000000001)))
        """;

    private const string MinimalSchematic = """
        (kicad_sch
          (version 20250114)
          (generator "eeschema")
          (generator_version "10.0")
          (uuid 00000000-0000-0000-0000-0000000000a1)
          (paper "A4")
          (lib_symbols)
          (sheet_instances (path "/" (page "1")))
          (embedded_fonts no))
        """;

    [Fact]
    public void Cli_versionRuns()
    {
        if (!KiCadInstalled) return;
        var (rc, stdout, _) = Cli.Capture("version");
        Assert.Equal(0, rc);
        Assert.False(string.IsNullOrWhiteSpace(stdout));
    }

    // CleanBoard without the Edge.Cuts rectangle: DRC reports invalid_outline.
    private const string NoOutlineBoard = """
        (kicad_pcb
          (version 20250114)
          (generator "pcbnew")
          (generator_version "10.0")
          (general (legacy_teardrops no))
          (paper "A4")
          (layers
            (0 "F.Cu" signal) (31 "B.Cu" signal)
            (32 "B.Adhes" user "B.Adhesive") (33 "F.Adhes" user "F.Adhesive")
            (34 "B.Paste" user) (35 "F.Paste" user)
            (36 "B.SilkS" user "B.Silkscreen") (37 "F.SilkS" user "F.Silkscreen")
            (38 "B.Mask" user) (39 "F.Mask" user)
            (44 "Edge.Cuts" user)
            (45 "B.CrtYd" user "B.Courtyard") (46 "F.CrtYd" user "F.Courtyard"))
          (net 0 "")
          (net 1 "N1"))
        """;

    [Fact]
    public void Cli_drcOfASyntheticCleanBoardIsClean()
    {
        if (!KiCadInstalled) return;
        var board = TempFile("clean.kicad_pcb", CleanBoard);
        var rpt = Cli.Drc(board, Path.Combine(_dir, "clean-drc.json"));
        Assert.True(rpt.Clean(), rpt.ToString());
    }

    [Fact]
    public void Cli_drcOfABoardWithoutOutlineIsNotClean()
    {
        if (!KiCadInstalled) return;
        var board = TempFile("nooutline.kicad_pcb", NoOutlineBoard);
        var rpt = Cli.Drc(board, Path.Combine(_dir, "nooutline-drc.json"));
        Assert.False(rpt.Clean());
        Assert.Contains(rpt.Violations, v => v.Type == "invalid_outline");   // no Edge.Cuts
    }

    [Fact]
    public void Cli_ercOfASyntheticEmptySheetIsClean()
    {
        if (!KiCadInstalled) return;
        var sch = TempFile("empty.kicad_sch", MinimalSchematic);
        var rpt = Cli.Erc(sch, Path.Combine(_dir, "empty-erc.json"));
        Assert.True(rpt.Clean(), rpt.ToString());
    }

    [Fact]
    public void Cli_netlistExportOfAnEmptySheetHasNoNets()
    {
        if (!KiCadInstalled) return;
        var sch = TempFile("empty.kicad_sch", MinimalSchematic);
        var nets = Cli.ExportNetlist(sch, Path.Combine(_dir, "empty.net"));
        Assert.Empty(nets);
    }

    [Fact]
    public void Cli_runOfAFailingCommandThrowsWithOutput()
    {
        if (!KiCadInstalled) return;
        // the operator-facing error of every gate: a nonzero rc carries both
        // streams in the message
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Cli.Drc(Path.Combine(_dir, "missing.kicad_pcb"), Path.Combine(_dir, "x-drc.json")));
        Assert.Contains("failed", ex.Message);
    }
}
