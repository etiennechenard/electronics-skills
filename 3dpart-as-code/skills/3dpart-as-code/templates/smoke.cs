// Smoke check that the template libraries work together when included by a
// .NET 10 file-based app (the way the project tools consume them):
//
//   dotnet run smoke.cs
//
// Real coverage lives in the xUnit project (tests/); this proves the
// #:include path with a few calls into the libraries, and of the project's
// model it asserts only that part.cs validates.

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
using Mesh;
using Part;
using Raster;

// a tetrahedron, wound outwards
V3 a = new(0, 0, 0), b = new(10, 0, 0), c = new(0, 10, 0), d = new(0, 0, 10);
var tet = new Stl();
tet.Tris.AddRange([new(a, c, b), new(a, b, d), new(a, d, c), new(b, c, d)]);
var r = MeshCheck.Analyze(tet);
var img = new Scene().Add(tet).Render(View.Iso, 64, 48);
var model = PartDesign.Model;
var ok = r.Problems().Count == 0 && Math.Abs(r.Volume - 1000.0 / 6) < 1e-6
    && img.Get(32, 24) != Rgb.White
    && model.Validate().Count == 0
    && Gates.Dims(model, new Dictionary<string, double>()).Count == model.Dims.Count;
Console.WriteLine(ok ? "smoke ok" : "smoke FAILED");
return ok ? 0 : 1;
