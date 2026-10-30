// The parameter sheet: mech/params.xlsx, generated from the model with the
// renders beside the numbers, and read back for the user's edits. part.cs
// stays the single source of truth; the sheet is a build output (gitignored)
// and a pull rewrites literals in part.cs through PartEdit.
//
// Sheets: Parameters, Fits, Measurements (reproduce mode), Views, Status.
// Every sheet is protected without a password; only editable cells are
// unlocked. A hidden Was column holds each value at generation, so
// an edit is judged three ways against the current part.cs (see Read).
//
// xlsx rather than CSV: a French-locale Excel saves CSV with `;` separators
// and decimal commas, while xlsx keeps numbers as numbers.
//
// Library file for #:include — types only, no top-level statements.
// Needs partmodel.cs, partedit.cs and mech.cs, plus the ClosedXML and
// Microsoft.CodeAnalysis.CSharp packages.

using System.Globalization;
using ClosedXML.Excel;
using Mech;
using PartEdit;
using PartModel;

namespace ParamSheet;

/// One change a pull applies. Measurement edits also carry the Source and
/// Ref to write; plain edits leave them null.
public sealed record SheetEdit(string Kind, string Name, double From, double To, string? Source = null, string? Ref = null)
{
    public override string ToString() => Source is null
        ? $"{Name}: {Literals.Format(From)} -> {Literals.Format(To)}"
        : $"{Name}: {Literals.Format(From)} -> {Literals.Format(To)}, source measured, ref {Ref}";
}

/// What the sheet holds that part.cs does not yet: edits to apply, problems
/// that block any pull, and warnings that do not.
public sealed record SheetRead(IReadOnlyList<SheetEdit> Edits, IReadOnlyList<string> Problems, IReadOnlyList<string> Warnings)
{
    public bool Clean => Edits.Count == 0 && Problems.Count == 0;
}

public sealed record WriteResult(IReadOnlyList<string> Problems, int Parameters, int Editable, int Fits, int Renders);

public static class Sheet
{
    public const string Parameters = "Parameters";
    public const string Fits = "Fits";
    public const string Measurements = "Measurements";
    public const string Views = "Views";
    public const string Status = "Status";

    const double Same = 1e-9;
    const int ImageWidth = 320;
    const int RowPixels = 20;
    const int PanelWidth = 480;
    static readonly XLColor MarkInk = XLColor.FromArgb(200, 40, 40);
    static readonly XLColor EditFill = XLColor.FromHtml("#FFF2CC");
    static readonly XLColor LockFill = XLColor.FromHtml("#D9D9D9");
    static readonly XLColor HeadFill = XLColor.FromHtml("#DDEBF7");

    // ---- editability ----------------------------------------------------

    /// Whether `name` may be edited from the sheet, judged from the current
    /// part.cs text: Why is the lock reason when not, Current the literal's
    /// value when it is.
    sealed record Edit(bool Ok, string Why, double Current);

    static Edit ParamEditability(Param p, string partText)
    {
        if (p.Source == Source.Derived)
            return new(false, p.Ref.Length > 0 ? $"derived: {p.Ref}" : "derived", 0);
        if (p.Source == Source.Datasheet)
            return new(false, "datasheet", 0);
        return Literal(p.Name, partText);
    }

    static Edit Literal(string name, string partText)
    {
        var at = Literals.Locate(partText, name);
        if (at.Kind != LocateKind.Found)
            return new(false, $"not a single literal in part.cs ({at})", 0);
        var text = partText.Substring(at.Start, at.Length).Replace(" ", "");
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? new(true, "", v)
            : new(false, $"not a single literal in part.cs ({text})", 0);
    }

    static bool Equal(double a, double b) => Math.Abs(a - b) <= Same;

    // ---- write ----------------------------------------------------------

    /// Write mech/params.xlsx. `gates` are the DIM/MESH/FIT problem lines
    /// shown on Status. Returns the problems that stopped the write (the
    /// file open in Excel); an existing file is then left intact.
    public static WriteResult Write(Model m, string partText, Layout l,
        IEnumerable<(string Gate, IReadOnlyList<string> Problems)> gates)
    {
        using var wb = new XLWorkbook();
        var editable = WriteParameters(wb, m, partText, l);
        WriteFits(wb, m, partText);
        var renders = 0;
        if (m.Measurements.Count > 0)
            renders += WriteMeasurements(wb, m, partText, l);
        renders += WriteViews(wb, m, l);
        WriteStatus(wb, m, gates);

        var tmp = Path.ChangeExtension(l.Sheet, ".tmp.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(l.Sheet))!);
        try
        {
            wb.SaveAs(tmp);
            try
            {
                if (File.Exists(l.Sheet))
                    using (new FileStream(l.Sheet, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                File.Move(tmp, l.Sheet, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new([$"{Path.GetFileName(l.Sheet)} is open (close it in Excel and re-run)"], 0, 0, 0, 0);
            }
        }
        finally
        {
            File.Delete(tmp);
        }
        return new([], m.Params.Count, editable, m.Fits.Count, renders);
    }

    static void Header(IXLWorksheet ws, params string[] names)
    {
        for (var i = 0; i < names.Length; i++)
            ws.Cell(1, i + 1).Value = names[i];
        var h = ws.Range(1, 1, 1, names.Length);
        h.Style.Font.Bold = true;
        h.Style.Fill.BackgroundColor = HeadFill;
        ws.SheetView.FreezeRows(1);
    }

    static void Number(IXLCell c, double v)
    {
        c.Value = v;
        c.Style.NumberFormat.Format = "0.0###";
    }

    static void Input(IXLCell c, bool editable)
    {
        c.Style.Fill.BackgroundColor = editable ? EditFill : LockFill;
        c.Style.Protection.Locked = !editable;
    }

    static int WriteParameters(XLWorkbook wb, Model m, string partText, Layout l)
    {
        var ws = wb.AddWorksheet(Parameters);
        Header(ws, "#", "Name", "Value", "Unit", "Tol", "Critical", "Source", "Ref", "Note", "Lock", "Was");
        var r = 1;
        var editable = 0;
        foreach (var p in m.Params)
        {
            r++;
            var e = ParamEditability(p, partText);
            if (m.Label(p.Name) is { } label)
            {
                ws.Cell(r, 1).Value = int.Parse(label, CultureInfo.InvariantCulture);
                ws.Cell(r, 1).Style.Font.Bold = true;
                ws.Cell(r, 1).Style.Font.FontColor = MarkInk;
            }
            ws.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(r, 2).Value = p.Name;
            Number(ws.Cell(r, 3), p.Value);
            Input(ws.Cell(r, 3), e.Ok);
            ws.Cell(r, 4).Value = p.Unit;
            Number(ws.Cell(r, 5), p.Tol);
            ws.Cell(r, 6).Value = p.Critical ? "yes" : "";
            ws.Cell(r, 7).Value = p.Source.ToString();
            ws.Cell(r, 8).Value = p.Ref;
            ws.Cell(r, 9).Value = p.Note;
            ws.Cell(r, 10).Value = e.Why;
            Number(ws.Cell(r, 11), p.Value);
            if (e.Ok)
                editable++;
        }
        Finish(ws, hide: 11);
        ws.Column(9).Width = Math.Min(ws.Column(9).Width, 40);
        ws.Column(10).Width = Math.Min(ws.Column(10).Width, 28);
        ws.SheetView.FreezeRows(1);
        // the views go under the table, side by side by pixel offset: the
        // table's columns are too narrow to hold one picture each
        var row = r + 2;
        if (m.Annotations.Count > 0)
            foreach (var body in m.Annotations.Select(a => a.Body).Distinct())
                Place(ws, ref row, $"{body}: where each # is on the part",
                    Renders(l, [.. new[] { "iso", "front", "top", "right" }.Select(v => $"{body}-dims-{v}.png")]), 2, 1, PanelWidth, packed: true);
        else
            PlaceViews(ws, m, l, ref row, 1, perRow: 2, PanelWidth, packed: true);
        return editable;
    }

    static void WriteFits(XLWorkbook wb, Model m, string partText)
    {
        var ws = wb.AddWorksheet(Fits);
        Header(ws, "Name", "Gap", "Note", "Lock", "Was");
        var r = 1;
        foreach (var f in m.Fits)
        {
            r++;
            var e = Literal(f.Name, partText);
            ws.Cell(r, 1).Value = f.Name;
            Number(ws.Cell(r, 2), f.Gap);
            Input(ws.Cell(r, 2), e.Ok);
            ws.Cell(r, 3).Value = f.Note;
            ws.Cell(r, 4).Value = e.Why;
            Number(ws.Cell(r, 5), f.Gap);
        }
        Finish(ws, hide: 5);
    }

    static int WriteMeasurements(XLWorkbook wb, Model m, string partText, Layout l)
    {
        var ws = wb.AddWorksheet(Measurements);
        Header(ws, "Id", "Measure", "Parameter", "Current", "Unit", "Tol", "Critical", "Source", "R1", "R2", "R3", "Lock");
        var r = 1;
        foreach (var x in m.Measurements)
        {
            r++;
            var p = m.P(x.Param);
            var e = ParamEditability(p, partText);
            ws.Cell(r, 1).Value = x.Id;
            ws.Cell(r, 2).Value = x.What;
            ws.Cell(r, 3).Value = x.Param;
            Number(ws.Cell(r, 4), p.Value);
            ws.Cell(r, 5).Value = p.Unit;
            Number(ws.Cell(r, 6), p.Tol);
            ws.Cell(r, 7).Value = p.Critical ? "yes" : "";
            ws.Cell(r, 8).Value = p.Source.ToString();
            for (var c = 9; c <= 11; c++)
            {
                ws.Cell(r, c).Style.NumberFormat.Format = "0.0###";
                Input(ws.Cell(r, c), e.Ok);
            }
            ws.Cell(r, 12).Value = e.Why;
        }
        ws.Columns(1, 12).AdjustToContents();
        var row = r + 2;
        var placed = 0;
        foreach (var body in m.Measurements.Select(x => x.Body).Distinct())
            placed += Place(ws, ref row, $"{body}: callouts to measure at",
                Renders(l, [.. new[] { "iso", "front", "top", "right" }.Select(v => $"{body}-callouts-{v}.png")]), perRow: 1);
        ws.Protect();
        return placed;
    }

    static int WriteViews(XLWorkbook wb, Model m, Layout l)
    {
        var ws = wb.AddWorksheet(Views);
        ws.Columns(1, 4).Width = (ImageWidth + 6) / 7.0;
        var row = 1;
        var placed = PlaceViews(ws, m, l, ref row, 1, perRow: 4);
        ws.Protect();
        return placed;
    }

    /// Each printed body's print pose, then the assembly and its views, as a grid of
    /// `perRow` columns starting at column `col`.
    static int PlaceViews(IXLWorksheet ws, Model m, Layout l, ref int row, int col, int perRow, int width = ImageWidth,
        bool packed = false)
    {
        var placed = 0;
        foreach (var b in m.Bodies.Where(b => b.Printed))
            placed += Place(ws, ref row, $"{b.Name}: print pose, up {b.Up}",
                Renders(l, [.. new[] { "iso", "front", "top", "right" }.Select(v => $"{b.Name}-{v}.png")]), perRow, col, width, packed);
        placed += Place(ws, ref row, "assembly",
            Renders(l, [.. new[] { "iso", "isoback", "front", "top" }.Select(v => $"assembly-{v}.png")]), perRow, col, width, packed);
        foreach (var v in m.Views)
            placed += Place(ws, ref row, $"assembly, {v.Name}",
                Renders(l, [$"assembly-{v.Name}-iso.png", $"assembly-{v.Name}-isoback.png"]), perRow, col, width, packed);
        return placed;
    }

    static List<string> Renders(Layout l, string[] names) =>
        [.. names.Select(n => Path.Combine(l.Renders, n)).Where(File.Exists)];

    /// A heading, then the images in a grid of `perRow` columns, each
    /// captioned with its file name above it. No render files: a text line.
    /// `packed` sets the images side by side by pixel offset from `col`
    /// (for narrow columns) and leaves out the file-name captions; each
    /// render carries its own title.
    static int Place(IXLWorksheet ws, ref int row, string heading, List<string> files, int perRow, int col = 1,
        int width = ImageWidth, bool packed = false)
    {
        ws.Cell(row, col).Value = heading;
        ws.Cell(row, col).Style.Font.Bold = true;
        row++;
        if (files.Count == 0)
        {
            ws.Cell(row, col).Value = "no renders yet (run render.cs)";
            row += 2;
            return 0;
        }
        for (var i = 0; i < files.Count; i += perRow)
        {
            var tallest = 0;
            for (var j = 0; j < perRow && i + j < files.Count; j++)
            {
                var (w, h) = PngSize(files[i + j]);
                var height = (int)Math.Round(width * (double)h / Math.Max(w, 1));
                tallest = Math.Max(tallest, height);
                if (packed)
                {
                    var (c, dx) = ColumnAt(ws, col, j * (width + 16));
                    ws.AddPicture(files[i + j]).MoveTo(ws.Cell(row, c), dx, 0).WithSize(width, height);
                }
                else
                {
                    ws.Cell(row, col + j).Value = Path.GetFileName(files[i + j]);
                    ws.AddPicture(files[i + j]).MoveTo(ws.Cell(row + 1, col + j)).WithSize(width, height);
                }
            }
            row += (packed ? 1 : 2) + (int)Math.Ceiling(tallest / (double)RowPixels);
        }
        return files.Count;
    }

    /// The column `px` pixels right of `col`'s left edge, and the offset
    /// into it: Excel clamps a picture's offset to its anchor cell's width.
    static (int Col, int Dx) ColumnAt(IXLWorksheet ws, int col, int px)
    {
        while (true)
        {
            var w = (int)Math.Round(ws.Column(col).Width * 7 + 5);
            if (px < w)
                return (col, px);
            px -= w;
            col++;
        }
    }

    static (int W, int H) PngSize(string path)
    {
        using var s = File.OpenRead(path);
        var b = new byte[24];
        return s.Read(b, 0, 24) == 24
            ? (b[16] << 24 | b[17] << 16 | b[18] << 8 | b[19], b[20] << 24 | b[21] << 16 | b[22] << 8 | b[23])
            : (ImageWidth, ImageWidth);
    }

    static void WriteStatus(XLWorkbook wb, Model m, IEnumerable<(string Gate, IReadOnlyList<string> Problems)> gates)
    {
        var ws = wb.AddWorksheet(Status);
        var r = 1;
        void Line(string a, string b = "")
        {
            ws.Cell(r, 1).Value = a;
            ws.Cell(r, 2).Value = b;
            r++;
        }
        Line("model", m.Name);
        Line("mode", m.Mode.ToString().ToLowerInvariant());
        Line("generated (UTC)", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        r++;
        Line("how to use", "Edit the highlighted cells on Parameters, Fits and (reproduce mode) Measurements, save, and close Excel.");
        Line("", "Then ask the agent to apply the sheet, or run: dotnet run mech/tools/sheet.cs -- pull");
        r++;
        foreach (var (gate, problems) in gates)
        {
            Line(gate, $"{problems.Count} problems");
            foreach (var p in problems)
                Line("", p);
        }
        ws.Column(1).Style.Font.Bold = true;
        ws.Columns(1, 2).AdjustToContents();
        ws.Protect();
    }

    static void Finish(IXLWorksheet ws, int hide)
    {
        ws.Columns(1, hide).AdjustToContents();
        ws.Column(hide).Hide();
        ws.Protect();
    }

    // ---- read -----------------------------------------------------------

    /// The edits the saved sheet holds against the current part.cs text.
    /// Opens with read/write sharing, so a sheet open in Excel can still be
    /// read (only its saved content counts). Rows match by Name/Id, never by
    /// position.
    public static SheetRead Read(string path, Model m, string partText)
    {
        var edits = new List<SheetEdit>();
        var problems = new List<string>();
        var warnings = new List<string>();
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(fs);
            var file = Path.GetFileName(path);
            var changed = new HashSet<string>();
            ReadParameters(wb, m, partText, file, changed, edits, problems);
            ReadFits(wb, m, partText, file, changed, edits, problems);
            if (m.Measurements.Count > 0)
                ReadMeasurements(wb, m, partText, file, changed, edits, problems, warnings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException
            or System.Xml.XmlException or ArgumentException or FormatException)
        {
            problems.Add($"{Path.GetFileName(path)} cannot be read: {e.Message}");
        }
        return new(edits, problems, warnings);
    }

    static bool TryNumber(IXLCell c, out double v)
    {
        v = 0;
        if (c.DataType != XLDataType.Number)
            return false;
        v = c.GetDouble();
        return double.IsFinite(v);
    }

    static string Show(IXLCell c) => c.IsEmpty() ? "empty" : $"'{c.GetFormattedString()}'";

    /// The Parameters/Fits row logic: value, Was and the current literal.
    /// `changed` collects every name whose cell differs from Was, edit or
    /// already applied, for the Measurements double-edit check.
    static void ReadRows(XLWorkbook wb, string sheet, string kind, int nameCol, int valueCol, int wasCol, string file,
        HashSet<string> changed, Func<string, (bool Known, Edit Edit)> lookup, List<SheetEdit> edits, List<string> problems)
    {
        if (!wb.TryGetWorksheet(sheet, out var ws))
        {
            problems.Add($"sheet {sheet} is missing; delete {file} and regenerate it");
            return;
        }
        var seen = new HashSet<string>();
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var name = row.Cell(nameCol).GetString();
            if (name.Length == 0)
                continue;
            if (!seen.Add(name))
            {
                problems.Add($"{sheet} {name}: row appears twice");
                continue;
            }
            var value = row.Cell(valueCol);
            var wasOk = TryNumber(row.Cell(wasCol), out var was);
            if (wasOk && TryNumber(value, out var same) && Equal(same, was))
                continue;
            changed.Add(name);
            if (!TryNumber(value, out var to))
            {
                problems.Add($"{sheet} {name}: {Show(value)} is not a number");
                continue;
            }
            var (known, e) = lookup(name);
            if (!known)
            {
                problems.Add($"{sheet} {name}: not in the current model; delete {file} and regenerate it");
                continue;
            }
            if (!e.Ok)
            {
                problems.Add($"{sheet} {name}: locked ({e.Why})");
                continue;
            }
            if (Equal(e.Current, to))
                continue;
            if (!wasOk || !Equal(e.Current, was))
            {
                problems.Add($"{sheet} {name}: conflict, the sheet was generated at {(wasOk ? Literals.Format(was) : "?")}, " +
                    $"part.cs now holds {Literals.Format(e.Current)} and the sheet says {Literals.Format(to)}; delete {file} and regenerate it");
                continue;
            }
            edits.Add(new(kind, name, e.Current, to));
        }
    }

    static void ReadParameters(XLWorkbook wb, Model m, string partText, string file, HashSet<string> changed,
        List<SheetEdit> edits, List<string> problems) =>
        ReadRows(wb, Parameters, "Param", 2, 3, 11, file, changed, name =>
            m.Params.FirstOrDefault(p => p.Name == name) is { } p ? (true, ParamEditability(p, partText)) : (false, new Edit(false, "", 0)),
            edits, problems);

    static void ReadFits(XLWorkbook wb, Model m, string partText, string file, HashSet<string> changed,
        List<SheetEdit> edits, List<string> problems) =>
        ReadRows(wb, Fits, "Fit", 1, 2, 5, file, changed, name =>
            m.Fits.Any(f => f.Name == name) ? (true, Literal(name, partText)) : (false, new Edit(false, "", 0)),
            edits, problems);

    static void ReadMeasurements(XLWorkbook wb, Model m, string partText, string file, HashSet<string> changed,
        List<SheetEdit> edits, List<string> problems, List<string> warnings)
    {
        if (!wb.TryGetWorksheet(Measurements, out var ws))
        {
            problems.Add($"sheet {Measurements} is missing; delete {file} and regenerate it");
            return;
        }
        var claimed = new HashSet<string>();
        var seen = new HashSet<string>();
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var id = row.Cell(1).GetString();
            var x = m.Measurements.FirstOrDefault(x => x.Id == id);
            var readings = new List<double>();
            var bad = false;
            for (var c = 9; c <= 11; c++)
            {
                var cell = row.Cell(c);
                if (cell.IsEmpty())
                    continue;
                if (TryNumber(cell, out var v))
                    readings.Add(v);
                else
                {
                    problems.Add($"{Measurements} {id}: R{c - 8} {Show(cell)} is not a number");
                    bad = true;
                }
            }
            if (bad || readings.Count == 0)
                continue;
            if (!seen.Add(id))
            {
                problems.Add($"{Measurements} {id}: row appears twice");
                continue;
            }
            if (x is null)
            {
                problems.Add($"{Measurements} {id}: not in the current model; delete {file} and regenerate it");
                continue;
            }
            var p = m.P(x.Param);
            var e = ParamEditability(p, partText);
            if (!e.Ok)
            {
                problems.Add($"{Measurements} {id}: {p.Name} is locked ({e.Why})");
                continue;
            }
            if (changed.Contains(p.Name))
            {
                problems.Add($"{Measurements} {id}: {p.Name} is also edited on Parameters; use one or the other");
                continue;
            }
            if (!claimed.Add(p.Name))
            {
                problems.Add($"{Measurements} {id}: {p.Name} is measured twice (another row also names it); use one");
                continue;
            }
            var sorted = readings.Order().ToArray();
            var median = sorted.Length switch { 1 => sorted[0], 2 => (sorted[0] + sorted[1]) / 2, _ => sorted[1] };
            if (sorted.Length > 1 && sorted[^1] - sorted[0] > p.Tol + Same)
                warnings.Add($"{id}: readings spread {Literals.Format(sorted[^1] - sorted[0])} mm, over the tolerance {Literals.Format(p.Tol)} of {p.Name}");
            if (Equal(e.Current, median) && p.Source == Source.Measured)
                continue;
            // Current is the row's Was: part.cs changed since the sheet was
            // made, to something other than the median, is a conflict
            if (TryNumber(row.Cell(4), out var was) && !Equal(e.Current, was) && !Equal(e.Current, median))
            {
                problems.Add($"{Measurements} {id}: conflict, the sheet was generated with {p.Name} at {Literals.Format(was)}, " +
                    $"part.cs now holds {Literals.Format(e.Current)} and the readings give {Literals.Format(median)}; delete {file} and regenerate it");
                continue;
            }
            var rf = $"{id}: {string.Join(" / ", readings.Select(Literals.Format))}";
            edits.Add(new("Param", p.Name, e.Current, median, "Measured", rf));
        }
    }

    // ---- apply ----------------------------------------------------------

    /// Apply the edits to the part.cs text: the new text, or the problems
    /// (all-or-nothing; the text is then unchanged).
    public static (string Text, IReadOnlyList<string> Problems) Apply(string partText, IEnumerable<SheetEdit> edits)
    {
        var text = partText;
        var problems = new List<string>();
        foreach (var e in edits)
        {
            var steps = new List<EditResult>();
            if (!Equal(e.From, e.To))
                steps.Add(Literals.SetValue(text, e.Name, e.To));
            else
                steps.Add(new(text));
            if (steps[^1].Ok && e.Source is not null)
                steps.Add(Literals.SetArgument(steps[^1].Text, e.Name, "Source", "Source." + e.Source, position: 3));
            if (steps[^1].Ok && e.Ref is not null)
                steps.Add(Literals.SetArgument(steps[^1].Text, e.Name, "Ref", Literals.Quote(e.Ref), position: 6));
            var bad = steps.FirstOrDefault(s => !s.Ok);
            if (bad is not null)
                problems.Add(bad.Problem!);
            else
                text = steps[^1].Text;
        }
        return problems.Count > 0 ? (partText, problems) : (text, problems);
    }
}
