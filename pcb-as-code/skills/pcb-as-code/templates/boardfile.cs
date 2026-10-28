// A .kicad_pcb read as s-expression text (no pcbnew): footprints with their
// pads in absolute coordinates, tracks, vias, zones and Edge.Cuts outlines,
// plus the few edits the router makes (drop or add copper, write back).
// Coordinates are KiCad nanometres, y down, page origin.
//
// Library file for #:include — types only, no top-level statements.

using System.Globalization;
using Kicad;
using Sx;

namespace PcbFile;

public static class Nm
{
    public static long Of(object atom) => (long)Math.Round(atom.D() * 1e6, MidpointRounding.ToEven);

    public static Sym Mm(long nm) => new((nm / 1_000_000m).ToString("0.######", CultureInfo.InvariantCulture));

    public static (long X, long Y) Rotate(long x, long y, double deg)
    {
        double a = deg % 360;
        if (a < 0) a += 360;
        if (a == 0) return (x, y);
        if (a == 90) return (y, -x);
        if (a == 180) return (-x, -y);
        if (a == 270) return (-y, x);
        double s = Math.Sin(a * Math.PI / 180), c = Math.Cos(a * Math.PI / 180);
        return ((long)Math.Round(y * s + x * c, MidpointRounding.AwayFromZero),
                (long)Math.Round(y * c - x * s, MidpointRounding.AwayFromZero));
    }

    public static double Norm360(double a)
    {
        a %= 360;
        return a < 0 ? a + 360 : a;
    }
}

public sealed class BPad
{
    public required List<object> Node { get; init; }
    public required BFootprint Fp { get; init; }
    public string Number => Node[1].Txt();
    public string Type => Node[2].Txt();                 // smd thru_hole np_thru_hole connect
    public string Shape => Node[3].Txt();                // circle rect oval roundrect ...
    public string? Net => Sexpr.Find(Node, "net")?[1].Txt();
    public long SizeX => Nm.Of(Sexpr.Find(Node, "size")![1]);
    public long SizeY => Nm.Of(Sexpr.Find(Node, "size")![2]);
    public double RRatio => Sexpr.Find(Node, "roundrect_rratio")?[1].D() ?? 0;
    public double ChamferRatio => Sexpr.Find(Node, "chamfer_ratio")?[1].D() ?? 0;
    public IReadOnlyList<string> Layers => Sexpr.Find(Node, "layers")!.Skip(1).Select(e => e.Txt()).ToList();
    public bool OnCopper => Layers.Any(l => l.EndsWith(".Cu"));

    /// Drill (x, y) in nm; (0, 0) for SMD pads. An oval drill is written (drill oval x y).
    public (long X, long Y) Drill
    {
        get
        {
            var d = Sexpr.Find(Node, "drill");
            if (d is null || d.Count < 2) return (0, 0);
            var v = d.Skip(1).Where(e => e is Sym s && s.Value != "oval" && double.TryParse(s.Value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out _)).ToList();
            if (v.Count == 0) return (0, 0);
            long x = Nm.Of(v[0]);
            return (x, v.Count > 1 ? Nm.Of(v[1]) : x);
        }
    }

    private List<object> At => Sexpr.Find(Node, "at")!;

    /// Position as stored: footprint-local, y already negated on the back.
    public (long X, long Y) Local => (Nm.Of(At[1]), Nm.Of(At[2]));

    /// Position in the library (front) frame.
    public (long X, long Y) LibLocal => Fp.Back ? (Local.X, -Local.Y) : Local;

    /// Absolute orientation (degrees, as stored).
    public double Angle => At.Count > 3 ? At[3].D() : 0;

    /// Orientation relative to the footprint, in the library frame.
    public double LibAngle => Nm.Norm360(Fp.Back ? Fp.Orient - Angle : Angle - Fp.Orient);

    public (long X, long Y) Position
    {
        get
        {
            var (x, y) = Nm.Rotate(Local.X, Local.Y, Fp.Orient);
            return (Fp.X + x, Fp.Y + y);
        }
    }
}

public sealed class BFootprint
{
    public required List<object> Node { get; init; }
    public string Id => Node[1].Txt();
    public string Ref => Field("Reference");
    public string Value => Field("Value");
    public long X => Nm.Of(Sexpr.Find(Node, "at")![1]);
    public long Y => Nm.Of(Sexpr.Find(Node, "at")![2]);
    public double Orient => Sexpr.Find(Node, "at")!.Count > 3 ? Sexpr.Find(Node, "at")![3].D() : 0;
    public bool Back => Sexpr.Find(Node, "layer")![1].Txt() == "B.Cu";
    public List<BPad> Pads { get; } = [];
    public bool Dnp => Sexpr.Find(Node, "attr")?.Skip(1).Any(e => e.Txt() == "dnp") ?? false;
    public string? SymbolPath => Sexpr.Find(Node, "path")?[1].Txt();

    public string Field(string name) =>
        Sexpr.FindAll(Node, "property").FirstOrDefault(p => p[1].Txt() == name)?[2].Txt() ?? "";

    /// A local point (as stored) to absolute nm.
    public (long X, long Y) Abs((long X, long Y) local)
    {
        var (x, y) = Nm.Rotate(local.X, local.Y, Orient);
        return (X + x, Y + y);
    }

    /// Graphic items (the fp_* shapes: line, rect, circle, arc, poly, curve;
    /// not fp_text or fp_text_box) on a layer.
    public IEnumerable<List<object>> Graphics(string layer) =>
        Node.OfType<List<object>>().Where(e => e[0].Txt().StartsWith("fp_") && e[0].Txt() is not ("fp_text" or "fp_text_box") &&
            Sexpr.Find(e, "layer")?[1].Txt() == layer);
}

/// One contour point list (closed implicitly), nm.
public sealed record Contour(List<(long X, long Y)> Pts);

public sealed class Board
{
    public string Path { get; }
    public List<object> Root { get; }
    public List<BFootprint> Footprints { get; } = [];

    private Board(string path, List<object> root)
    {
        Path = path;
        Root = root;
        foreach (var e in root.OfType<List<object>>().Where(e => e[0].Txt() == "footprint"))
        {
            var fp = new BFootprint { Node = e };
            fp.Pads.AddRange(Sexpr.FindAll(e, "pad").Select(p => new BPad { Node = p, Fp = fp }));
            Footprints.Add(fp);
        }
    }

    /// Throws on a file that is not a whole .kicad_pcb (empty, cut off, or
    /// another kind of s-expression).
    public static Board Load(string path) =>
        Sexpr.Parse(File.ReadAllText(path)) is List<object> { Count: > 0 } root && root[0] is Sym { Value: "kicad_pcb" }
            ? new(path, root)
            : throw new InvalidDataException(path + " is not a .kicad_pcb");

    public IEnumerable<List<object>> Items(string head) =>
        Root.OfType<List<object>>().Where(e => e[0].Txt() == head);

    public static bool Locked(List<object> item) => Sexpr.Find(item, "locked")?[1].Txt() == "yes";

    public static string NetOf(List<object> item) => Sexpr.Find(item, "net")?[1].Txt() ?? "";

    public static bool IsRuleArea(List<object> zone) => Sexpr.Find(zone, "keepout") is not null;

    /// The zone's layer tokens as written: "*.Cu" and "F&B.Cu" are not expanded.
    public static IReadOnlyList<string> ZoneLayers(List<object> zone) =>
        (Sexpr.Find(zone, "layers") ?? Sexpr.Find(zone, "layer"))!.Skip(1).Select(e => e.Txt()).ToList();

    /// The zone's outline as drawn, not its copper (see ZoneFills).
    public static List<(long X, long Y)> ZoneOutline(List<object> zone) =>
        Pts(Sexpr.Find(Sexpr.Find(zone, "polygon")!, "pts")!);

    /// The zone's copper as last filled: one polygon per filled island and
    /// layer, holes cut in as KiCad writes them (one outline, so Contains
    /// applies directly). Empty until the board was saved after a refill
    /// (`kicad-cli pcb drc --refill-zones --save-board`).
    public static List<(string Layer, List<(long X, long Y)> Pts)> ZoneFills(List<object> zone) =>
        Sexpr.FindAll(zone, "filled_polygon")
            .Select(f => (Sexpr.Find(f, "layer")![1].Txt(), Pts(Sexpr.Find(f, "pts")!))).ToList();

    public static List<(long X, long Y)> Pts(List<object> pts) =>
        pts.Skip(1).OfType<List<object>>().Where(xy => xy[0].Txt() == "xy")
            .Select(xy => (Nm.Of(xy[1]), Nm.Of(xy[2]))).ToList();

    public static (long X, long Y) Xy(List<object> node, string key)
    {
        var n = Sexpr.Find(node, key)!;
        return (Nm.Of(n[1]), Nm.Of(n[2]));
    }

    /// The routed (unlocked) tracks, arcs and vias; with drop, also take them off.
    public List<List<object>> RoutedCopper(bool drop = false)
    {
        var r = Root.OfType<List<object>>()
            .Where(e => e[0].Txt() is "segment" or "via" or "arc" && !Locked(e)).ToList();
        if (drop)
            Root.RemoveAll(e => r.Contains(e));
        return r;
    }

    public void Add(List<object> item)
    {
        int i = Root.FindLastIndex(e => e is List<object> l && l[0].Txt() == "embedded_fonts");
        Root.Insert(i < 0 ? Root.Count : i, item);
    }

    public static List<object> Segment((long X, long Y) a, (long X, long Y) b, long width, string layer, string net,
        string uuid, bool locked = false)
    {
        var s = Sexpr.S("segment", Sexpr.S("start", Nm.Mm(a.X), Nm.Mm(a.Y)), Sexpr.S("end", Nm.Mm(b.X), Nm.Mm(b.Y)),
            Sexpr.S("width", Nm.Mm(width)));
        if (locked)
            s.Add(Sexpr.S("locked", new Sym("yes")));
        s.AddRange([Sexpr.S("layer", layer), Sexpr.S("net", net), Sexpr.S("uuid", uuid)]);
        return s;
    }

    public static List<object> Via((long X, long Y) at, long size, long drill, string net, string uuid,
        bool locked = false)
    {
        var v = Sexpr.S("via", Sexpr.S("at", Nm.Mm(at.X), Nm.Mm(at.Y)), Sexpr.S("size", Nm.Mm(size)),
            Sexpr.S("drill", Nm.Mm(drill)), Sexpr.S("layers", "F.Cu", "B.Cu"));
        if (locked)
            v.Add(Sexpr.S("locked", new Sym("yes")));
        v.AddRange([Sexpr.S("net", net), Sexpr.S("uuid", uuid)]);
        return v;
    }

    /// Write the tree and resave it in KiCad's own format.
    public void Save(string? path = null)
    {
        path ??= Path;
        File.WriteAllText(path, Sexpr.Dump(Root) + "\n");
        Cli.Run("pcb", "upgrade", "--force", path);
    }

    // ------------------------------------------------------- Edge.Cuts
    /// Edge.Cuts shapes (board and footprints) chained into closed contours,
    /// arcs and circles approximated within maxError (nm). Throws
    /// NotSupportedException on a Bezier (gr_curve, fp_curve).
    public List<Contour> EdgeContours(long maxError = 5000) =>
        Contours(EdgeItems().Select(e => (e, Abs: Same))
                 .Concat(Footprints.SelectMany(fp => fp.Graphics("Edge.Cuts").Select(e => (e, Abs: (Func<(long, long), (long X, long Y)>)fp.Abs)))),
            maxError);

    private static readonly Func<(long, long), (long X, long Y)> Same = p => p;

    private IEnumerable<List<object>> EdgeItems() =>
        Root.OfType<List<object>>().Where(e => e[0].Txt().StartsWith("gr_") && Sexpr.Find(e, "layer")?[1].Txt() == "Edge.Cuts");

    /// A footprint's courtyard on F.CrtYd or B.CrtYd as closed contours
    /// (what pcbnew's GetCourtyard holds: the shapes' centre lines).
    public static List<Contour> Courtyard(BFootprint fp, string layer, long maxError = 5000) =>
        Contours(fp.Graphics(layer).Select(e => (e, Abs: (Func<(long, long), (long X, long Y)>)fp.Abs)), maxError);

    /// Bounding box of the board-level Edge.Cuts shapes (footprints' are not
    /// included), grown by half their stroke width (pcbnew's
    /// GetBoardEdgesBoundingBox): left, top, right, bottom. Throws when the
    /// board has no outline, and on a Bezier as EdgeContours does.
    public (long L, long T, long R, long B) EdgeBox()
    {
        long l = long.MaxValue, t = long.MaxValue, r = long.MinValue, b = long.MinValue;
        foreach (var e in EdgeItems())
        {
            if (Sexpr.Find(Sexpr.Find(e, "stroke") ?? [], "width") is not { } width)
                continue;           // gr_text: no outline
            var half = Nm.Of(width[1]) / 2;
            foreach (var c in Contours([(e, Same)], 5000))
                foreach (var (x, y) in c.Pts)
                    (l, t, r, b) = (Math.Min(l, x - half), Math.Min(t, y - half), Math.Max(r, x + half), Math.Max(b, y + half));
        }
        if (l > r)
            throw new InvalidOperationException($"{Path}: no Edge.Cuts outline");
        return (l, t, r, b);
    }

    /// Shapes chained into closed contours: closed shapes as they are, open
    /// ones (lines, arcs) joined end to end, in any order and direction. A
    /// chain that never closes (an open outline: a DRC error) comes back as
    /// one contour, closed implicitly.
    public static List<Contour> Contours(IEnumerable<(List<object> Item, Func<(long, long), (long X, long Y)> Abs)> shapes,
        long maxError)
    {
        var segs = new List<List<(long X, long Y)>>();     // open polylines, each a single shape
        var closed = new List<Contour>();
        void Shape(List<object> e, Func<(long, long), (long X, long Y)> abs)
        {
            switch (e[0].Txt()[3..])
            {
                case "line":
                    segs.Add([abs(Xy(e, "start")), abs(Xy(e, "end"))]);
                    break;
                case "arc":
                    {
                        var (s, m, t) = (abs(Xy(e, "start")), abs(Xy(e, "mid")), abs(Xy(e, "end")));
                        segs.Add(ArcPoints(s, m, t, maxError));
                        break;
                    }
                case "circle":
                    {
                        var (c, p) = (abs(Xy(e, "center")), abs(Xy(e, "end")));
                        closed.Add(new(CirclePoints(c, double.Hypot(p.X - c.X, p.Y - c.Y), maxError)));
                        break;
                    }
                case "rect":
                    {
                        var (a, b) = (Xy(e, "start"), Xy(e, "end"));
                        closed.Add(new([abs(a), abs((b.X, a.Y)), abs(b), abs((a.X, b.Y))]));
                        break;
                    }
                case "poly":
                    closed.Add(new(Pts(Sexpr.Find(e, "pts")!).Select(abs).ToList()));
                    break;
                case "curve":
                    throw new NotSupportedException($"{e[0].Txt()} (a Bezier) on an outline: draw it with arcs or lines");
            }
        }
        foreach (var (e, abs) in shapes)
            Shape(e, abs);
        // chain the open pieces end to end
        while (segs.Count > 0)
        {
            var chain = segs[0];
            segs.RemoveAt(0);
            bool grown = true;
            while (grown && chain[0] != chain[^1])
            {
                grown = false;
                for (int i = 0; i < segs.Count; i++)
                {
                    var s = segs[i];
                    if (Near(s[0], chain[^1])) chain.AddRange(s.Skip(1));
                    else if (Near(s[^1], chain[^1])) chain.AddRange(Enumerable.Reverse(s).Skip(1));
                    else if (Near(s[^1], chain[0])) chain.InsertRange(0, s.SkipLast(1));
                    else if (Near(s[0], chain[0])) chain.InsertRange(0, Enumerable.Reverse(s).SkipLast(1));
                    else continue;
                    segs.RemoveAt(i);
                    grown = true;
                    break;
                }
            }
            if (Near(chain[0], chain[^1]))
                chain.RemoveAt(chain.Count - 1);
            closed.Add(new(chain));
        }
        return closed;
    }

    private static bool Near((long X, long Y) a, (long X, long Y) b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) <= 2;

    /// Segments for an arc of the given sweep, radius r, within error
    /// (KiCad's rule: at most 45 degrees a segment, at least 2).
    public static int SegCount(double r, double error, double degrees)
    {
        double inc = 180 / Math.PI * Math.Acos(1.0 - error / Math.Max(1, r)) * 2;
        inc = Math.Min(360.0 / 8, inc);
        return Math.Max((int)Math.Round(degrees / inc, MidpointRounding.AwayFromZero), 2);
    }

    /// Arc start..end through mid: the end points, and between them the
    /// midpoints of n equal steps, on r + error/2 (chords straddle the arc).
    public static List<(long X, long Y)> ArcPoints((long X, long Y) s, (long X, long Y) m, (long X, long Y) t,
        long error)
    {
        var (cx, cy) = Center(s, m, t);
        double r = double.Hypot(s.X - cx, s.Y - cy);
        double a0 = Math.Atan2(s.Y - cy, s.X - cx), am = Math.Atan2(m.Y - cy, m.X - cx), a1 = Math.Atan2(t.Y - cy, t.X - cx);
        double sweep = a1 - a0;
        // pick the sweep direction that passes through mid
        double Wrap(double a) => a - 2 * Math.PI * Math.Floor((a + Math.PI) / (2 * Math.PI));
        double dm = Wrap(am - a0);
        sweep = Wrap(sweep);
        if (Math.Sign(dm) != Math.Sign(sweep) || Math.Abs(dm) > Math.Abs(sweep))
            sweep = sweep > 0 ? sweep - 2 * Math.PI : sweep + 2 * Math.PI;
        int n = SegCount(r, error, Math.Abs(sweep) * 180 / Math.PI);
        double rr = r + error / 2.0;
        var pts = new List<(long, long)> { s };
        for (int k = 0; k < n; k++)
        {
            double a = a0 + sweep * (k + 0.5) / n;
            pts.Add(((long)Math.Round(cx + rr * Math.Cos(a)), (long)Math.Round(cy + rr * Math.Sin(a))));
        }
        pts.Add(t);
        return pts;
    }

    public static List<(long X, long Y)> CirclePoints((long X, long Y) c, double r, long error)
    {
        int n = SegCount(r, error, 360);
        double rr = r + error / 2.0;
        return Enumerable.Range(0, n).Select(k => 2 * Math.PI * (k + 0.5) / n)
            .Select(a => ((long)Math.Round(c.X + rr * Math.Cos(a)), (long)Math.Round(c.Y + rr * Math.Sin(a)))).ToList();
    }

    private static (double X, double Y) Center((long X, long Y) a, (long X, long Y) b, (long X, long Y) c)
    {
        double ax = a.X, ay = a.Y, bx = b.X, by = b.Y, cx = c.X, cy = c.Y;
        double d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
        double ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / d;
        double uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / d;
        return (ux, uy);
    }

    /// Area of a contour (absolute), nm^2.
    public static double Area(IReadOnlyList<(long X, long Y)> p)
    {
        double s = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var (a, b) = (p[i], p[(i + 1) % p.Count]);
            s += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        return Math.Abs(s) / 2;
    }

    /// Even-odd point-in-polygon with SHAPE_LINE_CHAIN::PointInside's edge rule:
    /// a point on a left or bottom (max y) edge is inside, on a right or top one outside.
    public static bool Contains(IReadOnlyList<(long X, long Y)> poly, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var (pi, pj) = (poly[i], poly[j]);
            if ((pi.Y >= y) != (pj.Y >= y) && x < (double)(pj.X - pi.X) * (y - pi.Y) / (pj.Y - pi.Y) + pi.X)
                inside = !inside;
        }
        return inside;
    }
}
