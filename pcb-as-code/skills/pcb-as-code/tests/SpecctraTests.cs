using Specctra;
using Xunit;

namespace PcbAsCode.Tests;

public class SpecctraTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pcb-as-code-specctra-" + Guid.NewGuid().ToString("N")[..8]);

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

    // A DSN in KiCad's own layout — the indentation is load-bearing: the
    // shuffle transform reorders blocks matched by their exact leading
    // spaces (components at 4, place lines at 6, nets at 4), like pcbnew
    // writes them.
    private const string SampleDsn = """
        (pcb boards/test.dsn
          (parser
            (string_quote ")
            (space_in_quoted_tokens on)
            (host_cad "KiCad's Pcbnew")
            (host_version "10.0.7")
          )
          (resolution um 10)
          (unit um)
          (structure
            (layer F.Cu
              (type signal)
              (property
                (index 0)
              )
            )
            (layer B.Cu
              (type signal)
              (property
                (index 1)
              )
            )
          )
          (placement
            (component CAPC2012N
              (place C1 970000 -760000 front 90)
              (place C2 972000 -768000 front 0)
              (place C3 974000 -772000 front 270)
            )
          )
          (library
            (image CAPC2012N
              (pin round[T] 1000 500 0 0)
            )
          )
          (network
            (net C1_A
              (pins C1-A C2-A)
            )
            (net GND
              (pins C1-B C2-B)
            )
            (net VCC
              (pins C3-A C3-B)
            )
            (class kicad_default ""
              (circuit
                (use_layer F.Cu B.Cu)
              )
              (rule
                (width 250)
                (clearance 200.0)
                (clearance 50.0 (type smd_smd))
              )
            )
          )
        )
        """;

    [Fact]
    public void BumpClearances_bumpsTrackRules_smdSmdUntouched()
    {
        // pcbnew DSN shape: clearances are in um (older pcbnew wrote 200.0,
        // 10.x writes 200; both are read).
        // The >= 100 threshold means every real track/via rule (>= 0.1 mm)
        // gets the margin; the 50 um smd_smd pad-pad rule is left alone.
        var out_ = Dsn.BumpClearances(SampleDsn);
        Assert.Equal("(clearance 230.0)", Dsn.BumpClearances("(clearance 200)"));
        Assert.DoesNotContain("(clearance 200.0)", out_);
        Assert.Contains("(clearance 230.0", out_);         // 200 * 1.15
        Assert.Contains("(clearance 50.0 (type smd_smd))", out_);
        Assert.Contains("(width 250)", out_);              // not a clearance
    }

    [Fact]
    public void BumpClearances_thresholdIsTheRawValue()
    {
        // the >= 100 test is on the DSN's own number, whatever its unit
        var bumped = Dsn.BumpClearances("(clearance 40.0) (clearance 200)");
        Assert.Contains("(clearance 40.0", bumped);
        Assert.Contains("(clearance 230.0", bumped);
    }

    [Fact]
    public void BumpClearances_respectsCustomFactor()
    {
        var bumped = Dsn.BumpClearances("(clearance 200)", factor: 1.5);
        Assert.Contains("(clearance 300.0", bumped);
    }

    [Fact]
    public void Shuffle_reordersComponentsPlacesAndNets()
    {
        var shuffled = Dsn.Shuffle(SampleDsn, seed: 1);
        Assert.NotEqual(SampleDsn, shuffled);
        // same content, different order: every place line and net survives
        foreach (var place in new[] { "(place C1", "(place C2", "(place C3" })
            Assert.Contains(place, shuffled);
        foreach (var net in new[] { "(net GND", "(net VCC", "(net C1_A" })
            Assert.Contains(net, shuffled);
        // the rule block is untouched. (The result is deliberately NOT
        // s-expression-parsed: a pcbnew DSN carries `(string_quote ")`,
        // which is not balanced s-expr — DSNs are a regex-text domain.)
        Assert.Contains("(clearance 200.0)", shuffled);
    }

    [Fact]
    public void Shuffle_isDeterministicPerSeed_andSeedsDiffer()
    {
        Assert.Equal(Dsn.Shuffle(SampleDsn, 1), Dsn.Shuffle(SampleDsn, 1));
        Assert.NotEqual(Dsn.Shuffle(SampleDsn, 1), Dsn.Shuffle(SampleDsn, 7));
    }

    [Fact]
    public void ReadRoutes_scalesToNm_andNegatesY()
    {
        var ses = TempFile("routes.ses", """
            (session boards/test.ses
              (routes
                (resolution um 10)
                (library_out
                  (padstack via_600:300_um
                    (shape (circle 300 0 0))))
                (network_out
                  (net GND
                    (wire (path F.Cu 200000 100 200 500 200))
                    (wire (path B.Cu 200000 10 -20 30 -40))
                    (via via_600:300_um 300 400)))))
            """);
        var (wires, vias) = Ses.ReadRoutes(ses);
        Assert.Equal(2, wires.Count);

        var w = wires[0];                          // resolution um 10 -> 100 nm/unit
        Assert.Equal("GND", w.Net);
        Assert.Equal("F.Cu", w.Layer);
        Assert.Equal(20_000_000, w.WidthNm);       // 200000 units * 100
        Assert.Equal((10_000, -20_000), w.PointsNm[0]);   // y negated
        Assert.Equal((50_000, -20_000), w.PointsNm[1]);

        var w2 = wires[1];
        Assert.Equal((1_000, 2_000), w2.PointsNm[0]);     // -(-20)
        Assert.Equal((3_000, 4_000), w2.PointsNm[1]);

        var via = Assert.Single(vias);
        Assert.Equal(600_000, via.SizeNm);         // from the padstack name, in nm
        Assert.Equal(300_000, via.DrillNm);
        Assert.Equal((30_000, -40_000), (via.XNm, via.YNm));
    }

    [Fact]
    public void ReadRoutes_keepsPathsOnly_wiresWithoutPathSkipped()
    {
        var ses = TempFile("nopath.ses", """
            (session x.ses
              (routes (resolution um 10)
                (library_out)
                (network_out
                  (net N (wire (garbage 1)) (wire (path F.Cu 10 1 2))))))
            """);
        var (wires, _) = Ses.ReadRoutes(ses);
        var w = Assert.Single(wires);
        Assert.Equal("F.Cu", w.Layer);
    }

    [Fact]
    public void ReadRoutes_readsCircularPadstacks()
    {
        // padstacks without the _W:H_um name: the circular-shape fallback
        var ses = TempFile("circular.ses", """
            (session x.ses
              (routes (resolution um 10)
                (library_out
                  (padstack round_via
                    (shape (circle 300 0 0))))
                (network_out
                  (net N
                    (via round_via 300 400)))))
            """);
        var (_, vias) = Ses.ReadRoutes(ses);
        var v = Assert.Single(vias);
        Assert.Equal(30000, v.XNm);              // 300 units * 100 nm/unit
        Assert.Equal(-40000, v.YNm);             // y negated
        Assert.Equal(30000, v.SizeNm);           // first positive number of the shape
        Assert.Equal(15000, v.DrillNm);          // half
    }

    [Fact]
    public void ReadRoutes_throwsOnUnknownUnit()
    {
        var ses = TempFile("badunit.ses", """
            (session x.ses
              (routes (resolution parsec 10)
                (library_out)
                (network_out)))
            """);
        Assert.Throws<InvalidOperationException>(() => Ses.ReadRoutes(ses));
    }

    [Fact]
    public void ReadRoutes_throwsWithoutRoutes()
    {
        var ses = TempFile("noroutes.ses", "(session x.ses (nothing))");
        Assert.Throws<InvalidOperationException>(() => Ses.ReadRoutes(ses));
    }

    [Fact]
    public void Shuffle_throwsWhenTheDsnLayoutIsNotPcbnews()
    {
        // different indentation than pcbnew's → the shuffle would silently
        // no-op and defeat the retry mechanism
        Assert.Throws<InvalidOperationException>(() => Dsn.Shuffle(
            "(pcb x\n  (placement\n    (component X\n      (place A 0 0 front 0)\n    )\n  )\n)", 1));
    }

    [Fact]
    public void Route_throwsWhenFreeRoutingIsNotInstalled()
    {
        var dsn = TempFile("x.dsn", SampleDsn);
        var ex = Assert.Throws<FileNotFoundException>(() =>
            FreeRouting.Route(dsn, Path.Combine(_dir, "x.ses"),
                java: Path.Combine(_dir, "no-java.exe"), jar: Path.Combine(_dir, "no-fr.jar")));
        Assert.Contains("FreeRouting not installed", ex.Message);
    }
}
