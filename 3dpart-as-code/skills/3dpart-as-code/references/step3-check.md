# Step 3 — check: dimensions and meshes

`dotnet run mech/tools/check.cs` — refuses a stale build (`STALE
PROBLEMS`), then two gates.

## DIM

Every `ExpectedDim` against its probe: missing probes and values outside
nominal ± tol are problems. In reproduce mode the source rule joins in:
a critical parameter that is `Chosen`, or `Measured`/`Datasheet`/`Derived`
without a `Ref`, is a problem (`reproduce-mode.md`).

A DIM failure is fixed in part.cs or the script — never by widening the
tolerance until it passes. The tolerance says what the part tolerates;
the geometry must honour it.

## MESH

Every printed body's STL (print pose):

| Problem | Means | Usual cause |
|---|---|---|
| open edges | not watertight | an export of a non-solid (a face, a shell) |
| non-manifold edges | more than two faces on an edge | two solids touching along an edge, fused badly |
| edges wound opposite ways | inconsistent normals | a mirrored body not re-oriented |
| N shells | more than one body in one STL | a boolean left a floating sliver; a split that did not separate |
| degenerate triangles | zero area | a feature thinner than the tessellation |
| volume not positive | inside-out | the same as wound-opposite, everywhere |
| does not fit the envelope | too big for the printer, as is or turned 90° about z | split the body (part.cs), or change `Up` |
| tessellation lost material | mesh volume ≠ solid volume by > 1 % | `Tolerance` too coarse for small features |

pac validates solids before export, so on a healthy pipeline these stay at
zero; the gate exists because the slicer only ever sees the STL.

## The summary line

```
lid: 18432 triangles, 1 shell, 120 x 80 x 25 mm up -Z, 61.2 cm3, overhang beyond 45 deg: 312 mm2
```

Overhang is reported, never gated: support is the slicer's decision. But
read it — a large overhang on a functional surface usually means `Up`
should change (and the note in part.cs says why it did not).

Printability beyond what the gates measure (wall thickness against the
nozzle, unsupported bridges, a snap arm's strain, layer direction against
load) is judged on the renders and in the review (`3dpart-review`).
