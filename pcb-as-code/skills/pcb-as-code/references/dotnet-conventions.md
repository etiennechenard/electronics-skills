# .NET 10 file-based app conventions

Entry tools look like this (the shape of `templates/smoke.cs`, plus
`using Kicad;` for a tool that runs kicad-cli):

```csharp
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
using Kicad;
using PcbFile;
using Specctra;
using Sx;

// top-level statements; args[] carries the command line
```

- Run: `dotnet run tool.cs -- controller --seed 3` (the `--` separates dotnet
  args). Each entry file is its own app; sibling library files are included
  explicitly, never picked up implicitly.
- Library files (`sexpr.cs` etc.) declare types only — they cannot hold
  top-level statements. Keep them namespace-scoped (`Sx`, `Design`, `Kicad`,
  `Specctra`, `PcbFile`, `Fab`) and entry files thin. Include what a
  library needs too: `boardfile.cs` needs `sexpr.cs` and `kicad.cs`;
  `dsn.cs` and `fab.cs` need `boardfile.cs`.
- **Two `Board` types.** `Design.Board` (a board in the design) and
  `PcbFile.Board` (a .kicad_pcb read as text) share a name: a tool that
  imports both namespaces writes `PcbFile.Board.Load(...)`, or aliases one
  (`using Pcb = PcbFile.Board;`). A bare `Board` does not compile there.
- **Coordinates and units.**
  - KiCad files store millimetres: boards at nm resolution, schematics at
    0.001 mm. `boardfile.cs` works in nm integers (`Nm.Of`, `Nm.Mm`), y
    down.
  - DSN and SES use resolution units with y UP. Negate in both directions:
    `dsn.cs` and the `Ses` reader both do.
- **KiCad 10 board text.** Nets are referenced by name (`(net "GND")`) and
  there is no net table. A pad's `(at x y a)` is local to its footprint
  while its angle is absolute. See `step4-pcb.md` for the flip
  transform.
- **One run per tool at a time.** Two `dotnet run` of the same .cs at once
  share one build output, and the second fails on the locked exe. Run tools
  one after another, or run a copy of the file under another name.
- **A tool's own folder.**
  `AppContext.GetData("EntryPointFileDirectoryPath")` is the entry file's
  directory. Use it to find sibling tools and the project's folders from
  any working directory.
- Console output: one stable, greppable line per board per stage
  (`main: 371 track segments, 73 vias imported`,
  `main schematic: 412 pins checked, 0 problems`) — the audit trail humans
  and project logs grep; the machine gates consume the JSON reports
  (`kicad.cs`), not these lines.
- Never parse a DSN with the s-expression parser: pcbnew writes
  `(string_quote ")` which is not balanced s-expr. DSN is a regex-text
  domain; `Sx.Sexpr.Parse` throws a pointed error on it.
