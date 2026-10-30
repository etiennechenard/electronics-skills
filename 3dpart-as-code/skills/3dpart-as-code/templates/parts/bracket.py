"""The worked example's geometry: an L-bracket, its slip-fit pin and a coupon
of the pin's hole. Every number comes from part.cs through params.json."""

import pac
from build123d import Axis, Cylinder, GeomType, Pos, Rot, Box, chamfer, fillet

P = pac.load()
w, d, t = P["bracket.w"], P["base.d"], P["base.t"]
h, ut = P["upright.h"], P["upright.t"]
hole_d, hole_y = P["hole.d"], P["hole.y"]
sd, sx, sz = P["screw.d"], P["screw.x"], P["screw.z"]

# base on z = 0, upright at y = 0..ut, x centred
bracket = Pos(0, d / 2, t / 2) * Box(w, d, t) + Pos(0, ut / 2, h / 2) * Box(w, ut, h)
corner = bracket.edges().filter_by(Axis.X).filter_by(
    lambda e: abs(e.center().Y - ut) < 1e-6 and abs(e.center().Z - t) < 1e-6)
bracket = fillet(corner, radius=P["fillet.r"])
bracket -= Pos(0, hole_y, t / 2) * Cylinder(hole_d / 2, t)
for x in (-sx, sx):
    bracket -= Pos(x, ut / 2, sz) * Rot(90, 0, 0) * Cylinder(sd / 2, ut)

# the pin, in place: through the hole, centred on the base's mid-plane
pin = Pos(0, hole_y, t / 2) * Cylinder(P["pin.d"] / 2, P["pin.l"])
pin = chamfer(pin.edges().filter_by(GeomType.CIRCLE), length=P["pin.chamfer"])

pac.body("bracket", bracket)
pac.body("pin", pin)
m = P["coupon.margin"]
pac.coupon("hole-coupon", bracket,
           lo=(-hole_d / 2 - m, hole_y - hole_d / 2 - m, -1),
           hi=(hole_d / 2 + m, hole_y + hole_d / 2 + m, t + 1))

pac.probe_cyl("bracket.hole.d", bracket, near=(0, hole_y, t / 2))
pac.probe_cyl("bracket.screw.d", bracket, near=(sx, ut / 2, sz))
pac.probe_cyl("pin.d", pin, near=(0, hole_y, t / 2))
pac.probe_extent("bracket.base.t", bracket, "z", lo=(-w / 2 + 1, d - 6, -1), hi=(-w / 2 + 6, d - 1, h + 1))
pac.finish()
