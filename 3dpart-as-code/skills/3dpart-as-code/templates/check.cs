// The DIM and MESH gates over the last build: every expected dimension
// against its probe (and, in reproduce mode, where each critical number came
// from), then every printed STL — closed, oriented, one shell, inside the
// envelope, volume matching the solid — with an overhang report per body.
// Refuses to judge a stale build.
//
//   dotnet run mech/tools/check.cs [-- --mech <dir>]

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

var probes = Probes.Load(l);
Console.WriteLine($"{m.Name}: {m.Dims.Count} expected dimensions, {probes.Count} probes");
var bad = Gates.Report("DIM", [.. Gates.Dims(m, probes), .. Gates.Sources(m)]);
var lines = new List<string>();
var mesh = Gates.Meshes(m, l, probes, lines);
foreach (var line in lines)
    Console.WriteLine(line);
bad += Gates.Report("MESH", mesh);
return bad > 0 ? 1 : 0;
