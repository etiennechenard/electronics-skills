// The single source of truth for a project's boards: components, footprints
// and nets. The schematic generator, the PCB generators, the BOM exporter
// and the verifier all read this; nothing else decides what a part or a
// connection is.
//
// Conventions:
//   - one Comp per part: ref, symbol library id (lib:Name), value, footprint
//     id (null = the symbol's default footprint), pin -> net map, extra
//     fields (put the MPN in "MPN" — the BOM groups on it)
//   - net names are shared across boards (a ribbon pin carries the same name
//     on both boards it joins), which is what lets the verifier check the
//     whole unit pin by pin
//   - Fp = "" and OnBoard = false: a part that exists in the schematic only
//     to document an off-board thing (a switch or relay module hanging off
//     an on-board connector)
//   - Dnp = true: fitted on the board but not populated (still in the BOM,
//     marked DNP)
//   - InBom = false: no BOM line (e.g. board-only mechanical footprints)
//   - power flags: #FLGnn power:PWR_FLAG parts so ERC accepts power nets
//
// Library file for #:include — types only, no top-level statements.

namespace Design;

/// One component, on or off the board.
public sealed record Comp(
    string Ref,
    string Lib,
    string Value,
    string? Fp,
    IReadOnlyDictionary<string, string> Nets,
    bool InBom = true,
    bool OnBoard = true,
    bool Dnp = false,
    IReadOnlyDictionary<string, string>? Fields = null);

/// One board of the project.
public sealed record Board(
    string Title,
    Func<IReadOnlyList<Comp>> Parts,
    IReadOnlyList<string[]>? ExtraBom = null);

public static class DesignModel
{
    public static Comp Make(
        string @ref, string lib, string value, string? fp, IReadOnlyDictionary<string, string> nets,
        bool inBom = true, bool onBoard = true, bool dnp = false,
        IReadOnlyDictionary<string, string>? fields = null) =>
        new(@ref, lib, value, fp, nets, inBom, onBoard, dnp, fields);

    public static Comp Flag(int n, string net) =>
        new($"#FLG{n:00}", "power:PWR_FLAG", "PWR_FLAG", "",
            new Dictionary<string, string> { ["1"] = net }, InBom: false, OnBoard: false);

    // ------------------------------------------------------------------ //
    // The project's boards. Replace the example with the real ones.
    // ------------------------------------------------------------------ //
    public static readonly IReadOnlyDictionary<string, Board> Boards = new Dictionary<string, Board>
    {
        ["example"] = new("Example - one LED board", ExampleBoard),
    };

    private static IReadOnlyList<Comp> ExampleBoard() =>
    [
        Make("J1", "Connector_Generic:Conn_01x02", "5V IN",
            "Connector_JST:JST_PH_B2B-PH-K_1x02_P2.00mm_Vertical",
            new Dictionary<string, string> { ["1"] = "VIN", ["2"] = "GND" },
            fields: new Dictionary<string, string> { ["MPN"] = "JST B2B-PH-K-S (twisted pair to the supply)" }),
        Make("R1", "Device:R", "1k", "Resistor_SMD:R_0603_1608Metric",
            new Dictionary<string, string> { ["1"] = "VIN", ["2"] = "LED_A" },
            fields: new Dictionary<string, string> { ["MPN"] = "any 1 k 0603 >= 16 V" }),
        Make("D1", "Device:LED", "green", "Diode_SMD:D_0603_1608Metric",
            new Dictionary<string, string> { ["1"] = "GND", ["2"] = "LED_A" }),
        Flag(1, "VIN"),
        Flag(2, "GND"),
    ];

    /// {(ref, pin) -> net} for every on-board part's pins of a named board —
    /// what the verifier compares KiCad's exported netlist and the PCB pads
    /// against. Omitted here, and to be filtered from the netlist's extras
    /// by the verifier: '#' refs (power flags — KiCad DOES netlist them, as
    /// #PWR-style nodes) and off-board parts (OnBoard = false — their
    /// symbols appear in the netlist).
    public static Dictionary<(string Ref, string Pin), string> ExpectedPins(string board) =>
        ExpectedPins(Boards[board].Parts());

    public static Dictionary<(string Ref, string Pin), string> ExpectedPins(IEnumerable<Comp> parts)
    {
        var exp = new Dictionary<(string, string), string>();
        foreach (var c in parts)
        {
            if (c.Ref.StartsWith('#') || !c.OnBoard)
                continue;
            foreach (var (pin, net) in c.Nets)
                exp[(c.Ref, pin)] = net;
        }
        return exp;
    }
}
