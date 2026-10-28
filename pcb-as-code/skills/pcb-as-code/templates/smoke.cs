// Smoke check that the template libraries work together when included by a
// .NET 10 file-based app (the way the project tools consume them):
//
//   dotnet run smoke.cs
//
// Real coverage lives in the xUnit project (tests/); this proves the
// #:include path with a few calls into the libraries, and of the project's
// design it asserts only that design.cs registers a board.

#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property NoWarn=CA2266
#:include sexpr.cs
#:include design.cs
#:include kicad.cs
#:include specctra.cs
#:include boardfile.cs
#:include dsn.cs
#:include fab.cs

using Design;
using Fab;
using PcbFile;
using Specctra;
using Sx;

var node = Sexpr.S("property", "Reference", "U1");
var back = (List<object>)Sexpr.Parse(Sexpr.Dump(node));
var ok = back[0].Txt() == "property" && back[2].Txt() == "U1"
    && Dsn.BumpClearances("(clearance 100.0") == "(clearance 115.0"
    && DsnWriter.Q("U1-A") == "\"U1-A\""
    && Nm.Rotate(1, 0, 90) == (0, -1)
    && FabRules.Keys.ContainsKey("track_width")
    && DesignModel.Boards.Count > 0;
Console.WriteLine(ok ? "smoke ok" : "smoke FAILED");
return ok ? 0 : 1;
