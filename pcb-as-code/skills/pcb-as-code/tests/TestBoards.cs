using PcbFile;

namespace PcbAsCode.Tests;

/// A synthetic board, its project file and a fab.json, shared by the
/// board-file, DSN, fab and file-based-app tests.
static class TestBoards
{
    // a 20 x 10 mm board: J1 on the front (one SMD, one PTH pad), J2 on the
    // back turned 90 degrees (pad 2: library (1, 0.5) at 90 degrees, stored
    // flipped), a locked two-segment track, one routed track,
    // a via 0.35 mm (edge to edge) from J1's hole, and a small silk legend
    public const string Pcb = """
        (kicad_pcb (version 20260206) (generator "test")
          (gr_rect (start 100 100) (end 120 110) (stroke (width 0.1) (type default)) (layer "Edge.Cuts") (uuid "00000000-0000-0000-0000-000000000001"))
          (gr_text "TEST" (at 110 102) (layer "F.SilkS") (uuid "00000000-0000-0000-0000-00000000000d") (effects (font (size 0.8 0.8) (thickness 0.1))))
          (footprint "T:Two" (layer "F.Cu") (uuid "00000000-0000-0000-0000-000000000002") (at 105 105)
            (property "Reference" "J1" (at 0 -2 0) (layer "F.SilkS") (hide yes) (uuid "00000000-0000-0000-0000-000000000003") (effects (font (size 0.5 0.5) (thickness 0.05))))
            (pad "1" smd rect (at -1 0) (size 1 0.5) (layers "F.Cu" "F.Paste" "F.Mask") (net "A") (uuid "00000000-0000-0000-0000-000000000004"))
            (pad "2" thru_hole circle (at 1 0) (size 1.6 1.6) (drill 0.8) (layers "*.Cu" "*.Mask") (net "B") (uuid "00000000-0000-0000-0000-000000000005")))
          (footprint "T:One" (layer "B.Cu") (uuid "00000000-0000-0000-0000-000000000006") (at 115 105 -90)
            (property "Reference" "J2" (at 0 0 -90) (layer "B.SilkS") (uuid "00000000-0000-0000-0000-000000000007") (effects (font (size 1 1) (thickness 0.15)) (justify mirror)))
            (pad "1" smd rect (at 0 0 -90) (size 1 0.5) (layers "B.Cu" "B.Paste" "B.Mask") (net "A") (uuid "00000000-0000-0000-0000-000000000008"))
            (pad "2" smd rect (at 1 -0.5 -180) (size 1 0.5) (layers "B.Cu" "B.Paste" "B.Mask") (net "B") (uuid "00000000-0000-0000-0000-00000000000e")))
          (segment (start 104 105) (end 110 105) (width 0.25) (locked yes) (layer "F.Cu") (net "A") (uuid "00000000-0000-0000-0000-000000000009"))
          (segment (start 110 105) (end 114 105) (width 0.25) (locked yes) (layer "F.Cu") (net "A") (uuid "00000000-0000-0000-0000-00000000000a"))
          (segment (start 110 106) (end 111 106) (width 0.25) (layer "F.Cu") (net "B") (uuid "00000000-0000-0000-0000-00000000000b"))
          (via (at 106.9 105) (size 0.6) (drill 0.3) (layers "F.Cu" "B.Cu") (net "B") (uuid "00000000-0000-0000-0000-00000000000c"))
        )
        """;

    // board setup at the usual safe values except min_hole_to_hole (0.25,
    // below TestFab's 0.5); a Default and a Power class
    public const string Pro = """
        {
          "board": { "design_settings": { "rules": {
            "min_clearance": 0.2, "min_track_width": 0.2, "min_via_diameter": 0.6, "min_via_annular_width": 0.15,
            "min_through_hole_diameter": 0.3, "min_hole_clearance": 0.2, "min_hole_to_hole": 0.25,
            "min_copper_edge_clearance": 0.3, "min_text_height": 1.0, "min_text_thickness": 0.15,
            "solder_mask_min_width": 0.1 } } },
          "net_settings": {
            "classes": [
              { "name": "Default", "track_width": 0.25, "clearance": 0.2, "via_diameter": 0.6, "via_drill": 0.3 },
              { "name": "Power", "track_width": 0.5, "clearance": 0.2, "via_diameter": 0.8, "via_drill": 0.4 } ],
            "netclass_patterns": [ { "netclass": "Power", "pattern": "+*" } ] }
        }
        """;

    /// A made-up fab's limits (not any real fab's numbers).
    /// set: keys to override, each value the raw JSON of its "mm".
    public static string FabJson(DateOnly checkedOn, string? drop = null, string trackWidth = "0.2",
        IReadOnlyDictionary<string, string>? set = null)
    {
        var rules = new Dictionary<string, string>
        {
            ["track_width"] = trackWidth, ["track_spacing"] = "0.15", ["via_hole"] = "0.2", ["via_diameter"] = "0.45",
            ["via_annular_ring"] = "0.1", ["pth_hole"] = "0.2", ["pth_annular_ring"] = "0.15", ["npth_hole"] = "null",
            ["hole_to_hole"] = "0.5", ["pad_hole_to_pad_hole"] = "0.45", ["copper_to_edge"] = "0.3",
            ["silk_line_width"] = "0.15", ["silk_text_height"] = "1.0", ["mask_bridge"] = "0.1",
        };
        foreach (var (k, v) in set ?? new Dictionary<string, string>())
            rules[k] = v;
        if (drop is not null)
            rules.Remove(drop);
        var body = string.Join(",\n", rules.Select(r =>
            $"    \"{r.Key}\": {{ \"mm\": {r.Value}, \"quote\": \"{(r.Value == "null" ? "not published" : "TestFab page: " + r.Key + " " + r.Value.Trim('"'))}\" }}"));
        return $$"""
            {
              "fab": "TestFab", "process": "2-layer test", "source": "https://example.com/capabilities",
              "checked": "{{checkedOn:yyyy-MM-dd}}",
              "rules": {
            {{body}}
              }
            }
            """;
    }

    public static string Temp(string dir, string name, string text)
    {
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, name);
        File.WriteAllText(p, text);
        return p;
    }

    public static Board Load(string text = Pcb)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pcbascode-{Guid.NewGuid():N}.kicad_pcb");
        File.WriteAllText(path, text);
        try { return Board.Load(path); }
        finally { File.Delete(path); }
    }
}
