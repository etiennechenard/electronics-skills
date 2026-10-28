---
name: pcb-as-code
description: Build hardware design-as-code — KiCad 10+ schematics and PCBs generated from a C# design.cs single source of truth, pin-verified, autorouted with FreeRouting, ERC/DRC-gated and exported for fab, with tooling as .NET 10 file-based apps (dotnet run tool.cs) in pcb/tools. Use to start a hardware/PCB project on this pattern or to work in one (generated .kicad_sch/.kicad_pcb) — adding parts or nets, regenerating, rerouting, verifying, fab export.
---

# pcb-as-code

**The design lives in code; KiCad files are build outputs.** One source file holds every part, footprint and net; generators write the libraries, schematics and placed boards from it; FreeRouting puts copper on; a verifier cross-checks every pin against the source; an exporter refuses anything not clean. Proven end to end on a multi-board RF instrument. New projects copy the tested templates below and write their own generators on top — one entry file per stage in `pcb/tools/`: `gen_lib.cs`, `gen_schematics.cs`, `gen_<board>.cs`, `route.cs`, `verify.cs`, `export.cs`.

This file is the map; read the reference named for the stage you are working (paths relative to this file).

## Hard rules

1. **The tooling is C#**: single-entry .NET 10 file-based apps, `dotnet run tool.cs -- <args>` (SDK ≥ 10.0.300 for `#:include`). Shared code lives in library `.cs` files pulled in with `#:include` — types only, no top-level statements.
2. **`design.cs` is the single source of truth.** Schematics, boards, BOMs and verification derive from it; when a KiCad file disagrees, the design wins and the file is regenerated.
3. **Regenerate, never hand-edit.** Hand edits in KiCad are overwritten on the next run and surface only as verify failures. Opening files in KiCad to LOOK is encouraged before ordering.
4. **Every stage gates**: drawing self-checks → ERC 0 errors 0 warnings → netlist parity pin-by-pin → DRC 0 errors 0 unrouted → `FAB PROBLEMS: 0` → export. The exporter re-checks and refuses stale or dirty boards.
5. **Tests first for the tooling**: keep the copied xUnit suite green (`dotnet test pcb/tools/tests`) and extend the design tests for the project's real design.cs.

## The pipeline

| Step | Produces | Gate | Read |
|---|---|---|---|
| 0. kickoff | interrogation, repo layout, templates copied, design.cs skeleton, fab.json, docs skeletons | smoke + unit tests pass | `references/kickoff.md` |
| 1. design.cs | the single source of truth | design tests extended | `references/step1-design.md` |
| 2. gen_lib | project symbol/footprint libraries | kicad-cli upgrade clean | `references/step2-3-libraries-schematics.md` |
| 3. gen_schematics | one .kicad_sch per board | ERC 0/0 + drawing self-checks | `references/step2-3-libraries-schematics.md` |
| 4. gen_pcb | placed, symbol-linked boards + .kicad_pro | footprint ↔ symbol links; `FabCheck.Project` empty | `references/step4-pcb.md` |
| 5. route | FreeRouting copper + GND stitching | DRC 0 errors, 0 unrouted | `references/step5-route.md` |
| 6. verify | pin-parity vs design.cs | `TOTAL PROBLEMS: 0` | `references/step6-7-verify-export.md` |
| 7. export | gerbers, drill, pos, BOM, renders | refuses stale/dirty boards, `FAB PROBLEMS` > 0 | `references/step6-7-verify-export.md` |
| 8. firmware + docs | PlatformIO firmware, docs complete | native host tests | `references/step8-firmware-docs.md` |
| review | multi-wave fresh-eyes sweep of docs, source, schematics, boards | fixes in source, regenerated, gates re-run | the `hw-review` skill (`${CLAUDE_PLUGIN_ROOT}/skills/hw-review/`) |

**Review** is the deep pass the gates cannot be (datasheet conformance, docs↔design↔board consistency, enclosure, renders): run `hw-review` once the schematics generate clean (a wrong pin found before routing saves a re-route), after step 8 before ordering with a budget ≥ 6, and after any large change — it takes several waves to find everything.

## Toolchain (this machine)

| Tool | Fact |
|---|---|
| KiCad 10.0.7 | `C:\Program Files\KiCad\10.0\bin\kicad-cli.exe`, the default in `kicad.cs`; `KICAD_CLI` (or `Cli.Exe`) for a newer one — re-run the test suite to confirm it |
| kicad-cli | `sch erc`, `sch export netlist/pdf`, `sch/sym/fp upgrade --force`, `pcb drc`, `pcb export gerbers/drill/pos/svg`, `pcb render`, `pcb upgrade` |
| Zone fills | `pcb drc` and `pcb export gerbers` refill in memory (documented for both, verified for drc on 10.0.7 — re-check on a newer KiCad); `pcb drc --refill-zones --save-board` writes fills into the file (read with `Board.ZoneFills`). PDF/SVG exports do NOT refill — render a board saved that way |
| Board text | s-expressions, no pcbnew dependency; KiCad 10 references nets by name (`(net "GND")`, no net table) |
| Specctra DSN | kicad-cli has NO DSN export — `dsn.cs` writes it |
| FreeRouting | 1.4.5.1 known-good default: `C:\tools\freerouting-1.4.5.1\freerouting-1.4.5.1.jar`, Java 17+ at `C:\tools\jdk-25.0.4.1+1-jre\bin\java.exe` (defaults in `specctra.cs`; `Route(java:, jar:)` overrides). 2.x takes the same CLI (2.5 needs Java 25) |
| .NET SDK | 10.0.401 |
| PlatformIO | `pio run`; native host tests in `firmware/test/native` |

## Templates (`templates/`, shipped, tested)

| File | Provides |
|---|---|
| `sexpr.cs` | `Sx`: s-expression reader/writer for schematics/boards/footprints/SES, diff-stable dump, `Find`/`FindAll`/`S` builders |
| `design.cs` | `Design`: `Comp`, `Board`, `Boards` registry, `Flag`, `ExpectedPins` — with a worked example board |
| `kicad.cs` | `Kicad`: kicad-cli runner + gates — `Erc`, `Drc` (JSON, `Clean(allowStarvedThermal:)`), `ExportNetlist`/`Netlist.Read` |
| `specctra.cs` | `Specctra`: `Dsn.BumpClearances`, `Dsn.Shuffle` (seeded), `FreeRouting.Route` (headless, `-mt 1`), `Ses.ReadRoutes` |
| `boardfile.cs` | `PcbFile`: `Board.Load`, pads in absolute nm (`BPad.Position`, `Drill`, `LibAngle`), tracks/vias/zones (`ZoneFills`), `RoutedCopper(drop:)`, `EdgeContours`, `Courtyard`, `Contains`, `Segment`/`Via` builders, `Save` |
| `dsn.cs` | `Specctra.DsnWriter`: pcbnew-style DSN (`Write`), net classes + hole clearance from the .kicad_pro (`Project`) |
| `fab.cs` | `Fab`: `FabRules` from `pcb/fab.json`, `FabCheck.Project` / `FabCheck.Board` |
| `fabcheck.cs` | fab gate: `dotnet run pcb/tools/fabcheck.cs -- pcb/fab.json pcb/<board>.kicad_pcb...` → `FAB PROBLEMS: n` |
| `smoke.cs` | proves the include path: `dotnet run pcb/tools/smoke.cs` |

The xUnit suite in `tests/` compiles the templates directly (live kicad-cli tests early-return as passed when KiCad is absent): `dotnet test "${CLAUDE_PLUGIN_ROOT}/skills/pcb-as-code/tests"`. Kickoff copies templates and tests into the project.

## Gotchas (each paid for on a real build)

- FreeRouting: `-mt 1`, bump clearances 15%, shuffle the DSN per retry (one writer = one order, so an unshuffled retry repeats the failure); ~40 min per attempt on a dense board. Details: `references/step5-route.md`.
- ERC/DRC run `--severity-all`: warnings count. A DRC silk warning is a placement fix in the generator, not a waiver (fabcheck's silk warnings do not gate).
- DRC reports only via `kicad-cli pcb drc --format json` (pcbnew.WriteDRCReport aborts outside the GUI); a report older than its board is a stale gate.
- Locked generator copper is contract: FreeRouting keeps locked tracks and vias as fixed wiring, and copper-copy and routing re-runs keep it; locked arcs are not written to the DSN — draw generator copper as segments.
- Off-board part pins are unverifiable by construction — keep their net maps right in the design, wired via on-board connectors.
- Fab limits come from the fab's page, never from memory — they drift and depend on the process, mask colour included (`references/fabrication.md`); fabcheck fails on a `fab.json` checked over 90 days ago.
- Two `dotnet run` of the same .cs at once fail on a locked exe — one instance per tool.
- Panels replacing enclosure end plates: legends outside, mounting screws bonding GND, no through-hole leads (unless the one special part), check the outline against the physical case first.

## Other references

| File | Read when |
|---|---|
| `references/project-structure.md` | repository layout, AGENTS.md convention |
| `references/fabrication.md` | choosing the fab, fetching its limits into fab.json, the fab checks |
| `references/dotnet-conventions.md` | writing the C# file-based apps |
