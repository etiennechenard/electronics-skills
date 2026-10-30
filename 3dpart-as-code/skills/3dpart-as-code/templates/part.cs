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
        // The fits: one literal gap per line, so each can be tuned from the sheet.
        Fit[] fits =
        [
            new("press", 0.05, "interference-ish: driven in, holds by friction"),
            new("snug", 0.10, "by hand, no play"),
            new("slip", 0.20, "slides or turns freely, little play"),
            new("loose", 0.35, "drops in, visible play"),
        ];
        var slip = fits.First(f => f.Name == "slip");
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
        // Where each number sits, for the labelled views and the sheet's #
        // column: a span is a dimension line (its length is the value),
        // a single point a marker. Lines float a few mm off the faces.
        double V(string name) => P(ps, name).Value;
        double w = V("bracket.w"), d = V("base.d"), t = V("base.t"), h = V("upright.h"), ut = V("upright.t");
        double hd = V("hole.d"), hy = V("hole.y"), sd = V("screw.d"), sx = V("screw.x"), sz = V("screw.z");
        double pd = V("pin.d"), pl = V("pin.l"), pinTop = t / 2 + pl / 2;
        Annotation[] annotations =
        [
            new("bracket.w", "bracket", (-w / 2, -3, h + 3), (w / 2, -3, h + 3)),
            new("base.d", "bracket", (w / 2 + 3, 0, -3), (w / 2 + 3, d, -3)),
            new("base.t", "bracket", (w / 2 + 3, d + 3, 0), (w / 2 + 3, d + 3, t)),
            new("upright.h", "bracket", (-w / 2 - 3, -3, 0), (-w / 2 - 3, -3, h)),
            new("upright.t", "bracket", (-w / 2 - 3, 0, h + 3), (-w / 2 - 3, ut, h + 3)),
            new("fillet.r", "bracket", (w / 2, ut + 1, t + 1)),
            new("hole.d", "bracket", (-hd / 2, hy, t + 1), (hd / 2, hy, t + 1)),
            new("hole.y", "bracket", (-hd / 2 - 4, 0, t + 1), (-hd / 2 - 4, hy, t + 1)),
            new("screw.d", "bracket", (sx - sd / 2, -15, sz), (sx + sd / 2, -15, sz)),
            new("screw.x", "bracket", (0, -9, sz + sd / 2 + 3), (sx, -9, sz + sd / 2 + 3)),
            new("screw.z", "bracket", (-sx - sd / 2 - 3, -3, 0), (-sx - sd / 2 - 3, -3, sz)),
            new("pin.d", "pin", (-pd / 2, hy, pinTop + 2), (pd / 2, hy, pinTop + 2)),
            new("pin.l", "pin", (pd / 2 + 3, hy, pinTop - pl), (pd / 2 + 3, hy, pinTop)),
            new("pin.chamfer", "pin", (pd / 2, hy, pinTop)),
        ];
        return new Model
        {
            Name = "bracket",
            Envelope = new(220, 220, 250),
            Fits = fits,
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
            Annotations = annotations,
        };
    }

    static Param P(Param[] ps, string name) =>
        ps.FirstOrDefault(p => p.Name == name) ?? throw new KeyNotFoundException($"no parameter '{name}'");
}
