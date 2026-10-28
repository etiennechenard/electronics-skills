// Specctra DSN/SES handling for the FreeRouting loop (pure text handling,
// no pcbnew needed):
//
//   - BumpClearances: route with a 15% margin on every track/via clearance
//     rule >= 100 µm — KiCad writes DSN rule values in µm regardless of the
//     coordinate resolution (200 means 0.2 mm) — FreeRouting's checks are
//     slightly optimistic against KiCad's DRC, and this is cheaper than
//     failed DRCs (the smd_smd rule is pad-to-pad; pads are fixed, so a
//     value under 100 is left alone)
//   - Shuffle: reorder the DSN's components, their places and its nets.
//     FreeRouting's result depends on that order; KiCad always writes the
//     same one, so shuffling is what makes a retry differ
//   - Route: run FreeRouting headless. The 1.4.5.1 jar is the known-good
//     default (java/jar are parameters); start with -mt 1: on it, the
//     multi-threaded optimizer puts tracks through pads (shorts). Newer
//     releases (2.x, e.g. 2.5: same -de/-do/-mp/-mt CLI, needs Java 25)
//     are adopted head-to-head if they beat the default on the DRC gate
//   - ReadRoutes: the SES back, scaled to KiCad nm with y negated
//     (Specctra y is up, KiCad's is down)
//
// Merging the returned segments into the .kicad_pcb is the project tool's
// job: split each wire into its segments, skip segments < 10 um long (a
// pad's dot), skip a segment identical to one of the board's locked tracks
// and a via at a locked via's position (the generator's own fixed wiring,
// echoed back), and look nets up by name.
//
// Library file for #:include — types only, no top-level statements.

using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Sx;

namespace Specctra;

public static class Dsn
{
    /// FreeRouting's clearance checks run slightly optimistic vs KiCad DRC;
    /// this margin was determined empirically.
    public const double DefaultClearanceFactor = 1.15;

    public static string BumpClearances(string dsn, double factor = DefaultClearanceFactor) =>
        Regex.Replace(dsn, @"\(clearance ([\d.]+)", m =>
        {
            var v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var nv = v >= 100 ? v * factor : v;
            return "(clearance " + nv.ToString("F1", CultureInfo.InvariantCulture);
        });

    /// Reorder the DSN's component blocks, the place lines inside each
    /// component, and the nets of the first network block. Deterministic in
    /// <paramref name="seed"/>; a different seed gives a different order.
    /// (CRLF input is normalized — KiCad writes the DSN with CRLF.)
    public static string Shuffle(string dsn, int seed)
    {
        dsn = dsn.Replace("\r\n", "\n");
        var rnd = new Random(seed);
        int componentBlocks = 0, placeLines = 0, nets = 0;
        (dsn, int n) = ShuffleSection(dsn,
            new Regex(@"(\(placement\n)(.*?)(  \)\n  \(library)", RegexOptions.Singleline),
            new Regex(@"    \(component .*?\n    \)\n", RegexOptions.Singleline), rnd);
        componentBlocks += n;
        (dsn, n) = ShuffleSection(dsn,
            new Regex(@"(\(component [^\n]*\n)((?:      \(place [^\n]*\n)+)(    \))"),
            new Regex(@"      \(place [^\n]*\n"), rnd);
        placeLines += n;
        (dsn, n) = ShuffleSection(dsn,
            new Regex(@"(\(network\n)(.*?)(    \(class )", RegexOptions.Singleline),
            new Regex(@"    \(net .*?\n    \)\n", RegexOptions.Singleline), rnd);
        nets += n;
        if (componentBlocks == 0 || placeLines == 0 || nets == 0)
            throw new InvalidOperationException(
                "Shuffle matched no " + (componentBlocks == 0 ? "component" :
                placeLines == 0 ? "place" : "net") +
                " blocks: the DSN's indentation is not pcbnew's layout, so the " +
                "retry shuffling would silently no-op (see the regexes)");
        return dsn;
    }

    private static (string Txt, int Matched) ShuffleSection(string txt, Regex outer, Regex block, Random rnd)
    {
        int matched = 0;
        var outTxt = outer.Replace(txt, m =>
        {
            var blocks = block.Matches(m.Groups[2].Value).Select(b => b.Value).OrderBy(_ => rnd.Next()).ToList();
            matched += blocks.Count;
            return m.Groups[1].Value + string.Join("", blocks) + m.Groups[3].Value;
        });
        return (outTxt, matched);
    }
}

public sealed record WireSeg(string Net, string Layer, long WidthNm, (long X, long Y)[] PointsNm);

public sealed record ViaPt(string Net, long XNm, long YNm, long SizeNm, long DrillNm);

public static class Ses
{
    /// Text of every bare atom in a node, sub-lists flattened (head keywords
    /// included — callers skip what is not a number).
    private static IEnumerable<string> Flatten(object node)
    {
        if (node is List<object> l)
            foreach (var e in l)
                foreach (var t in Flatten(e))
                    yield return t;
        else if (node is Sym s)
            yield return s.Value;
    }

    /// Read the routes of a Specctra SES file: every wire path segment and
    /// via, scaled to KiCad nm with y negated. Widths too are scaled from
    /// the SES resolution.
    public static (IReadOnlyList<WireSeg> Wires, IReadOnlyList<ViaPt> Vias) ReadRoutes(string sesPath)
    {
        var tree = (List<object>)Sexpr.Parse(File.ReadAllText(sesPath));
        var routes = Sexpr.Find(tree, "routes") ?? throw new InvalidOperationException("no routes in " + sesPath);
        var res = Sexpr.Find(routes, "resolution") ?? throw new InvalidOperationException("no resolution");
        double unit = res[1].Txt() switch
        {
            "um" => 1000.0, "mm" => 1e6, "mil" => 25400.0,
            _ => throw new InvalidOperationException("unknown SES unit " + res[1].Txt())
        };
        double scale = unit / double.Parse(res[2].Txt(), CultureInfo.InvariantCulture);

        var stacks = new Dictionary<string, (long Size, long Drill)>();
        foreach (var ps in Sexpr.FindAll(Sexpr.Find(routes, "library_out") ?? [], "padstack"))
        {
            var name = ps[1].Txt();
            var m = Regex.Match(name, @"_(\d+):(\d+)_um");
            if (m.Success)
                stacks[name] = (long.Parse(m.Groups[1].Value) * 1000, long.Parse(m.Groups[2].Value) * 1000);
            else
            {
                // no size in the name: a circular shape's diameter. The
                // nesting varies (KiCad's vias all take the name branch
                // above); take the first positive number in the shape node.
                double d = 0;
                var shape = Sexpr.Find(ps, "shape");
                if (shape is not null)
                    foreach (var tok in shape.Skip(1).SelectMany(Flatten))
                    {
                        if (double.TryParse(tok, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out var v) && v > 0)
                        {
                            d = v;
                            break;
                        }
                    }
                d *= scale;
                stacks[name] = ((long)d, (long)(d / 2));
            }
        }

        long Pt(object o) => (long)Math.Round(double.Parse(((Sym)o).Value, CultureInfo.InvariantCulture) * scale);
        var wires = new List<WireSeg>();
        var vias = new List<ViaPt>();
        foreach (var net in Sexpr.FindAll(Sexpr.Find(routes, "network_out") ?? [], "net"))
        {
            var netName = net[1].Txt();
            foreach (var w in Sexpr.FindAll(net, "wire"))
            {
                var path = Sexpr.Find(w, "path");
                if (path is null)
                    continue;
                var layer = path[1].Txt();
                long width = (long)Math.Round(path[2].D() * scale);
                var xy = path.Skip(3).ToList();
                var pts = new (long, long)[xy.Count / 2];
                for (int k = 0; k < pts.Length; k++)
                    pts[k] = (Pt(xy[2 * k]), -Pt(xy[2 * k + 1]));
                wires.Add(new WireSeg(netName, layer, width, pts));
            }
            foreach (var v in Sexpr.FindAll(net, "via"))
            {
                var (size, drill) = stacks[v[1].Txt()];
                vias.Add(new ViaPt(netName, Pt(v[2]), -Pt(v[3]), size, drill));
            }
        }
        return (wires, vias);
    }
}

public static class FreeRouting
{
    public const string DefaultJava = @"C:\tools\jdk-25.0.4.1+1-jre\bin\java.exe";
    public const string DefaultJar = @"C:\tools\freerouting-1.4.5.1\freerouting-1.4.5.1.jar";

    /// Run FreeRouting headless: <c>java -Xmx3g -jar &lt;jar&gt; -de dsn -do ses -mp
    /// passes -mt threads</c>. threads defaults to 1 — the multi-threaded
    /// optimizer has put tracks through pads (see SKILL.md's route stage);
    /// widen only after a version proves DRC-clean multi-threaded. Throws
    /// when FreeRouting is not installed, when it times out, or when it
    /// produces no SES (with the tail of its output).
    /// One full route of a dense board can take ~40 min; pass a matching
    /// timeout.
    public static void Route(string dsn, string ses, int passes = 12, int maxSeconds = 7200,
        string? java = null, string? jar = null, int threads = 1)
    {
        java ??= DefaultJava;
        jar ??= DefaultJar;
        if (!File.Exists(java) || !File.Exists(jar))
            throw new FileNotFoundException($"FreeRouting not installed ({java}, {jar})");

        if (File.Exists(ses))
            File.Delete(ses);
        var psi = new ProcessStartInfo(java)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in new[] { "-Xmx3g", "-jar", jar, "-de", dsn, "-do", ses, "-mp", passes.ToString(), "-mt", threads.ToString() })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(maxSeconds * 1000))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException($"FreeRouting still running after {maxSeconds}s");
        }
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        if (!File.Exists(ses))
            throw new InvalidOperationException(
                "FreeRouting produced no SES\n" + Tail(stdout) + Tail(stderr));
    }

    private static string Tail(string s) => s.Length <= 3000 ? s : s[^3000..];
}
