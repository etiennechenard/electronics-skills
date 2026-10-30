using System.Diagnostics;
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
