// Build every body: write build/params.json from part.cs, export the boards
// the model shows (kicad-cli), run each geometry script (parts/*.py) in the
// project's venv, then gather the bodies and boards into build/assembly.step
// and cut the views' bodies. A build starts from an empty build/, so nothing
// a removed script once wrote survives.
//
//   dotnet run mech/tools/build.cs [-- --mech <dir>]

#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property NoWarn=CA2266
#:include partmodel.cs
#:include part.cs
#:include mech.cs
#:include stl.cs
#:include checks.cs

using Checks;
using Mech;
using Part;

var l = Layout.Here(args);
var m = PartDesign.Model;
var invalid = m.Validate();
if (invalid.Count > 0)
    return Gates.Report("MODEL", invalid) > 0 ? 1 : 0;

if (Directory.Exists(l.Build))
    foreach (var e in Directory.EnumerateFileSystemEntries(l.Build).Where(e => e != l.Params))
        if (Directory.Exists(e)) Directory.Delete(e, recursive: true); else File.Delete(e);
m.Write(l.Params);

string py;
try { py = Python.Ensure(l); }
catch (Exception e) when (e is InvalidOperationException or FileNotFoundException or System.ComponentModel.Win32Exception)
{
    Console.WriteLine(e.Message);
    return Gates.Report("BUILD", ["no Python environment"]) > 0 ? 1 : 0;
}

var problems = new List<string>();
if (m.Boards.Count > 0)
{
    if (KiCad.Find() is not { } cli)
        problems.Add("kicad-cli not found (the model shows boards): install KiCad, or set KICAD_CLI to kicad-cli's path");
    else
        foreach (var b in m.Boards)
        {
            var (bad, missing) = KiCad.Export(cli, l, b.Name, b.Pcb, b.Origin);
            problems.AddRange(bad);
            Console.WriteLine($"board {b.Name}: exported from {b.Pcb}" +
                (missing > 0 ? $" ({missing} 3D model file(s) not found: those parts are left out)" : ""));
        }
}
foreach (var s in m.Bodies.Select(b => b.Script).Distinct())
{
    if (!File.Exists(Path.Combine(l.Root, s)))
    {
        problems.Add($"{s}: no such script");
        continue;
    }
    var r = Python.Script(l, py, s);
    Console.Write(r.Stdout);
    if (r.Rc != 0)
    {
        Console.Write(r.Stderr);
        problems.Add($"{s}: failed (exit {r.Rc})");
    }
}
if (problems.Count == 0)
{
    var r = Python.Assembly(l, py);
    Console.Write(r.Stdout);
    if (r.Rc != 0)
    {
        Console.Write(r.Stderr);
        problems.Add($"assembly.step: failed (exit {r.Rc})");
    }
}
problems.AddRange(Gates.Outputs(m, l));
return Gates.Report("BUILD", problems) > 0 ? 1 : 0;
