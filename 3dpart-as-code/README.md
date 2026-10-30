# 3dpart-as-code

A Claude Code / ZCode plugin skill for 3D-printable parts, in the spirit of
[pcb-as-code](../pcb-as-code/README.md): the part lives in code, the STL and
STEP files are regenerated build outputs. Geometry is **build123d**
(Python, OpenCascade); everything around it — the model, the gates, the
renderer, the exporter — is **C# — .NET 10 file-based apps
(`dotnet run tool.cs`)**. The workflow ends at a validated STL + STEP: the
printer and the slicer (Cura or any other) stay outside it.

## What the workflow is

**Starting a project:** with the plugin installed, open the destination
folder as the working directory — a pcb-as-code project (the parts go into
a `mech/` beside its `pcb/`) or an empty folder — and type `/3dpart-as-code`
(or just describe the part you want printed: the skill also triggers from
its description). The agent runs the kickoff there: a short interrogation
(mode, function, mates, environment, printer size, hardware, assembly),
the `mech/` scaffold, the Python venv, the templates and the test suite.

Then:

- **`mech/tools/part.cs`** holds every number: parameters (with tolerance,
  source and whether critical), named fits (press, snug, slip, loose — gap
  per side, tuned from printed coupons), bodies and their print
  orientation, expected dimensions, clearance and insertion-sweep checks,
  the printer envelope, and in reproduce mode the measurement sheet.
- **`mech/parts/*.py`** — build123d scripts — read those numbers, build the
  bodies in assembly coordinates and probe the features part.cs expects.
  They hold no numbers of their own.
- **The gates:** `build.cs` (valid solids) → `check.cs` (`DIM PROBLEMS: 0`:
  every probe within tolerance; `MESH PROBLEMS: 0`: watertight, oriented,
  one shell, inside the printer, volume matching the solid) → `fit.cs`
  (`FIT PROBLEMS: 0`: no overlap, every gap, every insertion path clear) →
  `render.cs` (PNG views for people and reviewers) → `export.cs` (refuses
  anything stale or dirty; writes `mech/out/`: STL per printed body in print
  orientation, STEP, assembly, renders, manifest).
- **Two modes.** *Design* invents a part — an enclosure whose slots, seats
  and bosses come straight from a pcb-as-code board, a bracket, a jig.
  *Reproduce* copies an existing part: the agent looks for the maker's
  drawing or STEP first, then renders numbered callouts for the user to
  measure with calipers, and the DIM gate refuses any critical number that
  is still a guess.
- **The parameter sheet.** `sheet.cs` writes `mech/params.xlsx` — every
  number next to the renders. The user edits the highlighted cells in Excel
  and asks the agent to apply them; `sheet.cs -- pull` writes the changes
  back into `part.cs` (which stays the single source of truth) and the
  pipeline reruns. In reproduce mode the sheet is also the measurement sheet.
- **Coupons** — small slices of the critical features, cut from the real
  bodies — print in minutes, before the part, and tune the fits.
- **`/3dpart-review`** sweeps cheap read-only reviewer agents over the docs,
  part.cs, the scripts and the renders in waves (dimensions against mating
  parts' datasheets, printability, fits, docs consistency); the session
  triages, fixes the source and rebuilds.

## What's in the plugin

```
├── agents/
│   ├── part-reviewer-claude.md  the sweep's read-only reviewer twins (identical
│   └── part-reviewer-glm.md     bodies; each harness's cheap model, no write
│                                tools, no shell, datasheet-capable)
└── skills/
    ├── 3dpart-as-code/
    │   ├── SKILL.md         the map: rules, pipeline, toolchain, templates, gotchas
    │   ├── references/      kickoff, step1 … step6, reproduce mode, pcb bridge,
    │   │                    project structure, dotnet conventions
    │   ├── templates/       copied into a project's mech/tools/ (and parts/):
    │   │     partmodel.cs   the model types: Param, Fit, Body, ExpectedDim,
    │   │                    FitCheck, SweepCheck, Envelope, Measurement, Model
    │   │     part.cs        the project's model — ships a worked example
    │   │                    (L-bracket, slip-fit pin, hole coupon)
    │   │     mech.cs        mech/ layout, build inputs, the venv runner
    │   │     stl.cs         STL read/write + mesh checks, envelope, overhang
    │   │     raster.cs      dependency-free renderer → PNG
    │   │     checks.cs      the DIM / MESH / FIT / staleness gates
    │   │     partedit.cs   find and rewrite numeric literals in part.cs
    │   │     paramsheet.cs the params.xlsx writer / reader (ClosedXML)
    │   │     pac.py         the build123d helper (body, probes, coupons,
    │   │                    export; fit and assembly stages)
    │   │     parts/bracket.py   the example's geometry
    │   │     build.cs check.cs fit.cs render.cs sheet.cs export.cs smoke.cs
    │   │     requirements.txt   build123d pinned
    │   └── tests/           xUnit suite: mesh, renderer + PNG, model, gates,
    │                        and the example through the whole pipeline with
    │                        three deliberate breaks each gate must catch
    └── 3dpart-review/         the multi-wave review sweep (its own skill)
         ├── SKILL.md
         └── references/     wave-protocol.md, zcode-wall-clock.md
```

## Prerequisites

- **.NET SDK 10.0.300+** (file-based apps with `#:include`).
- **Python 3.12** (`py -3.12` on Windows): build123d's OpenCascade wheels
  lag new Python releases. `build.cs` creates `mech/.venv` and installs the
  pinned `requirements.txt` on first run (a few hundred MB).
- **KiCad 10** (`kicad-cli`) only to export mating boards as STEP.
- Any CAD viewer (Fusion 360 works) to look at the STEP files; any slicer
  for the STLs.

Tests: `dotnet test 3dpart-as-code/skills/3dpart-as-code/tests`. The pipeline
tests run live when `PAC_PYTHON` names an existing interpreter — point it
at a venv made from `templates/requirements.txt` — and return early when it
is unset or names no file; an interpreter without build123d fails them.
