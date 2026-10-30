using Mesh;
using PartModel;

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
