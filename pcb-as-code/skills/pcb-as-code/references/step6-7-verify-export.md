# Steps 6-7 — verify and export

## Step 6 — verify — everything against design.cs

For every board (`Kicad.Cli.ExportNetlist`, `DesignModel.ExpectedPins`,
`Sx.Sexpr` on the .kicad_pcb):

- Schematic netlist: every (ref, pin)→net of on-board parts matches the
  design; extras allowed only on `unconnected-*` nets, `#`-refs (power
  flags) and off-board parts (`OnBoard = false`). ExpectedPins omits the
  last two — KiCad netlists power symbols as #PWR-style nodes and includes
  off-board symbols — so the verifier must too; `unconnected-*` is a
  netlist-side net name, never an expected pin. An unlabelled `Net-(...)`
  net passes when its pins are exactly one design net's pins. Plus ERC 0/0.
- PCB pads: same pin-by-pin map; every expected footprint present; every
  footprint path-linked to its symbol.
- Project-specific construction rules as code (for example: no through-hole
  leads on a panel board except one special receptacle, no copper on the
  front panel's exterior face, every part clear of the case's slot edges).

Exit non-zero on any problem; print `TOTAL PROBLEMS: n`.

## Step 7 — export — fab + review outputs, gated

Only when every DRC report is clean (or starved-thermal-only) AND newer
than its board AND verify passes AND `fabcheck.cs` reports
`FAB PROBLEMS: 0` (which needs `fab.json` checked within 90 days: re-fetch
the fab's page before ordering). export.cs runs the gates as child
processes (verify.cs, fabcheck.cs) and
refuses on a non-zero exit; an entry file cannot be `#:include`d. Run
them with the project root as the working directory, as their arguments
are root-relative: `dotnet run pcb/tools/fabcheck.cs -- pcb/fab.json
pcb/<board>.kicad_pcb ...`. Then:

- gerbers: `pcb export gerbers --layers F.Cu,B.Cu,F.Mask,B.Mask,F.SilkS,B.SilkS,F.Paste,B.Paste,Edge.Cuts --subtract-soldermask --no-netlist`
- drill: `pcb export drill --format excellon --excellon-separate-th --generate-map --map-format pdf`
- placement: `pcb export pos --format csv --units mm --side both --smd-only --use-drill-file-origin`
- BOM: group by (value, footprint, MPN, DNP), refs in natural order, plus
  extra rows for parts with no symbol (`ExtraBom`); write the zip.
- renders: `pcb export svg` per side (B side `--mirror`) — or `pcb render`
  for 3D PNGs; schematics: `sch export pdf`.
- fab: the gerber zip uploads to JLCPCB as-is (and PCBWay accepts the same
  bundle) — see `fabrication.md`.

