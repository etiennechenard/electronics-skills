// STL meshes: read (binary or ASCII), write (binary), and the mesh gate a
// slicer depends on — closed, consistently oriented, outward, one shell, no
// degenerate triangles — plus the envelope fit and an overhang report in
// print orientation (+Z up, as pac.py writes the printed STLs).
//
// Library file for #:include — types only, no top-level statements.

using System.Globalization;
using System.Text;

namespace Mesh;

public readonly record struct V3(double X, double Y, double Z)
{
    public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static V3 operator *(V3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public double Dot(V3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public V3 Cross(V3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public double Length => Math.Sqrt(Dot(this));
    public V3 Unit => Length > 0 ? this * (1 / Length) : this;
}

public readonly record struct Tri(V3 A, V3 B, V3 C)
{
    /// Normal from the winding (counter-clockwise seen from outside), not
    /// the stored one: slicers trust the winding.
    public V3 Normal => (B - A).Cross(C - A).Unit;
    public double Area => (B - A).Cross(C - A).Length / 2;
}

public readonly record struct Box3(V3 Min, V3 Max)
{
    public V3 Size => Max - Min;
}

public sealed class Stl
{
    public List<Tri> Tris { get; } = [];

    /// A copy shifted by `by` (an exploded view's lifted lid).
    public Stl Moved(V3 by)
    {
        var s = new Stl();
        s.Tris.AddRange(Tris.Select(t => new Tri(t.A + by, t.B + by, t.C + by)));
        return s;
    }

    public static Stl Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var stl = new Stl();
        // binary: 80-byte header, uint32 count, 50 bytes per triangle. An
        // ASCII file can start with "solid" too, so the size decides.
        if (bytes.Length >= 84 && 84 + 50L * BitConverter.ToUInt32(bytes, 80) == bytes.Length)
        {
            var n = BitConverter.ToUInt32(bytes, 80);
            for (var i = 0; i < n; i++)
            {
                var o = 84 + i * 50 + 12;
                V3 R(int k) => new(BitConverter.ToSingle(bytes, o + k), BitConverter.ToSingle(bytes, o + k + 4),
                    BitConverter.ToSingle(bytes, o + k + 8));
                stl.Tris.Add(new(R(0), R(12), R(24)));
            }
            return stl;
        }
        var text = Encoding.ASCII.GetString(bytes);
        if (!text.TrimStart().StartsWith("solid", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{path}: neither a binary nor an ASCII STL");
        var vs = new List<V3>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
                continue;
            var f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            vs.Add(new(double.Parse(f[1], CultureInfo.InvariantCulture), double.Parse(f[2], CultureInfo.InvariantCulture),
                double.Parse(f[3], CultureInfo.InvariantCulture)));
        }
        if (vs.Count % 3 != 0)
            throw new InvalidDataException($"{path}: {vs.Count} vertices is not a whole number of triangles");
        for (var i = 0; i < vs.Count; i += 3)
            stl.Tris.Add(new(vs[i], vs[i + 1], vs[i + 2]));
        return stl;
    }

    public void Save(string path)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write(new byte[80]);
        w.Write((uint)Tris.Count);
        foreach (var t in Tris)
        {
            var n = t.Normal;
            foreach (var v in new[] { n, t.A, t.B, t.C })
            {
                w.Write((float)v.X);
                w.Write((float)v.Y);
                w.Write((float)v.Z);
            }
            w.Write((ushort)0);
        }
    }

    public Box3 Bounds()
    {
        if (Tris.Count == 0)
            return new(new(0, 0, 0), new(0, 0, 0));
        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue;
        double x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
        foreach (var t in Tris)
            foreach (var v in new[] { t.A, t.B, t.C })
            {
                x0 = Math.Min(x0, v.X); y0 = Math.Min(y0, v.Y); z0 = Math.Min(z0, v.Z);
                x1 = Math.Max(x1, v.X); y1 = Math.Max(y1, v.Y); z1 = Math.Max(z1, v.Z);
            }
        return new(new(x0, y0, z0), new(x1, y1, z1));
    }

    /// Signed volume (divergence theorem): positive when the winding faces out.
    public double Volume() => Tris.Sum(t => t.A.Dot(t.B.Cross(t.C))) / 6;
}

public sealed record MeshReport(
    int Triangles, int OpenEdges, int NonManifoldEdges, int MisorientedEdges, int Shells, int Degenerate,
    double Volume, double Area, Box3 Bounds, double OverhangArea)
{
    /// The gate's problems; overhang is reported, never a problem (support
    /// is the slicer's call).
    public IReadOnlyList<string> Problems()
    {
        var p = new List<string>();
        if (Triangles == 0) p.Add("empty mesh");
        if (OpenEdges > 0) p.Add($"{OpenEdges} open edges (not watertight)");
        if (NonManifoldEdges > 0) p.Add($"{NonManifoldEdges} non-manifold edges (more than 2 faces)");
        if (MisorientedEdges > 0) p.Add($"{MisorientedEdges} edges between faces wound opposite ways");
        if (Shells != 1 && Triangles > 0) p.Add($"{Shells} shells (one printed body is one shell)");
        if (Degenerate > 0) p.Add($"{Degenerate} degenerate (zero-area) triangles");
        if (Volume <= 0 && Triangles > 0) p.Add($"volume {Volume:0.###} mm3 is not positive (faces point inwards)");
        return p;
    }
}

public static class MeshCheck
{
    /// Vertices closer than this (mm) are one vertex. STL stores 32-bit
    /// floats, so shared vertices match exactly; the grid only absorbs noise.
    public const double Weld = 1e-4;

    public static MeshReport Analyze(Stl s, double overhangDeg = 45)
    {
        var ids = new Dictionary<(long, long, long), int>();
        int Id(V3 v)
        {
            var k = ((long)Math.Round(v.X / Weld), (long)Math.Round(v.Y / Weld), (long)Math.Round(v.Z / Weld));
            if (!ids.TryGetValue(k, out var i))
                ids[k] = i = ids.Count;
            return i;
        }
        var tris = s.Tris.Select(t => (A: Id(t.A), B: Id(t.B), C: Id(t.C))).ToList();
        var degenerate = 0;
        // per undirected edge: how many faces use it, and how many in each direction
        var edges = new Dictionary<(int, int), (int Fwd, int Back)>();
        var uf = new int[ids.Count];
        for (var i = 0; i < uf.Length; i++) uf[i] = i;
        int Root(int i) { while (uf[i] != i) i = uf[i] = uf[uf[i]]; return i; }
        for (var n = 0; n < tris.Count; n++)
        {
            var (a, b, c) = tris[n];
            if (a == b || b == c || a == c || s.Tris[n].Area < 1e-12)
            {
                degenerate++;
                continue;
            }
            foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
            {
                var key = u < v ? (u, v) : (v, u);
                var e = edges.GetValueOrDefault(key);
                edges[key] = u < v ? (e.Fwd + 1, e.Back) : (e.Fwd, e.Back + 1);
                uf[Root(u)] = Root(v);
            }
        }
        int open = 0, nonManifold = 0, misoriented = 0;
        foreach (var (f, bk) in edges.Values)
        {
            var uses = f + bk;
            if (uses == 1) open++;
            else if (uses > 2) nonManifold++;
            else if (f != 1) misoriented++;
        }
        var used = tris.Where((t, n) => !(t.A == t.B || t.B == t.C || t.A == t.C || s.Tris[n].Area < 1e-12))
            .Select(t => Root(t.A)).Distinct().Count();

        var bounds = s.Bounds();
        var cos = Math.Cos((90 - overhangDeg) * Math.PI / 180);
        var bed = bounds.Min.Z + 0.01;
        var overhang = s.Tris.Where(t => -t.Normal.Z > cos && Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z)) > bed)
            .Sum(t => t.Area);
        return new(s.Tris.Count, open, nonManifold, misoriented, used, degenerate, s.Volume(),
            s.Tris.Sum(t => t.Area), bounds, overhang);
    }

    /// Fits the printer as is, or turned 90° about z.
    public static bool FitsEnvelope(Box3 b, double ex, double ey, double ez)
    {
        var (x, y, z) = (b.Size.X, b.Size.Y, b.Size.Z);
        return z <= ez && ((x <= ex && y <= ey) || (y <= ex && x <= ey));
    }
}
