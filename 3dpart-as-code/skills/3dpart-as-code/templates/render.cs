// Review views as PNG in build/renders/: each printed body in print
// orientation (iso, front, top, right), the assembly without the coupons
// (both isos, front, top), with the boards part.cs shows in place of their
// keep-outs, each AssemblyView (both isos, 1600 x 1200: bodies hidden, cut
// or moved to see inside), each annotated body with its parameters drawn as
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
void Save(Scene s, View v, string name, string title, int w = 900, int h = 700)
{
    s.Render(v, w, h, title).SavePng(Path.Combine(l.Renders, $"{name}-{v.ToString().ToLowerInvariant()}.png"));
    n++;
}
var colour = m.Bodies.Select((b, i) => (b.Name, b.Color is { } c ? Rgb.Parse(c) : Rgb.Palette[i % Rgb.Palette.Length])).ToDictionary();
var replaced = m.Boards.SelectMany(b => b.Replaces ?? []).ToHashSet();
Scene Assembly(AssemblyView? view = null)
{
    var s = new Scene();
    var hide = view?.Hide ?? [];
    foreach (var b in m.Bodies.Where(b => b.Kind != BodyKind.Coupon && !replaced.Contains(b.Name) && !hide.Contains(b.Name)))
    {
        var mesh = view?.Cut is { } cut && cut.Body == b.Name ? Stl.Load(l.ViewStl(view.Name, b.Name)) : Stl.Load(l.AsmStl(b.Name));
        if (view?.Move is { } mv && mv.Body == b.Name)
            mesh = mesh.Moved(new V3(mv.By.X, mv.By.Y, mv.By.Z));
        s.Add(mesh, colour[b.Name]);
    }
    foreach (var b in m.Boards.Where(b => !hide.Contains(b.Name)))
    {
        s.Add(Stl.Load(l.BoardStl(b.Name, "board")), Rgb.Board);
        if (File.Exists(l.BoardStl(b.Name, "parts")))
            s.Add(Stl.Load(l.BoardStl(b.Name, "parts")), Rgb.Component);
    }
    return s;
}
View[] views = [View.Iso, View.Front, View.Top, View.Right];
foreach (var b in m.Bodies.Where(b => b.Printed))
{
    var s = new Scene().Add(Stl.Load(l.Stl(b.Name)), colour[b.Name]);
    foreach (var v in views)
        Save(s, v, b.Name, $"{b.Name} print up {b.Up}");
}
var asm = Assembly();
foreach (var v in new[] { View.Iso, View.IsoBack, View.Front, View.Top })
    Save(asm, v, "assembly", m.Name);
foreach (var av in m.Views)
{
    var s = Assembly(av);
    foreach (var v in new[] { View.Iso, View.IsoBack })
        Save(s, v, $"assembly-{av.Name}", $"{m.Name} {av.Name}", 1600, 1200);
}
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
