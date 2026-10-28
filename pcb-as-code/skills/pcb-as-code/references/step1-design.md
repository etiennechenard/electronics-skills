# Step 1 — design.cs — the source of truth

One `Comp` per part (ref, `lib:Symbol`, value, `lib:Footprint` or null for
the symbol's default, pin→net map, extra fields — put the MPN in `"MPN"`,
the BOM groups on it), plus per-board registries and `ExpectedPins()`.
Conventions the workflow depends on (see `../templates/design.cs`):

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

