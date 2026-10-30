# Step 2 — geometry: build123d scripts that hold no numbers

`mech/parts/<name>.py`, run by `dotnet run mech/tools/build.cs`. Read the
example (`parts/bracket.py`) first — it uses `body`, `coupon`, `probe_cyl`,
`probe_extent` and `finish`; `probe`, `region` and `import_step` are for
hand-rolled probes, your own booleans and reference bodies.

## The script's shape

```python
"""<what it builds>."""
# Frame: <origin and axes in assembly coordinates>
import pac
from build123d import Box, Cylinder, Pos, Rot, fillet, chamfer, Axis, GeomType

P = pac.load()                      # params.json; P["name"], P.fit("slip")
w, t = P["plate.w"], P["plate.t"]   # unpack once, at the top

plate = Pos(0, 0, t / 2) * Box(w, P["plate.d"], t)
plate -= Pos(0, 0, t / 2) * Cylinder(P["hole.d"] / 2, t)

pac.body("plate", plate)                                   # name from part.cs
pac.probe_cyl("plate.hole.d", plate, near=(0, 0, t / 2))   # measured off the solid
pac.finish()                                               # validate, export, write probes
```

- **Assembly coordinates.** Every body is built where it sits in the
  assembly, so the fit stage can check bodies against each other. pac turns
  printed bodies into their print pose for the STL.
- **Algebra mode** (`+`, `-`, `&`, `Pos(...) *`, `Rot(...) *`) reads closest
  to a drawing; builder mode (`with BuildPart()`) is fine where a sketch +
  extrude is clearer. One style per script.
- **Primitives are centred**: `Box(w, d, t)` spans ±w/2. Offsets come from
  part.cs numbers (`Pos(0, d / 2, t / 2)`).
- **Select edges by geometry**, not by index: `edges().filter_by(Axis.X)`,
  `filter_by(GeomType.CIRCLE)`, or a predicate on `e.center()`. Indices
  change when an upstream feature changes.
- **Fillets and chamfers last**, after the booleans that define the edges
  they round; a failing fillet usually means its radius exceeds a wall from
  part.cs — fix the numbers, not the script.

## pac's API

| Call | Does |
|---|---|
| `pac.load()` | parse `--params/--out/--script`, return `Params` |
| `P["name"]`, `P.param("name")`, `P.fit("slip")`, `P.envelope` | read part.cs |
| `pac.body(name, shape)` | register a body declared in part.cs for this script |
| `pac.probe(name, value)` | record a measured feature |
| `pac.probe_cyl(name, shape, near)` | diameter of the cylindrical face whose axis passes nearest `near` |
| `pac.probe_extent(name, shape, axis, lo, hi)` | extent along `axis` of the material inside the box lo..hi |
| `pac.region(lo, hi)` | that box, for your own booleans |
| `pac.coupon(name, shape, lo, hi)` | register the slice of `shape` inside lo..hi as a coupon body |
| `pac.import_step(path)` | a reference body from a STEP file |
| `pac.finish()` | validate, export, write `probe-<script>.json` |

`finish()` refuses: a declared body never registered, a printed body that
is not exactly one solid, an invalid solid. It writes `<body>.step` and
`asm/<body>.stl` (assembly coordinates, every body), `<body>.stl` (print
pose, printed bodies), and the probes plus `<body>.volume` and
`<body>.bbox.x|y|z`.

## Probing

A probe measures the built solid; part.cs holds the value it must have.
That is what catches a sign error in a `Pos`, a fillet that ate a wall, or
a boolean that silently did nothing.

- Holes, bosses, pins: `probe_cyl` near the feature's axis.
- Walls, floors, slot depths and widths: `probe_extent` in a small box
  around one spot (keep the box inside the feature, away from fillets).
- Overall size: the automatic bbox probes.
- Hand-rolled: anything else build123d can measure — a face's area, the
  distance between two faces — through `pac.probe`.

## Gate

`dotnet run mech/tools/build.cs` → `BUILD PROBLEMS: 0`. A Python error
prints its traceback (the last lines name the script's failing line). The
build always starts from an empty `build/` (params.json kept), so nothing
from a removed body survives.
