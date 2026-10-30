using System.Diagnostics;
using ClosedXML.Excel;
using Mech;
using ParamSheet;
using Part;
using Xunit;

namespace Part3dAsCode.Tests;

/// The templates are consumed two ways: compiled here as plain .cs files,
/// and #:include'd by .NET 10 file-based apps (dotnet run tool.cs) in the
/// project tools. This proves the second path, and runs the worked example
/// through the whole pipeline in a scratch mech/ layout — build, check, fit,
/// render, export — then breaks it three ways (a dimension, a fit, a stale
/// build) and expects each gate to say so.
///
/// The pipeline tests need a Python with build123d: PAC_PYTHON names its
/// interpreter (a venv made from mech/requirements.txt). Without it they
/// return early, reporting as passed — like pcb-as-code's live kicad-cli
/// tests when KiCad is absent.
public class FileBasedAppTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "part3dascode-app-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task Smoke_dotnetRunInclusionPathWorks()
    {
        if (!DotnetOnPath())
            return;
        var (rc, stdout, stderr) = await Run(Tool("smoke.cs"));
        Assert.True(rc == 0, $"smoke.cs exited {rc}\nstdout: {stdout}\nstderr: {stderr}");
        Assert.Contains("smoke ok", stdout);
    }

    [Fact]
    public async Task Example_goesThroughEveryGate_andEachBreakIsCaught()
    {
        if (!DotnetOnPath() || Pac() is null)
            return;
        var mech = Scaffold();

        var (rc, o, e) = await Tool(mech, "build");
        Assert.True(rc == 0, $"build: {o}{e}");
        Assert.Contains("parts/bracket.py: 3 bodies", o);
        Assert.Contains("view cutaway: bracket cut, +X of 0 kept", o);
        Assert.Contains("BUILD PROBLEMS: 0", o);

        (rc, o, e) = await Tool(mech, "check");
        Assert.True(rc == 0, $"check: {o}{e}");
        Assert.Contains("DIM PROBLEMS: 0", o);
        Assert.Contains("MESH PROBLEMS: 0", o);

        (rc, o, e) = await Tool(mech, "fit");
        Assert.True(rc == 0, $"fit: {o}{e}");
        Assert.Contains("clearance pin/bracket: gap 0.200 mm", o);
        Assert.Contains("FIT PROBLEMS: 0", o);

        (rc, o, e) = await Tool(mech, "render");
        Assert.True(rc == 0, $"render: {o}{e}");
        Assert.True(File.Exists(Path.Combine(mech, "build", "renders", "assembly-iso.png")));
        foreach (var v in new[] { "iso", "isoback" })
            Assert.True(File.Exists(Path.Combine(mech, "build", "renders", $"assembly-cutaway-{v}.png")), v);
        // the hide and move views render too (the pin lifted out / hidden)
        foreach (var v in new[] { "exploded", "open" })
            foreach (var f in new[] { "iso", "isoback" })
                Assert.True(File.Exists(Path.Combine(mech, "build", "renders", $"assembly-{v}-{f}.png")), $"{v}-{f}");
        // the cut keeps the bracket's +X half: about half its volume
        var half = new Layout(mech).ViewStl("cutaway", "bracket");
        var whole = Mesh.Stl.Load(new Layout(mech).AsmStl("bracket")).Volume();
        Assert.InRange(Mesh.Stl.Load(half).Volume() / whole, 0.4, 0.6);

        (rc, o, e) = await Tool(mech, "export");
        Assert.True(rc == 0, $"export: {o}{e}");
        foreach (var f in new[] { "bracket.stl", "pin.stl", "hole-coupon.stl", "bracket.step", "assembly.step", "manifest.json" })
            Assert.True(File.Exists(Path.Combine(mech, "out", f)), f);

        // a stale build: the geometry script changed after the build
        File.SetLastWriteTimeUtc(Path.Combine(mech, "parts", "bracket.py"), DateTime.UtcNow);
        (rc, o, _) = await Tool(mech, "export");
        Assert.Equal(1, rc);
        Assert.Contains("EXPORT REFUSED", o);
        (rc, o, _) = await Tool(mech, "check");
        Assert.Equal(1, rc);
        Assert.Contains("STALE PROBLEMS", o);

        // a dimension the geometry does not honour, and a fit too tight to hold
        var part = Path.Combine(mech, "tools", "part.cs");
        var src = File.ReadAllText(part);
        src = src.Replace("ExpectedDim.Of(P(ps, \"base.t\"), \"bracket.base.t\", 0.01)",
                          "new(\"bracket.base.t\", 4.5, 0.01)")
                 .Replace("Clearances = [new(\"pin\", \"bracket\", slip.Gap * 0.75, \"radial slip gap\")]",
                          "Clearances = [new(\"pin\", \"bracket\", 0.3, \"radial slip gap\")]");
        File.WriteAllText(part, src);
        (rc, o, e) = await Tool(mech, "build");
        Assert.True(rc == 0, $"build: {o}{e}");
        (rc, o, _) = await Tool(mech, "check");
        Assert.Equal(1, rc);
        Assert.Contains("bracket.base.t: 4 mm, expected 4.5", o);
        Assert.Contains("DIM PROBLEMS: 1", o);
        (rc, o, _) = await Tool(mech, "fit");
        Assert.Equal(1, rc);
        Assert.Contains("clearance pin/bracket: gap 0.2 mm, needs >= 0.3", o);
        Assert.Contains("FIT PROBLEMS: 1", o);
        (rc, _, _) = await Tool(mech, "render");
        Assert.Equal(0, rc);
        (rc, o, _) = await Tool(mech, "export");
        Assert.Equal(1, rc);
        Assert.Contains("EXPORT REFUSED", o);
    }

    /// A 20 x 10 mm board centred on (100, 100) on the KiCad page, no parts.
    private const string FixtureBoard = """
        (kicad_pcb (version 20240108) (generator "pac-test")
          (general (thickness 1.6))
          (paper "A4")
          (layers (0 "F.Cu" signal) (31 "B.Cu" signal) (44 "Edge.Cuts" user))
          (setup (pad_to_mask_clearance 0))
          (net 0 "")
          (gr_rect (start 90 95) (end 110 105) (stroke (width 0.1) (type default)) (fill none) (layer "Edge.Cuts"))
        )
        """;

    [Fact]
    public async Task Board_isExportedPlacedAndRendered_andItsFileIsAnInput()
    {
        if (!DotnetOnPath() || Pac() is null || KiCad.Find() is null)
            return;
        var mech = Scaffold();
        File.WriteAllText(Path.Combine(mech, "demo.kicad_pcb"), FixtureBoard);
        var part = Path.Combine(mech, "tools", "part.cs");
        // standing on edge behind the bracket: its +x along -X, its up along
        // +Z, so its F face looks along +Y; its centre at (0, -20, 30)
        File.WriteAllText(part, File.ReadAllText(part).Replace("Views =",
            "Boards = [new(\"demo\", \"demo.kicad_pcb\", (100, 100), \"-X\", \"+Z\", (0, -20, 30))],\n            Views ="));

        var (rc, o, e) = await Tool(mech, "build");
        Assert.True(rc == 0, $"build: {o}{e}");
        Assert.Contains("board demo: exported from demo.kicad_pcb", o);
        Assert.Contains("1 boards", o);
        var l = new Layout(mech);
        Assert.False(File.Exists(l.BoardStl("demo", "parts")));   // no 3D models: no parts mesh
        var mesh = Mesh.Stl.Load(l.BoardStl("demo", "board"));
        var all = mesh.Tris.SelectMany(t => new[] { t.A, t.B, t.C }).ToList();
        Assert.Equal(-10, all.Min(p => p.X), 1);
        Assert.Equal(10, all.Max(p => p.X), 1);
        Assert.Equal(-20, all.Min(p => p.Y), 1);
        Assert.InRange(all.Max(p => p.Y), -18.6, -18.3);   // the board's thickness, as KiCad's stackup gives it
        Assert.Equal(25, all.Min(p => p.Z), 1);
        Assert.Equal(35, all.Max(p => p.Z), 1);

        (rc, o, e) = await Tool(mech, "render");
        Assert.True(rc == 0, $"render: {o}{e}");

        // an edited board makes the build stale
        File.SetLastWriteTimeUtc(Path.Combine(mech, "demo.kicad_pcb"), DateTime.UtcNow);
        (rc, o, _) = await Tool(mech, "check");
        Assert.Equal(1, rc);
        Assert.Contains("is older than demo.kicad_pcb", o);
    }

    [Fact]
    public async Task Build_reportsAScriptError()
    {
        if (!DotnetOnPath() || Pac() is null)
            return;
        var mech = Scaffold();
        File.AppendAllText(Path.Combine(mech, "parts", "bracket.py"), "\nraise ValueError('broken on purpose')\n");
        var (rc, o, _) = await Tool(mech, "build");
        Assert.Equal(1, rc);
        Assert.Contains("broken on purpose", o);
        Assert.Contains("parts/bracket.py: failed", o);
    }

    [Fact]
    public async Task SheetPull_writesTheEditsIntoPartCs()
    {
        if (!DotnetOnPath() || !TestMeshes.CompiledModelIsTheExample)
            return;
        var mech = Scaffold();
        var part = Path.Combine(mech, "tools", "part.cs");
        var before = File.ReadAllText(part);
        var layout = new Layout(mech);
        var written = Sheet.Write(PartDesign.Model, before, layout, []);
        Assert.Empty(written.Problems);
        EditSheet(layout.Sheet, ("Parameters", "bracket.w", 42.0), ("Fits", "slip", 0.25));

        var (rc, o, e) = await Run(Path.Combine(mech, "tools", "sheet.cs"), "pull", "--mech", mech);
        Assert.True(rc == 0, $"sheet pull: {o}{e}");
        Assert.Contains("bracket.w: 40.0 -> 42.0", o);
        Assert.Contains("slip: 0.2 -> 0.25", o);
        Assert.Contains("PULL PROBLEMS: 0", o);
        Assert.Contains("rebuild: build, check, fit, render, sheet", o);

        var after = File.ReadAllText(part);
        var changed = before.Split('\n').Zip(after.Split('\n')).Where(z => z.First != z.Second).Select(z => z.Second.Trim()).ToList();
        Assert.Equal(2, changed.Count);
        Assert.Contains(changed, c => c.StartsWith("new(\"bracket.w\", 42.0,"));
        Assert.Contains(changed, c => c.StartsWith("new(\"slip\", 0.25,"));

        (rc, o, e) = await Run(Path.Combine(mech, "tools", "sheet.cs"), "pull", "--mech", mech);
        Assert.True(rc == 0, $"second pull: {o}{e}");
        Assert.Contains("nothing to pull", o);
        Assert.Contains("PULL PROBLEMS: 0", o);
    }

    [Fact]
    public async Task SheetPull_refusesALockedEditAndWritesNothing()
    {
        if (!DotnetOnPath() || !TestMeshes.CompiledModelIsTheExample)
            return;
        var mech = Scaffold();
        var part = Path.Combine(mech, "tools", "part.cs");
        var before = File.ReadAllText(part);
        var layout = new Layout(mech);
        Assert.Empty(Sheet.Write(PartDesign.Model, before, layout, []).Problems);
        EditSheet(layout.Sheet, ("Parameters", "bracket.w", 42.0), ("Parameters", "hole.d", 7.0));

        var (rc, o, _) = await Run(Path.Combine(mech, "tools", "sheet.cs"), "pull", "--mech", mech);
        Assert.Equal(1, rc);
        Assert.Contains("hole.d: locked (derived: pin.d + 2 x slip)", o);
        Assert.Contains("PULL PROBLEMS: 1", o);
        Assert.Equal(before, File.ReadAllText(part));

        File.Delete(layout.Sheet);
        (rc, o, _) = await Run(Path.Combine(mech, "tools", "sheet.cs"), "pull", "--mech", mech);
        Assert.Equal(1, rc);
        Assert.Contains("no params.xlsx (run sheet.cs)", o);
    }

    [Fact]
    public async Task Export_printsSheetProblemsAsAscii()
    {
        if (!DotnetOnPath() || !TestMeshes.CompiledModelIsTheExample)
            return;
        var mech = Scaffold();
        var layout = new Layout(mech);
        Assert.Empty(Sheet.Write(PartDesign.Model, File.ReadAllText(Path.Combine(mech, "tools", "part.cs")), layout, []).Problems);
        EditSheet(layout.Sheet, ("Parameters", "hole.d", 7.0));

        var (rc, o, _) = await Tool(mech, "export");
        Assert.Equal(1, rc);
        Assert.Contains("pin.d + 2 x slip", o);
        Assert.All(o, c => Assert.True(c < 128, $"non-ASCII character U+{(int)c:X4} in the export output"));
    }

    [Fact]
    public async Task Sheet_editPullRebuildLoop_andExportRefusesUnpulledEdits()
    {
        if (!DotnetOnPath() || Pac() is null)
            return;
        var mech = Scaffold();
        var layout = new Layout(mech);
        foreach (var step in new[] { "build", "check", "fit", "render" })
        {
            var (rc0, o0, e0) = await Tool(mech, step);
            Assert.True(rc0 == 0, $"{step}: {o0}{e0}");
        }

        var (rc, o, e) = await Tool(mech, "sheet");
        Assert.True(rc == 0, $"sheet: {o}{e}");
        Assert.Contains("SHEET PROBLEMS: 0", o);
        Assert.True(File.Exists(layout.Sheet));

        EditSheet(layout.Sheet, ("Parameters", "bracket.w", 42.0));
        (rc, o, _) = await Tool(mech, "sheet");
        Assert.Equal(1, rc);
        Assert.Contains("bracket.w: sheet value 42.0 not pulled", o);
        Assert.Contains("pull them first", o);

        (rc, o, _) = await Tool(mech, "export");
        Assert.Equal(1, rc);
        Assert.Contains("SHEET bracket.w: sheet value 42.0 not pulled", o);
        Assert.Contains("EXPORT REFUSED", o);

        (rc, o, e) = await Run(Path.Combine(mech, "tools", "sheet.cs"), "pull", "--mech", mech);
        Assert.True(rc == 0, $"pull: {o}{e}");
        Assert.Contains("bracket.w: 40.0 -> 42.0", o);

        foreach (var step in new[] { "build", "check", "fit", "render" })
        {
            var (rc1, o1, e1) = await Tool(mech, step);
            Assert.True(rc1 == 0, $"{step} after pull: {o1}{e1}");
        }
        Assert.Equal(42, Checks.Probes.Load(layout)["bracket.bbox.x"], 6);

        (rc, o, e) = await Tool(mech, "sheet");
        Assert.True(rc == 0, $"sheet after pull: {o}{e}");
        (rc, o, e) = await Tool(mech, "export");
        Assert.True(rc == 0, $"export after pull: {o}{e}");
    }

    /// Change cells of a generated sheet the way a user would in Excel.
    private static void EditSheet(string path, params (string Sheet, string Name, double Value)[] edits)
    {
        using var wb = new XLWorkbook(path);
        foreach (var (sheet, name, value) in edits)
        {
            var nameCol = sheet == "Parameters" ? 2 : 1;   // Parameters leads with the # column
            wb.Worksheet(sheet).RowsUsed().Skip(1).First(r => r.Cell(nameCol).GetString() == name).Cell(nameCol + 1).Value = value;
        }
        wb.Save();
    }

    [Fact]
    public void CopyTools_survivesTheProjectLayoutWhereTemplatesAreTools()
    {
        // kickoff's project layout: the suite lives in mech/tools/tests/
        // beside the tools, so the test's template folder IS mech/tools —
        // the copy must not copy a file onto itself (File.Copy throws)
        var tools = Directory.CreateDirectory(Path.Combine(_dir, "tools")).FullName;
        var f = Path.Combine(tools, "smoke.cs");
        File.WriteAllText(f, "// smoke\n");
        CopyTools(tools, tools);
        Assert.Equal("// smoke\n", File.ReadAllText(f));
    }

    /// The template tools into mech/tools. In a project's layout the
    /// templates ARE mech/tools (kickoff copies the suite beside them),
    /// where a copy is a self-copy — File.Copy throws on it, so skip it.
    private static void CopyTools(string templates, string tools)
    {
        foreach (var f in Directory.GetFiles(templates, "*.cs").Concat(Directory.GetFiles(templates, "pac.py")))
        {
            var dst = Path.Combine(tools, Path.GetFileName(f));
            if (string.Equals(dst, f, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                continue;
            File.Copy(f, dst);
        }
    }

    /// A scratch mech/ holding the tools and the worked example, whichever
    /// layout the suite runs in: the plugin's (templates/part.cs and
    /// templates/parts/bracket.py are the example) or a project's (the tools
    /// in mech/tools/, the example kept in mech/tools/tests/example/, since
    /// the project's own part.cs has replaced it).
    private string Scaffold()
    {
        var mech = Path.Combine(_dir, "mech");
        var tools = Directory.CreateDirectory(Path.Combine(mech, "tools")).FullName;
        var parts = Directory.CreateDirectory(Path.Combine(mech, "parts")).FullName;
        var templates = Path.GetDirectoryName(Tool("smoke.cs"))!;
        CopyTools(templates, tools);
        var fixture = FindUp("example", "part.cs");
        var (model, geometry) = fixture is not null
            ? (fixture, Path.Combine(Path.GetDirectoryName(fixture)!, "bracket.py"))
            : (Path.Combine(templates, "part.cs"), Path.Combine(templates, "parts", "bracket.py"));
        File.Copy(model, Path.Combine(tools, "part.cs"), overwrite: true);
        File.Copy(geometry, Path.Combine(parts, "bracket.py"));
        var req = Path.Combine(templates, "requirements.txt");
        File.Copy(File.Exists(req) ? req : Path.Combine(templates, "..", "requirements.txt"), Path.Combine(mech, "requirements.txt"));
        return mech;
    }

    private static string? Pac()
    {
        var py = Environment.GetEnvironmentVariable("PAC_PYTHON");
        return !string.IsNullOrEmpty(py) && File.Exists(py) ? py : null;
    }

    private static Task<(int Rc, string Stdout, string Stderr)> Tool(string mech, string name) =>
        Run(Path.Combine(mech, "tools", name + ".cs"));

    /// The tool next to the templates: templates/ in the plugin, the
    /// tests' parent folder in a project (mech/tools/tests/).
    private static string Tool(string file)
    {
        var path = FindUp("templates", file) ?? FindUp("", file);
        Assert.True(path is not null, $"{file} not found above {AppContext.BaseDirectory}");
        return path!;
    }

    private static async Task<(int Rc, string Stdout, string Stderr)> Run(string tool, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add(tool);
        if (args.Length > 0)
            psi.ArgumentList.Add("--");
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, await stdoutTask, await stderrTask);
    }

    /// Walk up from the test assembly until <paramref name="dir"/>/<paramref name="file"/> exists.
    private static string? FindUp(string dir, string file)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            var cand = Path.Combine(d.FullName, dir, file);
            if (File.Exists(cand))
                return cand;
            d = d.Parent;
        }
        return null;
    }

    private static bool DotnetOnPath()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("dotnet", "--version")
            { UseShellExecute = false, RedirectStandardOutput = true });
            return p is not null;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
}
