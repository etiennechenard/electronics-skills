using System.Diagnostics;
using Xunit;

namespace PcbAsCode.Tests;

/// The templates are consumed two ways: compiled here as plain .cs files,
/// and #:include'd by .NET 10 file-based apps (dotnet run tool.cs) in the
/// project tools. This proves the second path works, in both layouts: the
/// plugin's (templates/ next to tests/) and a project's (the templates flat
/// in pcb/tools/, the suite in pcb/tools/tests/).
public class FileBasedAppTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pcbascode-app-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
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
    public async Task Fabcheck_reportsTheBoardsProblemsAndFails()
    {
        if (!DotnetOnPath())
            return;
        var fab = TestBoards.Temp(_dir, "fab.json", TestBoards.FabJson(DateOnly.FromDateTime(DateTime.Today)));
        TestBoards.Temp(_dir, "t.kicad_pro", TestBoards.Pro);
        var pcb = TestBoards.Temp(_dir, "t.kicad_pcb", TestBoards.Pcb);
        var (rc, stdout, stderr) = await Run(Tool("fabcheck.cs"), fab, pcb);
        Assert.True(rc == 1, $"fabcheck.cs exited {rc}\nstdout: {stdout}\nstderr: {stderr}");
        Assert.Contains("t: 2 errors, 2 silkscreen warnings", stdout);
        Assert.Contains("FAB PROBLEMS: 2", stdout);
    }

    [Fact]
    public async Task Fabcheck_passesACleanFileAndRefusesAMissingOne()
    {
        if (!DotnetOnPath())
            return;
        var fab = TestBoards.Temp(_dir, "fab.json", TestBoards.FabJson(DateOnly.FromDateTime(DateTime.Today)));
        var (rc, stdout, stderr) = await Run(Tool("fabcheck.cs"), fab);
        Assert.True(rc == 0, $"fabcheck.cs exited {rc}\nstdout: {stdout}\nstderr: {stderr}");
        Assert.Contains("FAB PROBLEMS: 0", stdout);
        (rc, _, stderr) = await Run(Tool("fabcheck.cs"), fab, Path.Combine(_dir, "nope.kicad_pcb"));
        Assert.Equal(2, rc);
        Assert.Contains("no such file", stderr);
        var broken = TestBoards.Temp(_dir, "broken.json", "{ \"fab\": ");
        (rc, stdout, _) = await Run(Tool("fabcheck.cs"), broken);
        Assert.Equal(1, rc);
        Assert.Contains("not valid JSON", stdout);
        Assert.Contains("FAB PROBLEMS: 1", stdout);
        // a board cut off mid-file is an error, not a pass
        TestBoards.Temp(_dir, "cut.kicad_pro", TestBoards.Pro);
        var cut = TestBoards.Temp(_dir, "cut.kicad_pcb", TestBoards.Pcb[..(TestBoards.Pcb.Length / 2)]);
        (rc, stdout, _) = await Run(Tool("fabcheck.cs"), fab, cut);
        Assert.Equal(1, rc);
        Assert.Contains("cannot be read", stdout);
        // --max-age: a bad value is a usage error, a check older than it fails
        (rc, _, _) = await Run(Tool("fabcheck.cs"), fab, "--max-age", "soon");
        Assert.Equal(2, rc);
        var old = TestBoards.Temp(_dir, "old.json", TestBoards.FabJson(DateOnly.FromDateTime(DateTime.Today).AddDays(-10)));
        (rc, stdout, _) = await Run(Tool("fabcheck.cs"), old, "--max-age", "5");
        Assert.Equal(1, rc);
        Assert.Contains("10 days ago", stdout);
    }

    /// The tool next to the templates: templates/ in the plugin, the
    /// tests' parent folder in a project.
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
