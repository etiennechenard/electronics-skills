// The single source of truth: every number of every body. The geometry
// scripts (parts/*.py) read these values from params.json and hold none of
// their own; checks, fits and renders all derive from this file.
//
// This copy holds a worked example — an L-bracket with a slip-fit pin and a
// coupon of the pin's hole — that the tests build end to end. Replace it
// with the project's own model, keeping the shape.
//
// Library file for #:include — types only, no top-level statements.

using PartModel;

namespace Part;

public static class PartDesign
{
    public static readonly Model Model = Build();

    static Model Build()
    {
        // Assembly coordinates: the base plate lies on z = 0, the upright
        // stands at y = 0..upright.t, x is centred.
        var pinD = new Param("pin.d", 6.0, Tol: 0.05, Critical: true, Note: "the pin's diameter; sets the hole");
        var slip = Fits.Slip;
        Param[] ps =
        [
            new("bracket.w", 40.0, Tol: 0.2),
            new("base.d", 30.0, Tol: 0.2, Note: "depth of the base plate from the upright's back face"),
            new("base.t", 4.0, Tol: 0.1),
            new("upright.h", 30.0, Tol: 0.2),
            new("upright.t", 4.0, Tol: 0.1),
            new("fillet.r", 3.0, Note: "inside corner, base to upright"),
            pinD,
            new("pin.l", 16.0, Tol: 0.2),
            new("pin.chamfer", 0.6, Note: "lead-in at both ends"),
            new("hole.d", pinD.Value + 2 * slip.Gap, Source: Source.Derived, Tol: 0.05, Critical: true,
                Ref: "pin.d + 2 × slip"),
            new("hole.y", 18.0, Tol: 0.1, Note: "hole centre from the upright's back face"),
            new("screw.d", 3.4, Tol: 0.1, Note: "M3 clearance"),
            new("screw.x", 12.0, Tol: 0.1, Note: "± from the centre"),
            new("screw.z", 20.0, Tol: 0.1),
            new("coupon.margin", 4.0, Note: "material kept around the hole in its coupon"),
        ];
        return new Model
        {
            Name = "bracket",
            Envelope = new(220, 220, 250),
            Params = ps,
            Bodies =
            [
                new("bracket", "parts/bracket.py", "+Z", Note: "base on the bed; the screw holes print horizontal"),
                new("pin", "parts/bracket.py", "+Z"),
                new("hole-coupon", "parts/bracket.py", "+Z", BodyKind.Coupon, "the base around the pin hole"),
            ],
            Dims =
            [
                ExpectedDim.Of(P(ps, "hole.d"), "bracket.hole.d", 0.01),
                ExpectedDim.Of(P(ps, "pin.d"), "pin.d", 0.01),
                ExpectedDim.Of(P(ps, "base.t"), "bracket.base.t", 0.01),
                ExpectedDim.Of(P(ps, "screw.d"), "bracket.screw.d", 0.01),
                ExpectedDim.Of(P(ps, "bracket.w"), "bracket.bbox.x", 0.01),
                ExpectedDim.Of(P(ps, "base.d"), "bracket.bbox.y", 0.01),
                ExpectedDim.Of(P(ps, "upright.h"), "bracket.bbox.z", 0.01),
                ExpectedDim.Of(P(ps, "pin.l"), "pin.bbox.z", 0.01),
            ],
            // the pin sits in its hole with the slip gap all round, and goes
            // in from above without touching
            Clearances = [new("pin", "bracket", slip.Gap * 0.75, "radial slip gap")],
            Sweeps = [new("pin", (0, 0, 25), ["bracket"], slip.Gap * 0.75, Note: "dropped in from above")],
        };
    }

    static Param P(Param[] ps, string name) =>
        ps.FirstOrDefault(p => p.Name == name) ?? throw new KeyNotFoundException($"no parameter '{name}'");
}
