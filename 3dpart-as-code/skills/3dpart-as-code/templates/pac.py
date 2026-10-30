"""pac: the 3dpart-as-code helper for build123d geometry scripts.

A geometry script holds no numbers of its own: it reads them from
params.json (written by build.cs from part.cs), builds its bodies in
assembly coordinates, registers each one, probes the features part.cs
expects, and calls finish():

    import pac
    from build123d import *

    P = pac.load()
    plate = Box(P["plate.w"], P["plate.d"], P["plate.t"])
    pac.body("plate", plate)
    pac.probe_extent("plate.t", plate, "z", lo=(-1, -1, -9), hi=(1, 1, 9))
    pac.finish()

finish() validates each body (one valid closed solid per printed body),
then writes into the build directory:
  <body>.step        assembly coordinates, every body (fit checks, Fusion)
  asm/<body>.stl     assembly coordinates, every body (assembly renders)
  <body>.stl         print orientation (the body's `up` axis to +Z, centred,
                     on z = 0), printed bodies only: the file for the slicer
  probe-<script>.json  the probes, plus <body>.volume and <body>.bbox.x|y|z

The assembly stage (build.cs, after the scripts) writes assembly.step, the
placed meshes of the boards part.cs shows (from kicad-cli's STEPs in
boards/) and the views' cut bodies (asm/views/<view>/<body>.stl).

The fit stage runs this file directly:
  python pac.py fit --params build/params.json --out build
reads every <body>.step the clearances and sweeps name and writes fit.json.
"""

import argparse
import json
import math
import pathlib
import sys

from build123d import (Axis, Box, Compound, GeomType, Location, Pos, export_step,
                       export_stl, import_step as _import_step)
from OCP.gp import gp_Trsf

_state = None

# A body's up axis -> the rotation (axis, degrees) bringing it to +Z.
_UP = {
    "+Z": None,
    "-Z": (Axis.X, 180),
    "+X": (Axis.Y, -90),
    "-X": (Axis.Y, 90),
    "+Y": (Axis.X, 90),
    "-Y": (Axis.X, -90),
}


class PacError(Exception):
    pass


def _fail(msg):
    print(f"pac: {msg}", file=sys.stderr)
    sys.exit(1)


class Params:
    """params.json, read-only. P["name"] is a parameter's value (mm);
    P.fit("slip") a fit's gap per side; P.param("name") the full record."""

    def __init__(self, data, out, script):
        self.data = data
        self.out = out
        self.script = script
        self.bodies = {b["name"]: b for b in data["bodies"]}

    def __getitem__(self, name):
        return self.param(name)["value"]

    def param(self, name):
        try:
            return self.data["params"][name]
        except KeyError:
            raise PacError(f"no parameter '{name}' in part.cs") from None

    def fit(self, name):
        try:
            return self.data["fits"][name]
        except KeyError:
            raise PacError(f"no fit '{name}' in part.cs") from None

    @property
    def envelope(self):
        return tuple(self.data["envelope"])


class _State:
    def __init__(self, params):
        self.params = params
        self.bodies = {}
        self.probes = {}


def load(argv=None):
    """Parse --params/--out/--script (build.cs passes them) and return the
    Params. Call once, first."""
    global _state
    ap = argparse.ArgumentParser()
    ap.add_argument("--params", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--script", required=True, help="this script as part.cs names it")
    a = ap.parse_args(argv)
    data = json.loads(pathlib.Path(a.params).read_text(encoding="utf-8"))
    out = pathlib.Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    _state = _State(Params(data, out, a.script.replace("\\", "/")))
    return _state.params


def _st():
    if _state is None:
        raise PacError("call pac.load() first")
    return _state


def body(name, shape):
    """Register a body (assembly coordinates) under its part.cs name."""
    st = _st()
    b = st.params.bodies.get(name)
    if b is None:
        raise PacError(f"body '{name}' is not declared in part.cs")
    if b["script"] != st.params.script:
        raise PacError(f"body '{name}' belongs to {b['script']}, not {st.params.script}")
    if name in st.bodies:
        raise PacError(f"body '{name}' registered twice")
    st.bodies[name] = shape
    return shape


def probe(name, value):
    """Record a measured feature (`<body>.<feature>`); part.cs holds its
    expected value. Probe the built solid, never echo a parameter."""
    st = _st()
    if name in st.probes:
        raise PacError(f"probe '{name}' recorded twice")
    st.probes[name] = float(value)
    return value


def probe_cyl(name, shape, near):
    """Diameter of the cylindrical face (hole, boss, pin) whose axis passes
    nearest the point `near`."""
    best = None
    px, py, pz = near
    for f in shape.faces():
        if f.geom_type != GeomType.CYLINDER:
            continue
        ax = f.axis_of_rotation
        o, d = ax.position, ax.direction
        vx, vy, vz = px - o.X, py - o.Y, pz - o.Z
        cx = vy * d.Z - vz * d.Y
        cy = vz * d.X - vx * d.Z
        cz = vx * d.Y - vy * d.X
        dist = math.sqrt(cx * cx + cy * cy + cz * cz)
        if best is None or dist < best[0]:
            best = (dist, 2 * f.radius)
    if best is None:
        raise PacError(f"probe '{name}': no cylindrical face")
    return probe(name, best[1])


def region(lo, hi):
    """The box between corners lo and hi."""
    size = [h - l for l, h in zip(lo, hi)]
    if min(size) <= 0:
        raise PacError(f"region {lo}..{hi} is empty")
    return Pos(*[(l + h) / 2 for l, h in zip(lo, hi)]) * Box(*size)


def probe_extent(name, shape, axis, lo, hi):
    """Extent along axis ("x", "y" or "z") of the solid inside the box lo..hi:
    a wall's thickness, a slot's depth, a plate's height in one spot."""
    piece = shape & region(lo, hi)
    if not piece.solids():
        raise PacError(f"probe '{name}': no material inside {lo}..{hi}")
    return probe(name, getattr(piece.bounding_box().size, axis.upper()))


def coupon(name, shape, lo, hi):
    """Register the slice of `shape` inside lo..hi as the coupon body `name`:
    a few grams of the critical feature, printed before the part."""
    piece = shape & region(lo, hi)
    if len(piece.solids()) != 1:
        raise PacError(f"coupon '{name}': the region cuts {len(piece.solids())} solids, not 1")
    return body(name, piece.solids()[0])


def import_step(path):
    """A reference body from a STEP file (a board from kicad-cli pcb export
    step, a maker's model)."""
    return _import_step(str(path))


def _print_pose(shape, up):
    rot = _UP[up]
    s = shape if rot is None else shape.rotate(rot[0], rot[1])
    bb = s.bounding_box()
    return Pos(-(bb.min.X + bb.max.X) / 2, -(bb.min.Y + bb.max.Y) / 2, -bb.min.Z) * s


def finish():
    """Validate and export every registered body, write the probe file."""
    st = _st()
    p = st.params
    mine = [n for n, b in p.bodies.items() if b["script"] == p.script]
    missing = [n for n in mine if n not in st.bodies]
    if missing:
        _fail(f"{p.script} declares bodies it never registered: {', '.join(missing)}")
    tol, ang = p.data["tolerance"], p.data["angular_tolerance"]
    (p.out / "asm").mkdir(exist_ok=True)
    info = {}
    for name, shape in st.bodies.items():
        b = p.bodies[name]
        printed = b["kind"] != "reference"
        solids = shape.solids()
        if not solids:
            _fail(f"body '{name}' has no solid")
        if printed and len(solids) != 1:
            _fail(f"body '{name}' is {len(solids)} separate solids; a printed body is one")
        if not shape.is_valid:
            _fail(f"body '{name}' is not a valid solid (self-intersecting or open)")
        export_step(shape, str(p.out / f"{name}.step"))
        export_stl(shape, str(p.out / "asm" / f"{name}.stl"), tolerance=tol, angular_tolerance=ang)
        if printed:
            export_stl(_print_pose(shape, b["up"]), str(p.out / f"{name}.stl"),
                       tolerance=tol, angular_tolerance=ang)
        bb = shape.bounding_box()
        st.probes.setdefault(f"{name}.volume", shape.volume)
        for ax in "XYZ":
            st.probes.setdefault(f"{name}.bbox.{ax.lower()}", getattr(bb.size, ax))
        info[name] = {"kind": b["kind"], "solids": len(solids), "volume": shape.volume,
                      "bbox_min": [bb.min.X, bb.min.Y, bb.min.Z],
                      "bbox_max": [bb.max.X, bb.max.Y, bb.max.Z]}
    stem = pathlib.PurePosixPath(p.script).stem
    report = {"script": p.script, "bodies": info, "probes": st.probes}
    (p.out / f"probe-{stem}.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"{p.script}: {len(info)} bodies ({', '.join(info)}), {len(st.probes)} probes")


# ------------------------------------------------------------------ fit stage

def _overlap(a, b):
    i = a & b
    return sum(s.volume for s in i.solids()) if i is not None else 0.0


def _gap(a, b):
    ov = _overlap(a, b)
    # distance_to measures surface to surface: a body buried inside another
    # reads a positive distance, so the overlap decides first
    return (0.0 if ov > 1e-6 else a.distance_to(b)), ov


def fit(params_path, out):
    data = json.loads(pathlib.Path(params_path).read_text(encoding="utf-8"))
    out = pathlib.Path(out)
    cache = {}

    def shape(n):
        if n not in cache:
            f = out / f"{n}.step"
            if not f.exists():
                raise PacError(f"{f} missing: run build.cs first")
            cache[n] = _import_step(str(f))
        return cache[n]

    res = {"clearances": [], "sweeps": []}
    for c in data["clearances"]:
        g, ov = _gap(shape(c["a"]), shape(c["b"]))
        res["clearances"].append({"a": c["a"], "b": c["b"], "gap": g, "overlap": ov})
        print(f"clearance {c['a']}/{c['b']}: gap {g:.3f} mm, overlap {ov:.3f} mm3")
    for s in data["sweeps"]:
        mv = shape(s["moving"])
        fx, fy, fz = s["from"]
        for a in s["against"]:
            worst = None
            for i in range(s["steps"] + 1):
                t = i / s["steps"]
                moved = Pos(fx * (1 - t), fy * (1 - t), fz * (1 - t)) * mv
                g, ov = _gap(moved, shape(a))
                if worst is None or (ov, -g) > (worst["overlap"], -worst["gap"]):
                    worst = {"gap": g, "overlap": ov, "at": t}
            res["sweeps"].append({"moving": s["moving"], "against": a, **worst})
            print(f"sweep {s['moving']} vs {a}: worst gap {worst['gap']:.3f} mm, "
                  f"overlap {worst['overlap']:.3f} mm3 at {worst['at']:.2f} of the path")
    (out / "fit.json").write_text(json.dumps(res, indent=2), encoding="utf-8")


_AXIS = {"+X": (1, 0, 0), "-X": (-1, 0, 0), "+Y": (0, 1, 0), "-Y": (0, -1, 0), "+Z": (0, 0, 1), "-Z": (0, 0, -1)}


def _board_location(b):
    """A board's placement: its x along b["x"], its up along b["y"], its
    F face along x cross y, its origin (on the B face) at b["at"]."""
    ax, ay = _AXIS[b["x"]], _AXIS[b["y"]]
    n = (ax[1] * ay[2] - ax[2] * ay[1], ax[2] * ay[0] - ax[0] * ay[2], ax[0] * ay[1] - ax[1] * ay[0])
    at = b["at"]
    t = gp_Trsf()
    t.SetValues(ax[0], ay[0], n[0], at[0],
                ax[1], ay[1], n[1], at[1],
                ax[2], ay[2], n[2], at[2])
    return Location(t)


def _half(shape, keep, at):
    """The part of shape on the `keep` side ("+X": x >= at) of an axis
    plane, its cut faces capped."""
    bb = shape.bounding_box()
    lo, hi = [bb.min.X - 1, bb.min.Y - 1, bb.min.Z - 1], [bb.max.X + 1, bb.max.Y + 1, bb.max.Z + 1]
    i = "XYZ".index(keep[1])
    if keep[0] == "+":
        lo[i] = at
    else:
        hi[i] = at
    if hi[i] <= lo[i]:
        raise PacError(f"the cut at {keep[1]} = {at} leaves nothing of the body on the {keep} side")
    box = Pos(*[(a + b) / 2 for a, b in zip(lo, hi)]) * Box(*[b - a for a, b in zip(lo, hi)])
    return shape & box


def assembly(params_path, out):
    """Every body's STEP in one compound, in place, with the boards the
    model shows: build/assembly.step. Then each board's placed meshes
    (asm/<board>.stl, asm/<board>-parts.stl) and each view's cut body
    (asm/views/<view>/<body>.stl) for the renders."""
    data = json.loads(pathlib.Path(params_path).read_text(encoding="utf-8"))
    out = pathlib.Path(out)
    tol, ang = data["tolerance"], data["angular_tolerance"]
    parts, shapes = [], {}
    for b in data["bodies"]:
        f = out / f"{b['name']}.step"
        if not f.exists():
            raise PacError(f"{f} missing: a script did not finish")
        s = _import_step(str(f))
        s.label = b["name"]
        shapes[b["name"]] = s
        parts.append(s)
    for b in data.get("boards", []):
        loc = _board_location(b)
        for part in ("board", "parts"):
            f = out / "boards" / f"{b['name']}-{part}.step"
            if not f.exists():
                if part == "board":
                    raise PacError(f"{f} missing: build.cs exports it with kicad-cli")
                continue  # no 3D models on this board
            s = _import_step(str(f)).moved(loc)
            name = b["name"] if part == "board" else f"{b['name']}-parts"
            export_stl(s, str(out / "asm" / f"{name}.stl"), tolerance=max(tol, 0.05), angular_tolerance=max(ang, 0.3))
            s.label = name
            parts.append(s)
    export_step(Compound(children=parts, label=data["name"]), str(out / "assembly.step"))
    print(f"assembly.step: {len(data['bodies'])} bodies"
          + (f", {len(data.get('boards', []))} boards" if data.get("boards") else ""))
    for v in data.get("views", []):
        c = v.get("cut")
        if not c:
            continue
        cut = _half(shapes[c["body"]], c["keep"], c["at"])
        if not cut.solids():
            raise PacError(f"view {v['name']}: the cut leaves nothing of {c['body']}")
        d = out / "asm" / "views" / v["name"]
        d.mkdir(parents=True, exist_ok=True)
        export_stl(cut, str(d / f"{c['body']}.stl"), tolerance=tol, angular_tolerance=ang)
        print(f"view {v['name']}: {c['body']} cut, {c['keep']} of {c['at']:g} kept")


if __name__ == "__main__":
    ap = argparse.ArgumentParser(prog="pac.py")
    ap.add_argument("stage", choices=["fit", "assembly"])
    ap.add_argument("--params", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    try:
        (fit if a.stage == "fit" else assembly)(a.params, a.out)
    except PacError as e:
        _fail(str(e))
