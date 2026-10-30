// A dependency-free software renderer for STL meshes: orthographic views
// (front, back, left, right, top, bottom, two isos), z-buffer, flat shading with
// crease and silhouette lines, per-mesh colours, numbered callout markers,
// a title and a scale bar, written as PNG. The views are for people and for
// the reviewer agents, so they favour legibility over looks.
//
// Library file for #:include — types only, no top-level statements.

using System.IO.Compression;
using Mesh;

namespace Raster;

/// Front looks along +y (the part's front faces -y), Top looks down -z; Iso
/// is from front-right-top, IsoBack from back-left-top.
public enum View { Front, Back, Left, Right, Top, Bottom, Iso, IsoBack }

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb White = new(255, 255, 255);
    public static readonly Rgb Ink = new(40, 40, 46);
    public static readonly Rgb Marker = new(200, 40, 40);

    /// Distinct, print-friendly body colours, picked in order.
    public static readonly Rgb[] Palette =
        [new(86, 132, 196), new(222, 140, 60), new(96, 168, 110), new(176, 104, 168), new(196, 176, 72), new(110, 110, 120)];
}

public sealed record Callout(string Label, V3 At);

/// A dimension line From..To with its label at the middle.
/// A dimension line From..To with its label at the middle; From == To is a
/// single labelled point. Left out of a view where it is hidden behind the
/// part or seen end-on.
public sealed record DimLine(string Label, V3 From, V3 To);

public sealed class Image(int w, int h)
{
    public int W { get; } = w;
    public int H { get; } = h;
    public byte[] Px { get; } = new byte[w * h * 3];

    public void Set(int x, int y, Rgb c)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        var i = (y * W + x) * 3;
        Px[i] = c.R; Px[i + 1] = c.G; Px[i + 2] = c.B;
    }

    public Rgb Get(int x, int y)
    {
        var i = (y * W + x) * 3;
        return new(Px[i], Px[i + 1], Px[i + 2]);
    }

    public void Fill(int x0, int y0, int x1, int y1, Rgb c)
    {
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
                Set(x, y, c);
    }

    /// 5×7 text, `scale` pixels per dot; lower case prints as upper case.
    public int Text(int x, int y, string s, Rgb c, int scale = 2)
    {
        foreach (var ch in s.ToUpperInvariant())
        {
            var cols = Font.Glyph(ch);
            for (var cx = 0; cx < 5; cx++)
                for (var cy = 0; cy < 7; cy++)
                    if ((cols[cx] >> cy & 1) != 0)
                        Fill(x + cx * scale, y + cy * scale, x + (cx + 1) * scale, y + (cy + 1) * scale, c);
            x += 6 * scale;
        }
        return x;
    }

    public static int TextWidth(string s, int scale = 2) => s.Length * 6 * scale - scale;

    /// A straight line `thick` pixels wide.
    public void Line(double x0, double y0, double x1, double y1, Rgb c, int thick = 2)
    {
        var steps = (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)));
        for (var i = 0; i <= steps; i++)
        {
            var t = steps == 0 ? 0 : i / (double)steps;
            int x = (int)Math.Round(x0 + (x1 - x0) * t) - thick / 2, y = (int)Math.Round(y0 + (y1 - y0) * t) - thick / 2;
            Fill(x, y, x + thick, y + thick, c);
        }
    }

    public void SavePng(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var f = File.Create(path);
        f.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BE(ihdr, 0, (uint)W);
        BE(ihdr, 4, (uint)H);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 2;   // RGB
        Chunk(f, "IHDR", ihdr);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            for (var y = 0; y < H; y++)
            {
                z.WriteByte(0);   // filter: none
                z.Write(Px, y * W * 3, W * 3);
            }
        Chunk(f, "IDAT", raw.ToArray());
        Chunk(f, "IEND", []);
    }

    static void BE(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var head = new byte[8];
        BE(head, 0, (uint)data.Length);
        for (var i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
        s.Write(head);
        s.Write(data);
        // the CRC covers the chunk type and its data
        var c = Crc(0xFFFFFFFFu, head.AsSpan(4));
        c = Crc(c, data) ^ 0xFFFFFFFFu;
        var tail = new byte[4];
        BE(tail, 0, c);
        s.Write(tail);
    }

    static readonly uint[] CrcTable = [.. Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    })];

    static uint Crc(uint c, ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c;
    }
}

static class Font
{
    // classic 5×7, five column bytes per glyph, bit 0 at the top
    static readonly Dictionary<char, byte[]> G = new()
    {
        ['0'] = [0x3E, 0x51, 0x49, 0x45, 0x3E], ['1'] = [0x00, 0x42, 0x7F, 0x40, 0x00],
        ['2'] = [0x42, 0x61, 0x51, 0x49, 0x46], ['3'] = [0x21, 0x41, 0x45, 0x4B, 0x31],
        ['4'] = [0x18, 0x14, 0x12, 0x7F, 0x10], ['5'] = [0x27, 0x45, 0x45, 0x45, 0x39],
        ['6'] = [0x3C, 0x4A, 0x49, 0x49, 0x30], ['7'] = [0x01, 0x71, 0x09, 0x05, 0x03],
        ['8'] = [0x36, 0x49, 0x49, 0x49, 0x36], ['9'] = [0x06, 0x49, 0x49, 0x29, 0x1E],
        ['A'] = [0x7E, 0x11, 0x11, 0x11, 0x7E], ['B'] = [0x7F, 0x49, 0x49, 0x49, 0x36],
        ['C'] = [0x3E, 0x41, 0x41, 0x41, 0x22], ['D'] = [0x7F, 0x41, 0x41, 0x22, 0x1C],
        ['E'] = [0x7F, 0x49, 0x49, 0x49, 0x41], ['F'] = [0x7F, 0x09, 0x09, 0x09, 0x01],
        ['G'] = [0x3E, 0x41, 0x49, 0x49, 0x7A], ['H'] = [0x7F, 0x08, 0x08, 0x08, 0x7F],
        ['I'] = [0x00, 0x41, 0x7F, 0x41, 0x00], ['J'] = [0x20, 0x40, 0x41, 0x3F, 0x01],
        ['K'] = [0x7F, 0x08, 0x14, 0x22, 0x41], ['L'] = [0x7F, 0x40, 0x40, 0x40, 0x40],
        ['M'] = [0x7F, 0x02, 0x0C, 0x02, 0x7F], ['N'] = [0x7F, 0x04, 0x08, 0x10, 0x7F],
        ['O'] = [0x3E, 0x41, 0x41, 0x41, 0x3E], ['P'] = [0x7F, 0x09, 0x09, 0x09, 0x06],
        ['Q'] = [0x3E, 0x41, 0x51, 0x21, 0x5E], ['R'] = [0x7F, 0x09, 0x19, 0x29, 0x46],
        ['S'] = [0x46, 0x49, 0x49, 0x49, 0x31], ['T'] = [0x01, 0x01, 0x7F, 0x01, 0x01],
        ['U'] = [0x3F, 0x40, 0x40, 0x40, 0x3F], ['V'] = [0x1F, 0x20, 0x40, 0x20, 0x1F],
        ['W'] = [0x3F, 0x40, 0x38, 0x40, 0x3F], ['X'] = [0x63, 0x14, 0x08, 0x14, 0x63],
        ['Y'] = [0x07, 0x08, 0x70, 0x08, 0x07], ['Z'] = [0x61, 0x51, 0x49, 0x45, 0x43],
        ['-'] = [0x08, 0x08, 0x08, 0x08, 0x08], ['.'] = [0x00, 0x60, 0x60, 0x00, 0x00],
        ['/'] = [0x20, 0x10, 0x08, 0x04, 0x02], [':'] = [0x00, 0x36, 0x36, 0x00, 0x00],
        ['+'] = [0x08, 0x08, 0x3E, 0x08, 0x08], ['_'] = [0x40, 0x40, 0x40, 0x40, 0x40],
        ['('] = [0x00, 0x1C, 0x22, 0x41, 0x00], [')'] = [0x00, 0x41, 0x22, 0x1C, 0x00],
    };

    static readonly byte[] Blank = [0, 0, 0, 0, 0];

    public static byte[] Glyph(char c) => G.GetValueOrDefault(c, Blank);
}

public sealed class Scene
{
    readonly List<(Stl Mesh, Rgb Color)> _items = [];
    readonly List<Callout> _callouts = [];
    readonly List<DimLine> _dims = [];

    public Scene Add(Stl mesh, Rgb? color = null)
    {
        _items.Add((mesh, color ?? Rgb.Palette[_items.Count % Rgb.Palette.Length]));
        return this;
    }

    public Scene Mark(Callout c)
    {
        _callouts.Add(c);
        return this;
    }

    public Scene Dim(DimLine d)
    {
        _dims.Add(d);
        return this;
    }

    /// The camera for a view: direction looked along, and screen right/up.
    public static (V3 Dir, V3 Right, V3 Up) Camera(View v)
    {
        var d = v switch
        {
            View.Front => new V3(0, 1, 0),
            View.Back => new V3(0, -1, 0),
            View.Left => new V3(1, 0, 0),
            View.Right => new V3(-1, 0, 0),
            View.Top => new V3(0, 0, -1),
            View.Bottom => new V3(0, 0, 1),
            View.IsoBack => new V3(1, -1, -1).Unit,
            _ => new V3(-1, 1, -1).Unit,
        };
        var up = v is View.Top ? new V3(0, 1, 0) : v is View.Bottom ? new V3(0, -1, 0) : new V3(0, 0, 1);
        var r = d.Cross(up).Unit;
        return (d, r, r.Cross(d).Unit);
    }

    public Image Render(View view, int w = 900, int h = 700, string title = "")
    {
        var img = new Image(w, h);
        img.Fill(0, 0, w, h, Rgb.White);
        var (d, r, u) = Camera(view);
        var all = _items.SelectMany(i => i.Mesh.Tris.SelectMany(t => new[] { t.A, t.B, t.C })).ToList();
        if (all.Count == 0)
        {
            img.Text(12, 12, title + " (EMPTY)", Rgb.Ink);
            return img;
        }
        // frame the marks too, so a dimension line set off the part stays in view
        var frame = all.Concat(_callouts.Select(c => c.At)).Concat(_dims.SelectMany(dl => new[] { dl.From, dl.To })).ToList();
        double sx0 = frame.Min(p => p.Dot(r)), sx1 = frame.Max(p => p.Dot(r));
        double sy0 = frame.Min(p => p.Dot(u)), sy1 = frame.Max(p => p.Dot(u));
        var margin = Math.Min(48, Math.Min(w, h) / 8);
        var scale = Math.Min((w - 2 * margin) / Math.Max(sx1 - sx0, 1e-9), (h - 2 * margin) / Math.Max(sy1 - sy0, 1e-9));
        var cx = w / 2.0 - (sx0 + sx1) / 2 * scale;
        var cy = h / 2.0 + (sy0 + sy1) / 2 * scale;
        (double X, double Y, double Z) Project(V3 p) => (cx + p.Dot(r) * scale, cy - p.Dot(u) * scale, p.Dot(d));

        var depth = new double[w * h];
        Array.Fill(depth, double.PositiveInfinity);
        var normal = new V3[w * h];
        var light = (d * -1 + u * 0.6 + r * -0.4).Unit;
        foreach (var (mesh, color) in _items)
            foreach (var t in mesh.Tris)
            {
                var n = t.Normal;
                if (n.Length == 0) continue;
                var a = Project(t.A); var b = Project(t.B); var c = Project(t.C);
                var area = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
                if (Math.Abs(area) < 1e-12) continue;
                // two-sided: a flipped face still renders (the mesh gate flags it)
                var shade = 0.35 + 0.65 * Math.Abs(n.Dot(light));
                var px = new Rgb((byte)(color.R * shade), (byte)(color.G * shade), (byte)(color.B * shade));
                int x0 = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
                int x1 = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
                int y0 = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
                int y1 = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
                for (var y = y0; y <= y1; y++)
                    for (var x = x0; x <= x1; x++)
                    {
                        double qx = x + 0.5, qy = y + 0.5;
                        var w0 = ((b.X - qx) * (c.Y - qy) - (c.X - qx) * (b.Y - qy)) / area;
                        var w1 = ((c.X - qx) * (a.Y - qy) - (a.X - qx) * (c.Y - qy)) / area;
                        var w2 = 1 - w0 - w1;
                        if (w0 < -1e-9 || w1 < -1e-9 || w2 < -1e-9) continue;
                        var z = w0 * a.Z + w1 * b.Z + w2 * c.Z;
                        var i = y * w + x;
                        if (z >= depth[i]) continue;
                        depth[i] = z;
                        normal[i] = n;
                        img.Set(x, y, px);
                    }
            }

        // lines: silhouette (covered next to empty), depth steps, creases
        var finite = depth.Where(double.IsFinite).ToList();
        if (finite.Count == 0)
        {
            // every triangle projected away (a flat sheet seen edge-on)
            img.Text(12, 12, title + " (EMPTY)", Rgb.Ink);
            return img;
        }
        var step = (finite.Max() - finite.Min()) * 0.02 + 1e-9;
        var crease = Math.Cos(30 * Math.PI / 180);
        var line = new bool[w * h];
        for (var y = 0; y < h - 1; y++)
            for (var x = 0; x < w - 1; x++)
            {
                var i = y * w + x;
                foreach (var j in new[] { i + 1, i + w })
                {
                    bool fi = double.IsFinite(depth[i]), fj = double.IsFinite(depth[j]);
                    if (fi != fj) { line[fi ? i : j] = true; continue; }
                    if (!fi) continue;
                    if (Math.Abs(depth[i] - depth[j]) > step || Math.Abs(normal[i].Dot(normal[j])) < crease)
                        line[depth[i] < depth[j] ? i : j] = true;
                }
            }
        for (var i = 0; i < line.Length; i++)
            if (line[i]) img.Set(i % w, i / w, Rgb.Ink);

        // labels and lines scale with the image, so they stay legible when
        // the picture is shown small (the parameter sheet's panel)
        var ts = Math.Max(2, w / 300);
        bool Hidden(V3 p)
        {
            var (x, y, z) = Project(p);
            int px = (int)x, py = (int)y;
            if (px < 0 || py < 0 || px >= w || py >= h) return false;
            var dz = depth[py * w + px];
            return double.IsFinite(dz) && dz < z - 0.5;
        }
        var labels = _callouts.Select(c => { var (x, y, _) = Project(c.At); return (c.Label, X: x, Y: y); }).ToList();
        foreach (var dl in _dims)
        {
            var mid = (dl.From + dl.To) * 0.5;
            if (Hidden(mid))
                continue;
            var (ax, ay, _) = Project(dl.From);
            var (bx, by, _) = Project(dl.To);
            var span = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            var point = (dl.To - dl.From).Length == 0;
            if (!point && span < 8 * ts)
                continue;
            var (lx, ly, _) = Project(mid);
            if (!point)
            {
                double ux = -(by - ay) / span, uy = (bx - ax) / span;
                if (uy > 0 || (uy == 0 && ux < 0))
                    (ux, uy) = (-ux, -uy);   // off to the upper side, or right of a vertical line
                double nx = ux * 4 * ts, ny = uy * 4 * ts;
                img.Line(ax, ay, bx, by, Rgb.Marker, ts);
                img.Line(ax - nx, ay - ny, ax + nx, ay + ny, Rgb.Marker, ts);
                img.Line(bx - nx, by - ny, bx + nx, by + ny, Rgb.Marker, ts);
                // a line shorter than its label would vanish under it: label beside it
                if (span < Image.TextWidth(dl.Label, ts) + 10 * ts)
                    (lx, ly) = (lx + ux * 10 * ts, ly + uy * 10 * ts);
            }
            labels.Add((dl.Label, lx, ly));
        }

        foreach (var (label, x, y) in labels)
        {
            int tw = Image.TextWidth(label, ts), th = 7 * ts, pad = 2 * ts;
            // kept inside the picture and clear of the title line
            int bx = Math.Clamp((int)x - tw / 2 - pad, 1, Math.Max(1, w - tw - 2 * pad - 2));
            int by = Math.Clamp((int)y - th / 2 - pad, 32, Math.Max(32, h - th - 2 * pad - 2));
            img.Fill(bx - 1, by - 1, bx + tw + 2 * pad + 1, by + th + 2 * pad + 1, Rgb.White);
            img.Fill(bx, by, bx + tw + 2 * pad, by + th + 2 * pad, Rgb.Marker);
            img.Text(bx + pad, by + pad, label, Rgb.White, ts);
        }

        // scale bar: a round length near a fifth of the width
        var target = (w / 5.0) / scale;
        var pow = Math.Pow(10, Math.Floor(Math.Log10(target)));
        var len = new[] { 1, 2, 5, 10 }.Select(k => k * pow).LastOrDefault(v => v <= target * 1.0001, pow);
        var bar = (int)Math.Round(len * scale);
        img.Fill(16, h - 22, 16 + bar, h - 18, Rgb.Ink);
        img.Text(16 + bar + 8, h - 28, $"{len:0.###} MM", Rgb.Ink);
        img.Text(12, 12, $"{title} {view}".Trim(), Rgb.Ink);
        return img;
    }
}
