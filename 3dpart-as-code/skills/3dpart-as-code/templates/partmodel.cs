// The part model: every number the geometry uses — parameters, named fits,
// bodies, expected dimensions, fit and sweep checks, the measurement sheet
// and the largest printable part — in one place. part.cs builds a Model from
// these types; build.cs writes it to params.json for the geometry scripts,
// which hold no numbers of their own.
//
// Library file for #:include — types only, no top-level statements.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PartModel;

/// Design: the part is invented here. Reproduce: it copies an existing part,
/// so every critical dimension must come from a measurement or a drawing.
public enum Mode { Design, Reproduce }

/// Where a number comes from. Chosen and Derived are design decisions;
/// Measured (calipers on the real part) and Datasheet (a maker drawing or
/// STEP) are facts about something that already exists.
public enum Source { Chosen, Derived, Measured, Datasheet }

/// Part: a printed deliverable. Coupon: a printed test slice of a critical
/// feature, printed first. Reference: something the part mates with (an
/// imported board STEP, a keep-out volume) — fit-checked, never printed.
public enum BodyKind { Part, Coupon, Reference }

/// One number. Ref says where it came from: a measurement id (D3), a
/// datasheet URL and page, or the formula it is derived by.
public sealed record Param(string Name, double Value, string Unit = "mm", Source Source = Source.Chosen,
    double Tol = 0, bool Critical = false, string Ref = "", string Note = "");

/// A named fit: the gap per side between two mating surfaces, mm (a hole
/// for a slip-fit pin is pin + 2 × gap). The defaults are typical FDM
/// starting points; tune them once from printed coupons and keep the tuned
/// values in part.cs.
public sealed record Fit(string Name, double Gap, string Note = "");

public static class Fits
{
    public static readonly Fit Press = new("press", 0.05, "interference-ish: driven in, holds by friction");
    public static readonly Fit Snug = new("snug", 0.10, "by hand, no play");
    public static readonly Fit Slip = new("slip", 0.20, "slides or turns freely, little play");
    public static readonly Fit Loose = new("loose", 0.35, "drops in, visible play");

    public static IReadOnlyList<Fit> Defaults => [Press, Snug, Slip, Loose];
}

/// A body the geometry scripts produce. Up is the part axis that points up
/// on the printer (+Z, -Z, +X, -X, +Y, -Y): the STL is written in that
/// orientation, sitting on z = 0; the STEP stays in assembly coordinates.
public sealed record Body(string Name, string Script, string Up = "+Z", BodyKind Kind = BodyKind.Part, string Note = "")
{
    public bool Printed => Kind != BodyKind.Reference;
}

/// A dimension measured off the built solid (probe.json) and the value it
/// must have. Probe names are the script's (`<body>.<feature>`), plus the
/// automatic `<body>.volume` and `<body>.bbox.x|y|z` (assembly coordinates).
public sealed record ExpectedDim(string Probe, double Nominal, double Tol, string Note = "")
{
    /// The probe must match a parameter: nominal and tolerance are the parameter's.
    public static ExpectedDim Of(Param p, string probe, double? tol = null) =>
        new(probe, p.Value, tol ?? p.Tol, p.Name);
}

/// A and B must not overlap and must keep at least MinGap between them (in
/// assembly coordinates).
public sealed record FitCheck(string A, string B, double MinGap, string Note = "");

/// Moving travels in a straight line from its assembly position offset by
/// From to its assembly position (an insertion path) and must stay MinGap
/// clear of every Against body all the way; Steps samples the path.
public sealed record SweepCheck(string Moving, (double X, double Y, double Z) From, string[] Against,
    double MinGap, int Steps = 24, string Note = "");

/// The largest part the printer takes, mm. A printed body fits when its
/// bounding box in print orientation fits as is or turned 90° about z.
public sealed record Envelope(double X, double Y, double Z);

/// One line of the measurement sheet (reproduce mode): what to measure, the
/// parameter it feeds, and where the callout marker goes on the render.
public sealed record Measurement(string Id, string What, string Param, string Body, (double X, double Y, double Z) At);

public sealed class Model
{
    public required string Name { get; init; }
    public Mode Mode { get; init; } = Mode.Design;
    public required Envelope Envelope { get; init; }
    public IReadOnlyList<Param> Params { get; init; } = [];
    public IReadOnlyList<Fit> Fits { get; init; } = PartModel.Fits.Defaults;
    public required IReadOnlyList<Body> Bodies { get; init; }
    public IReadOnlyList<ExpectedDim> Dims { get; init; } = [];
    public IReadOnlyList<FitCheck> Clearances { get; init; } = [];
    public IReadOnlyList<SweepCheck> Sweeps { get; init; } = [];
    public IReadOnlyList<Measurement> Measurements { get; init; } = [];
    /// Files outside tools/ and parts/ the model or a script reads (board
    /// files, imported STEPs), relative to mech/: an output older than one
    /// is stale.
    public IReadOnlyList<string> Inputs { get; init; } = [];
    /// STL tessellation: chord deviation (mm) and angle (rad).
    public double Tolerance { get; init; } = 0.01;
    public double AngularTolerance { get; init; } = 0.1;

    public static readonly string[] Ups = ["+Z", "-Z", "+X", "-X", "+Y", "-Y"];

    public Param P(string name) =>
        Params.FirstOrDefault(p => p.Name == name) ?? throw new KeyNotFoundException($"no parameter '{name}'");

    public double this[string name] => P(name).Value;

    public Fit F(string name) =>
        Fits.FirstOrDefault(f => f.Name == name) ?? throw new KeyNotFoundException($"no fit '{name}'");

    public Body B(string name) =>
        Bodies.FirstOrDefault(b => b.Name == name) ?? throw new KeyNotFoundException($"no body '{name}'");

    /// The structural invariants: unique names, every reference resolves,
    /// sane values. build.cs refuses a model with any; the tests hold it empty.
    public IReadOnlyList<string> Validate()
    {
        var bad = new List<string>();
        void Dups(string what, IEnumerable<string> names)
        {
            foreach (var g in names.GroupBy(n => n).Where(g => g.Count() > 1))
                bad.Add($"{what} '{g.Key}' defined {g.Count()} times");
        }
        Dups("parameter", Params.Select(p => p.Name));
        Dups("fit", Fits.Select(f => f.Name));
        Dups("body", Bodies.Select(b => b.Name));
        Dups("expected dimension", Dims.Select(d => d.Probe));
        Dups("measurement", Measurements.Select(m => m.Id));

        if (Envelope.X <= 0 || Envelope.Y <= 0 || Envelope.Z <= 0)
            bad.Add($"envelope {Envelope} is not positive");
        if (Bodies.Count == 0)
            bad.Add("no bodies");
        if (!Bodies.Any(b => b.Kind == BodyKind.Part))
            bad.Add("no printed part among the bodies");
        if (Tolerance <= 0 || AngularTolerance <= 0)
            bad.Add("tessellation tolerances must be positive");
        foreach (var p in Params)
        {
            if (p.Tol < 0)
                bad.Add($"parameter '{p.Name}': negative tolerance");
            if (p.Critical && p.Tol <= 0)
                bad.Add($"parameter '{p.Name}' is critical but has no tolerance");
            if (!double.IsFinite(p.Value))
                bad.Add($"parameter '{p.Name}' is not a finite number");
        }
        foreach (var f in Fits.Where(f => f.Gap < 0))
            bad.Add($"fit '{f.Name}': negative gap");
        foreach (var b in Bodies)
        {
            if (!Ups.Contains(b.Up))
                bad.Add($"body '{b.Name}': up '{b.Up}' is not one of {string.Join(" ", Ups)}");
            if (string.IsNullOrWhiteSpace(b.Script))
                bad.Add($"body '{b.Name}': no script");
            if (b.Name.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_')))
                bad.Add($"body '{b.Name}': names are letters, digits, - and _ (they become file names)");
            if (b.Name == "assembly")
                bad.Add($"body '{b.Name}': reserved (the assembly renders and build/assembly.step)");
        }
        var names = Bodies.Select(b => b.Name).ToHashSet();
        foreach (var d in Dims)
        {
            if (d.Tol < 0)
                bad.Add($"expected dimension '{d.Probe}': negative tolerance");
            if (!names.Contains(d.Probe.Split('.')[0]))
                bad.Add($"expected dimension '{d.Probe}': probe names start with a body name");
        }
        foreach (var c in Clearances)
        {
            foreach (var n in new[] { c.A, c.B }.Where(n => !names.Contains(n)))
                bad.Add($"fit check {c.A}/{c.B}: no body '{n}'");
            if (c.A == c.B)
                bad.Add($"fit check {c.A}/{c.B}: a body against itself");
            if (c.MinGap < 0)
                bad.Add($"fit check {c.A}/{c.B}: negative gap");
        }
        foreach (var s in Sweeps)
        {
            foreach (var n in s.Against.Prepend(s.Moving).Where(n => !names.Contains(n)))
                bad.Add($"sweep of {s.Moving}: no body '{n}'");
            if (s.Against.Length == 0)
                bad.Add($"sweep of {s.Moving}: nothing to sweep against");
            if (s.Steps < 2)
                bad.Add($"sweep of {s.Moving}: fewer than 2 steps");
            if (s.From == (0, 0, 0))
                bad.Add($"sweep of {s.Moving}: zero travel");
            if (s.MinGap < 0)
                bad.Add($"sweep of {s.Moving}: negative gap");
            if (s.Against.Contains(s.Moving))
                bad.Add($"sweep of {s.Moving}: a body against itself");
        }
        var pnames = Params.Select(p => p.Name).ToHashSet();
        foreach (var m in Measurements)
        {
            if (!pnames.Contains(m.Param))
                bad.Add($"measurement {m.Id}: no parameter '{m.Param}'");
            if (!names.Contains(m.Body))
                bad.Add($"measurement {m.Id}: no body '{m.Body}'");
        }
        if (Mode == Mode.Design && Measurements.Count > 0)
            bad.Add("measurements are a reproduce-mode sheet; this model is in design mode");
        return bad;
    }

    /// params.json: what the geometry scripts and pac.py read.
    public JsonObject ToJson()
    {
        static JsonArray V((double X, double Y, double Z) v) => new(v.X, v.Y, v.Z);
        var ps = new JsonObject();
        foreach (var p in Params)
            ps[p.Name] = new JsonObject
            {
                ["value"] = p.Value, ["unit"] = p.Unit, ["source"] = Lower(p.Source), ["tol"] = p.Tol,
                ["critical"] = p.Critical, ["ref"] = p.Ref, ["note"] = p.Note,
            };
        var fs = new JsonObject();
        foreach (var f in Fits)
            fs[f.Name] = f.Gap;
        return new JsonObject
        {
            ["name"] = Name,
            ["mode"] = Lower(Mode),
            ["units"] = "mm",
            ["envelope"] = new JsonArray(Envelope.X, Envelope.Y, Envelope.Z),
            ["tolerance"] = Tolerance,
            ["angular_tolerance"] = AngularTolerance,
            ["params"] = ps,
            ["fits"] = fs,
            ["bodies"] = new JsonArray([.. Bodies.Select(b => (JsonNode)new JsonObject
            {
                ["name"] = b.Name, ["script"] = b.Script, ["up"] = b.Up, ["kind"] = Lower(b.Kind),
            })]),
            ["clearances"] = new JsonArray([.. Clearances.Select(c => (JsonNode)new JsonObject
            {
                ["a"] = c.A, ["b"] = c.B, ["min_gap"] = c.MinGap,
            })]),
            ["sweeps"] = new JsonArray([.. Sweeps.Select(s => (JsonNode)new JsonObject
            {
                ["moving"] = s.Moving, ["from"] = V(s.From),
                ["against"] = new JsonArray([.. s.Against.Select(a => (JsonNode)a)]),
                ["min_gap"] = s.MinGap, ["steps"] = s.Steps,
            })]),
        };
    }

    public string ToJsonText() => ToJson().ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// Write params.json, leaving the file (and its timestamp) alone when
    /// the content is unchanged, so staleness tracks real changes only.
    public bool Write(string path)
    {
        var text = ToJsonText();
        if (File.Exists(path) && File.ReadAllText(path) == text)
            return false;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, text);
        return true;
    }

    /// The measurement sheet as a Markdown table for docs/: id, what, the
    /// current value and where it came from. Unmeasured lines read "—".
    public string SheetMarkdown()
    {
        var w = new System.Text.StringBuilder();
        w.AppendLine("| Id | Measure | Parameter | Value | Tol | Critical | Source |");
        w.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var m in Measurements)
        {
            var p = P(m.Param);
            var known = p.Source is Source.Measured or Source.Datasheet;
            w.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {m.Id} | {m.What} | `{p.Name}` | {(known ? $"{p.Value:0.###} {p.Unit}" : "—")} | ±{p.Tol:0.###} | {(p.Critical ? "yes" : "")} | {Lower(p.Source)}{(p.Ref.Length > 0 ? $" ({p.Ref})" : "")} |"));
        }
        return w.ToString();
    }

    static string Lower<T>(T e) where T : Enum => e.ToString().ToLowerInvariant();
}
