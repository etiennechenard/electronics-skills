# Reproduce mode — copying a part that exists

The agent cannot measure. Reproducing a part is a loop between the agent,
which models, renders and asks, and the user, who measures. `Mode =
Mode.Reproduce` in part.cs switches on the rules that keep it honest.

## 1. Look for existing data first

Before anything is measured, search for the maker's own geometry:

- a **STEP/IGES model** (maker's site, distributor pages, TraceParts,
  GrabCAD) — import it as a reference body, or copy its dimensions;
- a **dimensioned drawing** (datasheet, mechanical drawing, patent
  figure) — each number becomes a `Datasheet` parameter with the document
  and page as `Ref`.

Only what no document gives is measured.

## 2. Intake

Ask for:
- photos from every side, square-on where possible, with a ruler or
  calipers in frame, plus close-ups of every mating feature;
- what the part mates with, and which surfaces matter (the user knows
  which hole is the critical one);
- for a **flat-ish part**: a flatbed scan (300 or 600 dpi, part on the
  glass, lid open or a white sheet behind). A scan is true scale: the agent
  can trace outlines and curves from it at a known mm-per-pixel, which
  beats calipers on curves. Ask for a ruler in the scan to confirm the
  scale;
- for a **broken part**: every piece. Symmetry and the mating parts give
  the missing geometry; mark those parameters `Derived` with the reasoning
  in `Ref`.

## 3. Feature breakdown and the sheet

Break the part into features (plate, boss, slot, rib, chamfer…) and decide,
with the user, which are **critical** (they mate or carry load) and which
are cosmetic. Then write the model:

- a `Param` per dimension, `Chosen` with a placeholder value from the
  photos, `Critical: true` where it matters, a tolerance from the function;
- a `Measurement` per dimension to measure: id (`D1`, `D2`…), what to
  measure in words ("bore diameter, the one nearest the tab"), the
  parameter it feeds, the body, and `At` — a point on the feature in
  assembly coordinates, where its numbered marker is drawn.

Build and render: `render.cs` writes `<body>-callouts-*.png`, the part
with a red numbered marker on every feature to measure. Put
`Model.SheetMarkdown()` (the measurement table) in `docs/mech.md` and send
the user the callout renders plus the table.

## 4. Measuring

Ask the user to measure each critical dimension three times and give the
median; calipers, not a ruler, for anything critical; inside jaws for
holes and slots, depth rod for depths; note anything worn, deformed or
broken. Transcribe each value into its parameter: `Source: Measured`,
`Ref: "D3"`. Cosmetic dimensions may stay estimated from the photos or
scan (`Chosen`, not critical).

The DIM gate's source rule fails every critical parameter that is still
`Chosen` — the build cannot pass while a critical number is a guess.

## 5. Model, print a coupon, compare

Model from the measured values, gates green, then print **coupons** of the
critical interfaces first (`step5-render-coupons.md`) and test them on the
real mating parts. Measure the printed coupon too: the difference between
the measured original and the printed copy tunes the named fits. Rebuild,
re-sheet, and only then print the whole part.

## The rules this mode adds

- No critical parameter `Chosen` (DIM gate).
- `Measured`/`Datasheet` need a `Ref`; `Derived` needs its formula (DIM gate).
- Measurements are only allowed in reproduce mode (`Model.Validate`).
- The sheet in `docs/mech.md` is regenerated from part.cs, never edited by
  hand — part.cs is the record.
