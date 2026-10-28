# Pipeline steps 1-8 — detail

Port each stage into an entry file built on the copied templates:
`gen_lib.cs`, `gen_schematics.cs`, `gen_<board>.cs`, `route.cs`,
`verify.cs`, `export.cs`. Order matters; each step names its input →
output and its gate.

## 1. design.cs — the source of truth

One `Comp` per part (ref, `lib:Symbol`, value, `lib:Footprint` or null for
the symbol's default, pin→net map, extra fields — put the MPN in `"MPN"`,
the BOM groups on it), plus per-board registries and `ExpectedPins()`.
Conventions the workflow depends on (see `templates/design.cs`):

- Net names are shared across boards — a ribbon pin carries the same name
  on both boards it joins, so one netlist check covers the whole unit.
- `Fp = ""` + `OnBoard = false`: schematic-only documentation of an
  off-board part (a switch or relay module hanging off an on-board
  connector); these pins are not verified.
- `Dnp = true`: on the board, not populated, marked DNP in the BOM.
- `InBom = false`: no BOM line (power flags, board-only mechanics).
- Power nets get `#FLGnn power:PWR_FLAG` parts or ERC fails them.
- Annotate WHY in comments (drive levels, tolerance reasoning, which pins
  need series resistors) — the design file is also the design record.
- **Simulate behavior-critical sections before design.cs freezes**:
  anywhere the behavior is load-bearing and not obvious from the netlist —
  analog paths, power sections, precision references, and mixed-signal
  behavior: power-up sequencing (RC-held reset/enable pins), timing
  through level shifters and shift registers, bus contention. Digital
  parts simulate fine (mixed-signal SPICE with logic-family timing
  models) — use the micro-cap skill when it is available on this machine;
  any SPICE works. Compare against the requirement and record the verdict
  in design.md; netlists and outputs live in `sim/`, referenced from
  design.md. What never needs it is pure connectivity: logic *function*
  belongs to the firmware and its host tests, wiring to the pin-parity
  gate.

## 2. gen_lib — project libraries

Write `pcb/lib/<proj>.kicad_sym` (custom symbols) and `pcb/lib/<proj>.pretty`
(footprints) plus the sym/fp lib tables, then resave with
`kicad-cli sym upgrade --force` / `fp upgrade --force` so KiCad owns the
format. Symbols' pin order and spacing decide where schematic wires can run.

## 3. gen_schematics — one .kicad_sch per board

The design supplies parts and nets; the generator holds the DRAWING: one
layout function per board placing symbols and drawing wires on a 2.54 mm
grid (0.001 mm file resolution). Drawing invariants that make the sheet
self-checking:

- Every wire tagged with the design net it carries; wires split at every
  connection point (pin, label, power symbol, wire end) so connectivity
  never depends on a wire END landing mid-wire.
- No item of one net may touch another net; every net one connected piece
  (far-apart pieces joined by local labels or same-name power symbols);
  every pin wired or explicitly no-connect; no undrawn parts.
- Label the widest clear stretch of a net's horizontal wire with the design
  net name, so "Update PCB from schematic" matches the generated boards;
  KiCad's automatic `Net-(...)` names are tolerated by the verifier when
  the pins match exactly one design net.
- Embed symbols from KiCad's libraries or lib/<proj>.kicad_sym; give every
  symbol a deterministic UUID (uuid5 of a project namespace +
  `board|sym|ref`) — the PCB generator links footprints to symbols by it.

Gate: `kicad-cli sch erc --severity-all` → 0 errors AND 0 warnings
(`Kicad.Cli.Erc`), then netlist parity (step 6).

## 4. gen_pcb — placed, linked boards (write the .kicad_pcb as text)

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

## 5. route — FreeRouting headless (the hard-won recipe)

Per board, from `pcb/tools/route.cs` using `Specctra.Dsn` / `FreeRouting` /
`Ses`:

1. Write the Specctra DSN. kicad-cli has no DSN export, so `dsn.cs` writes
   it from the board text:
   `DsnWriter.Write(board, classes, holeClearance, name)`, with
   `(classes, holeClearance) = DsnWriter.Project(<board>.kicad_pro)` and
   `name` the DSN's `(pcb <name>)` header (the board's file name).
   Load `board` with `PcbFile.Board.Load`, drop its routed copper
   (`RoutedCopper(drop: true)`) and write from that copy. The writer follows pcbnew's ExportSpecctraDSN (its
   header lists the conventions), for two-layer boards:
   - back-side parts are placed `back` at orientation + 180°, with images
     in the front frame;
   - consecutive locked segments chain into one fixed wire, and locked
     vias are fixed vias (locked arcs are not written);
   - padstack names follow pcbnew's, and round NPTH holes become keepouts;
   - the text layout is the one `Dsn.Shuffle` reads (a test holds that).
2. On that text, `Dsn.BumpClearances` (+15% on rules ≥ 100 µm) and, per
   attempt, `Dsn.Shuffle` (Retries below); write the result to
   `build/<board>.dsn`, the file `FreeRouting.Route(dsn, ses)` takes, with
   the SES path next to it. The bump because FreeRouting's checks are
   optimistic vs KiCad DRC; the smd_smd pad-pad rule is left alone.
3. Run `FreeRouting.Route` — start with `-mt 1`: on 1.4.5.1 the
   multi-threaded optimizer put tracks through pads (shorts on every
   multi-threaded run tested); widen the thread count only after a version
   proves DRC-clean multi-threaded. A dense board takes ~40 min per
   attempt (2 h timeout); simple ones minutes.
4. `Ses.ReadRoutes`, merge into the .kicad_pcb as track/via nodes. Each
   SES wire is a polyline: split it into its segments, then skip segments
   < 10 µm (a pad's dot), any segment matching a locked track (the SES
   echoes the fixed wiring, a chained wire included) and any via at a
   locked via's position; nets by name.
5. Add GND stitching (vias tying the two GND pours: a fence along both
   sides of chosen nets' tracks — think return-current paths for buses —
   then a coarse grid). Placement is computed from the board text: clear
   of tracks, pads, courtyards (`Board.Courtyard`) and other holes, with
   the via's clearance. Pour coverage IS checkable in code:
   `kicad-cli pcb drc --refill-zones --save-board` on a scratch copy writes
   the `filled_polygon`s of every zone; `Board.ZoneFills(zone)` reads them
   (one polygon per island and layer; `ZoneOutline` is the drawn outline,
   not the copper). A candidate via goes in only where a fill of each GND
   layer contains it (`Board.Contains`, which follows KiCad's edge rule). Still look at the result in KiCad before export: that is the
   sanctioned LOOK step.
6. Save (`Board.Save`, which also runs `pcb upgrade`), refill the zones in
   the saved board (`pcb drc --refill-zones --save-board`, with a scratch
   report), then gate: `Kicad.Cli.Drc` → `build/<board>-drc.json`,
   **0 errors, 0 unrouted**. Warnings are placement fixes in the generator
   (starved thermal on a pad also tied to copper elsewhere is tolerable —
   `Clean(allowStarvedThermal: true)`).

Retries: FreeRouting's result depends on the DSN's component/net order,
and one writer writes one order — so each attempt re-runs with
`Dsn.Shuffle(dsn, seed + attempt)` in a fresh process, ≤ 8 attempts; if
all fail, change the pass count (`-mp`) or nudge the placement near the
failing net. After a change that does NOT move parts or pads, copy the old
routed copper by net name instead of re-routing (`git show HEAD:pcb/<b>.kicad_pcb`
as the source). Snapshot boards to `<board>-backups/` before destructive
regens.

Versions: 1.4.5.1 is the known-good default — intermediate releases routed
worse on the boards tested — but it is a default, not a pin. The 2.x line
(e.g. 2.5, a claimed major optimizer overhaul) takes the same CLI, so
evaluating it is one parameter: pass the candidate jar (and its Java, 2.5
needs 25) to `FreeRouting.Route(java:, jar:)`, route a known board, and
adopt it only if it beats the default on the same gates — 0 unrouted /
0 DRC errors, faster or more reliably — keeping the comparison in the
project's `routing-comparison.md` and the old jar installed as fallback.

## 6. verify — everything against design.cs

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

## 7. export — fab + review outputs, gated

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

## 8. firmware + docs

Firmware in PlatformIO with the logic core as plain C/C++ under native host
tests (for example: a command parser, a USB session layer, a bus simulation
— no hardware needed to test). `docs/bom.md` covers the whole unit including
off-board parts; `assembly.md` covers build order, enclosure checks (get the
case first, check the panel outline against its real panels) and bring-up
checks; keep README's regeneration command list exact.

## Review — the multi-wave sweep (before the fab order, after any big change)

The gates above hold the mechanical line — ERC, pin parity, DRC, fab
limits — but they cannot see a consistent-but-wrong design: a net that
matches `design.cs`, the schematic and the board everywhere and is still
wrong against the part's datasheet; a placement that passes every rule and
still fights the enclosure; a doc that describes a board that no longer
exists. That is what the review sweep is for: **this plugin's
`hw-review` skill** (`${CLAUDE_PLUGIN_ROOT}/skills/hw-review/`) — a
fixed budget of cheap read-only reviewer agents in waves of at most 6,
each wave triaged by the session, its confirmed fixes applied **in the
source** and the boards regenerated before the next wave reviews the
updated branch. The proven experience on a real build: it took several
waves to find and fix all the issues — run the sweep with that
expectation, and before a fab order with a generous budget (≥ 6), because
boards cost money.

- **When:** once the schematics generate clean (before routing — a wrong
  pin found here saves a 40-minute re-route), fully after step 8 and
  before ordering, and after any large change (parts/nets added, re-routed,
  docs rewritten).
- **What the reviewers check** (read-only; they never run gates): pin maps
  against the parts' actual datasheets (WebFetch), missing decoupling and
  support passives, docs↔design↔board↔BOM consistency, enclosure and
  mechanical constraints the project rules don't cover, the renders
  visually (silk over pads, legend orientation, collisions), generator
  and tooling code.
- **Fix doctrine** (the sweep's own, same as the pipeline's): a finding on
  a generated file lands in `design.cs` or a generator — never a hand edit
  — then regenerate per README's command list (copper reuse when nothing
  moved: the project's `route.cs --from HEAD` where its route tool has one,
  else the net-name copy from `git show HEAD:pcb/<b>.kicad_pcb`, step 5),
  and the gates re-run with **no new red** before the
  next wave. The sweep installs a one-line note about this in the
  project's `AGENTS.md` if it is missing.
