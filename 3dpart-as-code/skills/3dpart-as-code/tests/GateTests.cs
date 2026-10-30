using Checks;
using Mech;
using PartModel;
using Xunit;
using static Part3dAsCode.Tests.TestMeshes;

namespace Part3dAsCode.Tests;

public class GateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "part3dascode-gate-" + Guid.NewGuid().ToString("N")[..8]);
    private Layout L => new(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Dims_passWithinTolerance_failOutside_andWhenNeverProbed()
    {
        var m = Model();
        Assert.Empty(Gates.Dims(m, new Dictionary<string, double> { ["box.bbox.x"] = 10.04 }));
        var bad = Gates.Dims(m, new Dictionary<string, double> { ["box.bbox.x"] = 10.2 });
        Assert.Contains("10.2 mm, expected 10 +/- 0.05 (+0.2)", Assert.Single(bad));
        Assert.Contains("never probed", Assert.Single(Gates.Dims(m, new Dictionary<string, double>())));
    }

    [Fact]
    public void Sources_onlyBindInReproduceMode()
    {
        Param[] ps =
        [
            new("guess", 3, Tol: 0.1, Critical: true),
            new("noref", 4, Source: Source.Measured, Tol: 0.1, Critical: true),
            new("ok", 5, Source: Source.Measured, Tol: 0.1, Critical: true, Ref: "D2"),
            new("formula", 6, Source: Source.Derived, Tol: 0.1, Critical: true),
            new("free", 7),
        ];
        Assert.Empty(Gates.Sources(Model(ps: ps)));
        var bad = string.Join("\n", Gates.Sources(Model(Mode.Reproduce, ps)));
        Assert.Contains("guess: critical but chosen", bad);
        Assert.Contains("noref: measured without a Ref", bad);
        Assert.Contains("formula: derived without a Ref", bad);
        Assert.DoesNotContain("ok:", bad);
        Assert.DoesNotContain("free", bad);
    }

    [Fact]
    public void Probes_mergeScripts_andRefuseADuplicate()
    {
        Temp(L.Build, "probe-a.json", """{"probes": {"box.bbox.x": 10}}""");
        Temp(L.Build, "probe-b.json", """{"probes": {"lid.t": 2}}""");
        Assert.Equal(2, Probes.Load(L).Count);
        Temp(L.Build, "probe-c.json", """{"probes": {"lid.t": 2.1}}""");
        Assert.Throws<InvalidDataException>(() => Probes.Load(L));
    }

    [Fact]
    public void Fit_judgesOverlapGapAndMissingResults()
    {
        var m = Model();
        Assert.Contains("fit.json missing", Assert.Single(Gates.Fit(m, L)));
        Temp(L.Build, "fit.json", """
            {"clearances": [{"a": "box", "b": "board", "gap": 0.25, "overlap": 0}],
             "sweeps": [{"moving": "box", "against": "board", "gap": 0.05, "overlap": 0, "at": 0.5}]}
            """);
        Assert.Contains("gap 0.05 mm, needs >= 0.1", Assert.Single(Gates.Fit(m, L)));
        Temp(L.Build, "fit.json", """
            {"clearances": [{"a": "box", "b": "board", "gap": 0, "overlap": 3.5}], "sweeps": []}
            """);
        var bad = Gates.Fit(m, L);
        Assert.Contains(bad, p => p.Contains("overlap 3.5 mm3"));
        Assert.Contains(bad, p => p.Contains("not in the fit report"));
    }

    [Fact]
    public void Meshes_checkEnvelopeAndVolumeAgainstTheSolid()
    {
        Directory.CreateDirectory(L.Build);
        MeshOf(Box(0, 0, 0, 120, 10, 10)).Save(L.Stl("box"));
        var lines = new List<string>();
        var bad = Gates.Meshes(Model(), L, new Dictionary<string, double> { ["box.volume"] = 12000 }, lines);
        Assert.Contains("does not fit the 100 x 50 x 40 envelope", Assert.Single(bad));
        Assert.Contains("box: 12 triangles, 1 shell, 120 x 10 x 10 mm up +Z", Assert.Single(lines));
        bad = Gates.Meshes(Model(), L, new Dictionary<string, double> { ["box.volume"] = 15000 }, []);
        Assert.Contains(bad, p => p.Contains("tessellation lost material"));
    }

    [Fact]
    public void Stale_flagsOldOutputsMissingOnesAndChangedParams()
    {
        var m = Model();
        Temp(L.Tools, "part.cs", "// model");
        Temp(L.Tools, "check.cs", "#:sdk Microsoft.NET.Sdk\n");
        Temp(L.Tools, "raster.cs", "// renderer");
        Temp(L.Parts, "box.py", "# geometry");
        m.Write(L.Params);
        var out1 = Temp(L.Build, "box.step", "x");
        Assert.Empty(Gates.Stale(m, L, [out1]));
        File.SetLastWriteTimeUtc(out1, DateTime.UtcNow.AddHours(-1));
        Assert.Contains("is older than", Assert.Single(Gates.Stale(m, L, [out1])));
        File.SetLastWriteTimeUtc(out1, DateTime.UtcNow.AddHours(1));
        // entry tools and reader-only libraries are not inputs
        File.SetLastWriteTimeUtc(Path.Combine(L.Tools, "check.cs"), DateTime.UtcNow.AddHours(2));
        File.SetLastWriteTimeUtc(Path.Combine(L.Tools, "raster.cs"), DateTime.UtcNow.AddHours(2));
        Assert.Empty(Gates.Stale(m, L, [out1]));
        Assert.Contains("missing", Assert.Single(Gates.Stale(m, L, [out1, Path.Combine(L.Build, "nope.stl")])));
        Assert.Contains("params.json differs", Assert.Single(Gates.Stale(Model(ps: [new("w", 9, Tol: 0.1)]), L, [out1])));
        var withInput = new Model
        {
            Name = m.Name, Envelope = m.Envelope, Params = m.Params, Bodies = m.Bodies, Dims = m.Dims,
            Clearances = m.Clearances, Sweeps = m.Sweeps, Inputs = ["../pcb/board.kicad_pcb"],
        };
        withInput.Write(L.Params);
        Assert.Contains("input ..", Assert.Single(Gates.Stale(withInput, L, [out1])));
    }

    [Fact]
    public void Layout_findsTheProbeFileOfAScript()
    {
        Assert.Equal(Path.Combine(_dir, "build", "probe-box.json"), L.Probe("parts/box.py"));
        Assert.Equal(Path.Combine(_dir, "build", "asm", "box.stl"), L.AsmStl("box"));
        Assert.Equal(_dir, Layout.Here(["--mech", _dir]).Root);
    }
}
