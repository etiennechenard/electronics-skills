# Blueprint — Excel parameter sheet for 3dpart-as-code

**Type:** medium
**Status:** done
**Main blueprint:** docs/Blueprint.md
**Project root:** ../../..
**Docs root:** docs

---

## Task

Give the user a spreadsheet front-end to a 3dpart-as-code part: a generated `mech/params.xlsx` that shows every number with the renders beside it. The user edits the editable cells in Excel and asks the agent to apply them; a tool writes the edits back into `part.cs` and the pipeline is rerun. Why: tuning a part (or filling in measurements in reproduce mode) is currently done by asking the agent in prose. A sheet the user can edit directly, next to pictures of the part, is faster and less error-prone — and `part.cs` stays the single source of truth, so git diffs, derivations and every gate keep working.

## Context & constraints

All paths below are relative to the project root (`3dpart-as-code/`). Templates live in `skills/3dpart-as-code/templates/`, the xUnit suite in `skills/3dpart-as-code/tests/`, the docs in `skills/3dpart-as-code/references/`.

- **Existing code:** `partmodel.cs` (`Param`, `Fit`, `Measurement`, `Model`, `Source`), `part.cs` (the worked example: L-bracket, pin, coupon), `mech.cs` (`Layout`, `Layout.NotInputs`), `checks.cs` (`Probes.Load`, `Gates.Dims/Sources/Meshes/Fit/Stale/BuildOutputs/Report`), `render.cs` (writes `build/renders/*.png`, including `<body>-callouts-*.png` in reproduce mode), `export.cs`. Tests: `FileBasedAppTests.cs` (copies every `templates/*.cs` into a scratch `mech/tools/`; its pipeline tests run live only when `PAC_PYTHON` is set), `ModelTests.cs`, `GateTests.cs`, `TestMeshes.cs` (the `Model(...)` test factory), `Part3dAsCode.Tests.csproj` (compiles each library by explicit path).
- **Runtime:** .NET SDK 10.0.300+ file-based apps (`dotnet run tool.cs`, `#:include`, `#:package`). These apps get trim/AOT analysis; the skill already records that some reflection-based APIs throw at run time under it.
- **Dependencies (decided):** **ClosedXML** for xlsx (MIT; it writes images, sheet protection and cell locking). **Microsoft.CodeAnalysis.CSharp** (Roslyn, syntax trees only) so `part.cs` is edited by its syntax tree, never by regex. Both pinned to one exact version each, identical in every `#:package` directive and in the test csproj. The first run of a tool that uses them downloads from NuGet.
- **xlsx, not CSV:** a French-locale Excel saves CSV with `;` separators and decimal commas. xlsx stores numbers as numbers.
- **Must not break:** hard rule 1 (`part.cs` holds every number); the existing gates and their `<GATE> PROBLEMS: n` lines; the pipeline test's text replacements on `part.cs` (it replaces `ExpectedDim.Of(P(ps, "base.t"), "bracket.base.t", 0.01)` and `Clearances = [new("pin", "bracket", slip.Gap * 0.75, "radial slip gap")]` verbatim, so both strings must survive in the example); console text stays ASCII; library files hold types only.
- **Out of scope:** interactive 3D in Excel (renders stay PNGs); editing tolerance, critical, source, ref or note from the sheet (changing those stays an agent edit to `part.cs`), except the Source/Ref that a measurement pull sets; copying the sheet into `out/`; a Google Sheets or CSV variant.

## Approach

- **Two new libraries and one tool.** `partedit.cs` (namespace `PartEdit`) finds and rewrites single numeric literals in `part.cs` text. `paramsheet.cs` (namespace `ParamSheet`) writes `params.xlsx` and reads back the user's edits. The entry tool `sheet.cs` generates the sheet, or with `-- pull` applies the edits to `part.cs`. Both libraries read outputs and never shape geometry, so they go into `Layout.NotInputs`.
- **Editability is decided by the source text, not by the file.** A value is editable when its `Source` is `Chosen` or `Measured` *and* `part.cs` declares it as exactly one literal. A declaration is an object creation whose first argument is the string literal `"<name>"` and whose second argument is a numeric literal (optionally negated). Generation shows locked cells with the reason why; pull recomputes editability from the current `part.cs`, so a tampered or unprotected sheet can't bypass it.
- **Three-way edit detection per value**, with no global hash. Each editable Parameters and Fits row carries a hidden `Was` (its value at generation time); Measurements rows have no `Was` and follow their own median rule (step 3):
  - sheet = Was → no edit;
  - sheet ≠ Was and current `part.cs` = sheet → already applied;
  - sheet ≠ Was and current = Was → edit to apply;
  - sheet ≠ Was and current ≠ Was → conflict.

  A sheet whose edits are all applied therefore counts as clean. That is what lets `sheet.cs` regenerate over it and lets `export.cs` pass.
- **Pull is all-or-nothing.** Any problem (not a number, locked, conflict, unknown name, double-edited) means nothing is written.
- **The loop:** build → check → fit → render → **sheet** → user edits → **sheet pull** → build → …. Export refuses while the sheet holds unapplied edits.

## Implementation steps

### Step 1 — Example declares its fits; prove the dependencies run in a file-based app

**Build:**
- `templates/part.cs`: the model declares an explicit fit list with literal gaps equal to today's defaults (press 0.05, snug 0.10, slip 0.20, loose 0.35) and their notes. A local `slip` is taken from that list, so `hole.d` (still `Derived`, `Ref` = its formula) derives from the declared fit, and the text `slip.Gap * 0.75` survives unchanged. The parameter list stays one `new(...)` per line with a literal value for every `Chosen` parameter.
- A throwaway file-based app in the session scratchpad (not committed) confirms, with the pinned versions:
  - ClosedXML writes an xlsx holding a protected sheet, an unlocked cell and an embedded PNG, then reads it back;
  - Roslyn parses a C# file and reports a literal's span.
  
  The versions pinned are the latest stable releases of ClosedXML and Microsoft.CodeAnalysis.CSharp on nuget.org at the time of this step; record both exact versions for steps 2–5 and the dotnet-conventions doc (step 6).

  **Outcome (recorded):** ClosedXML 0.105.1 and Microsoft.CodeAnalysis.CSharp 5.9.0. Under `dotnet run` on SDK 10.0.401 the spike built without trim/AOT warnings and ran correctly (protected sheet, unlocked cell and embedded PNG read back; Roslyn literal span excludes a leading minus sign). No `#:property PublishAot=false` is needed.

  The outcome decides whether entry tools using these packages need `#:property PublishAot=false`, i.e. whether there are trim/AOT warnings or reflection failures at run time. Record that decision for step 4 and the SKILL.md gotcha (step 6).

**Verify:** `dotnet test skills/3dpart-as-code/tests --filter ModelTests` passes, including `ExampleModel_derivesTheHoleFromThePinAndTheFit`. The scratch app exits 0 and prints the cell value and literal span it read back.

**Done:** yes

### Step 2 — `partedit.cs`: locate and rewrite literals in part.cs

**Build:**
- `templates/partedit.cs` (new), namespace `PartEdit`, types only.
- **Locate** a name in `part.cs` text. It considers every object creation (explicit `new Param(`/`new Fit(`, or target-typed `new(`) whose first argument is the string literal equal to the name, and returns one of:
  - `Found` (the value's span: the numeric literal, including its unary `-` when negated, so a rewrite replaces the sign too);
  - `Missing`;
  - `Ambiguous` (with the count; a Param and a Fit sharing a name are ambiguous);
  - `NotLiteral` (the declaration exists but its second argument is not a numeric literal, optionally negated).

  Names appearing in comments or other strings never match.
- **Set value:** replaces only that literal's text. The number is formatted culture-invariant, round-trip exact, with no exponent and at least one decimal digit (`41` → `41.0`), with a leading `-` for negatives. Every other byte of the file is unchanged, line endings included.
- **Set named argument** (`Source`, `Ref`) on a located declaration: replaces the named argument's expression if present, else the positional argument at that parameter's index (Param: Source 3, Ref 6) if it is given positionally, else appends `, Name: expr` after the last argument. A string value is emitted as a correctly escaped C# string literal.
- User-facing failures (missing, ambiguous, not a literal) are returned as results, never thrown.
- `templates/mech.cs`: `Layout.NotInputs` gains `partedit.cs` and `paramsheet.cs`.
- `tests/Part3dAsCode.Tests.csproj`: PackageReferences for the two pinned packages; a Compile item for `partedit.cs` (step 3 adds `paramsheet.cs`'s).
- `tests/PartEditTests.cs` (new): Found, Missing, Ambiguous and NotLiteral, each on the example `part.cs` (`bracket.w` found, `hole.d` not a literal, `slip` found as a Fit) and on small inline sources (a duplicate name, a name only in a comment, a negative literal). Set value on a negative literal gives the right sign both ways (`-2.0` → `3.0`, `2.0` → `-3.0`). Set value round-trips and leaves the rest byte-identical (CRLF preserved). Setting a named argument both replaces and appends, and escapes quotes in a Ref.

**Verify:** `dotnet test skills/3dpart-as-code/tests --filter PartEditTests` passes.

**Done:** yes

### Step 3 — `paramsheet.cs`: write the workbook and read the edits back

**Build:**
- `templates/paramsheet.cs` (new), namespace `ParamSheet`, types only; needs `partmodel.cs`, `partedit.cs` and `mech.cs`.
- `Layout` (`mech.cs`) gains `Sheet` = `mech/params.xlsx`.
- `tests/Part3dAsCode.Tests.csproj`: a Compile item for `paramsheet.cs`.
- **Write** takes the model, the `part.cs` text, the layout and the gate results (gate name → problem lines). It produces the sheets in this order:
  - **Parameters:** one row per Param in model order. Columns: Name, Value, Unit, Tol, Critical, Source, Ref, Note, Lock, Was (hidden).
    - Value is editable (unlocked, highlighted fill) iff Source is Chosen or Measured and Locate returns `Found`.
    - Otherwise the cell is locked with a grey fill, and Lock holds the reason: `derived: <Ref>`, `datasheet`, `not a single literal in part.cs (<result>)`.
  - **Fits:** Name, Gap, Note, Lock, Was (hidden); editable iff Locate returns `Found`.
  - **Measurements** (only when the model has measurements). Columns: Id, Measure, Parameter, Current, Unit, Tol, Critical, Source, R1, R2, R3, Lock.
    - R1–R3 are blank and are editable iff the measured parameter is editable by the Parameters rule.
    - Below the table, the callout renders of each measured body, captioned.
  - **Views:** grouped by heading, first each printed body's print-pose renders (iso, front, top, right), then the assembly renders. Images are in a grid at one fixed display width, each captioned with its file name. A missing render group is shown as a text line, not an error.
  - **Status:**
    - model name, mode, generation time (UTC);
    - how-to text: edit the highlighted cells, save, close Excel, then ask the agent to apply the sheet or run `dotnet run mech/tools/sheet.cs -- pull`;
    - per gate (DIM, MESH, FIT), its problem count and each problem line.
- **Shared formatting:** every sheet is protected without a password, so only editable cells accept input. The header row is frozen. Numbers are stored as numbers (never text) and displayed with up to 4 decimals.
- **Locked target file:** Write goes through a temporary file in `mech/` that then replaces `params.xlsx`. If `params.xlsx` is locked (open in Excel), Write returns the problem `params.xlsx is open (close it in Excel and re-run)` and leaves the existing file intact.
- **Read** takes the path, the model and the `part.cs` text, and returns edits, problems and warnings. Values compare as equal within 1e-9.
  - It opens the file with read/write sharing, so a sheet open in Excel can still be read (only its saved content counts). Rows are matched by their Name/Id cell, never by position, so sorting is harmless.
  - **Parameters and Fits rows:** a value equal to Was produces nothing. A changed value is judged in this order:
    - not a number → problem;
    - name not in the current model → problem;
    - not editable by the current editability → problem naming the lock reason;
    - current value = sheet value → skipped (already applied);
    - current value ≠ Was → conflict problem stating Was, the current value and the sheet value, and telling the user to delete params.xlsx and regenerate it;
    - otherwise → an edit {kind Param|Fit, name, from, to}.
  - **Measurement rows:**
    - all of R1–R3 blank → nothing (no edit, no problem);
    - filled readings must be numbers (else a problem);
    - target value = the median of the filled readings (1 → that reading, 2 → their mean, 3 → the middle one);
    - the edit sets that value plus `Source = Measured` and `Ref = "<id>: <r1> / <r2> / <r3>"`: the filled readings' values in R1→R3 order, blanks omitted, formatted like Set value (e.g. `"D3: 6.18 / 6.21 / 6.2"`, or `"D3: 6.2"` for one reading);
    - it is skipped when the value already equals the median and Source is already Measured;
    - a parameter that is also edited in Parameters → problem;
    - not editable → problem;
    - spread (max − min) > the parameter's Tol → a warning, not a problem.
- `tests/SheetTests.cs` (new; builds models with `TestMeshes.Model` and the example `part.cs`, whose text it finds the way `FileBasedAppTests` finds the fixture — `tests/example/part.cs` in a project, else `templates/part.cs`):
  - Write produces the five sheets (four in design mode) with the right editability and lock reasons (`hole.d` locked as derived, `bracket.w` editable, a Datasheet param locked);
  - editing a Value via ClosedXML in the test is read back as an edit;
  - each Read problem and the skip/conflict cases occur as listed above;
  - the median is right for 1, 2 and 3 readings;
  - the spread warning fires;
  - Write with the file held open by `FileShare.None` returns the "close it" problem, and the old file is unchanged.

**Verify:** `dotnet test skills/3dpart-as-code/tests --filter SheetTests` passes.

**Done:** yes

### Step 4 — `sheet.cs`: generate and pull

**Build:**
- `templates/sheet.cs` (new entry tool; the `part.cs` text it hands to the libraries is read from `mech/tools/part.cs`, i.e. `Layout.Tools`): `#:include` of partmodel, part, mech, stl, checks, partedit and paramsheet; the two pinned `#:package` lines; `PublishAot` per step 1's outcome.
- **Generate** (`dotnet run mech/tools/sheet.cs [-- --mech <dir>]`):
  - It refuses a stale or incomplete pipeline, using the same set as export: build outputs, `fit.json` when the model has clearances or sweeps, and the renders. Missing renders count. It prints `STALE PROBLEMS: n` and `run build, check, fit, render first`, then exits 1.
  - It refuses when an existing `params.xlsx` reads back any edit or problem. It lists them, says `pull them first, or delete mech/params.xlsx to discard them`, and exits 1.
  - It computes DIM (with Sources), MESH and FIT problems from the build's own files, as export does. These go into Status; they never block generation.
  - It writes the sheet. A Write problem (file open) → printed, exit 1.
  - On success it prints `<name>: params.xlsx, N parameters (E editable), F fits, R renders` and `SHEET PROBLEMS: 0`.
- **Pull** (`-- pull`):
  - no `params.xlsx` → `no params.xlsx (run sheet.cs)`, exit 1;
  - Read problems → each printed, then `PULL PROBLEMS: n`; `part.cs` is untouched; exit 1;
  - no edits → `nothing to pull`, `PULL PROBLEMS: 0`, exit 0;
  - otherwise it applies every edit to the `part.cs` text with PartEdit. Any failure there is a problem and nothing is written. On success it writes `part.cs` once and prints:
    - one line per change (`pin.d: 6.0 -> 6.2`, plus `, source measured, ref D3: ...` for a measurement);
    - the warnings;
    - `PULL PROBLEMS: 0`;
    - `rebuild: build, check, fit, render, sheet`.

  Exit 0.
- `tests/FileBasedAppTests.cs`:
  - **(non-live, needs dotnet only):** scaffold, have the test write a sheet through the compiled `ParamSheet` library, change `bracket.w` to 42 and `slip` to 0.25 in it, run `sheet.cs -- pull --mech <dir>`. Assert exit 0 and the change lines, then assert that `part.cs` holds `42.0` and `0.25` with every other line unchanged. A second pull prints `nothing to pull`.
  - **(live, `PAC_PYTHON`):** a separate test, `Sheet_editPullRebuildLoop_andExportRefusesUnpulledEdits`, scaffolds and runs build, check, fit, render; `Example_goesThroughEveryGate_andEachBreakIsCaught` is unchanged.
    - After render, `sheet` exits 0 and `params.xlsx` exists.
    - Edit `bracket.w` to 42. Rerunning `sheet` is refused (unapplied edits).
    - Pull, then build + check: exit 0, and the `bracket.bbox.x` probe reads 42.
    - fit + render + sheet: exit 0.

**Verify:** `dotnet test skills/3dpart-as-code/tests --filter FileBasedAppTests`, with `PAC_PYTHON` set to a build123d venv so the live test runs, passes.

**Done:** yes

### Step 5 — Export refuses unapplied sheet edits

**Build:**
- `templates/export.cs`: when `mech/params.xlsx` exists, it reads it against the current model and `mech/tools/part.cs` text, and each edit becomes a `SHEET <name>: sheet value <v> not pulled` problem and each Read problem a `SHEET <problem>` problem. Either way: `EXPORT REFUSED`, exit 1. Without the file there is no check. It gains the includes, `#:package` lines and `PublishAot` setting that `sheet.cs` uses.
- `tests/FileBasedAppTests.cs` (live): in the sheet loop test (step 4), between the edit and the pull, `export` exits 1 with `EXPORT REFUSED` and `SHEET bracket.w`. After the full rerun, `export` exits 0.

**Verify:** the live pipeline test passes with `PAC_PYTHON` set. Then, manually in the scratch project, deleting `params.xlsx` lets `export` pass.

**Done:** yes

### Step 6 — Documentation

**Build:**
- `skills/3dpart-as-code/SKILL.md`:
  - a pipeline row for `sheet` (after render): the user-edit loop and its pull;
  - template rows for `partedit.cs`, `paramsheet.cs` and `sheet.cs`;
  - the run order including `sheet.cs`;
  - gotchas:
    - Excel locks an open file: close it before `sheet.cs` regenerates; pull reads only saved content;
    - xlsx, never CSV (locale);
    - the `PublishAot` outcome from step 1;
    - the first run downloads NuGet packages;
    - export refuses unapplied sheet edits.
- `references/step5-render-coupons.md`: a section on the parameter sheet: what each sheet holds, why a cell is locked, the loop (generate → edit → save/close → pull → rerun), and that coupon results are typed into the Fits sheet.
- `references/step1-part.md`: an editable number is one literal per declaration (`new("x", 4.0, ...)`), never an expression or a shared variable. Parameter and fit names stay unique across both lists. Declare `Fits = [...]` in `part.cs` (as the example now does) to tune fits from the sheet. Remove the text saying the defaults are what the model uses unless replaced.
- `references/reproduce-mode.md`: measuring through the Measurements sheet. Callouts sit below the table, R1–R3 hold up to three readings, and pull writes the median with `Source: Measured` and `Ref: "<id>: <r1> / <r2> / <r3>"`. `SheetMarkdown()` still goes into `docs/mech.md`.
- `references/step6-export.md`: the new refusal.
- `references/kickoff.md`:
  - ignore `mech/params.xlsx` and `mech/~$params.xlsx`;
  - the csproj now compiles eight libraries;
  - prove `sheet.cs` as part of the example pipeline.
- `references/project-structure.md`: `params.xlsx` in the layout tree (ignored); the AGENTS.md snippet mentions the sheet loop.
- `references/dotnet-conventions.md`: the updated `NotInputs` list; `#:package` for the two pinned packages, with the exact versions step 1 recorded.
- `README.md` (plugin): a workflow bullet for the sheet; tree entries for the three new files.

**Verify:** `grep -rn "sheet.cs" skills/3dpart-as-code/SKILL.md skills/3dpart-as-code/references README.md` hits every file listed above. Following `step5-render-coupons.md`'s loop literally on the scratch project (generate, edit, pull, rerun) reproduces the documented outputs line for line.

**Done:** yes

## Files to change

- `skills/3dpart-as-code/templates/part.cs` — explicit fit list; `hole.d` from it.
- `skills/3dpart-as-code/templates/mech.cs` — `Layout.Sheet`; `NotInputs` += partedit.cs, paramsheet.cs.
- `skills/3dpart-as-code/templates/partedit.cs` (new) — locate/rewrite literals.
- `skills/3dpart-as-code/templates/paramsheet.cs` (new) — workbook write/read.
- `skills/3dpart-as-code/templates/sheet.cs` (new) — generate / pull tool.
- `skills/3dpart-as-code/templates/export.cs` — unapplied-edit refusal.
- `skills/3dpart-as-code/tests/Part3dAsCode.Tests.csproj` — packages, two Compile items.
- `skills/3dpart-as-code/tests/PartEditTests.cs` (new), `tests/SheetTests.cs` (new), `tests/FileBasedAppTests.cs`, `tests/TestMeshes.cs` (`ExamplePartText`, `CompiledModelIsTheExample`, `ExampleFact`) — the tests above.
- `skills/3dpart-as-code/SKILL.md`; `references/step1-part.md`, `step5-render-coupons.md`, `step6-export.md`, `reproduce-mode.md`, `kickoff.md`, `project-structure.md`, `dotnet-conventions.md`; `README.md` — docs.

## Acceptance criteria

- The whole suite is green with live pipeline tests — verify: `dotnet test skills/3dpart-as-code/tests` with `PAC_PYTHON` set to a build123d venv; 0 failed.
- The suite is green without Python (live tests early-return; the non-live pull test still runs) — verify: `dotnet test skills/3dpart-as-code/tests` with `PAC_PYTHON` unset.
- On a scratch project copied from the templates, the full loop works:
  1. build, check, fit, render, sheet, then edit `bracket.w` in Excel;
  2. export refuses;
  3. pull changes exactly one line of `part.cs`;
  4. the rerun passes every gate;
  5. export succeeds.

  Verify: run the commands; `git diff --no-index` the `part.cs` before and after the pull.
- Opening the generated `params.xlsx` in Excel shows the renders on Views, the gate counts on Status, `hole.d` greyed with `derived: pin.d + 2 × slip`, and refuses typing into a locked cell — verify: open it.
- With `params.xlsx` open in Excel, `sheet.cs` prints the "close it" message and exits 1 without a stack trace, and `sheet.cs -- pull` still reads the saved content — verify: run both with the file open.

## Test strategy

- `PartEditTests` — every Locate result, literal formatting, byte-identical rewrite, named-argument replace/append/escape.
- `SheetTests` — workbook layout and editability, edit detection including the three-way cases, every Read problem, the median and spread rules, the locked-file path.
- `FileBasedAppTests` — `sheet.cs -- pull` through `dotnet run` (non-live); generate → refuse-regenerate → export-refuse → pull → rebuild → export through the real pipeline (live).
- `ModelTests` (existing) — the example still validates, and `hole.d` derives from the declared slip fit.
- Excluded: what the sheet looks like in Excel (fills, image layout, protection UX) — checked by eye under Acceptance; ClosedXML's own rendering is not unit-tested beyond the cells and pictures being present.
- Excluded: reproduce mode end to end through the live pipeline — the example is design mode; the Measurements sheet is covered by `SheetTests` on a `TestMeshes.Model(Mode.Reproduce, ...)`.

## Open questions

