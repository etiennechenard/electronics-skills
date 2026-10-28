// kicad-cli runner plus the three machine-readable gates of the pipeline:
// ERC report, DRC report, and the netlist KiCad exports from a schematic.
//
// Library file for #:include — types only, no top-level statements.

using System.Diagnostics;
using System.Text.Json;
using Sx;

namespace Kicad;

public static class Cli
{
    /// kicad-cli path. The default is the known-good 10.0.7 install; set
    /// the KICAD_CLI environment variable (or assign Exe) to run against a
    /// newer KiCad — then re-run the test suite, whose live kicad-cli
    /// tests verify the new install (the CLI surface and JSON report
    /// formats used here have been stable).
    public static string Exe { get; set; } =
        Environment.GetEnvironmentVariable("KICAD_CLI") ?? @"C:\Program Files\KiCad\10.0\bin\kicad-cli.exe";

    /// Run kicad-cli, return stdout; throw with both streams on failure.
    public static string Run(params string[] args)
    {
        var (rc, stdout, stderr) = Capture(args);
        if (rc != 0)
            throw new InvalidOperationException(
                $"kicad-cli {string.Join(" ", args.Take(3))} failed:\n{stdout}{stderr}");
        return stdout;
    }

    /// Run kicad-cli without throwing; returns (exit code, stdout, stderr).
    public static (int Rc, string Stdout, string Stderr) Capture(params string[] args)
    {
        var psi = new ProcessStartInfo(Exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
    }

    // The gates the pipeline needs. pcb drc refills zones in memory before
    // checking; the board file on disk is left alone.

    /// `kicad-cli pcb drc --severity-all --format json -o <rpt> <board>` —
    /// writes the JSON report and returns it parsed.
    public static DrcReport Drc(string kicadPcb, string reportPath)
    {
        Run("pcb", "drc", "--severity-all", "--format", "json", "-o", reportPath, kicadPcb);
        return DrcReport.Load(reportPath);
    }

    /// `kicad-cli sch erc --severity-all --format json -o <rpt> <schematic>`
    /// — every error AND every warning counts.
    public static ErcReport Erc(string kicadSch, string reportPath)
    {
        Run("sch", "erc", "--severity-all", "--format", "json", "-o", reportPath, kicadSch);
        return ErcReport.Load(reportPath);
    }

    /// `kicad-cli sch export netlist -o <out> <schematic>` — returns the
    /// parsed {(ref, pin) -> net} map (leading '/' stripped from hierarchical
    /// net names).
    public static Dictionary<(string Ref, string Pin), string> ExportNetlist(string kicadSch, string outPath)
    {
        Run("sch", "export", "netlist", "-o", outPath, kicadSch);
        return Netlist.Read(outPath);
    }
}

public sealed record DrcViolation(string Severity, string Type, string Description);

public sealed class DrcReport
{
    public required IReadOnlyList<DrcViolation> Violations { get; init; }
    public required int Unconnected { get; init; }

    public static DrcReport Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var v = root.TryGetProperty("violations", out var vs)
            ? vs.EnumerateArray().Select(x => new DrcViolation(
                x.GetProperty("severity").GetString() ?? "",
                x.GetProperty("type").GetString() ?? "",
                x.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "")).ToList()
            : [];
        int unc = root.TryGetProperty("unconnected_items", out var ui) ? ui.GetArrayLength() : 0;
        return new DrcReport { Violations = v, Unconnected = unc };
    }

    /// A board is clean when nothing is unrouted and no violation remains
    /// (optionally tolerating starved-thermal warnings: a pad tied to a
    /// pour's thermal-relief spokes only — usually harmless when the pad
    /// also joins copper elsewhere).
    public bool Clean(bool allowStarvedThermal = false)
    {
        if (Unconnected > 0)
            return false;
        return Violations.All(v => allowStarvedThermal && v.Type == "starved_thermal");
    }

    public override string ToString() =>
        $"{Unconnected} unconnected, {Violations.Count} violations: " +
        string.Join(", ", Violations.GroupBy(v => $"{v.Severity} {v.Type}").Select(g => $"{g.Key} x{g.Count()}"));
}

public sealed record ErcViolation(string Severity, string Type, string Description);

public sealed class ErcReport
{
    public required IReadOnlyList<ErcViolation> Violations { get; init; }

    public static ErcReport Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var v = doc.RootElement.GetProperty("sheets").EnumerateArray()
            .SelectMany(s => s.TryGetProperty("violations", out var vs) ? vs.EnumerateArray() : Enumerable.Empty<JsonElement>())
            .Select(x => new ErcViolation(
                x.GetProperty("severity").GetString() ?? "",
                x.GetProperty("type").GetString() ?? "",
                x.TryGetProperty("description", out var d) ? d.GetString() ?? "" : ""))
            .ToList();
        return new ErcReport { Violations = v };
    }

    /// ERC is clean only with zero errors AND zero warnings.
    public bool Clean() => Violations.Count == 0;

    public override string ToString() =>
        $"{Violations.Count} violations: " +
        string.Join(", ", Violations.GroupBy(v => $"{v.Severity} {v.Type}").Select(g => $"{g.Key} x{g.Count()}"));
}

/// Reader for the s-expression netlist `kicad-cli sch export netlist` writes.
public static class Netlist
{
    public static Dictionary<(string Ref, string Pin), string> Read(string path)
    {
        var tree = (List<object>)Sexpr.Parse(File.ReadAllText(path));
        var got = new Dictionary<(string, string), string>();
        foreach (var net in Sexpr.FindAll(Sexpr.Find(tree, "nets") ?? [], "net"))
        {
            var name = Sexpr.Find(net, "name")![1].Txt().TrimStart('/');
            foreach (var node in Sexpr.FindAll(net, "node"))
                got[(Sexpr.Find(node, "ref")![1].Txt(), Sexpr.Find(node, "pin")![1].Txt())] = name;
        }
        return got;
    }
}
