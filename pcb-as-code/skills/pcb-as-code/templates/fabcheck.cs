// Holds boards to the fab's limits in pcb/fab.json (see fab.cs):
//
//   dotnet run fabcheck.cs -- <fab.json> [<board.kicad_pcb>...] [--max-age days]
//
// Checks fab.json itself (every key, a quote per value, checked within
// --max-age days, default 90), then per board its .kicad_pro rules and net
// classes and its geometry; with no board, the file alone. Prints one line
// per board and every error, silkscreen warnings grouped per rule; ends
// with `FAB PROBLEMS: n` (errors only) and exits 1 when n > 0. A file that
// cannot be read (not JSON, cut off) is an error; a usage error or a
// missing file exits 2. Paths are relative to the working
// directory: from the project root, `dotnet run pcb/tools/fabcheck.cs --
// pcb/fab.json pcb/<board>.kicad_pcb`. export.cs runs it as a gate.

#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property NoWarn=CA2266
#:include sexpr.cs
#:include kicad.cs
#:include boardfile.cs
#:include fab.cs

using Fab;
using PcbFile;

var argv = args.ToList();
int maxAge = 90;
if (argv.IndexOf("--max-age") is var k and >= 0)
{
    if (k + 1 >= argv.Count || !int.TryParse(argv[k + 1], out maxAge))
        return Usage("--max-age takes a number of days");
    argv.RemoveRange(k, 2);
}
if (argv.Count < 1)
    return Usage(null);
if (argv.FirstOrDefault(a => !File.Exists(a)) is { } missing)
    return Usage($"no such file: {missing}");

FabRules fab;
try { fab = FabRules.Load(argv[0]); }
catch (Exception e)
{
    Console.WriteLine($"  ERROR fab.json: cannot be read (not valid JSON?): {e.Message}");
    Console.WriteLine("FAB PROBLEMS: 1");
    return 1;
}
int problems = 0;
foreach (var p in fab.Problems(DateOnly.FromDateTime(DateTime.Today), maxAge))
{
    Console.WriteLine("  ERROR " + p);
    problems++;
}
Console.WriteLine($"fab: {fab.Fab}, {fab.Process}, checked {fab.Checked:yyyy-MM-dd} ({fab.Source})");

foreach (var pcb in argv.Skip(1))
{
    var name = Path.GetFileNameWithoutExtension(pcb);
    var pro = Path.ChangeExtension(pcb, ".kicad_pro");
    var issues = new List<FabIssue>();
    // a file that cannot be read is an error, never a pass
    void Read(string file, Action check)
    {
        try { check(); }
        catch (Exception e)         // malformed text surfaces as any of a dozen exception types
        {
            issues.Add(new(true, "read", Path.GetFileName(file), "cannot be read: " + e.Message));
        }
    }
    if (File.Exists(pro))
        Read(pro, () => issues.AddRange(FabCheck.Project(pro, fab)));
    else
        issues.Add(new(true, "project", name, $"no {Path.GetFileName(pro)} next to the board"));
    Read(pcb, () => issues.AddRange(FabCheck.Board(Board.Load(pcb), fab)));
    var errors = issues.Where(i => i.Error).ToList();
    var warns = issues.Where(i => !i.Error).ToList();
    Console.WriteLine($"{name}: {errors.Count} errors, {warns.Count} silkscreen warnings");
    foreach (var e in errors)
        Console.WriteLine(e);
    foreach (var g in warns.GroupBy(w => w.Rule))
        Console.WriteLine($"  warn  {g.Key}: {g.Count()} items, e.g. {g.First().Where}: {g.First().Text}");
    problems += errors.Count;
}
Console.WriteLine($"FAB PROBLEMS: {problems}");
return problems == 0 ? 0 : 1;

static int Usage(string? why)
{
    if (why is not null)
        Console.Error.WriteLine("fabcheck: " + why);
    Console.Error.WriteLine("usage: fabcheck.cs <fab.json> [<board.kicad_pcb>...] [--max-age days]");
    return 2;
}
