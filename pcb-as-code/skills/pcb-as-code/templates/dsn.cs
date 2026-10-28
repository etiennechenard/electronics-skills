// Specctra DSN writer for FreeRouting, from a .kicad_pcb read as text
// (kicad-cli has no DSN export, and pcbnew is not used). Modelled on
// pcbnew's ExportSpecctraDSN, layout included (Specctra.Dsn.Shuffle relies
// on it):
//
//   - boundary: the largest Edge.Cuts contour; every other contour (a
//     cutout, drawn on the board or in a footprint) becomes a signal keepout
//   - rule areas: keepout (no tracks, no vias), wire_keepout (no tracks) or
//     via_keepout (no vias), one per copper layer
//   - one image per footprint id, pins in the footprint's front-side frame
//     (a back-side part is placed "back" and mirrored by the router), round
//     holes with no copper ring (NPTH, or a plated hole as big as its pad)
//     as keepouts of the hole plus the hole clearance
//   - padstacks named as pcbnew names them (Round / Rect / Oval / RoundRect,
//     [A] all copper layers, [T] / [B] one side only), round rects as
//     polygons
//   - nets, net classes from the .kicad_pro patterns and assignments
//     (KiCad 8+: the first class in .kicad_pro order wins), locked tracks and
//     locked vias as fixed wiring (locked arcs are not written)
//
// Two copper layers (F.Cu, B.Cu): Write refuses a board with inner layers.
// Units: um, resolution 10; y up (negated). Numbers are written as pcbnew
// writes them (6 significant digits).
//
// Library file for #:include — types only, no top-level statements.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PcbFile;
using Sx;

namespace Specctra;

public sealed record NetClassRule(string Name, long Track, long Clearance, long ViaD, long ViaDrill, string[] Patterns);

public static class DsnWriter
{
    /// Net classes of a .kicad_pro: (Default first, then the others with
    /// their patterns), and the board's hole clearance.
    public static (List<NetClassRule> Classes, long HoleClearance) Project(string kicadPro)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(kicadPro));
        var ns = doc.RootElement.GetProperty("net_settings");
        var pats = (ns.TryGetProperty("netclass_patterns", out var np) && np.ValueKind == JsonValueKind.Array ? np.EnumerateArray().ToList() : [])
            .Select(p => (p.GetProperty("netclass").GetString()!, p.GetProperty("pattern").GetString()!)).ToList();
        // a net assigned to a class by name: {"NET": "Class"} or {"NET": ["Class", ...]}
        if (ns.TryGetProperty("netclass_assignments", out var asg) && asg.ValueKind == JsonValueKind.Object)
            foreach (var a in asg.EnumerateObject())
                foreach (var cls in a.Value.ValueKind == JsonValueKind.Array ? a.Value.EnumerateArray().ToList() : [a.Value])
                    if (cls.ValueKind == JsonValueKind.String)
                        pats.Add((cls.GetString()!, Regex.Escape(a.Name)));
        static long Nm(JsonElement e, string k) => (long)Math.Round(e.GetProperty(k).GetDouble() * 1e6);
        var classes = ns.GetProperty("classes").EnumerateArray().Select(c =>
        {
            var name = c.GetProperty("name").GetString()!;
            return new NetClassRule(name, Nm(c, "track_width"), Nm(c, "clearance"), Nm(c, "via_diameter"),
                Nm(c, "via_drill"), pats.Where(p => p.Item1 == name).Select(p => p.Item2).ToArray());
        }).OrderBy(c => c.Name == "Default" ? 0 : 1).ToList();      // stable: the others keep their order
        var rules = doc.RootElement.GetProperty("board").GetProperty("design_settings").GetProperty("rules");
        return (classes, Nm(rules, "min_hole_clearance"));
    }

    /// KiCad's net-class pattern match: anchored regex or anchored wildcard.
    public static bool Matches(string pattern, string net)
    {
        try
        {
            if (Regex.IsMatch(net, "^(?:" + pattern + ")$"))
                return true;
        }
        catch (ArgumentException) { }
        var wc = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return Regex.IsMatch(net, wc);
    }

    public static string ClassOf(IReadOnlyList<NetClassRule> classes, string net) =>
        classes.Skip(1).FirstOrDefault(c => c.Patterns.Any(p => Matches(p, net)))?.Name ?? classes[0].Name;

    // ----------------------------------------------------------- formatting
    private static string G(double um)
    {
        var s = um.ToString("G6", CultureInfo.InvariantCulture);
        if (s.Contains('E'))
            s = um.ToString("0.#####", CultureInfo.InvariantCulture);
        return s == "-0" ? "0" : s;
    }

    private static string U(long nm) => G(nm / 1000.0);
    private static string X(long nm) => U(nm);
    private static string Y(long nm) => U(-nm);

    /// pcbnew's quoting rule: delimiters, '-' after the first character, a
    /// leading '#', or empty.
    public static string Q(string s)
    {
        if (s.Length == 0 || s[0] == '#' || s.Any(c => "\t ()%{}".Contains(c)) || s.Skip(1).Contains('-'))
            return "\"" + s + "\"";
        return s;
    }

    private static string F6(long nm) => (nm / 1000.0).ToString("F6", CultureInfo.InvariantCulture);

    private static string Pts(IEnumerable<(long X, long Y)> pts, bool close, string indent = "            ")
    {
        var l = pts.ToList();
        if (close && l.Count > 0)
            l.Add(l[0]);
        var sb = new StringBuilder();
        int col = 0;
        foreach (var p in l)
        {
            var t = "  " + X(p.X) + " " + Y(p.Y);
            if (col > 0 && col + t.Length > 80)
            {
                sb.Append('\n').Append(indent);
                col = 0;
                t = t.TrimStart();
            }
            sb.Append(t);
            col += t.Length;
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------ padstacks
    private sealed record Stack(string Name, string Body);

    private static Stack Padstack(BPad pad)
    {
        var cu = pad.Layers.Where(l => l.EndsWith(".Cu")).ToList();
        bool all = cu.Contains("*.Cu") || (cu.Contains("F.Cu") && cu.Contains("B.Cu"));
        // the image is the front-side footprint: a pad on the part's own side
        // is top, one on the far side (B on a front part, F on a back one) bottom
        bool top = all || cu.Contains(pad.Fp.Back ? "B.Cu" : "F.Cu");
        string side = all ? "[A]" : top ? "[T]" : "[B]";
        string[] layers = all ? ["F.Cu", "B.Cu"] : top ? ["F.Cu"] : ["B.Cu"];
        long w = pad.SizeX, h = pad.SizeY;
        double dx = w / 2.0, dy = h / 2.0;
        var shapes = new List<string>();
        string name;
        switch (pad.Shape)
        {
            case "circle":
                name = $"Round{side}Pad_{F6(w)}_um";
                shapes.AddRange(layers.Select(l => $"(circle {l} {U(w)})"));
                break;
            case "oval" when w != h:
                {
                    name = $"Oval{side}Pad_{F6(w)}x{F6(h)}_um";
                    string path = w > h
                        ? $"{U(h)}  {G(-(dx - dy) / 1000)} 0  {G((dx - dy) / 1000)} 0"
                        : $"{U(w)}  0 {G(-(dy - dx) / 1000)}  0 {G((dy - dx) / 1000)}";
                    shapes.AddRange(layers.Select(l => $"(path {l} {path})"));
                    break;
                }
            case "oval":
                name = $"Round{side}Pad_{F6(w)}_um";
                shapes.AddRange(layers.Select(l => $"(circle {l} {U(w)})"));
                break;
            case "roundrect":
                {
                    long r = (long)Math.Round(Math.Min(w, h) * pad.RRatio);
                    long rr = (long)Math.Round(r * (2 - Math.Cos(Math.PI / 36)));
                    name = $"RoundRect{side}Pad_{F6(w)}x{F6(h)}_{F6(rr)}_um_{pad.ChamferRatio.ToString("F6", CultureInfo.InvariantCulture)}_0";
                    var poly = RoundRect(w, h, r, rr);
                    shapes.AddRange(layers.Select(l => $"(polygon {l} 0 {Pts(poly, true)})"));
                    break;
                }
            default:            // rect; chamfered_rect and trapezoid as their full rect, custom as its anchor rect (primitives not exported)
                name = $"Rect{side}Pad_{F6(w)}x{F6(h)}_um";
                shapes.AddRange(layers.Select(l => $"(rect {l} {G(-dx / 1000)} {G(-dy / 1000)} {G(dx / 1000)} {G(dy / 1000)})"));
                break;
        }
        var body = new StringBuilder();
        body.Append($"    (padstack {Q(name)}\n");
        foreach (var s in shapes)
            body.Append($"      (shape {s})\n");
        body.Append("      (attach off)\n    )\n");
        return new(name, body.ToString());
    }

    /// Rounded rectangle w x h, corner radius r, as a polygon (KiCad y-down
    /// nm): corner arcs on radius rr, steps of the full-circle segment count
    /// for a 5 um error, partial steps split evenly at the arc ends.
    private static List<(long X, long Y)> RoundRect(long w, long h, long r, long rr)
    {
        int n360 = Board.SegCount(rr, 5000, 360);
        double step = 360.0 / n360;
        var angles = new List<double>();
        double q = 90 / step;
        if (Math.Abs(q - Math.Round(q)) < 1e-9)
            for (int k = 0; k <= (int)Math.Round(q); k++) angles.Add(k * step);
        else
        {
            int m = (int)Math.Ceiling(q);
            double part = (90 - (m - 2) * step) / 2;
            angles.Add(0);
            for (int k = 0; k < m - 1; k++) angles.Add(part + k * step);
            angles.Add(90);
        }
        double cx = w / 2.0 - r, cy = h / 2.0 - r;
        var pts = new List<(long, long)>();
        // y-up DSN order: top-left (180 -> 90), top-right, bottom-right,
        // bottom-left; returned in y-down coordinates (the writer negates y)
        (double X, double Y, double A0)[] corners = [(-cx, cy, 180), (cx, cy, 90), (cx, -cy, 0), (-cx, -cy, -90)];
        foreach (var (ox, oy, a0) in corners)
            foreach (var a in angles)
            {
                double t = (a0 - a) * Math.PI / 180;
                pts.Add(((long)Math.Round(ox + rr * Math.Cos(t)), -(long)Math.Round(oy + rr * Math.Sin(t))));
            }
        return pts;
    }

    /// pcbnew's isRoundKeepout: such a pad is written as a hole keepout, not
    /// a pin (pcbnew drops it from its net too; DRC reports it unconnected).
    private static bool RoundKeepout(BPad p) => p.Shape == "circle" && (p.Drill.X >= p.SizeX || !p.OnCopper);

    // --------------------------------------------------------------- writer
    public static string Write(Board b, IReadOnlyList<NetClassRule> classes, long holeClearance, string name)
    {
        var inner = (Sexpr.Find(b.Root, "layers") ?? []).OfType<List<object>>()
            .Select(l => l.Count > 1 ? l[1].Txt() : "").Where(l => l.StartsWith("In") && l.EndsWith(".Cu")).ToList();
        if (inner.Count > 0)
            throw new NotSupportedException($"dsn.cs writes two-layer boards; this one has {string.Join(", ", inner)}");
        var sb = new StringBuilder();
        sb.Append($"(pcb {Q(name)}\n  (parser\n    (string_quote \")\n    (space_in_quoted_tokens on)\n" +
                  "    (host_cad \"KiCad's Pcbnew\")\n    (host_version \"10.0.7\")\n  )\n" +
                  "  (resolution um 10)\n  (unit um)\n  (structure\n");
        sb.Append("    (layer F.Cu\n      (type signal)\n      (property\n        (index 0)\n      )\n    )\n");
        sb.Append("    (layer B.Cu\n      (type signal)\n      (property\n        (index 1)\n      )\n    )\n");

        // boundary: the biggest contour; the rest are cutouts
        var contours = b.EdgeContours().OrderByDescending(c => Board.Area(c.Pts)).ToList();
        if (contours.Count == 0)
            throw new InvalidOperationException("no Edge.Cuts outline");
        sb.Append($"    (boundary\n      (path pcb 0 {Pts(contours[0].Pts, true)})\n    )\n");
        foreach (var c in contours.Skip(1))
            sb.Append($"    (keepout \"\" (polygon signal 0 {Pts(c.Pts, true)}))\n");

        foreach (var z in b.Items("zone").Where(Board.IsRuleArea))
        {
            var ko = Sexpr.Find(z, "keepout")!;
            bool tracks = Sexpr.Find(ko, "tracks")![1].Txt() == "not_allowed";
            bool noVias = Sexpr.Find(ko, "vias")![1].Txt() == "not_allowed";
            var kind = tracks && noVias ? "keepout" : tracks ? "wire_keepout" : noVias ? "via_keepout" : null;
            if (kind is null)
                continue;
            var pts = Board.ZoneOutline(z);
            foreach (var l in Board.ZoneLayers(z).SelectMany(l => l == "*.Cu" || l == "F&B.Cu" ? ["F.Cu", "B.Cu"] : new[] { l })
                         .Where(l => l is "F.Cu" or "B.Cu"))
                sb.Append($"    ({kind} \"\" (polygon {l} 0 {Pts(pts, true)}))\n");
        }

        var vias = classes.Select(c => (Name: $"Via[0-1]_{c.ViaD / 1000}:{c.ViaDrill / 1000}_um", c.ViaD)).ToList();
        var viaNames = vias.Select(v => v.Name).Distinct().ToList();
        sb.Append($"    (via {string.Join(" ", viaNames.Select(Q))})\n");
        sb.Append($"    (rule\n      (width {U(classes[0].Track)})\n      (clearance {U(classes[0].Clearance)})\n" +
                  "      (clearance 50 (type smd_smd))\n    )\n  )\n");

        // placement + images
        var groups = b.Footprints.GroupBy(f => f.Id).ToList();
        sb.Append("  (placement\n");
        foreach (var g in groups)
        {
            sb.Append($"    (component {Q(g.Key)}\n");
            foreach (var f in g)
            {
                double rot = Nm.Norm360(f.Back ? f.Orient + 180 : f.Orient);
                sb.Append($"      (place {Q(f.Ref)} {X(f.X)} {Y(f.Y)} {(f.Back ? "back" : "front")} {G(rot)} (PN {Q(f.Value)}))\n");
            }
            sb.Append("    )\n");
        }
        sb.Append("  )\n  (library\n");
        var stacks = new List<Stack>();
        var pinNames = new Dictionary<BPad, string>(ReferenceEqualityComparer.Instance);
        foreach (var g in groups)
        {
            var img = new StringBuilder();
            img.Append($"    (image {Q(g.Key)}\n");
            var f = g.First();
            var seen = new Dictionary<string, int>();
            foreach (var pad in f.Pads)
            {
                var (lx, ly) = pad.LibLocal;
                var (hx, hy) = pad.Drill;
                if (RoundKeepout(pad))
                {
                    long d = hx + 2 * holeClearance;
                    var at = lx == 0 && ly == 0 ? "" : $" {X(lx)} {Y(ly)}";
                    foreach (var l in new[] { "F.Cu", "B.Cu" })
                        img.Append($"      (keepout \"\" (circle {l} {U(d)}{at}))\n");
                    continue;
                }
                if (!pad.OnCopper)
                    continue;
                var st = Padstack(pad);
                if (stacks.All(s => s.Name != st.Name))
                    stacks.Add(st);
                var num = pad.Number;
                string pin = num;
                if (num != "" && !seen.ContainsKey(num))
                    seen[num] = 0;
                else
                    pin += "@" + (seen[num] = seen.GetValueOrDefault(num) + 1);
                double rel = pad.LibAngle;
                var rotTxt = Math.Abs(rel) < 1e-9 || Math.Abs(rel - 360) < 1e-9 ? "" : $" (rotate {G(rel)})";
                img.Append($"      (pin {Q(st.Name)}{rotTxt} {Q(pin)} {X(lx)} {Y(ly)})\n");
            }
            img.Append("    )\n");
            sb.Append(img);
            // every footprint of the group shares the image's pin names
            foreach (var fp in g)
            {
                var s2 = new Dictionary<string, int>();
                foreach (var pad in fp.Pads)
                {
                    if (RoundKeepout(pad) || !pad.OnCopper)
                        continue;
                    var num = pad.Number;
                    string pin = num;
                    if (num != "" && !s2.ContainsKey(num))
                        s2[num] = 0;
                    else
                        pin += "@" + (s2[num] = s2.GetValueOrDefault(num) + 1);
                    pinNames[pad] = pin;
                }
            }
        }
        foreach (var s in stacks)
            sb.Append(s.Body);
        // locked vias: fixed wiring, with padstacks of their own size
        static string ViaName(long d, long drill) => $"Via[0-1]_{d / 1000}:{drill / 1000}_um";
        var fixedVias = b.Items("via").Where(Board.Locked).Select(v =>
            (At: Board.Xy(v, "at"), D: Nm.Of(Sexpr.Find(v, "size")![1]), Drill: Nm.Of(Sexpr.Find(v, "drill")![1]), Net: Board.NetOf(v)))
            .ToList();
        foreach (var v in fixedVias)
            vias.Add((ViaName(v.D, v.Drill), v.D));
        foreach (var v in vias.DistinctBy(v => v.Name))
            sb.Append($"    (padstack {Q(v.Name)}\n      (shape (circle F.Cu {U(v.ViaD)}))\n" +
                      $"      (shape (circle B.Cu {U(v.ViaD)}))\n      (attach off)\n    )\n");
        sb.Append("  )\n");

        // network
        var nets = new Dictionary<string, List<string>>();
        foreach (var fp in b.Footprints)
            foreach (var pad in fp.Pads)
            {
                if (pad.Net is not { Length: > 0 } net || !pinNames.TryGetValue(pad, out var pin))
                    continue;
                if (!nets.TryGetValue(net, out var l))
                    nets[net] = l = [];
                l.Add($"{Q(fp.Ref)}-{Q(pin)}");          // pcbnew quotes each half
            }
        // a fixed wire's net must be in the network: copper with no pad to
        // reach is a generator error, not something to route around
        foreach (var net in b.Items("segment").Concat(b.Items("via")).Where(Board.Locked).Select(Board.NetOf).Distinct())
            if (!nets.ContainsKey(net))
                throw new InvalidOperationException($"locked copper on net \"{net}\", which has no pad");
        sb.Append("  (network\n");
        foreach (var (net, pins) in nets)
            sb.Append($"    (net {Q(net)}\n      (pins {string.Join(" ", pins)})\n    )\n");
        foreach (var c in classes)
        {
            var members = nets.Keys.Where(n => ClassOf(classes, n) == c.Name).Order(StringComparer.Ordinal).ToList();
            if (members.Count == 0)
                continue;
            var cname = c.Name == "Default" ? "kicad_default" : c.Name;
            sb.Append($"    (class {Q(cname)} {string.Join(" ", members.Select(Q))}\n" +
                      $"      (circuit\n        (use_via {Q($"Via[0-1]_{c.ViaD / 1000}:{c.ViaDrill / 1000}_um")})\n      )\n" +
                      $"      (rule\n        (width {U(c.Track)})\n        (clearance {U(c.Clearance)})\n      )\n    )\n");
        }
        sb.Append("  )\n  (wiring\n");
        // pcbnew chains consecutive tracks (file order, same net, layer and
        // width, each starting where the previous one ended) into one path
        var wires = new List<(string Net, string Layer, long W, List<(long X, long Y)> Pts)>();
        foreach (var t in b.Items("segment").Where(Board.Locked))
        {
            var (s, e) = (Board.Xy(t, "start"), Board.Xy(t, "end"));
            var w = Nm.Of(Sexpr.Find(t, "width")![1]);
            var (net, layer) = (Board.NetOf(t), Sexpr.Find(t, "layer")![1].Txt());
            if (wires.Count > 0 && wires[^1].Net == net && wires[^1].Layer == layer && wires[^1].W == w && wires[^1].Pts[^1] == s)
                wires[^1].Pts.Add(e);
            else
                wires.Add((net, layer, w, [s, e]));
        }
        foreach (var (net, layer, w, pts) in wires)
            sb.Append($"    (wire (path {layer} {U(w)}{string.Concat(pts.Select(p => $"  {X(p.X)} {Y(p.Y)}"))})" +
                      $"(net {Q(net)})(type fix))\n");
        foreach (var v in fixedVias)
            sb.Append($"    (via {Q(ViaName(v.D, v.Drill))} {X(v.At.X)} {Y(v.At.Y)}(net {Q(v.Net)})(type fix))\n");
        sb.Append("  )\n)\n");
        return sb.ToString();
    }
}
