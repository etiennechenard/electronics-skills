# Step 5 — renders and coupons

## Renders

`dotnet run mech/tools/render.cs` writes `build/renders/`:

| File | Shows |
|---|---|
| `<body>-iso/front/top/right.png` | each printed body in print pose — what the slicer will see, bed at the bottom |
| `assembly-iso/isoback/front/top.png` | every non-coupon body in place, references included |
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
part.cs (never one feature); rebuild; record the result in `docs/mech.md`
under Fits. Only then print the full part.
