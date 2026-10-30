# .NET 10 file-based app conventions

Entry tools look like this (the shape of `templates/check.cs`):

```csharp
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

var l = Layout.Here(args);     // mech/, from the tool's own folder or --mech <dir>
var m = PartDesign.Model;
```

- Run from anywhere: `dotnet run mech/tools/check.cs` (`-- --mech <dir>`
  points at another mech/). Each entry file is its own app; libraries are
  included explicitly, never picked up implicitly.
- **NuGet packages** come in with `#:package <id>@<version>`, pinned to one
  exact version (the sheet tools use `ClosedXML@0.105.1` and
  `Microsoft.CodeAnalysis.CSharp@5.9.0`, the same versions as the test
  csproj). The first run downloads them.
- **Library files declare types only** — no top-level statements — in
  their own namespace: `PartModel`, `Part`, `Mech`, `Mesh`, `Raster`,
  `Checks`, `PartEdit`, `ParamSheet`. Include what a library needs:
  `part.cs` needs `partmodel.cs`; `checks.cs` needs `partmodel.cs`,
  `mech.cs` and `stl.cs`; `raster.cs` needs `stl.cs`; `partedit.cs` needs
  the Roslyn package; `paramsheet.cs` needs `partmodel.cs`, `mech.cs`,
  `partedit.cs` and both packages.
- **What counts as a build input**: every library `.cs` in `mech/tools/`
  except the readers (`stl.cs`, `raster.cs`, `checks.cs`, `partedit.cs`,
  `paramsheet.cs` — `Layout.NotInputs`),
  plus pac.py, the scripts, requirements.txt and `Model.Inputs`. Entry
  files (a `#:sdk` line) never are. A new library that shapes no geometry
  belongs in `NotInputs`, or every edit to it forces a rebuild.
- **Trimming/AOT.** File-based apps are checked for trimming: build JSON
  with `JsonNode` implicit conversions (`new JsonArray(1.0, 2.0)`,
  `obj["k"] = 3.0`) — the generic `JsonArray.Add<T>` on a double compiles
  with an IL warning and throws at run time.
- **Console output**: one stable, greppable line per body or check, and
  `<GATE> PROBLEMS: n` last; the machine gates consume the JSON files, not
  these lines. ASCII only (Windows pipes use the OEM code page).
- **One run per tool at a time.** Two `dotnet run` of the same .cs at once
  share one build output and the second fails on the locked exe.
- `AppContext.GetData("EntryPointFileDirectoryPath")` is the entry file's
  folder; `Layout.Here` uses it.
