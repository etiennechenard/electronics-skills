# Step 6 — export

`dotnet run mech/tools/export.cs` writes `mech/out/`:

| File | For |
|---|---|
| `<body>.stl` | the slicer: print pose, on z = 0, one per part and coupon |
| `<body>.step` | CAD (Fusion 360): assembly coordinates, one per part and coupon |
| `assembly.step` | every body in place, references included |
| `renders/*.png` | the review views |
| `manifest.json` | per file: sha256, bytes; per STL: body, kind, up, size, volume |

## It refuses

- anything stale: a build output, `fit.json` or a render older than an
  input (part.cs and its libraries, pac.py, the scripts,
  requirements.txt, the model's `Inputs`), or a params.json that differs
  from what part.cs writes now;
- missing renders;
- a `mech/params.xlsx` holding edits never pulled into part.cs
  (`SHEET <name>: sheet value <v> not pulled`): run `sheet.cs -- pull`; or
  holding a sheet problem (`SHEET <problem>`, for example a conflict because
  part.cs changed since the sheet was made), which pull refuses too: fix the
  cell, or delete the sheet to discard it;
- any DIM, MESH or FIT problem, re-checked from the build's own files.

It prints `EXPORT REFUSED` with the list. Fix, then re-run the chain:
build → check → fit → render → export.

`out/` is replaced whole on every export and committed: it is the
deliverable. Units are millimetres throughout; Cura and every common slicer
read STL as mm.
