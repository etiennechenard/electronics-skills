// Review views as PNG in build/renders/: each printed body in print
// orientation (iso, front, top, right), the assembly without the coupons
// (both isos, front, top), each annotated body with its parameters drawn as
// numbered dimension lines (the numbers the parameter sheet's # column
// shows), and in reproduce mode each measured body with its numbered
// callouts — the sheet the calipers fill in. Refuses a stale build.
//
//   dotnet run mech/tools/render.cs [-- --mech <dir>]

#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property NoWarn=CA2266
#:include partmodel.cs
#:include part.cs
#:include mech.cs
#:include stl.cs
#:include checks.cs
#:include raster.cs

using Checks;
using Mech;
using Mesh;
using Part;
using PartModel;
using Raster;

var l = Layout.Here(args);
var m = PartDesign.Model;
var stale = Gates.Stale(m, l, Gates.BuildOutputs(m, l));
if (stale.Count > 0)
{
    Gates.Report("STALE", stale);
    Console.WriteLine("run build.cs first");
    return 1;
}

if (Directory.Exists(l.Renders))
    Directory.Delete(l.Renders, recursive: true);
var n = 0;
void Save(Scene s, View v, string name, string title)
{
    s.Render(v, title: title).SavePng(Path.Combine(l.Renders, $"{name}-{v.ToString().ToLowerInvariant()}.png"));
    n++;
}
var colour = m.Bodies.Select((b, i) => (b.Name, Rgb.Palette[i % Rgb.Palette.Length])).ToDictionary();
View[] views = [View.Iso, View.Front, View.Top, View.Right];
foreach (var b in m.Bodies.Where(b => b.Printed))
{
    var s = new Scene().Add(Stl.Load(l.Stl(b.Name)), colour[b.Name]);
    foreach (var v in views)
        Save(s, v, b.Name, $"{b.Name} print up {b.Up}");
}
var asm = new Scene();
foreach (var b in m.Bodies.Where(b => b.Kind != BodyKind.Coupon))
    asm.Add(Stl.Load(l.AsmStl(b.Name)), colour[b.Name]);
foreach (var v in new[] { View.Iso, View.IsoBack, View.Front, View.Top })
    Save(asm, v, "assembly", m.Name);
foreach (var g in m.Annotations.GroupBy(a => a.Body))
{
    var s = new Scene().Add(Stl.Load(l.AsmStl(g.Key)), colour[g.Key]);
    foreach (var a in g)
    {
        var label = m.Label(a.Param)!;
        var from = new V3(a.From.X, a.From.Y, a.From.Z);
        var to = a.To is { } t ? new V3(t.X, t.Y, t.Z) : from;
        s.Dim(new DimLine(label, from, to));
    }
    foreach (var v in views)
        Save(s, v, $"{g.Key}-dims", $"{g.Key} parameters");
}
foreach (var g in m.Measurements.GroupBy(x => x.Body))
{
    var s = new Scene().Add(Stl.Load(l.AsmStl(g.Key)), colour[g.Key]);
    foreach (var x in g)
        s.Mark(new Callout(x.Id, new V3(x.At.X, x.At.Y, x.At.Z)));
    foreach (var v in views)
        Save(s, v, $"{g.Key}-callouts", $"{g.Key} measure");
}
Console.WriteLine($"{m.Name}: {n} renders in {Path.GetRelativePath(l.Root, l.Renders)}");
return 0;
