# Project structure

## Repository layout (proven)

```
<project>/
├── AGENTS.md            thin orientation file for coding agents (see below)
├── README.md            what it is + the exact regeneration command list
├── docs/                design.md, pinmap.md, wiring.md, bom.md (whole unit,
│                        incl. off-board parts), assembly.md (build order +
│                        bring-up checks), fab.md, routing-comparison.md
├── sim/                 SPICE netlists + outputs for behavior-critical
│                        sections (referenced from design.md)
├── firmware/            PlatformIO project (PlatformIO is the external
│                         tool; the firmware is a first-class deliverable,
│                         not a generated build output like pcb/)
└── pcb/
    ├── *.kicad_sch|pcb|pro   build outputs (committed, regenerated)
    ├── fab.json              the fab's limits, fetched from its capability page
    ├── lib/                  generated project symbol + footprint libraries
    ├── tools/                the C# tools (design.cs + generators + this
    │                         skill's templates) and tests/
    ├── build/                DSN/SES, DRC reports, route logs (not fab output)
    ├── gerbers/<board>/      fab output + <board>.zip, -pos.csv, -bom.csv
    ├── renders/, schematics/ review views (PNG/SVG, schematic PDFs)
    └── <board>-backups/      zip snapshots before a destructive regen
```

## AGENTS.md — orienting any agent (thin, on purpose)

New projects scaffold a root `AGENTS.md`: the one file every coding agent
(ZCode, Claude Code, Codex, Cursor, …) reads without being asked. Keep it
thin — a pointer to the workflow plus this project's specifics. The
workflow itself is documented once, in the skill; a copied summary in
every project would drift from it (the same single-source-of-truth rule
the pipeline applies to hardware).

```markdown
# AGENTS.md — working in this repository

This project follows the **pcb-as-code** workflow (ZCode/Claude Code skill):
the design lives in code and the KiCad files are generated build outputs.

- `pcb/tools/design.cs` is the single source of truth for parts, footprints
  and nets. Never hand-edit `pcb/*.kicad_sch|kicad_pcb` or `pcb/lib/` —
  change the design or a generator and regenerate.
- Every stage gates: ERC 0/0, netlist pin-parity, DRC 0 errors / 0 unrouted,
  `FAB PROBLEMS: 0` (fabcheck against `pcb/fab.json`) before export. Run `dotnet test pcb/tools/tests` after touching tooling.
- The exact regeneration command list lives in README.md; project docs are
  under docs/ (design, pinmap, wiring, bom, assembly, fab).
- Fabricator: its limits are `pcb/fab.json` (fetched from its capability
  page), held by `fabcheck.cs`; the choice and its reasons in docs/fab.md.
- Firmware: PlatformIO (`firmware/`), native host tests in firmware/test/native.
- **Reviews (docs, schematics, boards) run as the pcb-as-code `hw-review` sweep:** waves of read-only reviewer agents, triaged by the session; confirmed fixes land in `design.cs`, a generator or the docs, and the boards are regenerated (never hand-edited) with the gates re-run before the next wave. Run it before a fab order and after any large change.
```

(If a tool predates AGENTS.md support, a one-line `CLAUDE.md` containing
`@AGENTS.md` forwards to it.)
