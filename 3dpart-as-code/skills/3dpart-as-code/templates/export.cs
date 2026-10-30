// Export the deliverables to out/: the STL and STEP of every printed body
// (parts and coupons), assembly.step, the renders, and manifest.json
// (sha256, size, print-orientation size, volume per file). Re-runs the DIM,
// MESH and FIT gates from the build's own files and refuses anything stale
// or dirty — out/ only ever holds a clean build. A params.xlsx holding
// edits that were never pulled into part.cs refuses too.
//
//   dotnet run mech/tools/export.cs [-- --mech <dir>]

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

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Checks;
using Mech;
using Mesh;
using ParamSheet;
using Part;
using PartModel;

var l = Layout.Here(args);
var m = PartDesign.Model;
var fitted = m.Clearances.Count > 0 || m.Sweeps.Count > 0;
var renders = Directory.Exists(l.Renders) ? Directory.GetFiles(l.Renders, "*.png") : [];
var problems = new List<string>(Gates.Stale(m, l, Gates.BuildOutputs(m, l)
    .Concat(fitted ? [l.FitReport] : [])
    .Concat(renders)));
if (renders.Length == 0)
    problems.Add("no renders (run render.cs)");
if (problems.Count == 0)
{
    var probes = Probes.Load(l);
    problems.AddRange(Gates.Dims(m, probes).Concat(Gates.Sources(m)).Select(p => $"DIM {p}"));
    problems.AddRange(Gates.Meshes(m, l, probes, []).Select(p => $"MESH {p}"));
    problems.AddRange(Gates.Fit(m, l).Select(p => $"FIT {p}"));
}
// Windows pipes default to the OEM code page; keep the console text ASCII.
static string Ascii(string s) =>
    string.Concat(s.Replace("×", "x").Select(c => c < 128 ? c : '?'));

var unpulled = new List<string>();
if (File.Exists(l.Sheet))
{
    var read = Sheet.Read(l.Sheet, m, File.ReadAllText(Path.Combine(l.Tools, "part.cs")));
    unpulled.AddRange(read.Edits.Select(e => $"SHEET {e.Name}: sheet value {PartEdit.Literals.Format(e.To)} not pulled"));
    unpulled.AddRange(read.Problems.Select(p => $"SHEET {Ascii(p)}"));
    problems.AddRange(unpulled);
}
if (problems.Count > 0)
{
    Gates.Report("EXPORT", problems);
    Console.WriteLine("EXPORT REFUSED: rebuild and re-run the gates (build, check, fit, render)");
    if (unpulled.Count > 0)
        Console.WriteLine("params.xlsx holds edits not in part.cs: run sheet.cs -- pull, or delete mech/params.xlsx to discard them");
    return 1;
}

if (Directory.Exists(l.Out))
    Directory.Delete(l.Out, recursive: true);
Directory.CreateDirectory(Path.Combine(l.Out, "renders"));
var files = new JsonArray();
void Copy(string src, string rel, JsonObject? extra = null)
{
    var dst = Path.Combine(l.Out, rel);
    File.Copy(src, dst);
    var entry = new JsonObject
    {
        ["file"] = rel.Replace('\\', '/'),
        ["bytes"] = new FileInfo(dst).Length,
        ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(dst))),
    };
    foreach (var (k, v) in extra ?? [])
        entry[k] = v?.DeepClone();
    files.Add((JsonNode)entry);
}
foreach (var b in m.Bodies.Where(b => b.Printed))
{
    var r = MeshCheck.Analyze(Stl.Load(l.Stl(b.Name)));
    var s = r.Bounds.Size;
    Copy(l.Stl(b.Name), b.Name + ".stl", new JsonObject
    {
        ["body"] = b.Name, ["kind"] = b.Kind.ToString().ToLowerInvariant(), ["up"] = b.Up,
        ["size_mm"] = new JsonArray(Math.Round(s.X, 3), Math.Round(s.Y, 3), Math.Round(s.Z, 3)),
        ["volume_cm3"] = Math.Round(r.Volume / 1000, 3),
    });
    Copy(l.Step(b.Name), b.Name + ".step", new JsonObject { ["body"] = b.Name });
}
Copy(l.Assembly, "assembly.step");
foreach (var png in renders.Order())
    Copy(png, Path.Combine("renders", Path.GetFileName(png)));
var manifest = new JsonObject
{
    ["name"] = m.Name,
    ["mode"] = m.Mode.ToString().ToLowerInvariant(),
    ["exported"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
    ["files"] = files,
};
File.WriteAllText(Path.Combine(l.Out, "manifest.json"), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{m.Name}: {files.Count} files exported to {Path.GetRelativePath(l.Root, l.Out)}");
Gates.Report("EXPORT", []);
return 0;
