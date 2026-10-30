using System.Text.Json.Nodes;
using Part;
using PartModel;
using Xunit;
using static Part3dAsCode.Tests.TestMeshes;

namespace Part3dAsCode.Tests;

/// The model's invariants. The first test runs over the project's own
/// part.cs, so it keeps testing the real model once the example is
/// replaced; add the project's own invariants beside it as the model grows.
public class ModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "part3dascode-model-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void ProjectModel_isValid()
    {
        var m = PartDesign.Model;
        Assert.Empty(m.Validate());
        Assert.Empty(Checks.Gates.Sources(m));
        // every critical parameter is held by an expected dimension: a
        // critical number nobody probes is a promise nobody checks
        foreach (var p in m.Params.Where(p => p.Critical))
            Assert.Contains(m.Dims, d => d.Note == p.Name || Math.Abs(d.Nominal - p.Value) < 1e-9);
    }

    [Fact]
    public void ExampleModel_derivesTheHoleFromThePinAndTheFit()
    {
        var m = PartDesign.Model;
        Assert.Equal(m["pin.d"] + 2 * m.F("slip").Gap, m["hole.d"], 9);
        Assert.Equal(Source.Derived, m.P("hole.d").Source);
    }

    [Fact]
    public void Validate_catchesBrokenReferencesAndValues()
    {
        var m = new Model
        {
            Name = "bad",
            Envelope = new(0, 10, 10),
            Params = [new("a", 1), new("a", 2), new("c", 1, Critical: true)],
            Bodies = [new("p", "parts/p.py", "up"), new("p", ""), new("r f", "parts/p.py", Kind: BodyKind.Reference),
                      new("assembly", "parts/p.py")],
            Dims = [new("ghost.d", 1, -1)],
            Clearances = [new("p", "nope", 0.1), new("p", "p", -1)],
            Sweeps = [new("p", (0, 0, 0), [], 0.1, Steps: 1), new("p", (0, 0, 25), ["p"], -0.05)],
            Measurements = [new("D1", "x", "zz", "nope", (0, 0, 0))],
        };
        var bad = string.Join("\n", m.Validate());
        foreach (var expected in new[]
        {
            "parameter 'a' defined 2 times", "body 'p' defined 2 times", "envelope", "'c' is critical but has no tolerance",
            "up 'up'", "no script", "'r f': names", "'ghost.d': negative tolerance", "probe names start with a body name",
            "no body 'nope'", "a body against itself", "negative gap", "nothing to sweep against", "fewer than 2 steps",
            "zero travel", "reserved", "measurement D1: no parameter 'zz'", "reproduce-mode sheet",
        })
            Assert.Contains(expected, bad);
        // the sweep checks fail their own way, not just through the clearances
        Assert.Contains(bad.Split('\n'), l => l.StartsWith("sweep of p: a body against itself"));
        Assert.Contains(bad.Split('\n'), l => l.StartsWith("sweep of p: negative gap"));
    }

    [Fact]
    public void NoPrintedPart_isInvalid()
    {
        var m = new Model { Name = "r", Envelope = new(1, 1, 1), Bodies = [new("ref", "s.py", Kind: BodyKind.Reference)] };
        Assert.Contains(m.Validate(), p => p.Contains("no printed part"));
    }

    [Fact]
    public void Json_carriesWhatTheScriptsRead()
    {
        var j = Model().ToJson();
        Assert.Equal(10, (double)j["params"]!["w"]!["value"]!);
        Assert.Equal("chosen", (string)j["params"]!["w"]!["source"]!);
        Assert.Equal(0.2, (double)j["fits"]!["slip"]!);
        Assert.Equal("reference", (string)j["bodies"]![1]!["kind"]!);
        Assert.Equal(10, (double)j["sweeps"]![0]!["from"]![2]!);
        Assert.Equal(3, j["envelope"]!.AsArray().Count);
        Assert.Equal(j.ToJsonString(), JsonNode.Parse(Model().ToJsonText())!.ToJsonString());
    }

    [Fact]
    public void Write_leavesAnUnchangedFileAlone()
    {
        var path = Path.Combine(_dir, "build", "params.json");
        Assert.True(Model().Write(path));
        var t = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, t);
        Assert.False(Model().Write(path));
        Assert.Equal(t, File.GetLastWriteTimeUtc(path));
        Assert.True(Model(ps: [new("w", 11, Tol: 0.1)]).Write(path));
    }

    [Fact]
    public void Lookups_nameWhatIsMissing()
    {
        var m = Model();
        Assert.Equal(10, m["w"]);
        Assert.Equal("board", m.B("board").Name);
        Assert.Contains("'nope'", Assert.Throws<KeyNotFoundException>(() => m.P("nope")).Message);
        Assert.Throws<KeyNotFoundException>(() => m.F("tight"));
    }

    [Fact]
    public void Sheet_listsEachMeasurementWithItsState()
    {
        var m = Model(Mode.Reproduce,
            [new("w", 10.02, Source: Source.Measured, Tol: 0.05, Critical: true, Ref: "D1"), new("h", 5, Tol: 0.1)],
            [new("D1", "overall width", "w", "box", (5, 0, 0)), new("D2", "height", "h", "box", (0, 0, 5))]);
        Assert.Empty(m.Validate());
        var md = m.SheetMarkdown();
        Assert.Contains("| D1 | overall width | `w` | 10.02 mm | ±0.05 | yes | measured (D1) |", md);
        Assert.Contains("| D2 | height | `h` | — |", md);
    }
}
