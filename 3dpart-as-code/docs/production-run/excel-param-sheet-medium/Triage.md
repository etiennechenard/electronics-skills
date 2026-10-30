# Triage — excel-param-sheet

## Owner checks

- Acceptance, Excel look: open `mech/params.xlsx` (from `sheet.cs` on the example) in Excel and judge the renders on Views, the gate counts on Status, `hole.d` greyed with `derived: pin.d + 2 × slip`, and that typing into a locked cell is refused. Automation verified: cells, lock flags, lock reasons, embedded pictures and protection flags through ClosedXML (SheetTests). Capture: none, not machine-verified.
- Acceptance, file open in Excel: with `params.xlsx` really open in Excel, `sheet.cs` prints the "close it" message and exits 1, and `sheet.cs -- pull` still reads the saved content. Automation verified: the same two behaviours with a `FileShare.None` / `FileShare.Read` handle (SheetTests), not Excel's own lock.

## Review pass (2026-10-29)

Four Haiku shards (3 slices + seam) over a 2187-line diff; 31 findings, 26 distinct after merging duplicates (S1.F1=S4.F1, S1.F3=S4.F4, S2.F8=S4.F6, S3.F3=S4.F7, S2.F1=S4.F3). Opus triage:

**Will fix**
- S1.F1=S4.F1 (bug): two Measurements rows naming one parameter, or a measurement plus an already-applied Parameters edit, are not rejected as double-edited. Test first, then paramsheet.cs ReadMeasurements: a claimed-names set, problem "measured twice"; the "also edited on Parameters" check covers every Parameters row that differs from Was.
- S1.F3=S4.F4 (bug): SetArgument appends a duplicate named argument when Source/Ref are positional. Test first, then partedit.cs SetArgument gains `position`; paramsheet.cs passes 3 (Source) and 6 (Ref). Blueprint Step 2 "Set named argument" bullet amended.
- S1.F5 (bug): number format `0.####` shows "40."; use `0.0###` (paramsheet.cs:136, :213), test the format first.
- S1.F6 (bug): params.tmp.xlsx left behind on a SaveAs failure; outer try/finally deletes it; kickoff.md ignore list adds `mech/params.tmp.xlsx`.
- S1.F7 (comment-noise): delete the doc line above `Input` in paramsheet.cs.
- S2.F1=S4.F3 (bug): tests pair the compiled `PartDesign.Model` with the fixture text; add `TestMeshes.CompiledModelIsTheExample` and return early in those tests when false.
- S2.F2 (bug, test): `Read_matchesRowsByNameNotPosition` renames to a duplicate name; rename to `base.d`, drop the repeated line, assert no problems.
- S2.F3 (coverage, partly): test callouts below the Measurements table and the Status model/mode/UTC/how-to cells.
- S2.F4 (coverage): test an equal median with a non-Measured Source yields one edit setting Source Measured and Ref.
- S2.F5 (divergence): amend Blueprint Step 4 and Step 5 test bullets to the separate live test.
- S2.F6 (divergence, Files to change only): amend Blueprint "Files to change" to name TestMeshes.cs.
- S2.F7 (bug, doc comment): partedit.cs Locate doc says any target-typed `new(` counts.
- S2.F8=S4.F6 (bug): Format loses digits for tiny values; test first, then shift the "R" mantissa instead of the F20 fallback.
- S3.F1 (bug): a conflict is refused by export and pull alike; the message says "delete params.xlsx and regenerate it"; step6-export.md and Blueprint Step 3 conflict bullet amended.
- S3.F2 (divergence): remove the `Fits.Defaults ... only what a model gets` clause in step1-part.md.
- S3.F3=S4.F7 + S3.F8 (divergence + doc bug): step5-render-coupons.md editable-cell paragraph rewritten (readings editable, lock-reason parenthetical, measurement-pull exception, double-edit refused).
- S3.F4 + S3.F7 (regression + doc): reproduce-mode.md restores "median of the three readings" for the no-sheet path and shows the one-reading Ref.
- S3.F6 (regression): dotnet-conventions.md lists PartEdit and ParamSheet and their needs.
- S4.F2 (divergence): export.cs prints SHEET lines through an Ascii helper; non-live test first.
- S4.F5 (coverage): tests for "sheet Fits is missing", "row appears twice", "sheet Measurements is missing".

**Dismissed**
- S1.F2: follows the blueprint's "Measurements rows have no Was"; the design gap is parked under Deferred.
- S1.F4: Blueprint Step 3 shows a missing render group as a text line; Step 4 ties the refusal to the same set as export (empty folder only).
- S2.F3 (Views order, stale refusal): Test strategy excludes image layout; the stale refusal runs through the shared Gates.Stale path.
- S2.F6 (search order): both orders resolve to the same file in every supported layout.
- S3.F5: README describes the agent workflow, same wording as the blueprint Task; pull's output says to rebuild.
- S4.F5 (Fit conflict): same ReadRows code the Param conflict test covers.
- S4.F8: the part.cs comment states a constraint for whoever copies the template.

**Main-blueprint sync**
- Main blueprint absent (3dpart-as-code/docs/Blueprint.md does not exist) - no sync.

**Deferred**
- S1.F2 (readings left in a sheet after a pull, then part.cs changed before the sheet is regenerated: pull would write the old median back over the newer value, and generate/export refuse with "not pulled"). Owner options: (a) treat the Measurements Current column as that row's Was so this is a conflict like Parameters, and amend the Approach line "Measurements rows have no Was"; (b) keep the blueprint's rule and document in reproduce-mode.md that readings left in the sheet win over a later part.cs edit until the sheet is regenerated.

Applied: all Will-fix items (code, tests, docs, blueprint amendments). The F6 temp-file cleanup has no dedicated failure-path test (a SaveAs failure cannot be triggered portably); the existing locked-file test asserts the temp file is gone. Tests using the compiled model skip (ExampleFact) when a project's own part.cs replaced the worked example.

Smoke: dotnet test skills/3dpart-as-code/tests passes 106/106 with PAC_PYTHON=C:	oolsuild123d-venv (live pipeline: build, check, fit, render, sheet, pull, export all compile and run) and 106/106 without it.

## After the run (2026-10-29)

- Owner check "Excel look" done: the owner opened the generated sheet and approved it.
- Added at the owner's request, without a blueprint: `Model.Annotations` (a parameter's line or point on a body, length-checked by `Validate`), `render.cs` numbered dimension views `<body>-dims-*.png` (hidden or end-on lines left out, labels scaled to stay legible), and on the Parameters sheet a leading `#` column with those views under the table.
- Deferred S1.F2 settled by the owner: option (a). A Measurements row uses its Current column as Was; readings left after a pull, with part.cs changed since to another value, are a conflict. Test: `Read_readingsLeftAfterAPull_andPartCsChangedSince_areAConflict`.
- Owner check "file open in Excel" done, with Excel itself holding `params.xlsx`: `sheet.cs` printed "params.xlsx is open (close it in Excel and re-run)" and exited 1; `sheet.cs -- pull` read the saved content (the owner's `bracket.w` 40 -> 42, saved with the file still open), rewrote part.cs and exited 0.
