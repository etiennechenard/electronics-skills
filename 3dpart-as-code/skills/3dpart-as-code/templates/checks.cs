// The gates: dimensions probed off the solids vs part.cs (DIM), where the
// critical numbers came from (in reproduce mode, part of DIM), the printed
// meshes (MESH), the fit and sweep results (FIT), and staleness — no output
// older than an input it derives from. Each gate returns its problems;
// Report prints them with the one greppable `<GATE> PROBLEMS: n` line.
// Console text stays ASCII: Windows pipes default to the OEM code page.
//
// Library file for #:include — types only, no top-level statements.
// Needs partmodel.cs, mech.cs and stl.cs.

using System.Globalization;
using System.Text.Json;
using Mech;
using Mesh;
using PartModel;

namespace Checks;

public static class Probes
{
    /// Every probe of every script (build/probe-*.json), merged. A probe
    /// name recorded by two scripts is an error, not a silent overwrite.
    public static Dictionary<string, double> Load(Layout l)
    {
        var all = new Dictionary<string, double>();
        if (!Directory.Exists(l.Build))
            return all;
        foreach (var f in Directory.EnumerateFiles(l.Build, "probe-*.json").Order())
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            foreach (var p in doc.RootElement.GetProperty("probes").EnumerateObject())
                if (!all.TryAdd(p.Name, p.Value.GetDouble()))
                    throw new InvalidDataException($"probe '{p.Name}' recorded by more than one script ({Path.GetFileName(f)})");
        }
        return all;
    }
}

public static class Gates
{
    static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    /// Each expected dimension against its probe.
    public static List<string> Dims(Model m, IReadOnlyDictionary<string, double> probes)
    {
        var bad = new List<string>();
        foreach (var d in m.Dims)
        {
            if (!probes.TryGetValue(d.Probe, out var v))
                bad.Add($"{d.Probe}: never probed (the geometry script records it)");
            else if (Math.Abs(v - d.Nominal) > d.Tol + 1e-9)
                bad.Add($"{d.Probe}: {F(v)} mm, expected {F(d.Nominal)} +/- {F(d.Tol)} ({(v - d.Nominal).ToString("+0.####;-0.####", CultureInfo.InvariantCulture)})");
        }
        return bad;
    }

    /// Reproduce mode: no critical number is a guess. A critical parameter
    /// is measured or from a maker drawing/datasheet, or derived from ones
    /// that are (its Ref names them).
    public static List<string> Sources(Model m)
    {
        if (m.Mode != Mode.Reproduce)
            return [];
        return [.. m.Params.Where(p => p.Critical && p.Source == Source.Chosen)
            .Select(p => $"{p.Name}: critical but chosen; measure it (or cite the maker's drawing)")
            .Concat(m.Params.Where(p => p.Critical && p.Source is Source.Measured or Source.Datasheet && p.Ref.Length == 0)
                .Select(p => $"{p.Name}: {p.Source.ToString().ToLowerInvariant()} without a Ref (measurement id or drawing)"))
            .Concat(m.Params.Where(p => p.Critical && p.Source == Source.Derived && p.Ref.Length == 0)
                .Select(p => $"{p.Name}: derived without a Ref (its formula)"))];
    }

    /// Every declared body was built: its STEP, and its print STL when printed.
    public static List<string> Outputs(Model m, Layout l)
    {
        var bad = new List<string>();
        foreach (var b in m.Bodies)
        {
            if (!File.Exists(l.Step(b.Name)))
                bad.Add($"{b.Name}: {Path.GetFileName(l.Step(b.Name))} missing (run build.cs)");
            if (b.Printed && !File.Exists(l.Stl(b.Name)))
                bad.Add($"{b.Name}: {Path.GetFileName(l.Stl(b.Name))} missing (run build.cs)");
        }
        return bad;
    }

    /// The printed STLs: mesh soundness, the envelope, and the mesh volume
    /// against the solid's (a tessellation that lost material). `lines`
    /// collects one summary line per body, overhang included.
    public static List<string> Meshes(Model m, Layout l, IReadOnlyDictionary<string, double> probes, List<string> lines,
        double overhangDeg = 45)
    {
        var bad = new List<string>();
        foreach (var b in m.Bodies.Where(b => b.Printed))
        {
            var path = l.Stl(b.Name);
            if (!File.Exists(path))
            {
                bad.Add($"{b.Name}: no STL");
                continue;
            }
            var r = MeshCheck.Analyze(Stl.Load(path), overhangDeg);
            bad.AddRange(r.Problems().Select(p => $"{b.Name}: {p}"));
            var s = r.Bounds.Size;
            var e = m.Envelope;
            if (!MeshCheck.FitsEnvelope(r.Bounds, e.X, e.Y, e.Z))
                bad.Add($"{b.Name}: {F(s.X)} x {F(s.Y)} x {F(s.Z)} mm (up {b.Up}) does not fit the {F(e.X)} x {F(e.Y)} x {F(e.Z)} envelope; split it or turn it");
            if (probes.TryGetValue($"{b.Name}.volume", out var vol) && vol > 0
                && Math.Abs(r.Volume - vol) > 0.01 * vol + 0.5)
                bad.Add($"{b.Name}: mesh volume {F(r.Volume)} mm3 vs solid {F(vol)} mm3; the tessellation lost material");
            lines.Add($"{b.Name}: {r.Triangles} triangles, {r.Shells} shell, {F(s.X)} x {F(s.Y)} x {F(s.Z)} mm up {b.Up}, " +
                      $"{F(r.Volume / 1000)} cm3, overhang beyond {overhangDeg:0} deg: {F(r.OverhangArea)} mm2");
        }
        return bad;
    }

    /// The fit report (pac.py fit) against the declared clearances and sweeps.
    public static List<string> Fit(Model m, Layout l)
    {
        var bad = new List<string>();
        if (m.Clearances.Count == 0 && m.Sweeps.Count == 0)
            return bad;
        if (!File.Exists(l.FitReport))
            return [$"{Path.GetFileName(l.FitReport)} missing (run fit.cs)"];
        using var doc = JsonDocument.Parse(File.ReadAllText(l.FitReport));
        var cl = doc.RootElement.GetProperty("clearances").EnumerateArray().ToList();
        var sw = doc.RootElement.GetProperty("sweeps").EnumerateArray().ToList();
        void Judge(string what, JsonElement r, double min)
        {
            var ov = r.GetProperty("overlap").GetDouble();
            var gap = r.GetProperty("gap").GetDouble();
            if (ov > 1e-3)
                bad.Add($"{what}: overlap {F(ov)} mm3");
            else if (gap < min - 1e-6)
                bad.Add($"{what}: gap {F(gap)} mm, needs >= {F(min)}");
        }
        foreach (var c in m.Clearances)
        {
            var r = cl.FirstOrDefault(e => e.GetProperty("a").GetString() == c.A && e.GetProperty("b").GetString() == c.B);
            if (r.ValueKind == JsonValueKind.Undefined)
                bad.Add($"clearance {c.A}/{c.B}: not in the fit report (re-run fit.cs)");
            else
                Judge($"clearance {c.A}/{c.B}", r, c.MinGap);
        }
        foreach (var s in m.Sweeps)
            foreach (var a in s.Against)
            {
                var r = sw.FirstOrDefault(e => e.GetProperty("moving").GetString() == s.Moving && e.GetProperty("against").GetString() == a);
                if (r.ValueKind == JsonValueKind.Undefined)
                    bad.Add($"sweep {s.Moving} vs {a}: not in the fit report (re-run fit.cs)");
                else
                    Judge($"sweep {s.Moving} vs {a} (worst at {F(r.GetProperty("at").GetDouble())} of the path)", r, s.MinGap);
            }
        return bad;
    }

    /// What build.cs writes: every body's STEP and assembly STL, the printed
    /// bodies' STLs, the probe files, assembly.step, each board's placed mesh
    /// (its parts' mesh is optional: a board may have no 3D models) and each
    /// view's cut body.
    public static IEnumerable<string> BuildOutputs(Model m, Layout l) =>
        m.Bodies.SelectMany(b => b.Printed ? new[] { l.Step(b.Name), l.AsmStl(b.Name), l.Stl(b.Name) } : [l.Step(b.Name), l.AsmStl(b.Name)])
            .Concat(m.Bodies.Select(b => l.Probe(b.Script)).Distinct())
            .Append(l.Assembly)
            .Concat(m.Boards.Select(b => l.AsmStl(b.Name)))
            .Concat(m.Views.Where(v => v.Cut is not null).Select(v => l.ViewStl(v.Name, v.Cut!.Value.Body)));

    /// No output older than the newest input, and params.json is what part.cs
    /// writes now. `outputs` are the files about to be trusted.
    public static List<string> Stale(Model m, Layout l, IEnumerable<string> outputs)
    {
        var bad = new List<string>();
        if (!File.Exists(l.Params) || File.ReadAllText(l.Params) != m.ToJsonText())
            bad.Add("params.json differs from part.cs (run build.cs)");
        var inputs = l.Inputs(m.AllInputs).ToList();
        foreach (var missing in inputs.Where(f => !File.Exists(f)))
            bad.Add($"input {Path.GetRelativePath(l.Root, missing)} missing");
        inputs = [.. inputs.Where(File.Exists)];
        if (inputs.Count == 0)
            return bad;
        var newest = inputs.MaxBy(File.GetLastWriteTimeUtc)!;
        var t = File.GetLastWriteTimeUtc(newest);
        foreach (var o in outputs)
            if (!File.Exists(o))
                bad.Add($"{Path.GetRelativePath(l.Root, o)} missing");
            else if (File.GetLastWriteTimeUtc(o) < t)
                bad.Add($"{Path.GetRelativePath(l.Root, o)} is older than {Path.GetRelativePath(l.Root, newest)}");
        return bad;
    }

    /// Print the problems, then `<GATE> PROBLEMS: n`; returns n.
    public static int Report(string gate, IReadOnlyList<string> problems)
    {
        foreach (var p in problems)
            Console.WriteLine($"  {p}");
        Console.WriteLine($"{gate} PROBLEMS: {problems.Count}");
        return problems.Count;
    }
}
