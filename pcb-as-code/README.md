# pcb-as-code

A Claude Code / ZCode plugin skill that formalizes a proven
hardware-project workflow — the design lives in code, KiCad files are
regenerated build outputs. Its pipeline tooling is **C# — .NET 10
file-based apps (`dotnet run tool.cs`)**.

## What the workflow is

A new project starts with a short kickoff (step 0): a requirements
interrogation that structures the repository and deploys the docs
skeletons before any design work. Then one source file (`design.cs`)
holds every part, footprint and net. Generators
write the symbol/footprint libraries, the schematics (with drawing
self-checks) and the placed, symbol-linked boards from it. FreeRouting puts
copper on headlessly (clearances pre-bumped, DSN order shuffled per retry,
single-threaded optimizer; 1.4.5.1 is the known-good default, other versions
such as 2.5 adopt head-to-head if they beat it). A verifier cross-checks
every pin of every schematic netlist and every PCB pad against the design,
plus ERC/DRC gates. An exporter refuses anything not clean and writes
gerbers, drill, placement, BOM and renders. Behavior-critical sections —
analog or mixed-signal — are simulated (e.g. with the micro-cap skill) and
their evidence kept in `sim/` before the design freezes. Lasting changes
go into the source or a generator — KiCad files are never hand-edited.
New projects
scaffold a thin root `AGENTS.md` so any coding agent orients itself
unasked — the workflow itself stays documented once, in the skill.

## What's in the plugin

```
├── agents/
│   ├── hw-reviewer-glm.md      the sweep's read-only reviewer twins
│   └── hw-reviewer-claude.md   (identical bodies): each harness's cheap
│                               fast model at max effort, no write tools,
│                               no shell, datasheet-capable
│                               (WebFetch/WebSearch)
└── skills/
    ├── pcb-as-code/
    │   ├── SKILL.md         the map: rules, pipeline at a glance, toolchain
    │   │                    facts, templates, gotchas — detail in references/
    │   ├── references/      stage detail read on demand: kickoff, pipeline
    │   │                    steps 1-8 + review, project structure + AGENTS.md,
    │   │                    fabrication, dotnet conventions
    │   ├── templates/       tested C# copied into new projects' pcb/tools/:
    │   │     sexpr.cs       KiCad s-expression reader/writer (sch/pcb/mod/SES)
    │   │     design.cs      single-source-of-truth model (Comp/Board/ExpectedPins)
    │   │     kicad.cs       kicad-cli runner + ERC/DRC/Netlist gates
    │   │     specctra.cs    DSN bump/shuffle, FreeRouting driver, SES reader
    │   │     boardfile.cs   a .kicad_pcb read as text: absolute pads, copper,
    │   │                    outlines, courtyards, KiCad's point-in-polygon rule
    │   │     dsn.cs         the Specctra DSN writer (kicad-cli has no DSN export)
    │   │     fab.cs         the fab's limits from pcb/fab.json + FabCheck
    │   │     fabcheck.cs    the fab gate: dotnet run fabcheck.cs -- fab.json boards
    │   │     smoke.cs       dotnet-run proof of the #:include path
    │   └── tests/           xUnit suite compiling the templates directly:
    │                        synthetic boards, live kicad-cli integration tests
    │                        (they early-return, reporting as passed, when KiCad is
    │                        absent) and dotnet-run tests of smoke.cs and
    │                        fabcheck.cs in either layout
    └── hw-review/           the multi-wave review sweep (its own skill,
                             runs on ZCode and Claude Code):
         ├── SKILL.md        routing: phases, non-negotiables, when to use
         └── references/
             └── wave-protocol.md  the playbook: budget + wave cut, per-surface
                                  check lists (docs / design source / boards +
                                  renders / datasheet pin checks), the stamped
                                  shard prompt, triage, the fix-and-regenerate
                                  cycle, the wave log, the final report
```

Run the tests: `dotnet test skills/pcb-as-code/tests`
Smoke the file-based path: `dotnet run skills/pcb-as-code/templates/smoke.cs`

## Fab limits

The skill ships no fab numbers. At kickoff the agent fetches the fab's
capability page and writes the project's `pcb/fab.json`, every value with
the page's verbatim quote and the check date. Code then holds the project
to it: `FabCheck.Project` covers the board setup and net classes, and
`FabCheck.Board` the geometry as built. `fabcheck.cs` is the gate before
export, and it fails on a `fab.json` checked more than 90 days ago. See
`references/fabrication.md`.

## Review sweeps

The pipeline's gates (ERC, pin parity, DRC, fab limits) hold the
mechanical line but cannot see a consistent-but-wrong design — a net that
matches `design.cs`, the schematic and the board everywhere and is still
wrong against the part's datasheet. The `hw-review` skill (same
overall strategy as the software factory's `review-waves`, tuned for
hardware; one plugin for both ZCode and Claude Code — reviewer twins pick
the harness's cheap model) covers
that: a fixed budget of cheap read-only reviewer agents in waves of at
most 6, each wave triaged by the session (false positives weeded), its
confirmed fixes applied **in the source** — `design.cs`, a generator or
the docs — and the boards regenerated with the gates re-run before the
next wave reviews the updated branch. Generated KiCad files are never
hand-edited, and nothing is committed. Documentation, the design source
and tooling, schematics and boards (renders read visually) go through the
same sweep, pin maps are checked against the parts' actual datasheets,
and the run closes with a severity summary and a whether-to-run-again
recommendation. Run it before a fab order (generous budget — boards cost
money) and after any large change; on a real build it took several waves
to find and fix everything, which is what the wave design is for. The
sweep installs a one-line review note in the project's `AGENTS.md` if it
is missing.

## Validation

- **A production multi-board RF instrument is built end to end with this
  C# tooling.** Its tools were checked against an earlier Python/pcbnew
  pipeline for the same boards:
  - the DSN of every board is identical to pcbnew's (boards without
    locked vias: their fixed-via output came later);
  - the GND stitching gives the same vias;
  - the verify output, BOM CSVs and gerbers match, the gerbers apart from
    sub-micron zone-fill vertices;
  - a full FreeRouting run of the densest board (about 1000 segments) is
    DRC-clean on its first attempt, in about 40 minutes.
- **`boardfile.cs`, `dsn.cs` and `fabcheck.cs` come from that build**,
  generalized. fabcheck, run on its boards with JLCPCB's page fetched into
  a fab.json, caught a real issue: a mask web set below JLCPCB's limit for
  black mask, which is wider than its limit for green.
- **The templates were also checked against real production KiCad files:**
  - parsing and round-tripping schematics, a 1.2 MB board and footprint
    libraries;
  - reading real DRC reports and a real SES.
- **On 10.0.7**, `kicad-cli pcb drc` was checked to refill zones in memory
  on an unfilled board, and `--refill-zones --save-board` to write the
  fills to the file (the build's GND stitching reads them).
