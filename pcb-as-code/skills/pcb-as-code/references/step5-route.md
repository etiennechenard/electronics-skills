# Step 5 — route — FreeRouting headless (the hard-won recipe)

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
   `pcb/build/<board>.dsn`, the file `FreeRouting.Route(dsn, ses)` takes, with
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
   sanctioned LOOK step (SKILL.md hard rule 3).
6. Save (`Board.Save`, which also runs `pcb upgrade`), refill the zones in
   the saved board (`pcb drc --refill-zones --save-board`, with a scratch
   report), then gate: `Kicad.Cli.Drc` → `pcb/build/<board>-drc.json`,
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

