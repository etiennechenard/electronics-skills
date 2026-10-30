// The FIT gate: pac.py measures every declared clearance and insertion
// sweep on the built STEPs (build/fit.json); each must show no overlap and
// at least its gap. Refuses to judge a stale build.
//
//   dotnet run mech/tools/fit.cs [-- --mech <dir>]

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
var stale = Gates.Stale(m, l, Gates.BuildOutputs(m, l));
if (stale.Count > 0)
{
    Gates.Report("STALE", stale);
    Console.WriteLine("run build.cs first");
    return 1;
}
if (m.Clearances.Count == 0 && m.Sweeps.Count == 0)
{
    Console.WriteLine($"{m.Name}: no clearances or sweeps declared");
    return Gates.Report("FIT", []);
}
try
{
    var r = Python.Fit(l, Python.Find(l));
    Console.Write(r.Stdout);
    if (r.Rc != 0)
    {
        Console.Write(r.Stderr);
        return Gates.Report("FIT", [$"pac.py fit failed (exit {r.Rc})"]) > 0 ? 1 : 0;
    }
}
catch (Exception e) when (e is InvalidOperationException or FileNotFoundException or System.ComponentModel.Win32Exception)
{
    Console.WriteLine(e.Message);
    return Gates.Report("FIT", ["no Python environment"]) > 0 ? 1 : 0;
}
return Gates.Report("FIT", Gates.Fit(m, l)) > 0 ? 1 : 0;
