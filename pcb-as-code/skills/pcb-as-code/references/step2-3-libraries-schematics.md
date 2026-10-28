# Steps 2-3 — libraries and schematics

## Step 2 — gen_lib — project libraries

Write `pcb/lib/<proj>.kicad_sym` (custom symbols) and `pcb/lib/<proj>.pretty`
(footprints) plus the sym/fp lib tables, then resave with
`kicad-cli sym upgrade --force` / `fp upgrade --force` so KiCad owns the
format. Symbols' pin order and spacing decide where schematic wires can run.

## Step 3 — gen_schematics — one .kicad_sch per board

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
(`Kicad.Cli.Erc`), then netlist parity (step 6, `step6-7-verify-export.md`).

