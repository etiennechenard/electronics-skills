---
name: 3dpart-as-code
description: Build 3D-printable parts design-as-code — every number in a C# part.cs single source of truth, geometry in build123d scripts that hold none, STL/STEP regenerated as build outputs and gated (dimensions probed off the solid, watertight single-shell meshes inside the printer envelope, clearance and insertion-sweep fits), with tooling as .NET 10 file-based apps (dotnet run tool.cs) in mech/tools. Design mode invents a part (an enclosure from a pcb-as-code board, a bracket); reproduce mode copies an existing one from a caliper measurement sheet. Use to start a printed-part project or to work in one (mech/, part.cs, parts/*.py) — adding a body, changing a dimension or fit, regenerating, checking, exporting STL for the slicer.
---

# 3dpart-as-code

**The part lives in code; STL and STEP are build outputs.** One C# file (`part.cs`) holds every number — parameters, named fits, bodies, expected dimensions, clearances, the measurement sheet, the largest printable part. build123d scripts (`parts/*.py`) read those numbers from `params.json`, build the solids and probe their features; C# gates check the probes, the meshes and the fits; an exporter refuses anything stale or dirty. The workflow ends at a validated STL + STEP: printer, slicer and their settings stay out of it.

This file is the map; read the reference named for the stage you are working (paths relative to this file).

## Hard rules

1. **`part.cs` is the single source of truth.** Every number lives there: parameters (with tolerance, source and whether critical), named fits (press, snug, slip, loose — gap per side), bodies and their print orientation, expected dimensions, clearances and sweeps, the measurement sheet, the printer envelope. `build.cs` writes it to `build/params.json`.
2. **Geometry scripts hold no numbers.** `parts/<name>.py` reads `params.json` through `pac.py`, builds its bodies in assembly coordinates, registers them, probes the features part.cs expects, and calls `pac.finish()`. A literal dimension in a script is a bug (0, 1, 2 and ±1 as arithmetic are fine; a probe window's pads are the one exception — they place a measuring box, they never size the part).
3. **Regenerate, never hand-edit the outputs.** Opening the STEP in Fusion 360 (or any CAD) to LOOK is encouraged; a change made there is lost on the next build. Lasting changes go into `part.cs` or a script.
4. **Every stage gates**: build OK → `DIM PROBLEMS: 0` → `MESH PROBLEMS: 0` → `FIT PROBLEMS: 0` → renders → export. Every tool after build refuses a stale build; the exporter re-runs the gates and refuses anything dirty.
5. **Probe the solid, never echo a parameter.** A probe measures the built geometry (`probe_cyl`, `probe_extent`, the automatic bbox and volume); recording `P["hole.d"]` as a probe proves nothing.
6. **Reproduce mode: no guessed critical number.** Every critical parameter is `Measured` or `Datasheet` with a `Ref` (measurement id, drawing), or `Derived` from those with its formula as `Ref` — the DIM gate holds it.
7. **Tests first for the tooling:** keep the copied xUnit suite green (`dotnet test mech/tools/tests`) and extend `ModelTests` with the project's own invariants.

## The pipeline

| Step | Produces | Gate | Read |
|---|---|---|---|
| 0. kickoff | interrogation, `mech/` scaffold, venv (build123d pinned), templates + suite copied, docs/mech.md | smoke + unit tests pass | `references/kickoff.md` |
| 1. part.cs | parameters, fits, bodies, expected dims, clearances/sweeps (reproduce: the measurement sheet) | `Model.Validate()` empty, model tests | `references/step1-part.md` (+ `references/reproduce-mode.md`) |
| 2. geometry | `parts/*.py` → STEP, STL (print pose), assembly STL, probes; `build.cs` (plus the boards' kicad-cli STEP exports) | `BUILD PROBLEMS: 0` (valid solids, every body built) | `references/step2-geometry.md` |
| 3. check | probes vs part.cs; printed meshes; envelope; overhang report; `check.cs` | `DIM PROBLEMS: 0`, `MESH PROBLEMS: 0` | `references/step3-check.md` |
| 4. fit | clearances and insertion sweeps on the STEPs (mating boards and keep-outs as reference bodies); `fit.cs` | `FIT PROBLEMS: 0` | `references/step4-fit.md` |
| 5. render + coupons | PNG views (print poses, the assembly with the boards in place, open / cutaway / exploded views, numbered parameter dimensions, callouts); coupon bodies printed first; `render.cs` | read the renders | `references/step5-render-coupons.md` |
| sheet | `mech/params.xlsx`: every number beside the renders; the user edits cells, `sheet.cs -- pull` writes them back into part.cs | `SHEET PROBLEMS: 0` / `PULL PROBLEMS: 0` | `references/step5-render-coupons.md` (the parameter sheet) |
| 6. export | `out/`: STL + STEP per printed body, assembly.step, renders, manifest; `export.cs` | refuses stale or dirty | `references/step6-export.md` |
| review | multi-wave fresh-eyes sweep of part.cs, scripts, renders, docs | fixes in the source, rebuilt, gates re-run | the `3dpart-review` skill (`${CLAUDE_PLUGIN_ROOT}/skills/3dpart-review/`) |

Run order from the project root: `dotnet run mech/tools/build.cs`, `check.cs`, `fit.cs`, `render.cs`, `sheet.cs` (optional, for the user to tune numbers), `export.cs`. The user edits `mech/params.xlsx` and asks you to apply it: `dotnet run mech/tools/sheet.cs -- pull`, then rebuild (build, check, fit, render, sheet). Run `3dpart-review` before printing anything big (hours of filament) and after any large change.

## Toolchain (this machine)

| Tool | Fact |
|---|---|
| .NET SDK | 10.0.300+ (`#:include`); tools are `dotnet run mech/tools/<tool>.cs` |
| Python | 3.12 for the venv (`py -3.12`): build123d's OCP wheels lag new Pythons, 3.14 has none |
| build123d | 0.13.0 + cadquery-ocp-novtk 8.0.1.1.0, pinned in `mech/requirements.txt`; build.cs creates `mech/.venv` on first run. `PAC_PYTHON` overrides the interpreter (tests, a shared venv) |
| kicad-cli | `pcb export step` for mating boards (`references/pcb-bridge.md`); `build.cs` runs it for every `Board` in part.cs (`KICAD_CLI` overrides the lookup) |
| Slicer | out of scope: `out/<body>.stl` is already in print orientation, on z = 0 |

## Templates (`templates/`, shipped, tested)

| File | Provides |
|---|---|
| `partmodel.cs` | `PartModel`: `Param`, `Fit`/`Fits`, `Body` (+`BodyKind` Part/Coupon/Reference, `Up`, `Color`), `Board` (a KiCad board shown in the renders), `AssemblyView` (open / cutaway / exploded renders), `ExpectedDim`, `FitCheck`, `SweepCheck`, `Envelope`, `Measurement`, `Model` (`Validate`, `ToJson`, `Write`, `SheetMarkdown`) |
| `part.cs` | `Part.PartDesign.Model` — the project's model; ships a worked example (L-bracket, slip-fit pin, hole coupon) |
| `mech.cs` | `Mech`: `Layout` (every path under mech/, the build's inputs), `Python` (venv ensure, run a script, pac stages), `KiCad` (find kicad-cli, export a board's STEPs) |
| `stl.cs` | `Mesh`: STL read/write, `MeshCheck.Analyze` (open/non-manifold/misoriented edges, shells, degenerate, volume, overhang), `FitsEnvelope` |
| `raster.cs` | `Raster`: software renderer → PNG (views, crease/silhouette lines, callouts, scale bar), no dependencies |
| `checks.cs` | `Checks`: `Probes.Load`, `Gates.Dims/Sources/Outputs/Meshes/Fit/Stale/BuildOutputs`, `Gates.Report` |
| `pac.py` | the build123d helper: `load`, `body`, `probe`, `probe_cyl`, `probe_extent`, `region`, `coupon`, `import_step`, `finish`; stages `fit` and `assembly` (assembly.step, the placed boards, the views' cut bodies) |
| `parts/bracket.py` | the example's geometry |
| `partedit.cs` | `PartEdit`: `Literals.Locate/SetValue/SetArgument` — find and rewrite one numeric literal in part.cs by its syntax tree (Roslyn) |
| `paramsheet.cs` | `ParamSheet`: `Sheet.Write` (params.xlsx via ClosedXML), `Sheet.Read` (edits, problems, warnings), `Sheet.Apply` |
| `build.cs` `check.cs` `fit.cs` `render.cs` `sheet.cs` `export.cs` | the pipeline's entry tools (`sheet.cs` generates the parameter sheet, or with `-- pull` applies it) |
| `smoke.cs` | proves the include path: `dotnet run mech/tools/smoke.cs` |
| `requirements.txt` | the pinned kernel |

The xUnit suite in `tests/` compiles the libraries directly and runs the example through the whole pipeline — then breaks a dimension, a fit and the build's freshness and expects each gate to catch it. The pipeline tests need `PAC_PYTHON` (a venv from requirements.txt) and return early, reporting as passed, without it: `dotnet test "${CLAUDE_PLUGIN_ROOT}/skills/3dpart-as-code/tests"`. Kickoff copies templates and tests into the project.

## Gotchas (each paid for)

- build123d primitives are **centred** on their origin (`Box`, `Cylinder`): place them with `Pos(...) *` from part.cs numbers, and write a body's frame down in a comment at the top of the script.
- `a.distance_to(b)` measures surface to surface: a body **buried inside** another reads a positive distance. pac's fit stage checks the overlap volume first — do the same in any ad-hoc check.
- An empty boolean result is an empty `Compound`, not `None`: test `.solids()`, never truthiness.
- The STL is in **print pose** (`up` axis to +Z, centred, on z = 0) — the STEP and `build/asm/*.stl` stay in assembly coordinates. Measure fits on the STEPs, printability on the STLs.
- Holes print small and bosses print fat on FDM: the named fits carry that, per side. Tune them once from a coupon and keep the tuned values in part.cs; never fudge one hole.
- `mech/params.xlsx` is a generated, gitignored front-end; part.cs stays the truth. **Excel locks an open file**: close it before `sheet.cs` regenerates (it prints `params.xlsx is open (close it in Excel and re-run)`); `sheet.cs -- pull` reads only what was saved. xlsx, never CSV: a French-locale Excel saves CSV with `;` and decimal commas. `sheet.cs` and `export.cs` use the ClosedXML and Roslyn packages (`#:package`), so their first run downloads from NuGet; `dotnet run` needs no `PublishAot=false` (the spike built with no trim/AOT warnings). `export.cs` refuses a sheet holding edits that were never pulled.
- File-based apps compile trimmed/AOT-checked: build JSON with `JsonNode` implicit conversions (`new JsonArray(x, y)`), not generic `Add<T>` on doubles — that throws at run time.
- An enclosure hides what it holds: declare `AssemblyView`s (open, cutaway, exploded) and, on a pcb-as-code project, a `Board` per board (`Replaces` its keep-outs) from the start, or no render ever shows a board on its standoffs. A `Board`'s Origin is the board's design centre on the KiCad page: read the pcb project's constant.
- Console text from the tools stays ASCII (`+/-`, `x`, `>=`): Windows pipes default to the OEM code page and turn `±`/`×`/`≥` into `?`.
- Two `dotnet run` of the same .cs at once fail on a locked exe — one instance per tool.
- A part bigger than the envelope is **split** in part.cs (two bodies, a joint between them, a clearance check on the joint), never scaled.

## Other references

| File | Read when |
|---|---|
| `references/reproduce-mode.md` | copying an existing part: sheet, callouts, the measure loop |
| `references/pcb-bridge.md` | an enclosure or bracket that mates with a pcb-as-code board |
| `references/project-structure.md` | repository layout, AGENTS.md convention |
| `references/dotnet-conventions.md` | writing the C# file-based tools |
