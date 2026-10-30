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
/// Color ("#rrggbb") is the body's colour in the renders; unset, the
/// palette picks one.
public sealed record Body(string Name, string Script, string Up = "+Z", BodyKind Kind = BodyKind.Part, string Note = "",
    string? Color = null)
{
    public bool Printed => Kind != BodyKind.Reference;
}

/// A KiCad board shown in place, with its parts' 3D models, in the assembly
/// renders and assembly.step. Display only: it is never fit-checked (KiCad's
/// component models are not reliable solids), so fits stay on keep-out
/// Reference bodies, which Replaces hides from the renders. build.cs exports
/// it with kicad-cli: the bare board (silkscreen and mask on) and its parts.
/// Origin is the KiCad page point (mm, y down) that becomes the board's zero;
/// X and Y are the assembly axes (+X, -Z, ...) its +x and its up (KiCad -y)
/// point along, so its F face looks along X x Y; At is where the origin
/// lands, on the B face. The .kicad_pcb (mech-relative) is a build input.
public sealed record Board(string Name, string Pcb, (double X, double Y) Origin, string X, string Y,
    (double X, double Y, double Z) At, string[]? Replaces = null, string Note = "")
{
    /// An axis name as a unit vector: "+X" -> (1, 0, 0).
    public static (int X, int Y, int Z) Axis(string a)
    {
        var s = a[0] == '-' ? -1 : 1;
        return a[1] switch { 'X' => (s, 0, 0), 'Y' => (0, s, 0), _ => (0, 0, s) };
    }

    /// The direction the F face looks along: X x Y.
    public (int X, int Y, int Z) Normal
    {
        get
        {
            var (a, b) = (Axis(X), Axis(Y));
            return (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }
    }
}

/// An extra assembly render for seeing inside: Hide leaves bodies (or
/// boards) out, Cut keeps the part of a body on one side of an axis plane
/// (Keep "+X" at 0: X >= 0, its cut faces capped), Move shifts a body (an
/// exploded lid). Rendered iso and iso-back as assembly-<name>-*.png.
public sealed record AssemblyView(string Name, string[]? Hide = null, (string Body, string Keep, double At)? Cut = null,
    (string Body, (double X, double Y, double Z) By)? Move = null, string Note = "");

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

/// Where a parameter sits on a body, in assembly coordinates, for the
/// labelled views (`<body>-dims-*.png`) and the parameter sheet: a span
/// From..To is drawn as a dimension line and its length must equal the
/// value; with no To, a point (a radius, a chamfer) is drawn as a marker.
public sealed record Annotation(string Param, string Body, (double X, double Y, double Z) From,
    (double X, double Y, double Z)? To = null);

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
    public IReadOnlyList<Annotation> Annotations { get; init; } = [];
    /// Files outside tools/ and parts/ the model or a script reads (board
    /// files, imported STEPs), relative to mech/: an output older than one
    /// is stale. The boards' .kicad_pcb files are added (AllInputs).
    public IReadOnlyList<string> Inputs { get; init; } = [];
    public IReadOnlyList<Board> Boards { get; init; } = [];
    public IReadOnlyList<AssemblyView> Views { get; init; } = [];

    /// Inputs plus every board file.
    public IEnumerable<string> AllInputs => Inputs.Concat(Boards.Select(b => b.Pcb)).Distinct();
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

    /// The label an annotated parameter carries in the views and on the
    /// sheet: its 1-based position in Params. Null when it has no annotation.
    public string? Label(string param)
    {
        if (!Annotations.Any(a => a.Param == param))
            return null;
        var i = Params.ToList().FindIndex(p => p.Name == param);
        return i < 0 ? null : (i + 1).ToString(CultureInfo.InvariantCulture);
    }

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
        Dups("annotation of", Annotations.Select(a => a.Param));

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
            if (!IsFileName(b.Name))
                bad.Add($"body '{b.Name}': names are letters, digits, - and _ (they become file names)");
            if (b.Name == "assembly")
                bad.Add($"body '{b.Name}': reserved (the assembly renders and build/assembly.step)");
            if (b.Color is { } c && !IsColor(c))
                bad.Add($"body '{b.Name}': color '{c}' is not #rrggbb");
        }
        var names = Bodies.Select(b => b.Name).ToHashSet();
        Dups("board", Boards.Select(b => b.Name));
        foreach (var b in Boards)
        {
            if (!IsFileName(b.Name))
                bad.Add($"board '{b.Name}': names are letters, digits, - and _ (they become file names)");
            foreach (var n in new[] { b.Name, b.Name + "-parts" }.Where(names.Contains))
                bad.Add($"board '{b.Name}': its mesh {n}.stl clashes with body '{n}'");
            foreach (var other in Boards.Where(o => !ReferenceEquals(o, b) && o.Name == b.Name + "-parts"))
                bad.Add($"board '{b.Name}': its mesh {b.Name}-parts.stl clashes with board '{other.Name}'");
            if (!b.Pcb.EndsWith(".kicad_pcb", StringComparison.OrdinalIgnoreCase))
                bad.Add($"board '{b.Name}': {b.Pcb} is not a .kicad_pcb");
            if (!Ups.Contains(b.X) || !Ups.Contains(b.Y))
                bad.Add($"board '{b.Name}': X '{b.X}' and Y '{b.Y}' must each be one of {string.Join(" ", Ups)}");
            else if (b.X[1] == b.Y[1])
                bad.Add($"board '{b.Name}': X '{b.X}' and Y '{b.Y}' are not perpendicular");
            foreach (var r in b.Replaces ?? [])
                if (Bodies.FirstOrDefault(x => x.Name == r) is not { } rb)
                    bad.Add($"board '{b.Name}': replaces no body '{r}'");
                else if (rb.Kind != BodyKind.Reference)
                    bad.Add($"board '{b.Name}': replaces '{r}', which is printed (only reference bodies)");
        }
        var boards = Boards.Select(b => b.Name).ToHashSet();
        Dups("view", Views.Select(v => v.Name));
        foreach (var v in Views)
        {
            if (!IsFileName(v.Name))
                bad.Add($"view '{v.Name}': names are letters, digits, - and _ (they become file names)");
            if ((v.Hide ?? []).Length == 0 && v.Cut is null && v.Move is null)
                bad.Add($"view '{v.Name}': hides, cuts and moves nothing (the assembly renders already show that)");
            foreach (var h in (v.Hide ?? []).Where(h => !names.Contains(h) && !boards.Contains(h)))
                bad.Add($"view '{v.Name}': hides no body or board '{h}'");
            if (v.Cut is { } cut)
            {
                if (!names.Contains(cut.Body))
                    bad.Add($"view '{v.Name}': cuts no body '{cut.Body}'");
                if (!Ups.Contains(cut.Keep))
                    bad.Add($"view '{v.Name}': keep '{cut.Keep}' is not one of {string.Join(" ", Ups)}");
                if ((v.Hide ?? []).Contains(cut.Body))
                    bad.Add($"view '{v.Name}': cuts '{cut.Body}', which it also hides");
            }
            if (v.Move is { } mv)
            {
                if (!names.Contains(mv.Body))
                    bad.Add($"view '{v.Name}': moves no body '{mv.Body}'");
                if (mv.By == (0, 0, 0))
                    bad.Add($"view '{v.Name}': moves '{mv.Body}' by nothing");
                if ((v.Hide ?? []).Contains(mv.Body))
                    bad.Add($"view '{v.Name}': moves '{mv.Body}', which it also hides");
            }
        }
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
        foreach (var a in Annotations)
        {
            if (!names.Contains(a.Body))
                bad.Add($"annotation of {a.Param}: no body '{a.Body}'");
            if (Params.FirstOrDefault(p => p.Name == a.Param) is not { } p)
            {
                bad.Add($"annotation of {a.Param}: no parameter '{a.Param}'");
                continue;
            }
            if (a.To is not { } to || p.Unit != "mm")
                continue;
            var (dx, dy, dz) = (to.X - a.From.X, to.Y - a.From.Y, to.Z - a.From.Z);
            var len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (Math.Abs(len - p.Value) > Math.Max(p.Tol, 0.01))
                bad.Add(string.Create(CultureInfo.InvariantCulture,
                    $"annotation of {a.Param}: the line is {len:0.###} mm long, the parameter is {p.Value:0.###} mm"));
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
            ["boards"] = new JsonArray([.. Boards.Select(b => (JsonNode)new JsonObject
            {
                ["name"] = b.Name, ["pcb"] = b.Pcb, ["origin"] = new JsonArray(b.Origin.X, b.Origin.Y),
                ["x"] = b.X, ["y"] = b.Y, ["at"] = V(b.At),
            })]),
            ["views"] = new JsonArray([.. Views.Select(v => (JsonNode)new JsonObject
            {
                ["name"] = v.Name,
                ["cut"] = v.Cut is { } c ? new JsonObject { ["body"] = c.Body, ["keep"] = c.Keep, ["at"] = c.At } : null,
            })]),
        };
    }

    static bool IsFileName(string n) => n.Length > 0 && n.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');

    static bool IsColor(string c) => c.Length == 7 && c[0] == '#' && c.Skip(1).All(char.IsAsciiHexDigit);

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
