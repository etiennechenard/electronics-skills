# Step 1 — part.cs: every number, once

`mech/tools/part.cs` builds one `Model` (`Part.PartDesign.Model`). The
geometry scripts read it as `build/params.json`; the gates read it
directly. Copy the example's shape and replace its content.

## Parameters

```csharp
new Param("hole.d", pinD.Value + 2 * slip.Gap, Source: Source.Derived, Tol: 0.05,
          Critical: true, Ref: "pin.d + 2 x slip", Note: "the pin's hole")
```

- **Names** are dotted, body or feature first (`lid.wall.t`, `slot.w`). The
  scripts index by them (`P["slot.w"]`).
- **Unit** is mm unless stated (`"deg"` for angles).
- **Source:** `Chosen` (a design decision), `Derived` (computed from other
  parameters — compute it in C#, never in the script, and put the formula
  in `Ref`), `Measured` (calipers; `Ref` = the measurement id), `Datasheet`
  (a maker drawing or STEP; `Ref` = the document and page).
- **Tol** is what the printed part may deviate before it stops working.
  **Critical** marks the numbers the part's function hangs on — a critical
  parameter needs a tolerance, and an `ExpectedDim` that probes it (the
  model test checks this).
- **Editable from the parameter sheet** (`sheet.cs`, step 5) means one literal per declaration:
  `new("x", 4.0, ...)`, never an expression or a shared variable. Names stay
  unique across parameters *and* fits. A derived number is computed in C#
  and is locked on the sheet (with its `Ref` as the reason).
- Derive in C#: a hole from its pin and fit, a wall from its screw boss, a
  lid from the box. A number typed twice is a number that drifts.

## Fits

Press 0.05, snug 0.10, slip 0.20, loose 0.35 mm, gap per side, are FDM
starting points. Declare them in `part.cs` as `Fits = [...]` with one
literal gap per `new("slip", 0.20, "note")` (the example does) so they can
be tuned from the parameter sheet. Every mating pair uses a named fit, never a bare
gap. Once a coupon is printed (step 5), put the tuned values in and record
the coupon result in `docs/mech.md`. Add project fits by name
(`new Fit("insert", ...)`).

## Bodies

```csharp
new Body("lid", "parts/case.py", Up: "-Z", Note: "printed upside down: the top face on the bed")
```

- One script may produce several bodies; each body is one file name
  (letters, digits, `-`, `_`).
- **Up** is the part axis that points up on the printer. Choose it for the
  strength direction (layers are weakest in tension across them), the
  surfaces that matter (the bed face is flat and smooth), and the
  overhangs. Write the reason in `Note`.
- **Kind**: `Part` (printed), `Coupon` (a printed test slice, step 5),
  `Reference` (a mating board, a bought part, a keep-out volume — fit-checked,
  never printed).
- **Color** (`"#c0c0c8"`): the body's colour in the renders; unset, the
  palette picks one. Give bought parts a recognisable colour (a switch
  silver, a battery black).

## Boards (pcb-as-code projects)

```csharp
Boards =
[
    new("ctl-pcb", "../pcb/controller.kicad_pcb", Origin: (150, 100), X: "+X", Y: "+Y", At: (0, 0, ctlZ),
        Replaces: ["controller", "controller-parts"]),
]
```

A board shown in place, with its parts' 3D models, in every assembly
render and in `assembly.step` (`pcb-bridge.md`). Display only: fits stay on
keep-out `Reference` bodies, which `Replaces` hides from the renders.
- **Origin**: the KiCad page point (mm) that becomes the board's zero, its
  design centre (pcb-as-code's page offset; read the constant, never retype it).
- **X, Y**: the assembly axes the board's +x and its up (KiCad −y) point
  along. Its F face looks along X × Y: a flat board with parts up is
  `"+X", "+Y"`; a front panel seen from the front with F outside is
  `"-X", "+Z"` (F along +Y).
- **At**: where Origin lands, on the board's B face.
- The `.kicad_pcb` is a build input: an edited board makes the build stale.

## Assembly views

```csharp
Views =
[
    new("open", Hide: ["lid"]),
    new("cutaway", Cut: ("lid", "+X", 0)),            // the lid's X >= 0 half, cut faces capped
    new("exploded", Move: ("lid", (0, 0, 120))),
]
```

Extra assembly renders for seeing inside a closed part (`assembly-<view>-
iso.png`, `-isoback.png`): hide bodies or boards, cut one body at an axis
plane, move one body. Declare them from the start for any enclosure: the
reviewers read them too.

## Expected dimensions

`ExpectedDim.Of(param, "<body>.<feature>", tol)` binds a probe to its
parameter. Every critical parameter gets one; so do the overall sizes
(`<body>.bbox.x|y|z`, automatic) and every mating feature. The tolerance
here is the geometry's (0.01 mm for exact CAD): the print tolerance lives
in the parameter and the fits.

## Clearances and sweeps

- `new FitCheck("pin", "bracket", minGap)` — in assembly position, no
  overlap and at least minGap between the two.
- `new SweepCheck("board", (0, 120, 0), ["case-left", "case-right"], minGap)`
  — the body travels from its position offset by `From` to its assembly
  position, staying minGap clear of every `Against` body. One per insertion
  path from the interrogation.

## Annotations

`Annotations = [...]` says where each parameter sits on a body, in assembly
coordinates computed from the parameters themselves: `new("bracket.w",
"bracket", (-w / 2, -3, h + 3), (w / 2, -3, h + 3))` is a dimension line
(its length must equal the value — `Validate` refuses one that does not),
`new("fillet.r", "bracket", (x, y, z))` a labelled point. Set lines a few mm
off the faces, and give parallel dimensions different offsets so they do
not stack. render.cs draws them as `<body>-dims-*.png`, numbered by the
parameter's position in `Params`; the parameter sheet shows that number in
its `#` column beside the views. Annotate every number the user may want to
tune; a parameter with none shows no `#`.

## Envelope and the rest

- `Envelope = new(x, y, z)` — the user's printer.
- `Inputs = ["../pcb/controller.kicad_pcb"]` — files outside tools/ and
  parts/ the model or a script reads; an output older than one is stale.
  The boards' files are added without listing them.
- `Tolerance` / `AngularTolerance` — STL chord error (default 0.01 mm) and
  angle (0.1 rad). Coarser for very large parts if the STL gets huge.

## Gate

`Model.Validate()` empty (build.cs refuses otherwise) and
`dotnet test mech/tools/tests` green. Add the project's own invariants to
`ModelTests` — a lid wider than its box by exactly two walls, a board
thinner than its slot by the slip fit.
