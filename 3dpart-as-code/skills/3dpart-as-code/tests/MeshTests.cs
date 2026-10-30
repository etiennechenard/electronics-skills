using System.Globalization;
using System.Text;
using Mesh;
using Xunit;
using static Part3dAsCode.Tests.TestMeshes;

namespace Part3dAsCode.Tests;

public class MeshTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "part3dascode-mesh-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void ClosedBox_isClean()
    {
        var r = MeshCheck.Analyze(MeshOf(Box(0, 0, 0, 10, 20, 30)));
        Assert.Empty(r.Problems());
        Assert.Equal(12, r.Triangles);
        Assert.Equal(1, r.Shells);
        Assert.Equal(6000, r.Volume, 6);
        Assert.Equal(2 * (200 + 300 + 600), r.Area, 6);
        Assert.Equal(new V3(10, 20, 30), r.Bounds.Size);
    }

    [Fact]
    public void OpenBox_reportsOpenEdges()
    {
        var tris = Box(0, 0, 0, 10, 10, 10);
        tris.RemoveRange(2, 2);   // the top
        var r = MeshCheck.Analyze(MeshOf(tris));
        Assert.Equal(4, r.OpenEdges);
        Assert.Contains(r.Problems(), p => p.Contains("open edges"));
    }

    [Fact]
    public void Moved_shiftsEveryVertex()
    {
        var s = MeshOf([new Tri(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0))]);
        var m = s.Moved(new V3(5, 0, 10));
        Assert.Equal(new V3(5, 0, 10), m.Tris[0].A);
        Assert.Equal(new V3(6, 0, 10), m.Tris[0].B);
        Assert.Equal(new V3(5, 1, 10), m.Tris[0].C);
    }

    [Fact]
    public void TwoBoxes_areTwoShells()
    {
        var r = MeshCheck.Analyze(MeshOf([.. Box(0, 0, 0, 5, 5, 5), .. Box(10, 0, 0, 5, 5, 5)]));
        Assert.Equal(2, r.Shells);
        Assert.Contains(r.Problems(), p => p.Contains("2 shells"));
    }

    [Fact]
    public void OneFlippedFace_isMisoriented_allFlipped_isInsideOut()
    {
        var tris = Box(0, 0, 0, 10, 10, 10);
        tris[0] = Flip(tris[0]);
        Assert.Contains(MeshCheck.Analyze(MeshOf(tris)).Problems(), p => p.Contains("wound opposite"));
        var inside = MeshCheck.Analyze(MeshOf(Box(0, 0, 0, 10, 10, 10).Select(Flip)));
        Assert.Equal(0, inside.MisorientedEdges);
        Assert.Contains(inside.Problems(), p => p.Contains("not positive"));
    }

    [Fact]
    public void Degenerate_andNonManifold_areReported()
    {
        var tris = Box(0, 0, 0, 10, 10, 10);
        tris.Add(new(new(0, 0, 0), new(5, 0, 0), new(10, 0, 0)));   // zero area
        Assert.Contains(MeshCheck.Analyze(MeshOf(tris)).Problems(), p => p.Contains("degenerate"));
        // a fin sharing the box's bottom-front edge: three faces on one edge
        var fin = Box(0, 0, 0, 10, 10, 10);
        fin.Add(new(new(0, 0, 0), new(10, 0, 0), new(5, -5, 0)));
        Assert.True(MeshCheck.Analyze(MeshOf(fin)).NonManifoldEdges > 0);
    }

    [Fact]
    public void BinaryAndAscii_loadTheSameMesh()
    {
        var box = MeshOf(Box(1, 2, 3, 4, 5, 6));
        var bin = Path.Combine(_dir, "b.stl");
        Directory.CreateDirectory(_dir);
        box.Save(bin);
        var w = new StringBuilder("solid t\n");
        foreach (var t in box.Tris)
        {
            w.Append("facet normal 0 0 0\nouter loop\n");
            foreach (var v in new[] { t.A, t.B, t.C })
                w.Append(string.Create(CultureInfo.InvariantCulture, $"vertex {v.X} {v.Y} {v.Z}\n"));
            w.Append("endloop\nendfacet\n");
        }
        w.Append("endsolid t\n");
        var ascii = Temp(_dir, "a.stl", w.ToString());
        var a = Stl.Load(ascii);
        var b = Stl.Load(bin);
        Assert.Equal(12, a.Tris.Count);
        Assert.Equal(a.Tris, b.Tris);
        Assert.Equal(120, b.Volume(), 4);
        Assert.Throws<InvalidDataException>(() => Stl.Load(Temp(_dir, "x.stl", "not a mesh")));
    }

    [Fact]
    public void Envelope_fitsAsIsOrTurned()
    {
        var b = MeshOf(Box(0, 0, 0, 200, 100, 30)).Bounds();
        Assert.True(MeshCheck.FitsEnvelope(b, 220, 120, 50));
        Assert.True(MeshCheck.FitsEnvelope(b, 120, 220, 50));    // turned 90°
        Assert.False(MeshCheck.FitsEnvelope(b, 190, 190, 50));
        Assert.False(MeshCheck.FitsEnvelope(b, 220, 220, 20));   // too tall
    }

    [Fact]
    public void Overhang_countsDownwardFacesAboveTheBed()
    {
        // a box on the bed (its bottom is not overhang) plus a raised slab
        // overhanging it
        var r = MeshCheck.Analyze(MeshOf([.. Box(0, 0, 0, 10, 10, 10), .. Box(-5, 0, 10, 20, 10, 2)]));
        Assert.Equal(200, r.OverhangArea, 6);   // the slab's whole underside (the box's top is hidden, not removed)
        Assert.Equal(0, MeshCheck.Analyze(MeshOf(Box(0, 0, 5, 10, 10, 10))).OverhangArea);
    }
}
