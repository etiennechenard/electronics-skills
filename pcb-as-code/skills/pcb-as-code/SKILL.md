---
name: pcb-as-code
description: Build hardware projects design-as-code — KiCad 10+ schematics and PCBs generated from a C# single source of truth, verified pin-by-pin against it, autorouted headlessly with FreeRouting, gated by ERC/DRC, and exported to fab gerbers/BOM/renders, with project tooling in C# (.NET 10 file-based apps, dotnet run tool.cs). Use when starting a new hardware/PCB project on this pattern (one design.cs source of truth, generated .kicad_sch/.kicad_pcb, C# tools in pcb/tools), or when working in a project already built this way — including adding parts or nets, regenerating boards, rerouting, verifying, and exporting for fabrication.
---

# pcb-as-code

A hardware workflow where **the design lives in code and KiCad files are
build outputs**: one source file holds every part, footprint and net;
generators write the schematic libraries, schematics and placed boards from
it; an autorouter puts copper on; a verifier cross-checks every pin of every
schematic and board against the source; an exporter refuses anything that is
not clean. Nothing is ever hand-edited in KiCad — a lasting change goes into
the source or a generator, and everything regenerates.

The pattern is proven end to end in C# on a production build, a
multi-board RF instrument: every stage, from design.cs to the fab zip,
including a full FreeRouting run of a dense board. The pipeline tooling is
**C#: .NET 10 file-based apps run with `dotnet run tool.cs`**. New projects
copy the tested templates shipped here and write their generators on top:
- the board writer;
- the schematic writer and its self-checks;
- the route, verify and export tools.

`references/pipeline-steps.md` gives the facts each of them needs.

This file is the map; the stage-by-stage detail lives in `references/`
(next to this file) — read the file named for the stage you are working.

## Hard rules

1. **The shipped tooling is C#.** The shipped tools are single-entry C#
   file-based apps: `dotnet run tool.cs -- <args>` (`.NET SDK ≥ 10.0.300`
   for `#:include`; this machine has 10.0.401). Shared code lives in plain
   library `.cs` files pulled in with `#:include` — types only, no
   top-level statements.
2. **The design file is the single source of truth.** Parts, footprints and
   nets exist in exactly one place (`design.cs`). Schematics, boards, BOMs
   and verification all derive from it. If a KiCad file and the design
   disagree, the design wins and the KiCad file is regenerated.
3. **Regenerate, never hand-edit.** Generators overwrite the KiCad files.
   Hand edits in KiCad are thrown away on the next run and only surface as
   verify failures. (Opening files in KiCad to LOOK is fine and encouraged
   before ordering.)
4. **Every stage gates.** Schematic drawing self-checks → ERC 0 errors
   0 warnings → netlist parity pin-by-pin vs the design → DRC 0 errors
   0 unrouted → `FAB PROBLEMS: 0` (fabcheck) → only then export. The
   exporter re-checks the gates and refuses stale or dirty boards.
5. **Tests first for the tooling.** The templates ship with an xUnit suite;
   after copying them into a project, keep them running
   (`dotnet test pcb/tools/tests`) and extend the design tests for the
   project's real design.cs.

## The pipeline at a glance

| Step | Produces | Gate | Detail |
|---|---|---|---|
| 0. kickoff | interrogation, repo layout, templates copied, design.cs skeleton, fab.json fetched, docs skeletons | templates proven: smoke + unit tests pass | `references/kickoff.md` |
| 1. design.cs | the single source of truth | design tests extended | `references/pipeline-steps.md` |
| 2. gen_lib | project symbol/footprint libraries | kicad-cli upgrade clean | `references/pipeline-steps.md` |
| 3. gen_schematics | one .kicad_sch per board | ERC 0 errors 0 warnings + drawing self-checks | `references/pipeline-steps.md` |
| 4. gen_pcb | placed, symbol-linked boards | footprint ↔ symbol links; project rules ≥ fab.json (`FabCheck.Project`) | `references/pipeline-steps.md` |
| 5. route | FreeRouting copper + GND stitching | DRC 0 errors, 0 unrouted | `references/pipeline-steps.md` |
| 6. verify | pin-parity vs design.cs | TOTAL PROBLEMS: 0 | `references/pipeline-steps.md` |
| 7. export | gerbers, drill, pos, BOM, renders | exporter refuses stale/dirty boards and `FAB PROBLEMS` > 0 | `references/pipeline-steps.md` |
| 8. firmware + docs | PlatformIO firmware, docs complete | native host tests | `references/pipeline-steps.md` |
| review | wave-swept fresh-eyes review of docs, schematics and boards (this plugin's `hw-review` skill) | confirmed fixes land in `design.cs`/generators/docs, boards regenerated, gates re-run — never hand-edited | `references/pipeline-steps.md` §Review; the `hw-review` skill |

The gates hold the mechanical line at every step; the **review sweep** is
the deep pass the gates cannot be: waves of cheap read-only reviewers over
the changed docs, design source, schematics and boards (renders included,
pin maps checked against the parts' actual datasheets), triaged by the
session, fixes applied in the source and regenerated. Run it **before a
fab order** and after any large change — the proven experience is it takes
several waves to find and fix everything.

## Toolchain facts (this machine)

| Tool | Fact |
|---|---|
| KiCad 10.0.7 (recommended) | `C:\Program Files\KiCad\10.0\bin\kicad-cli.exe` — the known-good default in `kicad.cs`; set `KICAD_CLI` (or assign `Cli.Exe`) to use anything newer. The CLI surface and JSON report formats used here have been stable; re-run the test suite (its live kicad-cli tests run against whatever `KICAD_CLI` points at) to confirm a new install |
| kicad-cli used for | `sch erc`, `sch export netlist`, `sch export pdf`, `sch/sym/fp upgrade --force`, `pcb drc`, `pcb export gerbers/drill/pos/svg`, `pcb render`, `pcb upgrade` |
| Zone fills | `pcb drc` and `pcb export gerbers` refill zones in memory when needed (documented for both; the drc half verified on 10.0.7 against a fill-stripped board — re-check when adopting a newer KiCad) — the board file keeps what was saved. `pcb drc --refill-zones --save-board` writes the fills into the file (verified on 10.0.7: the `filled_polygon`s stitching reads with `Board.ZoneFills`). PDF/SVG exports do NOT refill: render a board saved that way |
| Templates and boards | treated as s-expression text — no pcbnew dependency. KiCad 10 boards reference nets by name (`(net "GND")`, no net table) |
| Specctra DSN | kicad-cli has NO DSN export — `dsn.cs` writes it from the board text |
| FreeRouting | known-good default 1.4.5.1: jar `C:\tools\freerouting-1.4.5.1\freerouting-1.4.5.1.jar`, Java 17+ at `C:\tools\jdk-25.0.4.1+1-jre\bin\java.exe` (defaults in `specctra.cs`; `Route` takes java/jar overrides). The `-de/-do/-mp/-mt` CLI is unchanged in 2.x — e.g. 2.5 (needs Java 25, which this JRE is) routes the same command line |
| .NET 10 SDK | 10.0.401 — file-based apps with `#:include` work |
| PlatformIO | `pio run` builds firmware; native host tests for the firmware core (`firmware/test/native`) |

## Templates (shipped, tested)

| File | Provides |
|---|---|
| `sexpr.cs` | S-expression reader/writer (`Sx`): parse real schematics/boards/footprints/SES, diff-stable dump, `Find`/`FindAll`/`S` builders. Round-trip-verified on real KiCad files up to 1.2 MB |
| `design.cs` | The single-source-of-truth model (`Design`): `Comp`, `Board`, `Boards` registry, `Flag`, `ExpectedPins` — with a worked example board |
| `kicad.cs` | `Kicad`: kicad-cli runner (known-good default install; `KICAD_CLI` env override for newer KiCad) + the three gates — `Erc`, `Drc` (JSON reports, `Clean(allowStarvedThermal:)`), `ExportNetlist`/`Netlist.Read` |
| `specctra.cs` | `Specctra`: `Dsn.BumpClearances`, `Dsn.Shuffle` (seeded), `FreeRouting.Route` (headless, `-mt 1` default, java/jar parameters for other versions), `Ses.ReadRoutes` (nm-scaled, y-negated wires + vias) |
| `boardfile.cs` | `PcbFile`: a .kicad_pcb read as text — `Board.Load`, footprints with pads in absolute nm (`BPad.Position`, `Drill`, `LibAngle`), tracks/vias/zones (`ZoneFills`: the saved fills), `RoutedCopper(drop:)`, `EdgeContours`, `Courtyard`, `Contains` (KiCad's edge rule), `Segment`/`Via` builders, `Save` (+ `pcb upgrade`) |
| `dsn.cs` | `Specctra.DsnWriter`: the DSN FreeRouting reads, written the way pcbnew's ExportSpecctraDSN writes it (`Write`), net classes and hole clearance from the .kicad_pro (`Project`) |
| `fab.cs` | `Fab`: `FabRules` loaded from the project's `pcb/fab.json` (the fab's limits, fetched from its capability page, each with the page's quote and a check date) and `FabCheck.Project` / `FabCheck.Board` (see `references/fabrication.md`) |
| `fabcheck.cs` | the fab gate, from the project root: `dotnet run pcb/tools/fabcheck.cs -- pcb/fab.json pcb/<board>.kicad_pcb...` → `FAB PROBLEMS: n` |
| `smoke.cs` | 30-second proof the include path works: `dotnet run smoke.cs` from its folder (`dotnet run pcb/tools/smoke.cs` from a project's root) |

Unit tests live in `${CLAUDE_PLUGIN_ROOT}/skills/pcb-as-code/tests/` and
compile the templates directly (plus live kicad-cli integration tests on
synthetic boards — they early-return, reporting as passed, when KiCad is
absent; and `dotnet run` tests of smoke.cs and fabcheck.cs, which find the
tools in both the plugin's and a project's layout):

```
dotnet test "${CLAUDE_PLUGIN_ROOT}/skills/pcb-as-code/tests"
```

Step 0 (`references/kickoff.md`) copies templates and tests into the new
project; keep the suite running there and extend the design tests for the
project's real design.cs.

## Gotchas (each paid for on a real build)

- FreeRouting: 1.4.5.1 is the known-good default (adopt a newer version
  only head-to-head, see `references/pipeline-steps.md`); `-mt 1` by
  default; shuffle the DSN per retry; bump clearances 15%; expect ~40 min
  per attempt on a dense board.
- KiCad always writes the same DSN order — without shuffling, a retry
  is a rerun of the same failure.
- ERC/DRC `--severity-all`: warnings count. A DRC silk warning is a
  placement fix in the generator, not a waiver. (fabcheck's silkscreen
  warnings, silk below the fab's limit, do not gate: see fabrication.md.)
- The DRC report must be newer than the .kicad_pcb or the exporter must
  refuse (stale gate).
- DRC reports always go through `kicad-cli pcb drc --format json`; the
  in-process route (pcbnew.WriteDRCReport) aborts outside the GUI.
- Locked (generator) copper is contract: FreeRouting sees locked tracks
  and vias as fixed wiring (locked arcs are not written to the DSN: draw
  generator copper as segments), copper-copy keeps it, routing re-runs
  keep it.
- Off-board part pins are unverifiable by construction — keep their net
  maps right in the design and wire them via on-board connectors.
- Fab limits come from the fab, never from memory or a copy: fetch the
  capability page into `pcb/fab.json`, every value next to the page's own
  words, then let code hold the project to it (`FabCheck` in gen_pcb,
  `fabcheck.cs` before export). They drift, and they depend on the
  process (a mask bridge can depend on the mask colour). fabcheck fails on a `fab.json` checked over 90 days ago.
- Two `dotnet run` of the same .cs at once share one build output: the
  second fails on a locked exe. Run one instance per tool, or a copy of
  the file.
- Panels replacing enclosure end plates: legends outside, mounting screws
  bonding GND, no through-hole leads (unless the one special part), check
  the outline against the physical case before anything else.

## References

| File | Read when |
|---|---|
| `references/kickoff.md` | starting a new project (step 0): interrogation, scaffold, docs |
| `references/pipeline-steps.md` | working any of steps 1-8, or running the review sweep |
| `references/project-structure.md` | repository layout, AGENTS.md convention |
| `references/fabrication.md` | choosing the fab, fetching its limits into fab.json, the fab checks |
| `references/dotnet-conventions.md` | writing the C# file-based apps |
| `${CLAUDE_PLUGIN_ROOT}/skills/hw-review/` | the multi-wave review sweep itself — before a fab order, after a large change |
