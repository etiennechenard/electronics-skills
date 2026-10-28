# Fabrication: the fab's limits as checked data

The fab's limits are design rules, not prose, and they come from the fab,
never from memory or from this skill. The steps:

1. **Choose the fab and the process.** At kickoff: JLCPCB by default,
   PCBWay or another fab if the project needs it. The process covers
   layers, copper weight, finish and **mask colour**: some limits depend on
   it (a fab's mask bridge can be wider for black or white mask than for
   green: read the line for the colour ordered).
2. **Fetch the capability page** (WebFetch) and write `pcb/fab.json` from
   it. For each of the keys below, record the number in mm and the page's
   own words, quoted verbatim. When the page gives several numbers, take
   the one for the process ordered. A limit the page does not publish is
   `"mm": null`, with a quote saying so. A value worked out from another
   (a via ring from a sentence giving the via pad as the hole plus a
   margin) quotes the sentence it comes from. For the keys below, `mm` is
   always a number or `null` (anything else is reported, never read as
   unpublished), and `track_width`, `track_spacing`, `via_hole` and
   `pth_hole` are never `null`: every fab publishes them.
3. **Hold the project to it in code.** `fab.cs` loads the file (`FabRules`)
   and checks it (`FabCheck`); `fabcheck.cs` is the gate (below).
4. **Before ordering, fetch the page again.** `fabcheck.cs` fails when
   `checked` is more than 90 days old. Update `checked` only after
   re-reading every value against the page: the date is what the gate
   trusts.

```json
{
  "fab": "JLCPCB",
  "process": "<the process ordered: layers, material, thickness, copper, finish, mask colour>",
  "source": "https://jlcpcb.com/capabilities/pcb-capabilities",
  "checked": "yyyy-mm-dd",
  "rules": {
    "track_width": { "mm": 0.0, "quote": "<the page's line, verbatim>" },
    ...
  }
}
```

The numbers above are placeholders; the real ones come from the page.

| Key | The limit (all minimums, mm) |
|---|---|
| `track_width`, `track_spacing` | trace width; trace to trace spacing |
| `via_hole`, `via_diameter`, `via_annular_ring` | via drill, via pad, via ring |
| `pth_hole`, `pth_annular_ring` | plated pad drill and its ring |
| `npth_hole` | non-plated hole |
| `hole_to_hole` | hole to hole, edge to edge (vias) |
| `pad_hole_to_pad_hole` | plated pad hole to plated pad hole, edge to edge |
| `copper_to_edge` | copper to the routed edge |
| `silk_line_width`, `silk_text_height` | silkscreen stroke and text height |
| `mask_bridge` | solder mask bridge (web) between openings |

Other keys can be kept in the file as information (impedance stackups,
board thickness range). `FabCheck` ignores them.

## What checks the limits

- **`FabCheck.Project(kicadPro, fab)`** checks the board setup's minimums
  and every net class against the fab's limits:
  - track width and clearance;
  - via diameter, drill and ring;
  - the minimum through-hole drill;
  - hole to hole;
  - copper to edge;
  - silk text height and thickness;
  - mask web width.

  gen_pcb calls it after writing the `.kicad_pro` and refuses to go on when
  anything falls short, silk minimums included. KiCad's DRC and the DSN
  classes then hold the copper to at least the fab's limits. One gap:
  KiCad has a single hole-to-hole minimum for every pair, held to
  `hole_to_hole`, so the fab's plated-pad limit is `Board`'s check alone.
- **`FabCheck.Board(board, fab)`** checks the board as built, including
  what footprints bring with them:
  - every track width;
  - every via's drill, diameter and ring;
  - every plated pad's drill and ring (a custom-shaped pad's ring is not
    measured: its size is its anchor's), and every NPTH hole;
  - hole to hole, edge to edge, using the pad-to-pad limit between two
    plated pad holes and `hole_to_hole` otherwise;
  - silkscreen text height and stroke, and silkscreen line widths.

  Here silkscreen below the limit is a warning: the fab prints it thin or
  drops it, and KiCad's stock footprints draw thinner lines than most fabs
  publish. Everything else is an error. A drill offset is not read: the
  hole is taken at the pad centre.
- **`dotnet run pcb/tools/fabcheck.cs -- pcb/fab.json pcb/<board>.kicad_pcb ...`**,
  from the project root, runs both, plus the file's own checks: every key
  present, a quote per value, positive numbers or null, and `checked`
  within 90 days (`--max-age`). It
  prints one line per board, every error, and the warnings grouped per
  rule, and ends with `FAB PROBLEMS: n`. It exits 1 when n > 0 (a file it
  cannot read counts as an error), and 2 on a usage error or a missing
  file. export.cs runs it as a gate, next to the DRC and verify gates, and
  refuses on any non-zero exit.

Copper spacing and copper to edge are geometry KiCad's DRC already
measures. `Project` makes sure DRC measures them against at least the
fab's numbers.

The project's own choices sit above the fab's minimums, with margin. They
live in the generators: the rules gen_pcb writes and the net classes.
`docs/fab.md` records the reasons for them, the fab, the process and any
add-ons ordered. It does not copy the fab's numbers or the check date:
those live in `fab.json`.

## Fab-specific notes

- **JLCPCB:**
  - assembly is integrated with LCSC: the BOM carries an LCSC column,
    with a C-number per part. Basic and Extended parts differ, and
    Extended parts add a handling fee;
  - export.cs's bundle uploads as-is: RS-274X gerbers, Excellon drill, the
    KiCad pos CSV and the BOM, zipped.
- **PCBWay:**
  - takes the same gerber bundle;
  - parts are sourced by MPN at quote time;
  - a wider special-process catalogue: heavy copper, flex, Rogers.
- **RF / impedance:** take the fab's published stackup into gen_pcb's
  stackup block, and order the impedance-controlled process.
- **Other fabs:** the same file and the same checks. Only the fetch
  differs.
