using ClosedXML.Excel;
using Mech;
using ParamSheet;
using PartModel;
using Raster;
using Xunit;

namespace Part3dAsCode.Tests;

public sealed class SheetTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "pac-sheet-" + Guid.NewGuid().ToString("N"));
    readonly string _example = TestMeshes.ExamplePartText();
    readonly Layout _l;

    public SheetTests()
    {
        Directory.CreateDirectory(_dir);
        _l = new Layout(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static readonly (string Gate, IReadOnlyList<string> Problems)[] NoGates = [("DIM", []), ("MESH", []), ("FIT", [])];

    /// A reproduce-mode model and its part.cs text: w is measurable, h is a
    /// datasheet number, d is derived.
    static (Model Model, string Text) Reproduce(params Measurement[] extra)
    {
        const string text = """
            Param[] ps =
            [
                new("w", 10.0, Tol: 0.1),
                new("h", 20.0, Source: Source.Datasheet, Ref: "maker p.3"),
                new("d", w + 2),
                new("t", 3.0, Tol: 0.5, Source: Source.Measured, Ref: "D2"),
            ];
            """;
        Param[] ps =
        [
            new("w", 10.0, Tol: 0.1),
            new("h", 20.0, Source: Source.Datasheet, Ref: "maker p.3"),
            new("d", 12.0, Source: Source.Derived, Ref: "w + 2"),
            new("t", 3.0, Tol: 0.5, Source: Source.Measured, Ref: "D2"),
        ];
        Measurement[] ms =
        [
            new("D1", "overall width", "w", "box", (0, 0, 0)),
            new("D2", "wall", "t", "box", (1, 0, 0)),
            new("D3", "height", "h", "box", (2, 0, 0)),
            .. extra,
        ];
        return (TestMeshes.Model(Mode.Reproduce, ps, ms), text);
    }

    Model Example => Part.PartDesign.Model;

    string Write(Model m, string text)
    {
        var r = Sheet.Write(m, text, _l, NoGates);
        Assert.Empty(r.Problems);
        return _l.Sheet;
    }

    static void Mutate(string path, Action<XLWorkbook> change)
    {
        using var wb = new XLWorkbook(path);
        change(wb);
        wb.Save();
    }

    /// Parameters leads with the # column; every other sheet with the name.
    static int NameCol(IXLWorksheet ws) => ws.Name == "Parameters" ? 2 : 1;

    static IXLRow RowOf(IXLWorksheet ws, string name) =>
        ws.RowsUsed().Skip(1).First(r => r.Cell(NameCol(ws)).GetString() == name);

    /// The cell `col` of a row, or its value cell (just right of the name).
    static void SetValue(string path, string sheet, string name, double v, int col = 0) =>
        Mutate(path, wb =>
        {
            var ws = wb.Worksheet(sheet);
            RowOf(ws, name).Cell(col > 0 ? col : NameCol(ws) + 1).Value = v;
        });

    [ExampleFact]
    public void Write_designModelHasFourSheets()
    {
        var path = Write(Example, _example);
        using var wb = new XLWorkbook(path);
        Assert.Equal(["Parameters", "Fits", "Views", "Status"], wb.Worksheets.Select(w => w.Name));
        Assert.All(wb.Worksheets, w => Assert.True(w.IsProtected));
    }

    [Fact]
    public void Write_reproduceModelHasFiveSheets()
    {
        var (m, text) = Reproduce();
        using var wb = new XLWorkbook(Write(m, text));
        Assert.Equal(["Parameters", "Fits", "Measurements", "Views", "Status"], wb.Worksheets.Select(w => w.Name));
    }

    [Fact]
    public void Write_editabilityAndLockReasons()
    {
        var (m, text) = Reproduce();
        using var wb = new XLWorkbook(Write(m, text));
        var ws = wb.Worksheet("Parameters");
        Assert.False(RowOf(ws, "w").Cell(3).Style.Protection.Locked);
        Assert.False(RowOf(ws, "t").Cell(3).Style.Protection.Locked);
        Assert.True(RowOf(ws, "h").Cell(3).Style.Protection.Locked);
        Assert.Equal("datasheet", RowOf(ws, "h").Cell(10).GetString());
        Assert.True(RowOf(ws, "d").Cell(3).Style.Protection.Locked);
        Assert.Equal("derived: w + 2", RowOf(ws, "d").Cell(10).GetString());
        Assert.Equal("0.0###", RowOf(ws, "w").Cell(3).Style.NumberFormat.Format);
    }

    [ExampleFact]
    public void Write_exampleLocksTheDerivedHoleAndFreesTheChosenNumbers()
    {
        using var wb = new XLWorkbook(Write(Example, _example));
        var ws = wb.Worksheet("Parameters");
        var hole = RowOf(ws, "hole.d");
        Assert.True(hole.Cell(3).Style.Protection.Locked);
        Assert.StartsWith("derived: pin.d", hole.Cell(10).GetString());
        var w = RowOf(ws, "bracket.w");
        Assert.False(w.Cell(3).Style.Protection.Locked);
        Assert.Equal(XLDataType.Number, w.Cell(3).DataType);
        Assert.False(RowOf(wb.Worksheet("Fits"), "slip").Cell(2).Style.Protection.Locked);
    }

    [ExampleFact]
    public void Write_anAmbiguousNameIsLockedWithItsReason()
    {
        var text = _example + "\nvar dup = new Param(\"bracket.w\", 1.0);";
        using var wb = new XLWorkbook(Write(Example, text));
        var row = RowOf(wb.Worksheet("Parameters"), "bracket.w");
        Assert.True(row.Cell(3).Style.Protection.Locked);
        Assert.Contains("not a single literal in part.cs (declared 2 times)", row.Cell(10).GetString());
    }

    [ExampleFact]
    public void Write_viewsEmbedTheRendersAndNameWhatIsMissing()
    {
        var m = Example;
        Directory.CreateDirectory(_l.Renders);
        new Scene().Add(TestMeshes.MeshOf(TestMeshes.Box(0, 0, 0, 10, 10, 10))).Render(View.Iso, 300, 200)
            .SavePng(Path.Combine(_l.Renders, "bracket-iso.png"));
        var r = Sheet.Write(m, _example, _l, NoGates);
        Assert.Equal(1, r.Renders);
        using var wb = new XLWorkbook(_l.Sheet);
        var views = wb.Worksheet("Views");
        Assert.Single(views.Pictures);
        Assert.Contains(views.CellsUsed(), c => c.GetString() == "bracket-iso.png");
        Assert.Contains(views.CellsUsed(), c => c.GetString().StartsWith("no renders yet"));
    }

    [ExampleFact]
    public void Write_parametersShowTheLabelledViewsUnderTheTable_andTheirNumbers()
    {
        Directory.CreateDirectory(_l.Renders);
        new Scene().Add(TestMeshes.MeshOf(TestMeshes.Box(0, 0, 0, 10, 10, 10))).Render(View.Iso, 300, 200)
            .SavePng(Path.Combine(_l.Renders, "bracket-dims-iso.png"));
        Sheet.Write(Example, _example, _l, NoGates);
        using var wb = new XLWorkbook(_l.Sheet);
        var ws = wb.Worksheet("Parameters");
        Assert.Equal("#", ws.Cell(1, 1).GetString());
        var picture = Assert.Single(ws.Pictures);
        Assert.True(picture.TopLeftCell.Address.RowNumber > Example.Params.Count + 1);
        Assert.Contains(ws.CellsUsed(), c => c.GetString() == "bracket: where each # is on the part");
        Assert.Equal(1, RowOf(ws, "bracket.w").Cell(1).GetValue<int>());
        Assert.True(RowOf(ws, "coupon.margin").Cell(1).IsEmpty());
    }

    [Fact]
    public void Write_withoutAnnotationsThePanelShowsThePlainViews()
    {
        var (m, text) = Reproduce();
        Directory.CreateDirectory(_l.Renders);
        new Scene().Add(TestMeshes.MeshOf(TestMeshes.Box(0, 0, 0, 10, 10, 10))).Render(View.Iso, 300, 200)
            .SavePng(Path.Combine(_l.Renders, "box-iso.png"));
        Sheet.Write(m, text, _l, NoGates);
        using var wb = new XLWorkbook(_l.Sheet);
        var ws = wb.Worksheet("Parameters");
        var picture = Assert.Single(ws.Pictures);
        Assert.True(picture.TopLeftCell.Address.RowNumber > m.Params.Count + 1);
        Assert.Contains(ws.CellsUsed(), c => c.GetString().StartsWith("box: print pose"));
        Assert.True(RowOf(ws, "w").Cell(1).IsEmpty());
    }

    [ExampleFact]
    public void Write_statusListsEachGateAndItsProblems()
    {
        Sheet.Write(Example, _example, _l, [("DIM", ["bracket.w: 41 mm, expected 40"]), ("MESH", []), ("FIT", [])]);
        using var wb = new XLWorkbook(_l.Sheet);
        var texts = wb.Worksheet("Status").CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Contains("DIM", texts);
        Assert.Contains("1 problems", texts);
        Assert.Contains("bracket.w: 41 mm, expected 40", texts);
        Assert.Contains("0 problems", texts);
    }

    [ExampleFact]
    public void Write_aFileOpenInExcelIsReportedAndLeftIntact()
    {
        var path = Write(Example, _example);
        var before = File.ReadAllBytes(path);
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var r = Sheet.Write(Example, _example, _l, NoGates);
            Assert.Equal(["params.xlsx is open (close it in Excel and re-run)"], r.Problems);
        }
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(Path.ChangeExtension(path, ".tmp.xlsx")));
    }

    [ExampleFact]
    public void Read_aFreshSheetIsClean()
    {
        var read = Sheet.Read(Write(Example, _example), Example, _example);
        Assert.True(read.Clean);
        Assert.Empty(read.Warnings);
    }

    [ExampleFact]
    public void Read_aSheetOpenInExcelCanStillBeRead()
    {
        var path = Write(Example, _example);
        using var excel = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        Assert.True(Sheet.Read(path, Example, _example).Clean);
    }

    [ExampleFact]
    public void Read_anEditedValueIsAnEdit()
    {
        var path = Write(Example, _example);
        SetValue(path, "Parameters", "bracket.w", 42);
        SetValue(path, "Fits", "slip", 0.25);
        var read = Sheet.Read(path, Example, _example);
        Assert.Empty(read.Problems);
        Assert.Equal(
            [new SheetEdit("Param", "bracket.w", 40, 42), new SheetEdit("Fit", "slip", 0.2, 0.25)],
            read.Edits);
    }

    [ExampleFact]
    public void Read_matchesRowsByNameNotPosition()
    {
        var path = Write(Example, _example);
        Mutate(path, wb =>
        {
            var ws = wb.Worksheet("Parameters");
            var a = RowOf(ws, "bracket.w");
            var b = RowOf(ws, "base.t");
            for (var c = 1; c <= 11; c++)
                (a.Cell(c).Value, b.Cell(c).Value) = (b.Cell(c).Value, a.Cell(c).Value);
        });
        SetValue(path, "Parameters", "bracket.w", 43);
        var read = Sheet.Read(path, Example, _example);
        Assert.Empty(read.Problems);
        Assert.Equal([new SheetEdit("Param", "bracket.w", 40, 43)], read.Edits);
    }

    [ExampleFact]
    public void Read_notANumberIsAProblem()
    {
        var path = Write(Example, _example);
        Mutate(path, wb => RowOf(wb.Worksheet("Parameters"), "bracket.w").Cell(3).Value = "6,2");
        var read = Sheet.Read(path, Example, _example);
        Assert.Empty(read.Edits);
        Assert.Contains(read.Problems, p => p.Contains("Parameters bracket.w") && p.Contains("not a number"));
    }

    [ExampleFact]
    public void Read_aNameNotInTheModelIsAProblem()
    {
        var path = Write(Example, _example);
        Mutate(path, wb =>
        {
            var row = RowOf(wb.Worksheet("Parameters"), "bracket.w");
            row.Cell(2).Value = "gone";
            row.Cell(3).Value = 41.0;
        });
        Assert.Contains(Sheet.Read(path, Example, _example).Problems, p => p.Contains("gone") && p.Contains("not in the current model"));
    }

    [ExampleFact]
    public void Read_aLockedCellEditIsAProblemNamingTheReason()
    {
        var path = Write(Example, _example);
        SetValue(path, "Parameters", "hole.d", 7.5);
        var read = Sheet.Read(path, Example, _example);
        Assert.Empty(read.Edits);
        Assert.Contains(read.Problems, p => p.Contains("hole.d") && p.Contains("locked") && p.Contains("derived: pin.d"));
    }

    [ExampleFact]
    public void Read_editabilityIsRecomputedFromPartCs()
    {
        var path = Write(Example, _example);
        SetValue(path, "Parameters", "bracket.w", 42);
        var text = _example.Replace("new(\"bracket.w\", 40.0,", "new(\"bracket.w\", 40.0 + 0,");
        Assert.Contains(Sheet.Read(path, Example, text).Problems, p => p.Contains("bracket.w") && p.Contains("locked"));
    }

    [ExampleFact]
    public void Read_anEditAlreadyAppliedToPartCsIsSkipped()
    {
        var path = Write(Example, _example);
        SetValue(path, "Parameters", "bracket.w", 42);
        var applied = Sheet.Apply(_example, Sheet.Read(path, Example, _example).Edits).Text;
        Assert.True(Sheet.Read(path, Example, applied).Clean);
    }

    [ExampleFact]
    public void Read_aChangedPartCsMakesAConflict()
    {
        var path = Write(Example, _example);
        SetValue(path, "Parameters", "bracket.w", 42);
        var moved = _example.Replace("new(\"bracket.w\", 40.0,", "new(\"bracket.w\", 41.0,");
        var read = Sheet.Read(path, Example, moved);
        Assert.Empty(read.Edits);
        var p = Assert.Single(read.Problems);
        Assert.Contains("conflict", p);
        Assert.Contains("delete params.xlsx and regenerate it", p);
        Assert.Contains("40.0", p);
        Assert.Contains("41.0", p);
        Assert.Contains("42.0", p);
    }

    [ExampleFact]
    public void Read_aSheetNotAWorkbookIsAProblem()
    {
        File.WriteAllText(_l.Sheet, "not a workbook");
        var read = Sheet.Read(_l.Sheet, Example, _example);
        Assert.Contains(read.Problems, p => p.StartsWith("params.xlsx cannot be read"));
    }

    [Fact]
    public void Read_anUnknownMeasurementIdIsAProblem()
    {
        var (m, text) = Reproduce();
        var path = Write(m, text);
        Mutate(path, wb =>
        {
            var row = RowOf(wb.Worksheet("Measurements"), "D1");
            row.Cell(1).Value = "D9";
            row.Cell(9).Value = 10.2;
        });
        Assert.Contains(Sheet.Read(path, m, text).Problems, p => p.Contains("D9") && p.Contains("not in the current model"));
    }

    [Theory]
    [InlineData(new[] { 10.4 }, 10.4, "D1: 10.4")]
    [InlineData(new[] { 10.2, 10.4 }, 10.3, "D1: 10.2 / 10.4")]
    [InlineData(new[] { 10.5, 10.1, 10.3 }, 10.3, "D1: 10.5 / 10.1 / 10.3")]
    public void Read_aMeasurementWritesTheMedianWithSourceAndRef(double[] readings, double median, string reference)
    {
        var (m, text) = Reproduce();
        var path = Write(m, text);
        Mutate(path, wb =>
        {
            var row = RowOf(wb.Worksheet("Measurements"), "D1");
            for (var i = 0; i < readings.Length; i++)
                row.Cell(9 + i).Value = readings[i];
        });
        var read = Sheet.Read(path, m, text);
        var e = Assert.Single(read.Edits);
        Assert.Equal("w", e.Name);
        Assert.Equal(median, e.To, 9);
        Assert.Equal("Measured", e.Source);
        Assert.Equal(reference, e.Ref);
    }

    [Fact]
    public void Read_blankReadingsProduceNothing()
    {
        var (m, text) = Reproduce();
        var read = Sheet.Read(Write(m, text), m, text);
        Assert.True(read.Clean);
    }

    [Fact]
    public void Read_aTextReadingIsAProblem()
    {
        var (m, text) = Reproduce();
        var path = Write(m, text);
        Mutate(path, wb => RowOf(wb.Worksheet("Measurements"), "D1").Cell(10).Value = "ten");
        var read = Sheet.Read(path, m, text);
        Assert.Empty(read.Edits);
        Assert.Contains(read.Problems, p => p.Contains("D1") && p.Contains("R2") && p.Contains("not a number"));
    }

    [Fact]
    public void Read_aSpreadOverTheToleranceIsAWarningNotAProblem()
    {
        var (m, text) = Reproduce();
        var path = Write(m, text);
        Mutate(path, wb =>
        {
            var row = RowOf(wb.Worksheet("Measurements"), "D1");
            row.Cell(9).Value = 10.0;
            row.Cell(10).Value = 10.3;
        });
        var read = Sheet.Read(path, m, text);
        Assert.Empty(read.Problems);
        Assert.Single(read.Edits);
        Assert.Contains(read.Warnings, w => w.StartsWith("D1:") && w.Contains("spread"));
    }

    [Fact]
    public void Read_aMeasurementOfAParameterAlsoEditedOnParametersIsAProblem()
    {
        var (m, text) = Reproduce();
        var path = Write(m, text);
        SetValue(path, "Parameters", "w", 11);
        SetValue(path, "Measurements", "D1", 10.2, col: 9);
        var read = Sheet.Read(path, m, text);
        Assert.Contains(read.Problems, p => p.Contains("D1") && p.Contains("also edited on Parameters"));
    }

    [Fact]
    public void Read_aMeasurementOfALockedParameterIsAProblem()
    {
        var (m, text) = Reproduce();
        var path = Write(m, text);
        SetValue(path, "Measurements", "D3", 20.1, col: 9);
        var read = Sheet.Read(path, m, text);
        Assert.Empty(read.Edits);
        Assert.Contains(read.Problems, p => p.Contains("D3") && p.Contains("locked") && p.Contains("datasheet"));
    }

    [Fact]
    public void Read_aMeasurementAlreadyAppliedIsSkipped()
    {
        var (m, text) = Reproduce();
        var path = Write(m, text);
        SetValue(path, "Measurements", "D2", 3.0, col: 9);
        Assert.True(Sheet.Read(path, m, text).Clean);
    }

    [Fact]
    public void Read_readingsLeftAfterAPull_andPartCsChangedSince_areAConflict()
    {
        var (m, text) = Reproduce();
        var path = Write(m, text);
        SetValue(path, "Measurements", "D1", 10.2, col: 9);
        var pulled = Sheet.Apply(text, Sheet.Read(path, m, text).Edits).Text;
        // the rebuilt model now holds w as measured at the median: clean
        var rebuilt = TestMeshes.Model(Mode.Reproduce,
            [.. m.Params.Select(q => q.Name == "w" ? q with { Value = 10.2, Source = Source.Measured, Ref = "D1" } : q)],
            [.. m.Measurements]);
        Assert.True(Sheet.Read(path, rebuilt, pulled).Clean);

        var later = pulled.Replace("new(\"w\", 10.2,", "new(\"w\", 10.5,");
        var read = Sheet.Read(path, m, later);
        Assert.Empty(read.Edits);
        var p = Assert.Single(read.Problems);
        Assert.Contains("D1: conflict", p);
        Assert.Contains("10.5", p);
        Assert.Contains("delete params.xlsx and regenerate it", p);
    }

    [Fact]
    public void Read_twoMeasurementsOfOneParameterAreAProblem()
    {
        var (m, text) = Reproduce(new Measurement("D4", "width again", "w", "box", (3, 0, 0)));
        var path = Write(m, text);
        SetValue(path, "Measurements", "D1", 10.2, col: 9);
        SetValue(path, "Measurements", "D4", 10.6, col: 9);
        var read = Sheet.Read(path, m, text);
        Assert.False(read.Clean);
        var p = Assert.Single(read.Problems);
        Assert.Contains("D4", p);
        Assert.Contains("measured twice", p);
    }

    [Fact]
    public void Read_aMeasurementOfAParameterWhoseParametersEditIsAlreadyAppliedIsAProblem()
    {
        var (m, text) = Reproduce();
        var path = Write(m, text);
        SetValue(path, "Parameters", "w", 11);
        SetValue(path, "Measurements", "D1", 10.2, col: 9);
        var applied = text.Replace("new(\"w\", 10.0,", "new(\"w\", 11.0,");
        var read = Sheet.Read(path, m, applied);
        Assert.Contains(read.Problems, p => p.Contains("D1") && p.Contains("also edited on Parameters"));
    }

    [Fact]
    public void Read_anEqualMedianOnANonMeasuredParameterStillSetsSourceAndRef()
    {
        var (m, text) = Reproduce();
        var path = Write(m, text);
        SetValue(path, "Measurements", "D1", 10.0, col: 9);
        var edit = Assert.Single(Sheet.Read(path, m, text).Edits);
        Assert.Equal(new SheetEdit("Param", "w", 10, 10, "Measured", "D1: 10.0"), edit);
        var (applied, problems) = Sheet.Apply(text, [edit]);
        Assert.Empty(problems);
        Assert.Contains("new(\"w\", 10.0, Tol: 0.1, Source: Source.Measured, Ref: \"D1: 10.0\"),", applied);
    }

    [Fact]
    public void Read_aMissingSheetIsAProblem()
    {
        var (m, text) = Reproduce();
        var path = Write(m, text);
        Mutate(path, wb =>
        {
            wb.Worksheet("Fits").Delete();
            wb.Worksheet("Measurements").Delete();
        });
        var problems = Sheet.Read(path, m, text).Problems;
        Assert.Contains(problems, p => p.Contains("sheet Fits is missing") && p.Contains("delete params.xlsx"));
        Assert.Contains(problems, p => p.Contains("sheet Measurements is missing"));
    }

    [Fact]
    public void Read_aNameAppearingTwiceIsAProblem()
    {
        var (m, text) = Reproduce();
        var path = Write(m, text);
        Mutate(path, wb => wb.Worksheet("Parameters").RowsUsed().Last().Cell(2).Value = "w");
        Assert.Contains(Sheet.Read(path, m, text).Problems, p => p.Contains("Parameters w: row appears twice"));
    }

    [Fact]
    public void Write_measurementsShowTheCalloutsBelowTheTable_andStatusSaysHowToUseTheSheet()
    {
        var (m, text) = Reproduce();
        Directory.CreateDirectory(_l.Renders);
        new Scene().Add(TestMeshes.MeshOf(TestMeshes.Box(0, 0, 0, 10, 10, 10))).Render(View.Iso, 300, 200)
            .SavePng(Path.Combine(_l.Renders, "box-callouts-iso.png"));
        var r = Sheet.Write(m, text, _l, NoGates);
        Assert.Equal(1, r.Renders);
        using var wb = new XLWorkbook(_l.Sheet);
        var ws = wb.Worksheet("Measurements");
        var picture = Assert.Single(ws.Pictures);
        Assert.True(picture.TopLeftCell.Address.RowNumber > m.Measurements.Count + 1);
        Assert.Contains(ws.CellsUsed(), c => c.GetString() == "box-callouts-iso.png");
        var status = wb.Worksheet("Status").CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Contains("model", status);
        Assert.Contains("mode", status);
        Assert.Contains("reproduce", status);
        Assert.Contains("generated (UTC)", status);
        Assert.Contains(status, t => t.Contains("sheet.cs -- pull"));
    }

    [Fact]
    public void Apply_replacesPositionalSourceAndRef()
    {
        const string text = "new(\"w\", 10.0, \"mm\", Source.Chosen, 0.1, false, \"old\"),";
        var edit = new SheetEdit("Param", "w", 10, 10.4, "Measured", "D1: 10.4");
        var (applied, problems) = Sheet.Apply(text, [edit]);
        Assert.Empty(problems);
        Assert.Equal("new(\"w\", 10.4, \"mm\", Source.Measured, 0.1, false, \"D1: 10.4\"),", applied);
    }

    [Fact]
    public void Apply_writesTheValueSourceAndRef()
    {
        var (m, text) = Reproduce();
        var path = Write(m, text);
        SetValue(path, "Measurements", "D1", 10.4, col: 9);
        var edits = Sheet.Read(path, m, text).Edits;
        var (applied, problems) = Sheet.Apply(text, edits);
        Assert.Empty(problems);
        Assert.Contains("new(\"w\", 10.4, Tol: 0.1, Source: Source.Measured, Ref: \"D1: 10.4\"),", applied);
    }

    [Fact]
    public void Apply_isAllOrNothing()
    {
        var edits = new[]
        {
            new SheetEdit("Param", "bracket.w", 40, 42),
            new SheetEdit("Param", "hole.d", 7, 8),
        };
        var (text, problems) = Sheet.Apply(_example, edits);
        Assert.Equal(_example, text);
        Assert.Single(problems);
    }
}
