# Step 4 — fit: clearances and insertion paths

`dotnet run mech/tools/fit.cs` runs `pac.py fit` on the built STEPs and
gates `build/fit.json`.

## What is measured

- **Clearance** (`FitCheck`): the overlap volume of the two bodies, then
  their minimum surface distance. Overlap > 0.001 mm³ or a gap under
  `MinGap` is a problem.
- **Sweep** (`SweepCheck`): the moving body is placed at `Steps + 1` points
  along the straight path from `position + From` to its position; at each
  point against each `Against` body, overlap then distance. The worst point
  is reported with where it sits on the path (0 = start, 1 = home).

The overlap comes first because `distance_to` measures surface to surface:
a body buried entirely inside another reads a positive distance.

## Reference bodies

Mating parts that are not printed are `BodyKind.Reference` bodies, built by
a script like any other:

- **A board**: `pac.import_step("../pcb/build/controller.step")` from
  `kicad-cli pcb export step` (`pcb-bridge.md`), or a plain plate from its
  outline when only the edges matter.
- **A bought part**: its maker's STEP, or a simple solid from its datasheet
  drawing (`Datasheet` parameters).
- **A keep-out**: a box or cylinder for space that must stay free — a
  connector's mating plug, a finger, a cable bend radius, a part's swing.

Imported files go into part.cs's `Inputs`, so a changed board makes the
build stale.

## Choosing the gaps

`MinGap` is the fit you designed for, minus the geometry's own slack: the
example's pin has a 0.20 mm slip gap per side and checks ≥ 0.15. For a
part that must merely not collide (a board in a case), use the loose fit
or a few tenths. A sweep's gap may be smaller than the seated clearance
when the path is guided.

## Sweeps are paths, not magic

A sweep is a straight line. A part that tilts in, or goes in two moves,
takes two sweeps (one per segment: the second starts where the first
ends), or a keep-out body that covers the swing. Cost grows with
`Steps` × bodies × solid complexity; 24 steps is plenty for a smooth path,
more only near a tight spot.
