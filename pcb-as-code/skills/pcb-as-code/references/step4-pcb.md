# Step 4 — gen_pcb — placed, linked boards (write the .kicad_pcb as text)

Write each board directly as s-expression text with `Sx.Sexpr`, and its
`<board>.kicad_pro` next to it: the board setup's design rules and the net
classes with their patterns (track and via sizes, clearances) live in the
project file, and DRC, `DsnWriter.Project` and `FabCheck.Project` all read
them from there. The board holds:
- the stackup;
- GND zones. Unfilled outlines are fine: kicad-cli refills them for DRC and
  gerbers;
- footprints embedded from the libraries (or board-only mechanics without
  symbols), placed by the generator's own layout code. Each footprint's
  path `"/" + uuid5(NS, "board|sym|ref")` links it to its symbol.

Copper the generator draws itself (a fanout, panel wiring) is written
LOCKED: locked tracks survive routing as fixed wires. Finish with
`kicad-cli pcb upgrade --force`, so KiCad owns the format, then hold the
project file to the fab: with `fab = FabRules.Load(<the project's pcb/fab.json>)`,
`FabCheck.Project(<board>.kicad_pro, fab)` must be empty
(`fabrication.md`).

The `.kicad_pro` keys the tools read (KiCad's own names, all mm):
- `board.design_settings.rules`: `min_track_width`, `min_clearance`,
  `min_via_diameter`, `min_via_annular_width`,
  `min_through_hole_diameter`, `min_hole_clearance` (required: the DSN's
  hole keepouts), `min_hole_to_hole`, `min_copper_edge_clearance`,
  `min_text_height`, `min_text_thickness`, `solder_mask_min_width`;
- `net_settings.classes[]`: `name` (one is `Default`), `track_width`,
  `clearance`, `via_diameter`, `via_drill`;
- `net_settings.netclass_patterns[]`: `netclass`, `pattern`;
- `net_settings.netclass_assignments` (optional): net name to class name,
  or to a list of them.

Write it as JSON holding at least these keys (the tests'
`TestBoards.Pro` is a minimal example); KiCad fills in the rest with its
defaults when it opens the project.

The format facts a writer needs (KiCad 10, board version 20260206):
- **Nets are referenced by name.** Pads, tracks, vias and zones carry
  `(net "GND")`, and there is no net table. An unconnected pad of a part
  gets KiCad's own name, `unconnected-(REF-PadN)`.
- **Pad positions are local.** A pad's `(at x y a)` is its offset in the
  footprint, rotated with it. The angle `a` is the absolute pad angle, not
  relative to the footprint. `boardfile.cs`'s `BPad.Position` gives the
  absolute point and `BPad.LibAngle` the angle relative to the footprint.
- **Flipping to the back** (pcbnew's Flip, top to bottom):
  - the footprint's orientation becomes `-orient`;
  - local y is negated in every child (pads, graphics, texts, polygon
    points);
  - F.* and B.* layers swap;
  - a pad's stored angle becomes `orient - a`, where `orient` is the new
    (stored, already negated) footprint orientation and `a` the pad's
    library angle;
  - a text's stored angle becomes `orient + 180 - a`, same `orient`;
  - texts get `(justify mirror)`;
  - `fp_arc` start and end swap, so arcs stay counter-clockwise.
- **Rotation sense:** a rotation by `a` maps (x, y) to
  `(x cos a + y sin a, -x sin a + y cos a)`, counter-clockwise on screen
  with y down (`Nm.Rotate`).

The board writer is the project's own code: it is not a template, because
placement and copper are per project. Write it on `Sx.Sexpr`, and test the
transforms above on a synthetic footprint: a pad on the back, rotated, must
land where `boardfile.cs` reads it back.

