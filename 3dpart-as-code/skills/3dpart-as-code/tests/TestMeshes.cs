using Mesh;
using PartEdit;
using PartModel;
using Xunit;

namespace Part3dAsCode.Tests;

/// Synthetic meshes and models for the tests.
public static class TestMeshes
{
    /// Two triangles of the quad a-b-c-d, counter-clockwise seen from outside.
    static IEnumerable<Tri> Quad(V3 a, V3 b, V3 c, V3 d) => [new(a, b, c), new(a, c, d)];

    /// An axis-aligned box, wound outwards.
    public static List<Tri> Box(double x0, double y0, double z0, double sx, double sy, double sz)
    {
        V3 P(int i, int j, int k) => new(x0 + i * sx, y0 + j * sy, z0 + k * sz);
        return
        [
            .. Quad(P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0)),   // bottom
            .. Quad(P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1)),   // top
            .. Quad(P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1)),   // front
            .. Quad(P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0)),   // back
            .. Quad(P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0)),   // left
            .. Quad(P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1)),   // right
        ];
    }

    /// The worked example's part.cs text: tests/example/part.cs in a
    /// project (where the project's own part.cs has replaced the template),
    /// else templates/part.cs in the plugin.
    public static string ExamplePartText()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            foreach (var rel in new[] { Path.Combine("example", "part.cs"), Path.Combine("templates", "part.cs") })
                if (File.Exists(Path.Combine(d.FullName, rel)))
                    return File.ReadAllText(Path.Combine(d.FullName, rel));
        throw new FileNotFoundException($"no example part.cs above {AppContext.BaseDirectory}");
    }

    /// True when the compiled `Part.PartDesign.Model` is the worked example
    /// (the plugin's layout); in a project it is the project's own model.
    public static bool CompiledModelIsTheExample { get; } = ModelMatches(ExamplePartText());

    static bool ModelMatches(string text)
    {
        var m = Part.PartDesign.Model;
        return m.Params.Select(p => p.Name).Concat(m.Fits.Select(f => f.Name))
            .All(n => Literals.Locate(text, n).Kind != LocateKind.Missing);
    }

    public static Stl MeshOf(IEnumerable<Tri> tris)
    {
        var s = new Stl();
        s.Tris.AddRange(tris);
        return s;
    }

    public static Tri Flip(Tri t) => new(t.A, t.C, t.B);

    public static string Temp(string dir, string name, string text)
    {
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, name);
        File.WriteAllText(p, text);
        return p;
    }

    /// A minimal valid model: one box part, one reference.
    public static Model Model(Mode mode = Mode.Design, IReadOnlyList<Param>? ps = null, IReadOnlyList<Measurement>? ms = null) => new()
    {
        Name = "t",
        Mode = mode,
        Envelope = new(100, 50, 40),
        Params = ps ?? [new("w", 10, Tol: 0.1)],
        Bodies = [new("box", "parts/box.py"), new("board", "parts/box.py", Kind: BodyKind.Reference)],
        Dims = [new("box.bbox.x", 10, 0.05)],
        Clearances = [new("box", "board", 0.2)],
        Sweeps = [new("box", (0, 0, 10), ["board"], 0.1)],
        Measurements = ms ?? [],
    };
}

/// A Fact that needs the compiled model to be the worked example.
public sealed class ExampleFactAttribute : FactAttribute
{
    public ExampleFactAttribute()
    {
        if (!TestMeshes.CompiledModelIsTheExample)
            Skip = "the project's own part.cs replaced the worked example";
    }
}
