// Fabricator limits as data, and the checks that hold a project to them.
//
// The numbers are never written from memory or copied from this skill: at
// project start (and again before ordering) the agent fetches the fab's
// capability page and writes the project's pcb/fab.json from it, every
// value next to the page's own words:
//
//   {
//     "fab": "<fab>", "process": "<the process ordered, e.g. 2-layer FR-4 1 oz>",
//     "source": "<capability page URL>", "checked": "<yyyy-mm-dd>",
//     "rules": {
//       "track_width": { "mm": <number>, "quote": "<the page's line, verbatim>" },
//       "npth_hole":   { "mm": null, "quote": "not published" },
//       ...
//     }
//   }
//
// Every key of FabRules.Keys must be present ("mm": null when the page has
// no such limit, with the quote saying so); other keys are kept as
// information and not checked. FabCheck then holds the project to it:
//   - Project: the .kicad_pro's rules and net classes, so KiCad's DRC and
//     the DSN classes enforce at least the fab's minimums;
//   - Board: the board's own geometry (tracks, vias, drills, annular rings,
//     hole-to-hole, silk), including what footprints bring with them.
// Copper spacing and copper-to-edge are geometry KiCad's DRC already
// measures; Project makes sure its rules are at least the fab's.
//
// Library file for #:include — types only, no top-level statements.

using System.Globalization;
using System.Text.Json;
using PcbFile;
using Sx;

namespace Fab;

/// One limit: millimetres (null when the fab publishes none) and the
/// capability page's text it was read from. Bad says why an entry's "mm" is
/// neither a number nor null: Problems reports it, it is never read as
/// unpublished.
public sealed record FabRule(double? Mm, string Quote, string? Bad = null);

public sealed record FabRules(
    string Fab, string Process, string Source, DateOnly Checked, IReadOnlyDictionary<string, FabRule> Rules)
{
    /// The limits FabCheck uses, all minimums in mm.
    public static readonly IReadOnlyDictionary<string, string> Keys = new Dictionary<string, string>
    {
        ["track_width"] = "trace width",
        ["track_spacing"] = "trace to trace (and to pad) spacing",
        ["via_hole"] = "via drill",
        ["via_diameter"] = "via pad diameter",
        ["via_annular_ring"] = "via annular ring",
        ["pth_hole"] = "plated through-hole drill (pads)",
        ["pth_annular_ring"] = "plated through-hole pad annular ring",
        ["npth_hole"] = "non-plated hole",
        ["hole_to_hole"] = "hole to hole, edge to edge (vias)",
        ["pad_hole_to_pad_hole"] = "plated pad hole to plated pad hole, edge to edge",
        ["copper_to_edge"] = "copper to board edge",
        ["silk_line_width"] = "silkscreen line width",
        ["silk_text_height"] = "silkscreen text height",
        ["mask_bridge"] = "solder mask bridge (web)",
    };

    /// The value of a limit, or null when the fab publishes none.
    public double? this[string key] => Rules.TryGetValue(key, out var r) ? r.Mm : null;

    public static FabRules Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException($"the top level is {root.ValueKind}, not an object");
        // a field of the wrong type reads as missing, so Problems names it
        string Str(string k) => root.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        var rules = new Dictionary<string, FabRule>();
        if (root.TryGetProperty("rules", out var rs) && rs.ValueKind == JsonValueKind.Object)
            foreach (var r in rs.EnumerateObject())
            {
                if (r.Value.ValueKind != JsonValueKind.Object)
                {
                    rules[r.Name] = new(null, "", $"{r.Value.GetRawText()} is not an object {{ \"mm\": ..., \"quote\": ... }}");
                    continue;
                }
                var quote = r.Value.TryGetProperty("quote", out var q) && q.ValueKind == JsonValueKind.String ? q.GetString()! : "";
                rules[r.Name] = !r.Value.TryGetProperty("mm", out var m) ? new(null, quote, "no \"mm\" (a number, or null when the page publishes none)")
                    : m.ValueKind == JsonValueKind.Number && m.TryGetDouble(out var mm) && double.IsFinite(mm) ? new(mm, quote)
                    : m.ValueKind == JsonValueKind.Null ? new(null, quote)
                    : new(null, quote, $"\"mm\" is {m.GetRawText()}, not a number or null");
            }
        DateOnly.TryParseExact(Str("checked"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var checkedOn);
        return new(Str("fab"), Str("process"), Str("source"), checkedOn, rules);
    }

    /// Limits every fab publishes: null here means the page was misread.
    public static readonly IReadOnlySet<string> Required = new HashSet<string> { "track_width", "track_spacing", "via_hole", "pth_hole" };

    /// What is wrong with the file itself (for the keys of Keys): missing
    /// keys or quotes, a quote still the template's placeholder, a value that
    /// is neither a positive number nor null (or null for a Required one), no
    /// source, or a check older than maxAgeDays (re-fetch the page: fabs
    /// change their numbers).
    public IEnumerable<string> Problems(DateOnly today, int maxAgeDays = 90)
    {
        if (Fab == "") yield return "fab.json: no \"fab\"";
        if (!Source.StartsWith("http", StringComparison.Ordinal)) yield return "fab.json: \"source\" is not the capability page's URL";
        if (Checked == default) yield return "fab.json: \"checked\" is not a yyyy-mm-dd date";
        else if (Checked > today) yield return $"fab.json: checked {Checked:yyyy-MM-dd} is in the future";
        else if (today.DayNumber - Checked.DayNumber > maxAgeDays)
            yield return $"fab.json: checked {Checked:yyyy-MM-dd}, {today.DayNumber - Checked.DayNumber} days ago: " +
                         $"re-fetch {Source} and update the values";
        foreach (var (key, what) in Keys)
        {
            if (!Rules.TryGetValue(key, out var r)) { yield return $"fab.json: missing \"{key}\" ({what})"; continue; }
            if (r.Bad is not null) yield return $"fab.json: \"{key}\": {r.Bad}";
            if (string.IsNullOrWhiteSpace(r.Quote)) yield return $"fab.json: \"{key}\" has no quote from the page";
            else if (r.Quote.TrimStart().StartsWith('<')) yield return $"fab.json: \"{key}\" quote is a placeholder: {r.Quote}";
            if (r.Mm is <= 0) yield return $"fab.json: \"{key}\" is {r.Mm} mm, not a positive number";
            if (r.Mm is null && r.Bad is null && Required.Contains(key))
                yield return $"fab.json: \"{key}\" is null, but every fab publishes it: read the page again";
        }
    }
}

/// A rule broken. Errors are what a fab rejects or cannot make, and every
/// .kicad_pro setting below the fab's limit (silk minimums included);
/// warnings are the board's own silkscreen below the fab's limit (printed
/// thin or dropped).
public sealed record FabIssue(bool Error, string Rule, string Where, string Text)
{
    public override string ToString() => $"  {(Error ? "ERROR" : "warn ")} {Rule}: {Where}: {Text}";
}

public static class FabCheck
{
    const double Eps = 1e-6;

    /// The project's own rules (.kicad_pro): the board setup's minimums and
    /// every net class must be at or above the fab's limits, or KiCad's DRC
    /// (and FreeRouting, which takes its rules from the classes) would
    /// accept copper the fab cannot make. KiCad has one hole-to-hole minimum
    /// for every pair, held to hole_to_hole; the fab's plated-pad limit is
    /// Board's check.
    public static List<FabIssue> Project(string kicadPro, FabRules fab)
    {
        var issues = new List<FabIssue>();
        using var doc = JsonDocument.Parse(File.ReadAllText(kicadPro));
        var name = Path.GetFileName(kicadPro);
        var rules = doc.RootElement.GetProperty("board").GetProperty("design_settings").GetProperty("rules");
        double Rule(string k) => rules.TryGetProperty(k, out var v) ? v.GetDouble() : 0;
        void Min(string where, string what, double have, string key, double? need = null)
        {
            need ??= fab[key];
            if (need is double n && have + Eps < n)
                issues.Add(new(true, key, where, $"{what} {Mm(have)} mm < {fab.Fab}'s {Mm(n)} mm"));
        }
        var setup = $"{name} board setup";
        Min(setup, "min_track_width", Rule("min_track_width"), "track_width");
        Min(setup, "min_clearance", Rule("min_clearance"), "track_spacing");
        Min(setup, "min_via_diameter", Rule("min_via_diameter"), "via_diameter");
        Min(setup, "min_via_annular_width", Rule("min_via_annular_width"), "via_annular_ring");
        // one minimum drill for vias and pads alike: the larger of the two limits
        var hole = Math.Max(fab["via_hole"] ?? 0, fab["pth_hole"] ?? 0);
        Min(setup, "min_through_hole_diameter", Rule("min_through_hole_diameter"), "via_hole", hole > 0 ? hole : null);
        Min(setup, "min_hole_to_hole", Rule("min_hole_to_hole"), "hole_to_hole");
        Min(setup, "min_copper_edge_clearance", Rule("min_copper_edge_clearance"), "copper_to_edge");
        Min(setup, "min_text_height", Rule("min_text_height"), "silk_text_height");
        Min(setup, "min_text_thickness", Rule("min_text_thickness"), "silk_line_width");
        Min(setup, "solder_mask_min_width", Rule("solder_mask_min_width"), "mask_bridge");
        foreach (var c in doc.RootElement.GetProperty("net_settings").GetProperty("classes").EnumerateArray())
        {
            var where = $"{name} net class {c.GetProperty("name").GetString()}";
            double Get(string k) => c.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
            Min(where, "track_width", Get("track_width"), "track_width");
            Min(where, "clearance", Get("clearance"), "track_spacing");
            Min(where, "via_diameter", Get("via_diameter"), "via_diameter");
            Min(where, "via_drill", Get("via_drill"), "via_hole");
            Min(where, "via ring", (Get("via_diameter") - Get("via_drill")) / 2, "via_annular_ring");
        }
        return issues;
    }

    /// The board's geometry, as built: tracks, vias, pad drills and rings,
    /// hole-to-hole spacing and silkscreen.
    public static List<FabIssue> Board(Board b, FabRules fab)
    {
        var issues = new List<FabIssue>();
        void Min(bool error, string key, string where, string what, long haveNm)
        {
            if (fab[key] is double n && haveNm / 1e6 + Eps < n)
                issues.Add(new(error, key, where, $"{what} {Mm(haveNm / 1e6)} mm < {fab.Fab}'s {Mm(n)} mm"));
        }
        static string At((long X, long Y) p) => $"({Mm(p.X / 1e6)}, {Mm(p.Y / 1e6)})";

        foreach (var t in b.Items("segment").Concat(b.Items("arc")))
            if (Sexpr.Find(t, "layer")?[1].Txt().EndsWith(".Cu") == true)
                Min(true, "track_width", $"track {PcbFile.Board.NetOf(t)} at {At(PcbFile.Board.Xy(t, "start"))}", "width",
                    Nm.Of(Sexpr.Find(t, "width")![1]));

        var holes = new List<(long X, long Y, long R, bool PadHole, string Name)>();
        foreach (var v in b.Items("via"))
        {
            var at = PcbFile.Board.Xy(v, "at");
            long size = Nm.Of(Sexpr.Find(v, "size")![1]), drill = Nm.Of(Sexpr.Find(v, "drill")![1]);
            var where = $"via {PcbFile.Board.NetOf(v)} at {At(at)}";
            Min(true, "via_hole", where, "drill", drill);
            Min(true, "via_diameter", where, "diameter", size);
            Min(true, "via_annular_ring", where, "ring", (size - drill) / 2);
            holes.Add((at.X, at.Y, drill / 2, false, where));
        }
        foreach (var fp in b.Footprints)
            foreach (var pad in fp.Pads)
            {
                var (dx, dy) = pad.Drill;
                if (dx == 0)
                    continue;
                var where = $"{fp.Ref} pad {pad.Number}";
                if (pad.Type == "np_thru_hole")
                    Min(true, "npth_hole", where, "hole", Math.Min(dx, dy));
                else
                {
                    Min(true, "pth_hole", where, "drill", Math.Min(dx, dy));
                    // a custom pad's size is its anchor, not its copper: its ring is unmeasured
                    if (pad.OnCopper && pad.Shape != "custom")
                        Min(true, "pth_annular_ring", where, "ring", Math.Min(pad.SizeX - dx, pad.SizeY - dy) / 2);
                }
                // an oval hole counts as its enclosing circle: conservative. A
                // drill (offset) is not read: the hole is taken at the pad centre
                var p = pad.Position;
                holes.Add((p.X, p.Y, Math.Max(dx, dy) / 2, pad.Type == "thru_hole", where));
            }
        for (int i = 0; i < holes.Count; i++)
            for (int j = i + 1; j < holes.Count; j++)
            {
                var (a, c) = (holes[i], holes[j]);
                var key = a.PadHole && c.PadHole && fab["pad_hole_to_pad_hole"] is not null ? "pad_hole_to_pad_hole" : "hole_to_hole";
                if (fab[key] is not double n)
                    continue;
                long reach = a.R + c.R + (long)(n * 1e6);
                if (Math.Abs(a.X - c.X) > reach || Math.Abs(a.Y - c.Y) > reach)
                    continue;
                long gap = (long)Math.Round(double.Hypot(a.X - c.X, a.Y - c.Y)) - a.R - c.R;
                Min(true, key, $"{a.Name} / {c.Name}", "hole gap", gap);
            }

        // silkscreen: visible text and stroked lines on F/B.SilkS
        void Silk(List<object> e, string where)
        {
            if (Sexpr.Find(e, "layer")?[1].Txt() is not ("F.SilkS" or "B.SilkS"))
                return;
            var head = e[0].Txt();
            bool box = head is "gr_text_box" or "fp_text_box";
            if (head is "gr_text" or "fp_text" or "property" || box)
            {
                if (Sexpr.Find(e, "hide")?[1].Txt() == "yes" || e.Any(x => x is Sym { Value: "hide" }))
                    return;
                var font = Sexpr.Find(Sexpr.Find(e, "effects") ?? [], "font");
                if (font is not null && Sexpr.Find(font, "size") is { } size)
                    Min(false, "silk_text_height", where, "text height", Nm.Of(size[1]));
                if (font is not null && Sexpr.Find(font, "thickness") is { } th)
                    Min(false, "silk_line_width", where, "text stroke", Nm.Of(th[1]));
                if (!box || Sexpr.Find(e, "border")?[1].Txt() != "yes")
                    return;
            }
            // a width of 0 is a filled shape's absent outline, but a line or
            // arc of width 0 is still printed (at KiCad's default width)
            if (Sexpr.Find(Sexpr.Find(e, "stroke") ?? [], "width") is { } w &&
                (Nm.Of(w[1]) > 0 || Sexpr.Find(e, "fill")?[1].Txt() is not ("yes" or "solid")))
                Min(false, "silk_line_width", where, $"{head} width", Nm.Of(w[1]));
        }
        foreach (var e in b.Root.OfType<List<object>>())
            Silk(e, "board");
        foreach (var fp in b.Footprints)
            foreach (var e in fp.Node.OfType<List<object>>())
                Silk(e, fp.Ref);
        return issues;
    }

    // 6 decimals (nm): a shortfall of a few um still shows in the message
    static string Mm(double mm) => mm.ToString("0.######", CultureInfo.InvariantCulture);
}
