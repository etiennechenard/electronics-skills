// Build every body: write build/params.json from part.cs, run each geometry
// script (parts/*.py) in the project's venv, then gather the bodies into
// build/assembly.step. A build starts from an empty build/, so nothing a
// removed script once wrote survives.
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
