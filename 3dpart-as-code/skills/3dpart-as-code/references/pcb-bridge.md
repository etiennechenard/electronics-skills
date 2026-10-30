# pcb-bridge — parts that mate with a pcb-as-code board

An enclosure, a bracket or a front bezel for a pcb-as-code project takes
its interface numbers from the board, never retyped: when the board moves
a connector, the case moves its cutout on the next build, and the stale
check forces that build.

## Reading the board in part.cs (C#)

The project's `pcb/tools/` already has the board reader. Include it by
relative path in every consumer of part.cs — each entry tool and the test
csproj — rather than copying it (part.cs then also needs `mech.cs`, which
every consumer already includes):

```csharp
#:include ../../pcb/tools/sexpr.cs
#:include ../../pcb/tools/kicad.cs
#:include ../../pcb/tools/boardfile.cs
```

(in the csproj: `<Compile Include="..\..\..\pcb\tools\sexpr.cs" />` and
the same for the other two). Then, in part.cs:

```csharp
var root = Mech.Layout.Here([]).Root;                 // mech/, from a tool or from the tests
var board = PcbFile.Board.Load(Path.Combine(root, "../pcb/controller.kicad_pcb"));
var edge = board.EdgeContours();                      // outlines, nm, KiCad page coords
var holes = board.Footprints.SelectMany(f => f.Pads)
    .Where(p => p.Type is "thru_hole" or "np_thru_hole" && f.Ref.StartsWith("H"));
```

- **Units and axes.** boardfile works in nm integers with **y down** in
  KiCad page coordinates. Convert to mm (`/ 1e6`), flip y, and re-centre on
  the board (the outline's bounding-box centre) or on whatever datum the
  case uses. Do this once, in a helper in part.cs, with a test.
- **Make the numbers parameters**: `new Param("board.w", w, Source:
  Source.Derived, Ref: "controller.kicad_pcb Edge.Cuts")`, mounting holes,
  connector centres. Scripts never read the board.
- **Inputs**: list the board files in `Model.Inputs`
  (`"../pcb/controller.kicad_pcb"`): an edited board makes the build stale.
- Where pcb-as-code's `design.cs` holds the number (a slot width, a panel
  constant in a library file), include it instead and use the constant —
  the design wins over its outputs. A constant that only lives in an entry
  file (`gen_*.cs`) cannot be included: move it to a library file in the
  pcb project first.

## The board as a reference body (fit)

```
kicad-cli pcb export step --subst-models --min-distance 0.01mm -f -o pcb/build/controller.step pcb/controller.kicad_pcb
```

- `--board-only` gives the bare FR4 (edges, slots, holes); without it the
  parts' 3D models come too — the tall ones are what collide with a lid.
  Footprints without a 3D model export nothing: cover them with keep-out
  boxes from their datasheet heights.
- Run it from the pcb project's export step (or by hand) and list the STEP
  in `Model.Inputs`. The script imports it as a `Reference` body
  (`pac.import_step`) and places it from part.cs numbers (KiCad's STEP
  origin is the page origin unless `--grid-origin`/`--drill-origin`/
  `--user-origin` is given: pick one and keep it).
- Typical checks: the board against every case body (`FitCheck`, the
  loose fit), the board's slide into its slots (`SweepCheck`), connector
  mates (a keep-out the size of the mating plug, in front of the cutout,
  against the case).

## Panels that are boards

When PCBs replace an enclosure's end panels, the case's interface is the
panel's outline and screw holes: read them from the panel boards' Edge.Cuts
and holes, give the case a seat (rabbet or ledge) per panel and a boss per
screw, and sweep the boards that slide in past those bosses.
