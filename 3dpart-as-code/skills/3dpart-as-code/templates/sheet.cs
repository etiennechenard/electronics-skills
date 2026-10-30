// The parameter sheet: generate mech/params.xlsx (every number with the
// renders beside it, the gate results on Status), or pull the user's edits
// from it back into part.cs. Run after render.cs; refuses a stale pipeline
// and refuses to regenerate over edits that were never pulled.
//
//   dotnet run mech/tools/sheet.cs [-- --mech <dir>]          generate
//   dotnet run mech/tools/sheet.cs -- pull [--mech <dir>]     apply the edits
//
// After a pull, rebuild: build, check, fit, render, sheet.

#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property NoWarn=CA2266
#:package ClosedXML@0.105.1
#:package Microsoft.CodeAnalysis.CSharp@5.9.0
#:include partmodel.cs
#:include part.cs
#:include mech.cs
#:include stl.cs
#:include checks.cs
#:include partedit.cs
#:include paramsheet.cs

using System.Text;
using Checks;
using Mech;
using ParamSheet;
using PartEdit;
using Part;

var l = Layout.Here(args);
var m = PartDesign.Model;
var partFile = Path.Combine(l.Tools, "part.cs");
var partBytes = File.ReadAllBytes(partFile);
var hasBom = partBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
var partText = new UTF8Encoding(hasBom).GetString(partBytes, hasBom ? 3 : 0, partBytes.Length - (hasBom ? 3 : 0));

// Windows pipes default to the OEM code page; keep the console text ASCII.
static string Ascii(string s) =>
    string.Concat(s.Replace("×", "x").Select(c => c < 128 ? c : '?'));

return args.Contains("pull") ? Pull() : Generate();

int Pull()
{
    if (!File.Exists(l.Sheet))
    {
        Console.WriteLine($"no {Path.GetFileName(l.Sheet)} (run sheet.cs)");
        return 1;
    }
    var read = Sheet.Read(l.Sheet, m, partText);
    if (read.Problems.Count > 0)
    {
        Gates.Report("PULL", [.. read.Problems.Select(Ascii)]);
        return 1;
    }
    if (read.Edits.Count == 0)
    {
        Console.WriteLine("nothing to pull");
        Gates.Report("PULL", []);
        return 0;
    }
    var (text, bad) = Sheet.Apply(partText, read.Edits);
    if (bad.Count > 0)
    {
        Gates.Report("PULL", [.. bad.Select(Ascii)]);
        return 1;
    }
    File.WriteAllBytes(partFile, [.. (hasBom ? Encoding.UTF8.Preamble.ToArray() : []), .. Encoding.UTF8.GetBytes(text)]);
    foreach (var e in read.Edits)
        Console.WriteLine(Ascii(e.ToString()));
    foreach (var w in read.Warnings)
        Console.WriteLine(Ascii($"  warning: {w}"));
    Gates.Report("PULL", []);
    Console.WriteLine("rebuild: build, check, fit, render, sheet");
    return 0;
}

int Generate()
{
    var fitted = m.Clearances.Count > 0 || m.Sweeps.Count > 0;
    var renders = Directory.Exists(l.Renders) ? Directory.GetFiles(l.Renders, "*.png") : [];
    var stale = new List<string>(Gates.Stale(m, l, Gates.BuildOutputs(m, l)
        .Concat(fitted ? [l.FitReport] : [])
        .Concat(renders)));
    if (renders.Length == 0)
        stale.Add("no renders (run render.cs)");
    if (stale.Count > 0)
    {
        Gates.Report("STALE", stale);
        Console.WriteLine("run build, check, fit, render first");
        return 1;
    }

    if (File.Exists(l.Sheet))
    {
        var read = Sheet.Read(l.Sheet, m, partText);
        if (!read.Clean)
        {
            Gates.Report("SHEET", [.. read.Edits.Select(e => $"{e.Name}: sheet value {Literals.Format(e.To)} not pulled").Concat(read.Problems).Select(Ascii)]);
            Console.WriteLine("pull them first, or delete mech/params.xlsx to discard them");
            return 1;
        }
    }

    var probes = Probes.Load(l);
    var result = Sheet.Write(m, partText, l,
    [
        ("DIM", [.. Gates.Dims(m, probes), .. Gates.Sources(m)]),
        ("MESH", Gates.Meshes(m, l, probes, [])),
        ("FIT", Gates.Fit(m, l)),
    ]);
    if (result.Problems.Count > 0)
    {
        Gates.Report("SHEET", result.Problems);
        return 1;
    }
    Console.WriteLine($"{m.Name}: {Path.GetFileName(l.Sheet)}, {result.Parameters} parameters ({result.Editable} editable), " +
        $"{result.Fits} fits, {result.Renders} renders");
    Gates.Report("SHEET", []);
    return 0;
}
