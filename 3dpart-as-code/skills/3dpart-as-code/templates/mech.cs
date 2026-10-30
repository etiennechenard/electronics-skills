// The mech/ layout and the Python runner: where every input and output
// lives, the project's venv (build123d pinned in requirements.txt), and
// running a geometry script or pac.py's fit stage with the right paths.
//
// Library file for #:include — types only, no top-level statements.

using System.Diagnostics;

namespace Mech;

/// mech/ and its folders. Root is the folder holding tools/, parts/ and
/// requirements.txt; the tools find it from their own location.
public sealed record Layout(string Root)
{
    public string Tools => Path.Combine(Root, "tools");
    public string Parts => Path.Combine(Root, "parts");
    public string Build => Path.Combine(Root, "build");
    public string Renders => Path.Combine(Build, "renders");
    public string Out => Path.Combine(Root, "out");
    public string Sheet => Path.Combine(Root, "params.xlsx");
    public string Params => Path.Combine(Build, "params.json");
    public string FitReport => Path.Combine(Build, "fit.json");
    public string Requirements => Path.Combine(Root, "requirements.txt");
    public string Venv => Path.Combine(Root, ".venv");
    public string Pac => Path.Combine(Tools, "pac.py");

    public string Step(string body) => Path.Combine(Build, body + ".step");
    public string Stl(string body) => Path.Combine(Build, body + ".stl");
    public string AsmStl(string body) => Path.Combine(Build, "asm", body + ".stl");
    public string Assembly => Path.Combine(Build, "assembly.step");

    /// kicad-cli's exports of a board: part "board" (the bare board) or "parts"
    /// (its components' 3D models), in the board's own frame.
    public string Boards => Path.Combine(Build, "boards");
    public string BoardStep(string board, string part) => Path.Combine(Boards, $"{board}-{part}.step");
    /// A board's placed meshes: asm/<board>.stl and asm/<board>-parts.stl.
    public string BoardStl(string board, string part) => AsmStl(part == "board" ? board : $"{board}-parts");
    /// A view's cut body: asm/views/<view>/<body>.stl.
    public string ViewStl(string view, string body) => Path.Combine(Build, "asm", "views", view, body + ".stl");

    /// The probe file a script writes (pac.finish: probe-<script stem>.json).
    public string Probe(string script) => Path.Combine(Build, $"probe-{Path.GetFileNameWithoutExtension(script)}.json");

    /// The layout of the running tool: `--mech <dir>` in args wins, else the
    /// parent of the entry file's folder (mech/tools/x.cs → mech/). Outside
    /// a file-based app (the test suite) it walks up from the assembly to the
    /// first folder holding tools/part.cs, so part.cs can find files
    /// relative to mech/ either way (`Layout.Here([]).Root`).
    public static Layout Here(string[] args)
    {
        var i = Array.IndexOf(args, "--mech");
        if (i >= 0 && i + 1 < args.Length)
            return new(Path.GetFullPath(args[i + 1]));
        if (AppContext.GetData("EntryPointFileDirectoryPath") is string dir)
            return new(Path.GetFullPath(Path.Combine(dir, "..")));
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "tools", "part.cs")))
                return new(d.FullName);
        return new(Directory.GetCurrentDirectory());
    }

    /// The template libraries that read outputs but never shape them.
    public static readonly string[] NotInputs = ["stl.cs", "raster.cs", "checks.cs", "partedit.cs", "paramsheet.cs"];

    /// Every file the outputs derive from: the C# library files in tools/
    /// (part.cs and what it includes; entry tools — the files with a `#:sdk`
    /// line — and NotInputs are not inputs), pac.py, the geometry scripts,
    /// requirements.txt, and the model's extra inputs (mech-relative: board
    /// files, imported STEPs).
    public IEnumerable<string> Inputs(IEnumerable<string>? extra = null)
    {
        var sep = Path.DirectorySeparatorChar;
        IEnumerable<string> Files(string dir, string pattern, SearchOption o) =>
            Directory.Exists(dir) ? Directory.EnumerateFiles(dir, pattern, o)
                .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}")) : [];
        var libs = Files(Tools, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(f => !NotInputs.Contains(Path.GetFileName(f)) && !File.ReadLines(f).Any(line => line.StartsWith("#:sdk")));
        return libs.Concat(Files(Tools, "*.py", SearchOption.TopDirectoryOnly))
            .Concat(Files(Parts, "*.py", SearchOption.AllDirectories))
            .Concat(File.Exists(Requirements) ? [Requirements] : [])
            .Concat((extra ?? []).Select(e => Path.GetFullPath(Path.Combine(Root, e))));
    }
}

/// kicad-cli, for the boards a model shows (PartModel.Board).
public static class KiCad
{
    /// KICAD_CLI wins (`env` stands in for it in the tests); then PATH; then
    /// the newest install in the usual places. Null when none is found.
    public static string? Find(string? env = null)
    {
        env ??= Environment.GetEnvironmentVariable("KICAD_CLI");
        if (!string.IsNullOrEmpty(env))
            return env;
        var exe = OperatingSystem.IsWindows() ? "kicad-cli.exe" : "kicad-cli";
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = Path.Combine(dir, exe);
            if (File.Exists(p))
                return p;
        }
        if (OperatingSystem.IsWindows())
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "KiCad");
            if (Directory.Exists(root))
                return Directory.EnumerateDirectories(root)
                    .Select(d => (Dir: d, Version: Version.TryParse(Path.GetFileName(d), out var v) ? v : new Version(0, 0)))
                    .OrderByDescending(d => d.Version)
                    .Select(d => Path.Combine(d.Dir, "bin", exe))
                    .FirstOrDefault(File.Exists);
            return null;
        }
        return new[] { "/Applications/KiCad/KiCad.app/Contents/MacOS/kicad-cli", "/usr/bin/kicad-cli", "/usr/local/bin/kicad-cli" }
            .FirstOrDefault(File.Exists);
    }

    /// The STEP export of one part of a board: "board" is the bare board with
    /// its silkscreen and mask, "parts" its components' 3D models. Origin is
    /// the KiCad page point (mm) that becomes the STEP's zero.
    public static string[] ExportArgs(string pcb, string step, string part, (double X, double Y) origin)
    {
        var o = FormattableString.Invariant($"{origin.X}x{origin.Y}mm");
        string[] what = part == "board"
            ? ["--no-components", "--include-silkscreen", "--include-soldermask"]
            : ["--no-board-body", "--subst-models"];
        return ["pcb", "export", "step", "-f", "--user-origin", o, .. what, "-o", step, pcb];
    }

    /// Export both parts of a board into build/boards/. A board with no 3D
    /// models gives no parts file (that is not a failure). Returns the
    /// problems and the number of 3D model files kicad-cli could not find.
    public static (List<string> Problems, int MissingModels) Export(string cli, Layout l, string name, string pcb,
        (double X, double Y) origin)
    {
        var bad = new List<string>();
        var missing = 0;
        Directory.CreateDirectory(l.Boards);
        var path = Path.GetFullPath(Path.Combine(l.Root, pcb));
        if (!File.Exists(path))
            return ([$"board {name}: {pcb} not found"], 0);
        if (!ExecutableExists(cli))
            return ([$"board {name}: kicad-cli not found: {cli}"], 0);
        foreach (var part in new[] { "board", "parts" })
        {
            var step = l.BoardStep(name, part);
            if (File.Exists(step))
                File.Delete(step);
            var r = Python.Run(cli, ExportArgs(path, step, part, origin));
            var text = r.Stdout + r.Stderr;
            missing += text.Split('\n').Count(line => line.Contains("File not found", StringComparison.OrdinalIgnoreCase));
            if (part == "board" && (r.Rc != 0 || !File.Exists(step)))
                bad.Add($"board {name}: kicad-cli failed (exit {r.Rc}): {text.Trim()}");
        }
        return (bad, missing);
    }

    /// Process.Start with UseShellExecute = false resolves a bare name on the
    /// PATH (appending .exe on Windows); anything carrying a directory must
    /// be a real file.
    internal static bool ExecutableExists(string exe) =>
        Path.IsPathRooted(exe) || exe.Contains(Path.DirectorySeparatorChar) || exe.Contains(Path.AltDirectorySeparatorChar)
            ? File.Exists(exe)
            : (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => Path.Combine(d, OperatingSystem.IsWindows() && !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe + ".exe" : exe))
                .Any(File.Exists);
}

public static class Python
{
    /// The interpreter: PAC_PYTHON wins (tests, a shared venv), else the
    /// project's .venv.
    public static string Find(Layout l)
    {
        var env = Environment.GetEnvironmentVariable("PAC_PYTHON");
        if (!string.IsNullOrEmpty(env))
            return env;
        return OperatingSystem.IsWindows()
            ? Path.Combine(l.Venv, "Scripts", "python.exe")
            : Path.Combine(l.Venv, "bin", "python");
    }

    /// The interpreter, creating the venv and installing requirements.txt
    /// on first use. build123d wheels lag new Pythons, so the venv is made
    /// from 3.12 (`py -3.12` on Windows, `python3.12` elsewhere).
    public static string Ensure(Layout l)
    {
        var py = Find(l);
        if (File.Exists(py))
            return py;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PAC_PYTHON")))
            throw new FileNotFoundException($"PAC_PYTHON points at {py}, which does not exist");
        Console.WriteLine($"creating {l.Venv} (Python 3.12) and installing {Path.GetFileName(l.Requirements)}");
        var (exe, pre) = OperatingSystem.IsWindows() ? ("py", new[] { "-3.12" }) : ("python3.12", Array.Empty<string>());
        Check(Run(exe, [.. pre, "-m", "venv", l.Venv]), "creating the venv");
        Check(Run(py, ["-m", "pip", "install", "--disable-pip-version-check", "-q", "-r", l.Requirements]),
            "pip install -r requirements.txt");
        return py;
    }

    /// Run a geometry script the way pac.load() expects.
    public static (int Rc, string Stdout, string Stderr) Script(Layout l, string python, string script) =>
        Run(python, [Path.Combine(l.Root, script), "--params", l.Params, "--out", l.Build, "--script", script], l);

    /// pac.py's assembly stage → build/assembly.step, every body in place.
    public static (int Rc, string Stdout, string Stderr) Assembly(Layout l, string python) =>
        Run(python, [l.Pac, "assembly", "--params", l.Params, "--out", l.Build], l);

    /// pac.py's fit stage → build/fit.json.
    public static (int Rc, string Stdout, string Stderr) Fit(Layout l, string python) =>
        Run(python, [l.Pac, "fit", "--params", l.Params, "--out", l.Build], l);

    /// Run without throwing; pac.py is importable (PYTHONPATH = tools/).
    public static (int Rc, string Stdout, string Stderr) Run(string exe, IEnumerable<string> args, Layout? l = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        if (l is not null)
        {
            psi.Environment["PYTHONPATH"] = l.Tools;
            psi.WorkingDirectory = l.Root;
        }
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, o.GetAwaiter().GetResult(), e.GetAwaiter().GetResult());
    }

    /// Throw with the interpreter's own output (a traceback's last lines
    /// name the failing line of the geometry script).
    public static string Check((int Rc, string Stdout, string Stderr) r, string what)
    {
        if (r.Rc != 0)
            throw new InvalidOperationException($"{what} failed (exit {r.Rc}):\n{r.Stdout}{r.Stderr}");
        return r.Stdout;
    }
}
