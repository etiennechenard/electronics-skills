# Step 5 — renders and coupons

## Renders

`dotnet run mech/tools/render.cs` writes `build/renders/`:

| File | Shows |
|---|---|
| `<body>-iso/front/top/right.png` | each printed body in print pose — what the slicer will see, bed at the bottom |
| `assembly-iso/isoback/front/top.png` | every non-coupon body in place, references included |
| `<body>-dims-*.png` | each annotated body with its parameters as numbered dimension lines (the number is the parameter sheet's `#`); a line hidden behind the part or seen end-on is left out of that view |
| `<body>-callouts-*.png` | reproduce mode: the body with its numbered measurement markers |

Views are orthographic: Front looks along +y, Top looks down −z, Iso is
from front-right-top, IsoBack from back-left-top. Each carries a scale bar
(screen-plane length) and outlines at silhouettes, depth steps and creases.

**Read them** (the Read tool shows images) after every geometry change:
a feature on the wrong face, a missing hole, a part floating off the bed in
print pose, a body intersecting its reference — the renders show what no
probe was written for. The review sweep reads them too.

For a closer look, open `build/<body>.step` or `build/assembly.step` in
Fusion 360 (or any CAD viewer). Look only — changes there are lost.

## The parameter sheet

`dotnet run mech/tools/sheet.cs` (after render) writes `mech/params.xlsx`, a
generated, gitignored front-end to part.cs — the user tunes numbers beside
the renders instead of describing them in prose.

| Sheet | Holds |
|---|---|
| Parameters | `#` (the number on the labelled views), then every parameter: value, unit, tolerance, critical, source, ref, note, and why a cell is locked; under the table, the `<body>-dims-*` views (the plain views when the model has no annotations) |
| Fits | the named fits' gaps |
| Measurements | reproduce mode only: the measurement table with readings R1-R3, the callout renders below it |
| Views | the renders: each printed body's print pose, then the assembly |
| Status | model, mode, generation time, the DIM / MESH / FIT problems, how to use the sheet |

A **highlighted** cell is editable: the Value of a `Chosen` or `Measured`
parameter, or a fit's Gap, that part.cs declares as one literal
(`new("x", 4.0, ...)`), and in reproduce mode the R1-R3 readings of a
measurement whose parameter is editable. Every other value is grey and
locked, with the reason in the Lock column: `derived: <formula>`,
`datasheet`, or `not a single literal in part.cs (<why>)` where `<why>` is
`no declaration`, `declared n times` or `not a numeric literal`. Tolerance,
critical, source, ref and note are never edited from the sheet — that stays
an edit to part.cs — except that a measurement pull sets `Source: Measured`
and its `Ref` (see `reproduce-mode.md`). A parameter edited on Parameters and
also through a measurement is a problem.

**The loop:** generate -> the user edits and saves, then **closes Excel** ->
`dotnet run mech/tools/sheet.cs -- pull` (prints one `name: old -> new` line
per change, then `PULL PROBLEMS: 0` and `rebuild: build, check, fit, render,
sheet`) -> rebuild. A pull is all-or-nothing: a value that is not a number, a
locked cell, or a number part.cs changed since the sheet was generated is a
problem and nothing is written. `sheet.cs` refuses to regenerate over edits
not yet pulled, and `export.cs` refuses them too. Coupon results are typed
into the Fits sheet (the tuned gaps) and recorded in `docs/mech.md`.

## Coupons

A coupon is a small printed slice of a critical feature: the slot a board
slides in, a press-fit bore, a snap hook, a screw boss with its insert. It
takes minutes and a few grams instead of hours, and it is what tunes the
fits.

- Declare it in part.cs: `new Body("slot-coupon", "parts/case.py", "+Z",
  BodyKind.Coupon, "20 mm of the left wall around the board slot")`.
- Cut it in the script from the real body, so it can never drift from it:
  `pac.coupon("slot-coupon", wall, lo=(...), hi=(...))` — lo/hi from part.cs
  numbers, the region chosen so the slice is one solid that stands on the
  bed.
- Coupons go through the mesh gate and export like parts.

**The coupon loop:** print the coupons first; test them against the real
mating part (the board, the screw, the pin); adjust the named fit in
part.cs (never one feature) — or in the Gap cell of the Fits sheet, then
`sheet.cs -- pull`; rebuild; record the result in `docs/mech.md` under Fits. Only then print the full part.
